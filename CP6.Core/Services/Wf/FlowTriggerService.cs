// CP6.Core/Services/Wf/FlowTriggerService.cs
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.Wf;

public class TriggerFireResult
{
    public bool Success { get; init; }
    /// <summary>幂等撞键命中既有成功流水（HTTP 层据此回 200 而非 201）</summary>
    public bool Replayed { get; init; }
    public Guid? InstanceId { get; init; }
    public string? Error { get; init; }
    public static TriggerFireResult Ok(Guid instanceId, bool replayed = false)
        => new() { Success = true, InstanceId = instanceId, Replayed = replayed };
    public static TriggerFireResult Fail(string error) => new() { Success = false, Error = error };
}

public interface IFlowTriggerService
{
    /// <summary>统一发起（D2，spec §3.1）：Enabled 检查 → 幂等闸（撞键幂等返回既有 InstanceId 不报错）
    /// → 运行时双检 E-WF-022/023 → 变量构造由调用方完成 → SubmitAsync(trigger.StarterUserId) → 写流水 → 更新水位。</summary>
    Task<TriggerFireResult> FireAsync(Wf_FlowTrigger trigger, string? varsJson,
                                      int source, string idempotencyKey, CancellationToken ct);

    /// <summary>Manual fire joins the request's current IAM transaction on PostgreSQL.</summary>
    Task<TriggerFireResult> FireManualAsync(Wf_FlowTrigger trigger, string? varsJson, CancellationToken ct);

    Task<TriggerFireResult> FireMessageAsync(Wf_FlowTrigger trigger, string? rawKey, string idempotencyKey,
        string bodyJson, CancellationToken ct);

    /// <summary>扫描到期定时触发和旧占坑。PostgreSQL 下每条与当前租户权限一起提交。</summary>
    Task<int> ScanTimersOnceAsync(CancellationToken ct);
}

public class FlowTriggerService : IFlowTriggerService
{
    /// <summary>占坑补跑宽限：FiredUtc 早于此宽限仍未回填的占坑行才补跑（避免与正在进行的第二段抢跑）</summary>
    public static readonly TimeSpan RecoveryGrace = TimeSpan.FromMinutes(2);
    private const int BatchSize = 50;
    private const string ManualKeyPrefix = "manual:";

    private readonly CP6Context _db;
    private readonly IFlowEngine _engine;

    public FlowTriggerService(CP6Context db, IFlowEngine engine)
    {
        _db = db;
        _engine = engine;
    }

    public Task<TriggerFireResult> FireAsync(Wf_FlowTrigger trigger, string? varsJson,
                                                   int source, string idempotencyKey, CancellationToken ct)
        => FireCoreAsync(trigger, varsJson, source, idempotencyKey, null, ct);

    public Task<TriggerFireResult> FireManualAsync(Wf_FlowTrigger trigger, string? varsJson, CancellationToken ct)
    {
        var authorityTransaction = _db.Database.IsNpgsql()
            ? _db.Database.CurrentTransaction ?? throw new CurrentPermissionUnavailableException()
            : null;
        return FireCoreAsync(trigger, varsJson, trigger.TriggerType, $"{ManualKeyPrefix}{Guid.NewGuid():N}",
            authorityTransaction, ct);
    }

