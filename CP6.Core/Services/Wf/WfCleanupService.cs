using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

public sealed class CleanupResult
{
    public int ServiceJobsDeleted { get; set; }
    public int TriggerFiresDeleted { get; set; }
    public int StaleReservationCount { get; set; }
    public JobRetentionResultV1? ServiceJobsPlan { get; set; }
    public JobRetentionResultV1? TriggerFiresPlan { get; set; }
}

public interface IWfCleanupService
{
    Task<CleanupResult> CleanupOnceAsync(DateTime nowUtc, CancellationToken ct);
    Task<CleanupResult> ResumePendingAsync(DateTime nowUtc, CancellationToken ct);
}

/// <summary>WFS 原保留 Owner。终态服务作业和触发回执分别保存计划，长期占坑仍用于幂等。</summary>
public sealed class WfCleanupService(CP6Context db, WfsInfraOptions options, JobRetentionSourceV1? approval = null,
    TimeProvider? time = null) : IWfCleanupService
{
    public Task<CleanupResult> CleanupOnceAsync(DateTime nowUtc, CancellationToken ct) => RunAsync(nowUtc, false, ct);

    public Task<CleanupResult> ResumePendingAsync(DateTime nowUtc, CancellationToken ct) => RunAsync(nowUtc, true, ct);

    private async Task<CleanupResult> RunAsync(DateTime nowUtc, bool resumeOnly, CancellationToken ct)
    {
        if (options.CleanupRetentionDays <= 0) return new();
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("清理截止时间须使用 UTC。", nameof(nowUtc));

        var journal = new JobRetentionV1(db, approval, time);
        var jobs = await journal.RunForWorkerAsync(JobRetentionClassV1.WorkflowJob, nowUtc, options.CleanupRetentionDays, resumeOnly, ct);
        var fires = await journal.RunForWorkerAsync(JobRetentionClassV1.WorkflowTrigger, nowUtc, options.CleanupRetentionDays, resumeOnly, ct);
        var stale = 0;
        if (!resumeOnly && fires.PlanId.HasValue)
        {
            var cutoff = nowUtc.AddDays(-options.StaleReservationAlertDays);
            stale = await db.Wf_TriggerFires.AsNoTracking()
                .CountAsync(f => f.InstanceId == null && f.Error == null && f.FiredUtc < cutoff, ct);
        }

        return new()
        {
            ServiceJobsDeleted = jobs.Disposed,
            TriggerFiresDeleted = fires.Disposed,
            StaleReservationCount = stale,
            ServiceJobsPlan = jobs,
            TriggerFiresPlan = fires
        };
    }
}
