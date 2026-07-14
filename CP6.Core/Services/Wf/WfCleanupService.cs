using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

/// <summary>清理一轮的统计结果（worker 侧据此写 OperLog）。</summary>
public sealed class CleanupResult
{
    /// <summary>本轮硬删的终态 <see cref="Wf_ServiceJob"/> 行数。</summary>
    public int ServiceJobsDeleted { get; set; }
    /// <summary>本轮硬删的终态 <see cref="Wf_TriggerFire"/> 行数（成功起单或失败）。</summary>
    public int TriggerFiresDeleted { get; set; }
    /// <summary>老化占坑数（<c>InstanceId==null &amp;&amp; Error==null</c> 且超过滞留告警天数）——永不清，仅告警计数。</summary>
    public int StaleReservationCount { get; set; }
}

/// <summary>C-T1 终态 job/流水清理服务（波⑤ 引擎基建）。</summary>
public interface IWfCleanupService
{
    /// <summary>清理一轮（时间注入便于测试）。当前租户作用域内（worker 经 <c>TenantScopeRunner</c> 逐租户调用）。</summary>
    Task<CleanupResult> CleanupOnceAsync(DateTime nowUtc, CancellationToken ct);
}

/// <summary>
/// 终态 job/流水清理服务（spec §4，波⑤ I-C）。清理面/保留面谓词：
/// <list type="bullet">
/// <item><b>Wf_ServiceJob</b>：终态（Succeeded/Failed/Cancelled）且 <c>CompletedAtUtc</c> 早于保留期截止 → 硬删；
/// 在途（Pending/Running）无论多老 → 永不清。</item>
/// <item><b>Wf_TriggerFire</b>：<c>FiredUtc</c> 早于保留期截止且已终态（<c>InstanceId!=null || Error!=null</c>）→ 硬删；
/// 占坑行（<c>InstanceId==null &amp;&amp; Error==null</c>）→ 永不清（幂等占位，删除会破坏幂等闸）。</item>
/// <item><b>老化占坑告警</b>：占坑行且 <c>FiredUtc</c> 早于滞留告警天数 → 计入 <see cref="CleanupResult.StaleReservationCount"/>（不删）。</item>
/// </list>
/// 保留期 <see cref="WfsInfraOptions.CleanupRetentionDays"/> &lt;= 0 → 禁用清理（直接返回空结果）。
/// <para>
/// <b>幂等窗口契约</b>：<c>Wf_TriggerFire</c> 终态行的清理保留期即 message/event 端点的幂等保证窗口（= 保留期天数，
/// 波③ §3.4 呼应）。清理保留期之前（默认 180 天前）的 Idempotency-Key 因终态流水已被硬删，重放不再命中幂等闸，
/// 会重复起单。缩短保留期即缩短幂等窗口——运维调 <c>Wfs:CleanupRetentionDays</c> 须知此代价。占坑行永不清，
/// 保证在途占位不因清理误删而放行重复起单。
/// </para>
/// </summary>
public sealed class WfCleanupService : IWfCleanupService
{
    private const int BatchSize = 500;

    private readonly CP6Context _db;
    private readonly WfsInfraOptions _opts;

    public WfCleanupService(CP6Context db, WfsInfraOptions opts)
    {
        _db = db;
        _opts = opts;
    }

    public async Task<CleanupResult> CleanupOnceAsync(DateTime nowUtc, CancellationToken ct)
    {
        var result = new CleanupResult();

        // 保留期 <= 0 → 禁用清理（连老化告警一并不跑，按契约「直接返回空结果」）。
        if (_opts.CleanupRetentionDays <= 0)
            return result;

        var retentionCutoff = nowUtc.AddDays(-_opts.CleanupRetentionDays);

        // ── Wf_ServiceJob：终态 + 超龄 → 分批硬删；在途永不清 ──────────────────
        result.ServiceJobsDeleted = await DeleteInBatchesAsync(
            _db.Wf_ServiceJobs.Where(j =>
                (j.Status == ServiceJobStatus.Succeeded
                 || j.Status == ServiceJobStatus.Failed
                 || j.Status == ServiceJobStatus.Cancelled)
                && j.CompletedAtUtc != null && j.CompletedAtUtc < retentionCutoff),
            ct);

        // ── Wf_TriggerFire：终态（已起单或已失败）+ 超龄 → 分批硬删；占坑永不清 ──
        result.TriggerFiresDeleted = await DeleteInBatchesAsync(
            _db.Wf_TriggerFires.Where(f =>
                f.FiredUtc < retentionCutoff
                && (f.InstanceId != null || f.Error != null)),
            ct);

        // ── 老化占坑告警：占坑（两者皆 null）且超过滞留告警天数 → 仅计数，不删 ──
        var staleCutoff = nowUtc.AddDays(-_opts.StaleReservationAlertDays);
        result.StaleReservationCount = await _db.Wf_TriggerFires
            .Where(f => f.InstanceId == null && f.Error == null && f.FiredUtc < staleCutoff)
            .CountAsync(ct);

        return result;
    }

    /// <summary>每批 <see cref="BatchSize"/> 条 <c>OrderBy(Id).Take(N) → RemoveRange → SaveChanges</c> 循环至清空，返回累计删除数。</summary>
    private async Task<int> DeleteInBatchesAsync<T>(IQueryable<T> query, CancellationToken ct) where T : class
    {
        var total = 0;
        while (true)
        {
            var batch = await query.OrderBy(e => EF.Property<Guid>(e, "Id")).Take(BatchSize).ToListAsync(ct);
            if (batch.Count == 0) break;
            _db.Set<T>().RemoveRange(batch);
            await _db.SaveChangesAsync(ct);
            total += batch.Count;
            if (batch.Count < BatchSize) break;
        }
        return total;
    }
}
