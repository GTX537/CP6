using System.Data;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Wf;

/// <summary>服务任务异步底座扫描服务（spec §4.1）。worker 周期调 <see cref="ScanOnceAsync"/>。</summary>
public interface IWfServiceJobService
{
    /// <summary>扫一遍到期待执行 job（含 reaper），逐条 lease 抢占 + 状态闸 + 执行 + 恢复/重试/路由。返回处理条数。
    /// <paramref name="nowUtc"/> 注入便于确定性单测；<paramref name="workerId"/> 为 lease 持有者标识。</summary>
    Task<int> ScanOnceAsync(DateTime nowUtc, string workerId, CancellationToken ct = default);
}

/// <summary>
/// 服务任务异步引擎核心（spec §4.2 主循环 + §4.6 lease reaper）。与 <see cref="FlowEngine"/> 共享同一 scoped
/// <see cref="CP6Context"/>（DI 注入）。PostgreSQL 在同一 Main 权威事务内提交 token 推进、业务回调、
/// 通知及作业终态。外部执行器使用已领取的作业身份并在该事务外运行。
/// <para>正确性护栏：① <b>reaper（P0-4）</b> 只回收 <c>Running &amp;&amp; LockExpiresAtUtc &lt; nowUtc</c> 的过期租约
/// （未过期租约绝不误杀）；② <b>状态闸（P0-5）</b> 执行前校验 实例 Running &amp;&amp; token Active@job.NodeId，否则标 Cancelled
/// 不执行（撤回/驳回/token 已离开 → 不重复副作用，配合 executor 幂等闭合崩溃窗口）；③ <b>指数退避（P0-1）</b>
/// 失败且 AttemptCount&lt;MaxAttempts → Pending + <c>nowUtc + Backoff·2^(AttemptCount-1)</c>，耗尽 → Failed + 错误路由。</para>
/// <para>注：lease 抢占的并发-skip（RowVersion）依赖数据库 provider —— SQL Server 生产由
/// <c>[Timestamp] RowVersion</c> 触发 <see cref="DbUpdateConcurrencyException"/>，多 worker 抢同一 job 时败方跳过；
/// EF InMemory 不实现行版本，故单测以注入 nowUtc 做行为级覆盖（reaper/状态闸/退避/路由），不依赖并发-skip。</para>
/// </summary>
public sealed class WfServiceJobService : IWfServiceJobService
{
    /// <summary>租约时长（应 &gt; 单次 executor 预期耗时；过期即视为 worker 崩溃被 reaper 回收）。</summary>
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

    /// <summary>指数退避基数（秒）。首切固定常量；per-node 退避覆盖留后。</summary>
    public const int BackoffBaseSec = 30;

    /// <summary>单次扫描批量上限（避免长事务）。</summary>
    private const int BatchSize = 50;

    /// <summary>服务任务发起者身份（系统）。沿用 <see cref="WfTimeoutService.SystemActor"/> 口径。</summary>
    private static readonly Guid SystemActor = Guid.Empty;

    private readonly CP6Context _db;
    private readonly FlowEngine _engine;
    private readonly IReadOnlyDictionary<string, IServiceTaskExecutor> _executors;
    private readonly ILogger<WfServiceJobService>? _logger;
    private readonly ConnectorControlRuntimeV1? _connectorResults;
    private readonly ApprovalTaskDecisionOwnerV2? _appRecorder;

