using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;

namespace CP6.WebApi.BackgroundServices;

/// <summary>WMS 原始扫描留存 Owner。扫描日志与业务事件使用各自的留存范围。</summary>
public sealed class WmsScanRetentionCleanupService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    JobRetentionSourceV1 approval,
    ILogger<WmsScanRetentionCleanupService> logger,
    TimeProvider? clock = null) : BackgroundService
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (approval.EnvironmentId == Guid.Empty || approval.SourceId == Guid.Empty) return;
        var interval = TimeSpan.FromHours(Math.Clamp(configuration.GetValue<int?>("Wms:ScanCleanupIntervalHours") ?? 24, 1, 168));
        await Task.Delay(TimeSpan.FromSeconds(60), time, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "WMS 扫描留存本轮执行失败。"); }
            await Task.Delay(interval, time, stoppingToken);
        }
    }

    internal Task RunOnceAsync(CancellationToken ct) =>
        TenantScopeRunner.ForEachTenantAsync(scopeFactory, async (services, tenant, token) =>
        {
            var journal = new JobRetentionV1(services.GetRequiredService<CP6Context>(), approval, time);
            var result = await journal.RunForWorkerAsync(JobRetentionClassV1.WmsRawScan, time.GetUtcNow(), 0, false, token);
            if (result.PlanId is not null)
                logger.LogInformation("WMS 扫描留存租户 {Tenant}，计划 {Plan}，结果 {State}，已核实处置 {Count}。",
                    tenant, result.PlanId, result.State, result.Disposed);
        }, logger, ct);
}
