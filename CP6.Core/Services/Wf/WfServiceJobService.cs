using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

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
/// <see cref="CP6Context"/>（DI 注入），故 ResumeServiceTokenAsync/FailServiceTokenAsync 的 token 推进/路由与本表 job
/// 写在同一 DbContext 上协同（它们各自带 ×3 乐观并发重试 + 内部 SaveChanges；job 实体独立追踪，互不冲突）。
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

    public WfServiceJobService(CP6Context db, FlowEngine engine, IEnumerable<IServiceTaskExecutor> executors)
    {
        _db = db;
        _engine = engine;
        _executors = (executors ?? Array.Empty<IServiceTaskExecutor>())
            .ToDictionary(e => e.Key, StringComparer.OrdinalIgnoreCase);
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
        var jobs = await _db.Wf_ServiceJobs
            .Where(j => j.Status == ServiceJobStatus.Pending && j.NextAttemptAtUtc <= nowUtc)
            .OrderBy(j => j.NextAttemptAtUtc)
            .Take(BatchSize)
            .ToListAsync(ct);

        int processed = 0;
        foreach (var job in jobs)
        {
            // ③ lease 抢占：RowVersion 守（生产多 worker 并发，败方 DbUpdateConcurrencyException → reload + 跳过）
            job.Status = ServiceJobStatus.Running;
            job.LockedBy = workerId;
            job.LockedAtUtc = nowUtc;
            job.LockExpiresAtUtc = nowUtc + LeaseDuration;
            try { await _db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { await _db.Entry(job).ReloadAsync(ct); continue; }

            // ── 单 job 隔离（FIX 2）：lease 抢占成功后的处理体（状态闸→执行→恢复/重试/路由）包 try/catch。
            //    某条 job 抛出（如 executor 之外的引擎/DB 异常）不得中止整批：吞掉 + continue，留其 Running+lease，
            //    由后续扫描的 reaper（租约过期）回收重投。lease-claim 自身在本 try 之外（已有并发-skip 的 continue）。
            try
            {
                // ④ 执行前状态闸（P0-5）：实例 Running 且 token Active 且仍在 job.NodeId，否则 Cancelled 不执行
                var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == job.InstanceId, ct);
                var tok = await _db.Wf_FlowTokens.FirstOrDefaultAsync(t => t.Id == job.TokenId, ct);
                if (inst is null || inst.Status != FlowInstanceStatus.Running
                    || tok is null || tok.Status != FlowTokenStatus.Active || tok.NodeId != job.NodeId)
                {
                    job.Status = ServiceJobStatus.Cancelled;
                    job.CompletedAtUtc = nowUtc;
                    await _db.SaveChangesAsync(ct);
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
                if (key is null)
                {
                    result = ServiceTaskResult.Ok();   // none = 纯等待 timer，无 executor 直接推进
                }
                else if (!_executors.TryGetValue(key, out var exec))
                {
                    result = ServiceTaskResult.Fail($"E-WF-018 动作/连接器未注册:{key}");
                }
                else
                {
                    var sctx = new ServiceTaskContext
                    {
                        InstanceId = job.InstanceId,
                        TokenId = job.TokenId,
                        NodeId = job.NodeId,
                        StarterId = inst.StarterId,
                        JobId = job.Id,
                        AttemptNo = job.AttemptCount,
                        ActorId = SystemActor,
                        NowUtc = nowUtc,
                        VarsJson = inst.VarsJson,
                        ActionRefJson = job.ActionRefJson,
                    };
                    try { result = await exec.ExecuteAsync(sctx); }
                    catch (Exception ex) { result = ServiceTaskResult.Fail(ex.Message); }   // executor 异常不炸引擎
                }

                // ⑥ 成功 → 恢复 token（幂等，内部 advance/dispatch/SaveChanges + ×3 重试）；⑦ 失败 → 退避重试 / 耗尽路由
                if (result.Success)
                {
                    await _engine.ResumeServiceTokenAsync(job.InstanceId, job.TokenId, job.NodeId, result.OutputVars);
                    job.Status = ServiceJobStatus.Succeeded;
                    job.CompletedAtUtc = nowUtc;
                    await _db.SaveChangesAsync(ct);
                }
                else
                {
                    if (job.AttemptCount < job.MaxAttempts)
                    {
                        job.Status = ServiceJobStatus.Pending;
                        job.NextAttemptAtUtc = nowUtc + TimeSpan.FromSeconds(BackoffBaseSec * Math.Pow(2, job.AttemptCount - 1));
                        job.LastError = Trunc(result.Error);
                        job.LockedBy = null;
                        job.LockedAtUtc = null;
                        job.LockExpiresAtUtc = null;
                        await _db.SaveChangesAsync(ct);
                    }
                    else
                    {
                        // FIX 1：先 FailServiceTokenAsync（其 ×3 重试内部 ChangeTracker.ReloadAsync 会重载本 job，
                        //         若先置终态字段会被 reload 回滚成 Running/丢 LastError）→ 路由完成后再置终态字段并保存（clobber-proof，对齐成功路径次序）。
                        await _engine.FailServiceTokenAsync(job.InstanceId, job.TokenId, job.NodeId, result.Error);
                        job.Status = ServiceJobStatus.Failed;
                        job.LastError = Trunc(result.Error);
                        job.CompletedAtUtc = nowUtc;
                        await _db.SaveChangesAsync(ct);
                    }
                }
                processed++;
            }
            catch (Exception)
            {
                // 单 job 隔离：留其 Running+lease，由 reaper 回收重投；不中止整批，不 rethrow（无 logger，吞掉）。
                continue;
            }
        }

        return processed;
    }

    /// <summary>LastError 截断 ≤1000 字（对齐实体列长，spec §2.3）。</summary>
    private static string? Trunc(string? s)
        => s is null ? null : (s.Length <= 1000 ? s : s[..1000]);
}
