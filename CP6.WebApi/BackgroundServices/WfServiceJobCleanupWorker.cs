using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.BackgroundServices;

/// <summary>
/// C-T1 终态 job/流水清理 Worker（波⑤ 引擎基建 I-C）。每日 03:00 UTC 一轮，经
/// <see cref="TenantScopeRunner"/> 逐租户调 <see cref="IWfCleanupService.CleanupOnceAsync"/>：
/// 硬删超保留期（默认 180 天）的终态 <c>Wf_ServiceJob</c>/<c>Wf_TriggerFire</c>，在途/占坑永不清，
/// 老化占坑仅告警计数。每租户有动作即写一行 OperLog（删除计数 + 老化计数），老化 > 0 置 <c>IsAlert</c>。
/// v1 单实例；多实例并跑靠 SaveChanges 幂等（RemoveRange 同一行竞争败方 0 行，无副作用）。
/// </summary>
public class WfServiceJobCleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WfServiceJobCleanupWorker> _logger;

    public WfServiceJobCleanupWorker(IServiceScopeFactory scopeFactory, ILogger<WfServiceJobCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WFS 终态清理 Worker 启动（每日 03:00 UTC）");
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;
                var next = now.Date.AddHours(3);              // 今日 03:00 UTC
                if (next <= now) next = next.AddDays(1);      // 已过则顺延到明日 03:00
                var untilNext = next - now;

                try { await Task.Delay(untilNext, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }

                try
                {
                    await TenantScopeRunner.ForEachTenantAsync(_scopeFactory, async (sp, tenantId, ct) =>
                    {
                        var svc = sp.GetRequiredService<IWfCleanupService>();
                        var r = await svc.CleanupOnceAsync(DateTime.UtcNow, ct);

                        if (r.ServiceJobsDeleted + r.TriggerFiresDeleted > 0 || r.StaleReservationCount > 0)
                        {
                            var db = sp.GetRequiredService<CP6Context>();
                            db.Sys_OperLogs.Add(new Sys_OperLog
                            {
                                TenantId = tenantId,
                                UserName = "system",
                                HttpMethod = "JOB",
                                RequestUrl = "/jobs/wfs-cleanup",
                                Controller = nameof(WfServiceJobCleanupWorker),
                                Action = "WfsCleanup",
                                RequestBody = $"job删{r.ServiceJobsDeleted} fire删{r.TriggerFiresDeleted} 老化占坑{r.StaleReservationCount}",
                                StatusCode = 200,
                                IsAlert = r.StaleReservationCount > 0,   // 老化占坑需运维关注
                            });
                            await db.SaveChangesAsync(ct);

                            _logger.LogInformation(
                                "WFS清理 租户{Tenant} job删{Jobs} fire删{Fires} 老化占坑{Stale}",
                                tenantId, r.ServiceJobsDeleted, r.TriggerFiresDeleted, r.StaleReservationCount);
                        }
                    }, _logger, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { _logger.LogError(ex, "WFS 终态清理异常"); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { _logger.LogInformation("WFS 终态清理 Worker 停止"); }
    }
}