    public async Task<TriggerFireResult> FireMessageAsync(Wf_FlowTrigger trigger, string? rawKey, string idempotencyKey,
        string bodyJson, CancellationToken ct)
    {
        if (!_db.Database.IsNpgsql())
            return await FireCoreAsync(trigger, MessageVars(trigger, bodyJson), WfTriggerType.Message, idempotencyKey, null, ct);

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await AuthenticationCompletionGateV1.AcquireAsync(_db, ct);
        var current = await _db.Wf_FlowTriggers.SingleOrDefaultAsync(
            t => t.Id == trigger.Id && t.TriggerType == WfTriggerType.Message, ct);
        if (current is not { Enabled: true })
            throw new ScopeAuthorizationExceptionV1("trigger not found", 404);
        if (rawKey == null || !WfApiKeyHelper.Verify(rawKey, current.ApiKeyHash))
            throw new ScopeAuthorizationExceptionV1("invalid api key", 401);

        var tenant = await _db.Sys_Tenants.AsNoTracking().Where(t => t.Id == _db.CurrentTenantId)
            .Select(t => new { t.Enable, t.ExpireDate }).SingleOrDefaultAsync(ct);
        if (tenant is not { Enable: true } || tenant.ExpireDate <= DateTime.Now)
            throw new ScopeAuthorizationExceptionV1("TENANT_INACTIVE", 403);
        var result = await FireCoreAsync(current, MessageVars(current, bodyJson),
            WfTriggerType.Message, idempotencyKey, tx, ct);
        if (tenant.ExpireDate <= DateTime.Now)
            throw new ScopeAuthorizationExceptionV1("TENANT_INACTIVE", 403);
        ct.ThrowIfCancellationRequested();
        try
        {
            await tx.CommitAsync(ct);
        }
        catch (Exception error) when (error is DbException or TimeoutException or OperationCanceledException)
        {
            throw new ScopeAuthorizationExceptionV1("AUTHORITY_COMMIT_UNKNOWN", inner: error);
        }
        return result;
    }

    private static string MessageVars(Wf_FlowTrigger trigger, string bodyJson)
        => WfTriggerVarsMapper.FilterBySchema(bodyJson, WfTriggerConfig.ParseMessage(trigger.ConfigJson).VarsSchema);