    public WfServiceJobService(CP6Context db, FlowEngine engine, IEnumerable<IServiceTaskExecutor> executors,
        ILogger<WfServiceJobService>? logger = null, ConnectorControlRuntimeV1? connectorResults = null,
        ApprovalTaskDecisionOwnerV2? appRecorder = null)
    {
        _db = db;
        _engine = engine;
        _executors = (executors ?? Array.Empty<IServiceTaskExecutor>())
            .ToDictionary(e => e.Key, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
        _connectorResults = connectorResults;
        _appRecorder = appRecorder;
    }

    public async Task<int> ScanOnceAsync(DateTime nowUtc, string workerId, CancellationToken ct = default)
    {
        // ① reaper（P0-4）：回收过期租约（Running 且 LockExpiresAtUtc < nowUtc）。
        var expired = await _db.Wf_ServiceJobs
            .Where(j => j.Status == ServiceJobStatus.Running
                        && j.LockExpiresAtUtc != null && j.LockExpiresAtUtc < nowUtc)
            .ToListAsync(ct);
        foreach (var j in expired)
        {
            // reaper 只回收过期租约、复位为 Pending 重投；**不自增 AttemptCount**——尝试计数由执行段
            // 在调 executor 之前持久化（见下 ⑤）。这样「抢到 lease 但从未执行就崩溃」不会烧掉重试配额（票2）。
            j.Status = ServiceJobStatus.Pending;
            j.LockedBy = null;
            j.LockedAtUtc = null;
            j.LockExpiresAtUtc = null;
        }
        if (expired.Count > 0) await _db.SaveChangesAsync(ct);

        // ② 取到期 Pending（reaper 之后查，刚回收的也可能纳入本轮）
        var jobIds = await _db.Wf_ServiceJobs
            .Where(j => j.Status == ServiceJobStatus.Pending && j.NextAttemptAtUtc <= nowUtc)
            .OrderBy(j => j.NextAttemptAtUtc)
            .Take(BatchSize)
            .Select(j => j.Id)
            .ToListAsync(ct);

        int processed = 0;
        foreach (var jobId in jobIds)
        {
            var job = await _db.Wf_ServiceJobs.SingleOrDefaultAsync(
                j => j.Id == jobId && j.Status == ServiceJobStatus.Pending && j.NextAttemptAtUtc <= nowUtc, ct);
            if (job is null) continue;

            // ③ lease 抢占：RowVersion 守（生产多 worker 并发，败方 DbUpdateConcurrencyException → reload + 跳过）
            job.Status = ServiceJobStatus.Running;
            job.LockedBy = workerId;
            job.LockedAtUtc = nowUtc;
            job.LockExpiresAtUtc = nowUtc + LeaseDuration;
            try { await _db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { await _db.Entry(job).ReloadAsync(ct); continue; }

            try
            {
                // ── 子流程内部 Kind 短路（子流程 spec §3.2 worker 兜底入口）：不进 executor 注册表、不走状态闸④
                //    （job.TokenId=子实例 Id 哨兵，绝不能走闸④否则被误 Cancelled）。复核幂等：组未齐/已处理 →
                //    引擎内状态闸零动作，凭据照常消费。
                if (job.Kind == WfJobKind.SubFlowResume)
                {
                    if (await CommitClaimAsync(job, async current =>
                    {
                        if (!await _engine.TryCompleteSubFlowResumeAsync(current, ct, nowUtc))
                        {
                            current.Status = ServiceJobStatus.Failed;
                            current.LastError = Trunc("E-WF-025 subFlowResume 载荷非法");
                            current.CompletedAtUtc = nowUtc;
                        }
                        return true;
                    }, ct)) processed++;
                    continue;
                }

                // ④ 执行前状态闸（P0-5）：实例 Running 且 token Active 且仍在 job.NodeId，否则 Cancelled 不执行
                var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == job.InstanceId, ct);
                var tok = await _db.Wf_FlowTokens.FirstOrDefaultAsync(t => t.Id == job.TokenId, ct);
                if (inst is null || inst.Status != FlowInstanceStatus.Running
                    || tok is null || tok.Status != FlowTokenStatus.Active || tok.NodeId != job.NodeId)
                {
                    if (await CommitClaimAsync(job, current => CompleteAsync(current, null, nowUtc, ct), ct))
                        processed++;
                    continue;
                }

                // ⑤ 执行：先把「本次尝试已开始」持久化（AttemptCount++ 立即入库），再调 executor。
                //    崩溃于 executor 期间 → 计数已落库（记 1 次）；崩溃于此保存之前 → 计数未增（记 0 次）。
                //    reaper 因此无需（也不再）自增，杜绝「抢占未执行」误烧配额（票2）。
                job.AttemptCount++;
                await _db.SaveChangesAsync(ct);
                ServiceTaskResult result;
                var actionRef = ServiceTaskActionRef.Parse(job.ActionRefJson ?? "{}");
                var key = ServiceTaskActionRef.ResolveExecutorKey(actionRef);
                if (job.Kind is (ServiceKind.WebApi or WfJobKind.DeferredWebApiStart) && key != ServiceKind.WebApi)
                {
                    // 旧 WebAPI 作业缺可信动作快照，保留原节点等待核证。
                    result = ServiceTaskResult.Blocked("UNVERIFIED_LEGACY");
                }
                else if (key is null)
                {
                    result = ServiceTaskResult.Ok();   // none = 纯等待 timer，无 executor 直接推进
                }
                else if (!_executors.TryGetValue(key, out var exec))
                {
                    result = ServiceTaskResult.Fail($"E-WF-018|{key}");
                }
                else
                {
                    var deferredStart = job.Kind == WfJobKind.DeferredWebApiStart;
                    var sctx = new ServiceTaskContext
                    {
                        InstanceId = job.InstanceId,
                        TokenId = job.TokenId,
                        NodeId = job.NodeId,
                        StarterId = inst.StarterId,
                        JobId = job.Id,
                        AttemptNo = job.AttemptCount,
                        ActorId = deferredStart ? inst.StarterId : SystemActor,
                        NowUtc = deferredStart ? job.DueAtUtc : nowUtc,
                        VarsJson = inst.VarsJson,
                        ActionRefJson = job.ActionRefJson,
                        ClaimWorker = job.LockedBy,
                        ClaimedAtUtc = job.LockedAtUtc,
                        ExecutionCancellation = ct,
                    };
                    try { result = await exec.ExecuteAsync(sctx); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception ex) when (actionRef.ConnectorPin != null)
                    {
                        // 原派发可能已经提交。仅让同一 Job 重新判断原操作，不能沿业务失败边推进。
                        _logger?.LogWarning("连接器派发状态待核对，Job {JobId}，错误类型 {ErrorType}", job.Id, ex.GetType().Name);
                        result = ServiceTaskResult.HoldDispatch("DISPATCH_CHECK_REQUIRED", nowUtc.AddSeconds(30));
                    }
                    catch (Exception ex) { result = ServiceTaskResult.Fail(ex.Message); }
                }

                if (result.ConnectorResultId is Guid resultId)
                {
                    try
                    {
                        var applications = _connectorResults ?? throw new ScopeAuthorizationExceptionV1("RESULT_APPLICATION_UNAVAILABLE");
                        if (await applications.ApplyResultAsync(job, resultId, _engine, _appRecorder, ct)) processed++;
                    }
                    catch (Npgsql.PostgresException error) when (
                        error.SqlState == Npgsql.PostgresErrorCodes.CheckViolation &&
                        error.MessageText is "APP_RESUME_DECISION_INCOMPLETE" or "CONNECTOR_RESUME_INCOMPLETE")
                    {
                        // 已明确回滚，释放当前租约供下一轮按原件本地重试。
                        var held = ServiceTaskResult.HoldDispatch(error.MessageText, nowUtc);
                        await CommitClaimAsync(job, current => CompleteAsync(current, held, nowUtc, ct), ct);
                    }
                    catch (ScopeAuthorizationExceptionV1 error)
                    {
                        // 原件已提交。当前门或 Recorder 不可用时保留原租约供本地恢复。
                        _logger?.LogWarning("连接器结果应用受阻，Job {JobId}，状态 {Status}", job.Id, error.Status);
                    }
                    continue;
                }
                if (await CommitClaimAsync(job, current => CompleteAsync(current, result, nowUtc, ct), ct))
                    processed++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                // An ambiguous commit keeps its persisted outcome. The next scan only reaps a still-running lease.
                _db.ChangeTracker.Clear();
                _logger?.LogWarning(error, "Workflow service job {JobId} did not return a completion result.", jobId);
            }
        }

        return processed;
    }

    private async Task<bool> CommitClaimAsync(Wf_ServiceJob claimed, Func<Wf_ServiceJob, Task<bool>> complete,
        CancellationToken ct)
    {
        if (!_db.Database.IsNpgsql())
        {
            if (!await complete(claimed)) return false;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        // The worker context has committed its technical claim. Re-read business state after the Main lock.
        _db.ChangeTracker.Clear();
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await AuthenticationCompletionGateV1.AcquireAsync(_db, ct);
            var tenantId = _db.CurrentTenantId;
            var tenant = await _db.Sys_Tenants.AsNoTracking().Where(t => t.Id == tenantId)
                .Select(t => new { t.Enable, t.ExpireDate }).SingleOrDefaultAsync(ct);
            if (tenant is not { Enable: true } || tenant.ExpireDate <= DateTime.Now)
                return false;

            var job = await LockJobAsync(_db, claimed.Id, ct);
            var now = await DatabaseUtcClock.ReadUtcNowAsync(_db, ct);
            if (job is null || !CurrentClaim(job, claimed.LockedBy, claimed.LockedAtUtc, claimed.AttemptCount, now))
                return false;
            var deadline = job.LockExpiresAtUtc!.Value;

            if (!await complete(job)) return false;
            await _db.SaveChangesAsync(ct);
            if (tenant.ExpireDate <= DateTime.Now || deadline <= await DatabaseUtcClock.ReadUtcNowAsync(_db, ct))
                return false;
            await tx.CommitAsync(ct);
            return true;
        }
        finally
        {
            _db.ChangeTracker.Clear();
        }
    }

    internal static bool CurrentClaim(Wf_ServiceJob job, string? worker, DateTime? claimedAt, int attempt, DateTime now) =>
        job.Status == ServiceJobStatus.Running && worker != null && job.LockedBy == worker &&
        // PostgreSQL 时间戳为微秒精度。
        claimedAt != null && job.LockedAtUtc?.Ticks / 10 == claimedAt.Value.Ticks / 10 &&
        job.AttemptCount == attempt && job.LockExpiresAtUtc > now;

    internal static IQueryable<Wf_ServiceJob> LockJob(CP6Context db, Guid jobId) =>
        db.Wf_ServiceJobs.FromSqlInterpolated($"""
            SELECT j.* FROM public."Wf_ServiceJob" AS j
            WHERE j."TenantId" = {db.CurrentTenantId} AND j."Id" = {jobId} FOR UPDATE
            """);

    internal static Task<Wf_ServiceJob?> LockJobAsync(CP6Context db, Guid jobId, CancellationToken ct) =>
        LockJob(db, jobId).SingleOrDefaultAsync(ct);

    private async Task<bool> CompleteAsync(Wf_ServiceJob job, ServiceTaskResult? result, DateTime nowUtc, CancellationToken ct)
    {
        var active = await _db.Wf_FlowInstances.AnyAsync(i => i.Id == job.InstanceId
            && i.Status == FlowInstanceStatus.Running, ct)
            && await _db.Wf_FlowTokens.AnyAsync(t => t.Id == job.TokenId
                && t.Status == FlowTokenStatus.Active && t.NodeId == job.NodeId, ct);
        if (!active)
        {
            job.Status = ServiceJobStatus.Cancelled;
        }
        else if (result is null)
        {
            // A claim from an outdated pre-execution read can be reaped and executed by the next scan.
            return false;
        }
        else if (result.AdmissionBlocked || result.DispatchHeld)
        {
            // 原 Job 与 token 均保留，准入失败没有外呼或错误边推进。新准入由恢复路径独立判断。
            job.Status = ServiceJobStatus.Pending;
            job.LastError = result.Error;
            job.NextAttemptAtUtc = result.CheckOriginalAfterUtc ?? DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
            if (result.AdmissionBlocked) job.AttemptCount--;
            job.LockedBy = null;
            job.LockedAtUtc = null;
            job.LockExpiresAtUtc = null;
            return true;
        }
        else if (result.Success)
        {
            await _engine.ResumeServiceTokenAsync(job.InstanceId, job.TokenId, job.NodeId, result.OutputVars);
            job.Status = ServiceJobStatus.Succeeded;
        }
        else
        {
            if (job.AttemptCount < job.MaxAttempts)
            {
                job.Status = ServiceJobStatus.Pending;
                job.LastError = Trunc(result.Error);
                job.NextAttemptAtUtc = nowUtc + TimeSpan.FromSeconds(BackoffBaseSec * Math.Pow(2, job.AttemptCount - 1));
                job.LockedBy = null;
                job.LockedAtUtc = null;
                job.LockExpiresAtUtc = null;
                return true;
            }

            await _engine.FailServiceTokenAsync(job.InstanceId, job.TokenId, job.NodeId, result.Error);
            job.LastError = Trunc(result.Error);
            job.Status = ServiceJobStatus.Failed;
        }

        job.CompletedAtUtc = nowUtc;
        return true;
    }

    /// <summary>LastError 截断 ≤1000 字（对齐实体列长，spec §2.3）。</summary>
    private static string? Trunc(string? s)
        => s is null ? null : (s.Length <= 1000 ? s : s[..1000]);
}
