// CP6.Core/Services/Wf/FlowTriggerService.cs
using System.Security.Cryptography;
using System.Text;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

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

    /// <summary>timer 扫描一轮（worker 复用；lease 语义 = RowVersion 乐观并发 + NextDueUtc 前移即抢占）。</summary>
    Task<int> ScanTimersOnceAsync(CancellationToken ct);
}

public class FlowTriggerService : IFlowTriggerService
{
    /// <summary>占坑补跑宽限：FiredUtc 早于此宽限仍未回填的占坑行才补跑（避免与正在进行的第二段抢跑）</summary>
    public static readonly TimeSpan RecoveryGrace = TimeSpan.FromMinutes(2);
    private const int BatchSize = 50;

    private readonly CP6Context _db;
    private readonly IFlowEngine _engine;

    public FlowTriggerService(CP6Context db, IFlowEngine engine)
    {
        _db = db;
        _engine = engine;
    }

    public async Task<TriggerFireResult> FireAsync(Wf_FlowTrigger trigger, string? varsJson,
                                                   int source, string idempotencyKey, CancellationToken ct)
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
            catch (DbUpdateException)
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

        // ④ 第二段：SubmitAsync + 流水回填 + 水位 同一显式事务（映射表⑥，引擎原子接缝）
        //    trigger 可能被上游 ChangeTracker.Clear 失联 → 用库内跟踪实例回写水位
        var trackedTrigger = await _db.Wf_FlowTriggers.FirstOrDefaultAsync(t => t.Id == trigger.Id, ct) ?? trigger;
        try
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            var instanceId = await _engine.SubmitAsync(trackedTrigger.FlowKey, trackedTrigger.StarterUserId, varsJson ?? "{}");
            fire.InstanceId = instanceId;
            fire.Error = null;                              // 失败重试成功 → 清错
            trackedTrigger.LastFiredUtc = DateTime.UtcNow;  // 水位
            await _db.SaveChangesAsync(ct);
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

    public Task<int> ScanTimersOnceAsync(CancellationToken ct)
        => ScanTimersOnceAsync(DateTime.UtcNow, ct);

    /// <summary>测试重载（注入 nowUtc，映射表⑤）——B-T2 实现。</summary>
    public async Task<int> ScanTimersOnceAsync(DateTime nowUtc, CancellationToken ct)
    {
        var processed = 0;

        // ── ① 补跑扫描（spec §3.2 崩溃恢复）：宽限期外仍未完成的占坑行 → 补第二段 ──
        var staleIds = await _db.Wf_TriggerFires
            .Where(f => f.Source == WfTriggerType.Timer && f.InstanceId == null && f.Error == null
                        && f.FiredUtc < nowUtc - RecoveryGrace)
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
        var dueIds = await _db.Wf_FlowTriggers
            .Where(t => t.Enabled && t.TriggerType == WfTriggerType.Timer
                        && t.NextDueUtc != null && t.NextDueUtc <= nowUtc)
            .OrderBy(t => t.NextDueUtc)
            .Take(BatchSize)
            .Select(t => t.Id)
            .ToListAsync(ct);
        foreach (var id in dueIds)
        {
            ct.ThrowIfCancellationRequested();
            var trig = await _db.Wf_FlowTriggers.FirstOrDefaultAsync(t => t.Id == id, ct);   // 逐条重查（同上契约）
            if (trig == null || !trig.Enabled || trig.NextDueUtc == null || trig.NextDueUtc > nowUtc) continue;

            var dueUtc = trig.NextDueUtc.Value;
            var key = $"{trig.Id}:{dueUtc:O}";
            var cfg = WfTriggerConfig.ParseTimer(trig.ConfigJson);

            // 第一段：抢占 + 占坑，单 SaveChanges（隐式单事务）＝「NextDueUtc 前移 + INSERT 占坑行」原子提交。
            // misfire：NextUtc 从 nowUtc 起算严格未来下一个 → 跨过的历史到期点只补最近（本次），不追积压（spec §3.2）。
            var next = WfCronHelper.NextUtc(cfg.Cron, nowUtc);
            var fire = new Wf_TriggerFire
            {
                TriggerId = trig.Id, IdempotencyKey = key,
                FiredUtc = nowUtc, Source = WfTriggerType.Timer,
            };
            if (next == null)
            {
                // 保存后被改坏的 cron：停摆 + 记错（不占坑发起，不无限重扫）
                trig.NextDueUtc = null;
                fire.Error = "E-WF-022: cron 解析失败";
                _db.Wf_TriggerFires.Add(fire);
                await _db.SaveChangesAsync(ct);
                processed++;
                continue;
            }
            trig.NextDueUtc = next;
            _db.Wf_TriggerFires.Add(fire);
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
            await FireAsync(trig, cfg.VarsJson, WfTriggerType.Timer, key, ct);
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