    private async Task<TriggerFireResult> FireCoreAsync(Wf_FlowTrigger trigger, string? varsJson,
        int source, string idempotencyKey, IDbContextTransaction? authorityTransaction, CancellationToken ct)
    {
        // ① Enabled 检查（spec §3.1 顺序：先于幂等闸）
        if (!trigger.Enabled) return TriggerFireResult.Fail("触发器已停用");

        // ② 幂等闸：先查既有流水（Local + 库，防同 context 二次调用漏变更追踪器）
        var fire = _db.Wf_TriggerFires.Local
                       .FirstOrDefault(f => f.TriggerId == trigger.Id && f.IdempotencyKey == idempotencyKey)
                   ?? await _db.Wf_TriggerFires
                       .FirstOrDefaultAsync(f => f.TriggerId == trigger.Id && f.IdempotencyKey == idempotencyKey, ct);
        if (fire == null)
        {
            fire = new Wf_TriggerFire
            {
                TriggerId = trigger.Id,
                IdempotencyKey = idempotencyKey,
                FiredUtc = DateTime.UtcNow,
                Source = source,
                PayloadHash = source == WfTriggerType.Timer ? null : HashOrNull(varsJson),
            };
            _db.Wf_TriggerFires.Add(fire);
            try { await _db.SaveChangesAsync(ct); }
            catch (DbUpdateException) when (authorityTransaction == null)
            {
                // 并发撞唯一索引：让位既有行（另一实例先占坑），转入撞键分支
                _db.Entry(fire).State = EntityState.Detached;
                fire = await _db.Wf_TriggerFires
                    .FirstAsync(f => f.TriggerId == trigger.Id && f.IdempotencyKey == idempotencyKey, ct);
            }
        }
        if (fire.InstanceId != null)
            return TriggerFireResult.Ok(fire.InstanceId.Value, replayed: true);   // 幂等成功不是错误（spec §3.1）
        // InstanceId==null（占坑未完成或上次失败）→ 补跑第二段（共享契约末条语义）

        // ③ 运行时双检（spec §5：发起人/流程可能在保存后被停用）
        var starterOk = await _db.Sys_Users.AnyAsync(u => u.Id == trigger.StarterUserId && u.Enable, ct);
        if (!starterOk) return await FailFireAsync(fire, "E-WF-022: 发起人不存在或已停用", ct);
        var flowOk = await _db.Wf_FlowDefs.AnyAsync(d => d.FlowKey == trigger.FlowKey && d.Enable, ct);
        if (!flowOk) return await FailFireAsync(fire, "E-WF-023: 目标流程不存在或未启用", ct);

        // The authority paths have already loaded their trigger in this transaction.
        if (authorityTransaction != null)
            return await SubmitWithAuthorityAsync(authorityTransaction, trigger, fire, varsJson, ct);

        // Legacy two-phase callers can pass a detached trigger.
        var trackedTrigger = await _db.Wf_FlowTriggers.FirstOrDefaultAsync(t => t.Id == trigger.Id, ct) ?? trigger;
        try
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            var instanceId = await CompleteFireAsync(trackedTrigger, fire, varsJson, deferFollowUps: false, ct);
            await tx.CommitAsync(ct);
            return TriggerFireResult.Ok(instanceId);
        }
        catch (Exception ex)
        {
            // SubmitAsync 半途实体已随事务回滚，但仍挂在变更追踪器上 → 清追踪器后重查流水行回填 Error
            _db.ChangeTracker.Clear();
            var fresh = await _db.Wf_TriggerFires.FirstAsync(f => f.Id == fire.Id, ct);
            fresh.Error = Trunc($"E-WF-024: {ex.Message}");
            await _db.SaveChangesAsync(ct);
            return TriggerFireResult.Fail(fresh.Error);
        }
    }

    private async Task<TriggerFireResult> SubmitWithAuthorityAsync(IDbContextTransaction tx, Wf_FlowTrigger trigger,
        Wf_TriggerFire fire, string? varsJson, CancellationToken ct)
    {
        const string start = "wf_trigger_start";
        await tx.CreateSavepointAsync(start, ct);
        TriggerFireResult result;
        try
        {
            var instance = await CompleteFireAsync(trigger, fire, varsJson, deferFollowUps: true, ct);
            result = TriggerFireResult.Ok(instance);
        }
        catch (InvalidOperationException ex) when (ex is not (CurrentPermissionUnavailableException or IamAdministrationExceptionV1
            or ScopeAuthorizationExceptionV1))
        {
            // A rejected workflow can keep its admitted fire receipt. Database failures
            // propagate to the caller's transaction, including an unknown commit result.
            await tx.RollbackToSavepointAsync(start, ct);
            _db.ChangeTracker.Clear();
            var receipt = await _db.Wf_TriggerFires.SingleAsync(f => f.Id == fire.Id, ct);
            result = await FailFireAsync(receipt, Trunc($"E-WF-024: {ex.Message}"), ct);
        }
        await tx.ReleaseSavepointAsync(start, ct);
        return result;
    }

    private async Task<Guid> CompleteFireAsync(Wf_FlowTrigger trigger, Wf_TriggerFire fire,
        string? varsJson, bool deferFollowUps, CancellationToken ct)
    {
        var instanceId = deferFollowUps
            ? await _engine.SubmitWithDeferredFollowUpsAsync(trigger.FlowKey, trigger.StarterUserId, varsJson ?? "{}", ct)
            : await _engine.SubmitAsync(trigger.FlowKey, trigger.StarterUserId, varsJson ?? "{}");
        fire.InstanceId = instanceId;
        fire.Error = null;
        trigger.LastFiredUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return instanceId;
    }

    public Task<int> ScanTimersOnceAsync(CancellationToken ct)
        => ScanTimersOnceAsync(DateTime.UtcNow, ct);

    public Task<int> ScanTimersOnceAsync(DateTime nowUtc, CancellationToken ct)
        => _db.Database.IsNpgsql()
            ? ScanTimersWithAuthorityAsync(nowUtc, ct)
            : ScanLegacyTimersAsync(nowUtc, ct);

    private IQueryable<Wf_FlowTrigger> EnabledTimers()
        => _db.Wf_FlowTriggers.Where(t => t.Enabled && t.TriggerType == WfTriggerType.Timer);

    private IQueryable<Wf_FlowTrigger> DueTimers(DateTime nowUtc)
        => EnabledTimers().Where(t => t.NextDueUtc <= nowUtc);

    private IQueryable<Wf_TriggerFire> RecoverableTimers(DateTime nowUtc)
        => _db.Wf_TriggerFires.Where(f => f.Source == WfTriggerType.Timer && f.InstanceId == null
            && f.Error == null && f.FiredUtc < nowUtc - RecoveryGrace);

    private async Task<int> ScanTimersWithAuthorityAsync(DateTime nowUtc, CancellationToken ct)
    {
        var processed = 0;
        // A legacy manual fire cannot recover as a timer because it has no human actor.
        var enabledTimers = EnabledTimers();
        var staleIds = await RecoverableTimers(nowUtc)
            .Where(f => !f.IdempotencyKey.StartsWith(ManualKeyPrefix)
                && enabledTimers.Any(t => t.Id == f.TriggerId))
            .OrderBy(f => f.FiredUtc).Take(BatchSize).Select(f => f.Id).ToListAsync(ct);
        foreach (var id in staleIds)
            if (await ProcessTimerWithAuthorityAsync(id, nowUtc, recovery: true, ct)) processed++;

        var dueIds = await DueTimers(nowUtc)
            .OrderBy(t => t.NextDueUtc).Take(BatchSize).Select(t => t.Id).ToListAsync(ct);
        foreach (var id in dueIds)
            if (await ProcessTimerWithAuthorityAsync(id, nowUtc, recovery: false, ct)) processed++;

        return processed;
    }

    private async Task<bool> ProcessTimerWithAuthorityAsync(Guid id, DateTime nowUtc, bool recovery,
        CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await AuthenticationCompletionGateV1.AcquireAsync(_db, ct);
            var tenant = await _db.Sys_Tenants.AsNoTracking().Where(t => t.Id == _db.CurrentTenantId)
                .Select(t => new { t.Enable, t.ExpireDate }).SingleOrDefaultAsync(ct);
            if (tenant is not { Enable: true } || tenant.ExpireDate <= DateTime.Now) return false;

            Wf_TriggerFire? fire = null;
            Wf_FlowTrigger? trigger;
            if (recovery)
            {
                fire = await RecoverableTimers(nowUtc).SingleOrDefaultAsync(f => f.Id == id, ct);
                if (fire == null) return false;
                trigger = await EnabledTimers().SingleOrDefaultAsync(t => t.Id == fire.TriggerId, ct);
            }
            else
            {
                trigger = await DueTimers(nowUtc).SingleOrDefaultAsync(t => t.Id == id, ct);
            }
            if (trigger == null) return false;

            var cfg = WfTriggerConfig.ParseTimer(trigger.ConfigJson);
            if (fire == null)
            {
                fire = ClaimTimer(trigger, cfg, nowUtc);
                await _db.SaveChangesAsync(ct);
            }
            if (fire.Error == null)
                await FireCoreAsync(trigger, cfg.VarsJson, WfTriggerType.Timer, fire.IdempotencyKey, tx, ct);

            if (tenant.ExpireDate <= DateTime.Now) return false;
            await tx.CommitAsync(ct);
            return true;
        }
        finally
        {
            // The worker owns this context. Reload each slot after waiting for the current authority.
            _db.ChangeTracker.Clear();
        }
    }

    private Wf_TriggerFire ClaimTimer(Wf_FlowTrigger trigger, WfTimerTriggerConfig cfg, DateTime nowUtc)
    {
        var key = $"{trigger.Id}:{trigger.NextDueUtc!.Value:O}";
        // 从扫描时刻推到未来一档。跨过的历史到期点只触发一次（spec §3.2）。
        trigger.NextDueUtc = WfCronHelper.NextUtc(cfg.Cron, nowUtc);
        var fire = new Wf_TriggerFire
        {
            TriggerId = trigger.Id, IdempotencyKey = key,
            FiredUtc = nowUtc, Source = WfTriggerType.Timer,
            Error = trigger.NextDueUtc == null ? "E-WF-022: cron 解析失败" : null,
        };
        _db.Wf_TriggerFires.Add(fire);
        return fire;
    }

    private async Task<int> ScanLegacyTimersAsync(DateTime nowUtc, CancellationToken ct)
    {
        var processed = 0;

        // ── ① 补跑扫描（spec §3.2 崩溃恢复）：宽限期外仍未完成的占坑行 → 补第二段 ──
        var staleIds = await RecoverableTimers(nowUtc)
            .OrderBy(f => f.FiredUtc)
            .Take(BatchSize)
            .Select(f => f.Id)
            .ToListAsync(ct);
        foreach (var fireId in staleIds)
        {
            ct.ThrowIfCancellationRequested();
            // 每条重查（FireAsync 失败路径 ChangeTracker.Clear 会使批量实体失联——调用方契约）
            var fire = await _db.Wf_TriggerFires.FirstOrDefaultAsync(f => f.Id == fireId, ct);
            if (fire == null || fire.InstanceId != null || fire.Error != null) continue;   // 已被别人完成
            var trig = await _db.Wf_FlowTriggers.FirstOrDefaultAsync(t => t.Id == fire.TriggerId, ct);
            if (trig == null) continue;
            var cfg = WfTriggerConfig.ParseTimer(trig.ConfigJson);
            await FireAsync(trig, cfg.VarsJson, WfTriggerType.Timer, fire.IdempotencyKey, ct);
            processed++;
        }

        // ── ② 到期扫描 + 占坑两段式 ──
        var dueIds = await DueTimers(nowUtc)
            .OrderBy(t => t.NextDueUtc)
            .Take(BatchSize)
            .Select(t => t.Id)
            .ToListAsync(ct);
        foreach (var id in dueIds)
        {
            ct.ThrowIfCancellationRequested();
            var trig = await _db.Wf_FlowTriggers.FirstOrDefaultAsync(t => t.Id == id, ct);   // 逐条重查（同上契约）
            if (trig == null || !trig.Enabled || trig.NextDueUtc == null || trig.NextDueUtc > nowUtc) continue;

            var cfg = WfTriggerConfig.ParseTimer(trig.ConfigJson);

            // 第一段：抢占 + 占坑，单 SaveChanges（隐式单事务）＝「NextDueUtc 前移 + INSERT 占坑行」原子提交。
            var fire = ClaimTimer(trig, cfg, nowUtc);
            if (fire.Error != null)
            {
                // 保存后被改坏的 cron：停摆 + 记错（不占坑发起，不无限重扫）
                await _db.SaveChangesAsync(ct);
                processed++;
                continue;
            }
            try { await _db.SaveChangesAsync(ct); }
            catch (DbUpdateException)   // 含 DbUpdateConcurrencyException：RowVersion 被抢 / 占坑撞唯一键 → 让位
            {
                _db.Entry(fire).State = EntityState.Detached;
                _db.Entry(trig).State = EntityState.Detached;
                continue;
            }

            // 第一段提交后：库内 RowVersion 已被写触达（真库=OUTPUT 回读；SQLite 测试基座=AFTER UPDATE
            // 触发器刷新但 HasTrigger 关 RETURNING → 追踪实例仍持旧令牌）。从追踪器移除，令第二段 FireAsync
            // 重查到带当前 RowVersion 的鲜活实例，避免同上下文二次写触达（LastFiredUtc）撞乐观并发自误伤。
            _db.Entry(trig).State = EntityState.Detached;

            // 第二段：完成（FireAsync 复用占坑行回填 InstanceId/Error；两半各自幂等）
            await FireAsync(trig, cfg.VarsJson, WfTriggerType.Timer, fire.IdempotencyKey, ct);
            processed++;
        }

        return processed;
    }

    private async Task<TriggerFireResult> FailFireAsync(Wf_TriggerFire fire, string error, CancellationToken ct)
    {
        fire.Error = Trunc(error);
        await _db.SaveChangesAsync(ct);
        return TriggerFireResult.Fail(error);
    }

    private static string? HashOrNull(string? s)
        => s == null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    private static string Trunc(string s) => s.Length <= 1000 ? s : s[..1000];
}
