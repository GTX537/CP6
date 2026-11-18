using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Erp;

namespace CP6.WebApi.BackgroundServices;

public sealed class OperLogCleanupService(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<OperLogCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = configuration.GetSection("OperLog");
        if (options.GetValue<int?>("RetentionDays") is <= 0) return;
        var interval = Math.Clamp(options.GetValue<int?>("CleanupIntervalHours") ?? 24, 1, 168);
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(interval));
        do
        {
            try
            {
                var count = await CleanupOnceAsync(stoppingToken);
                if (count != 0) logger.LogInformation("审计保留任务已核对处置回执，共 {Count} 条", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (OrderPriceCorrectionException e) { logger.LogWarning("审计清理未确认：{Code}", e.Message); }
            catch (Exception e) { logger.LogError("审计清理未确认：{Type}", e.GetType().Name); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task<int> CleanupOnceAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CP6Context>();
        return await new AuditRetentionV1(db).RunDueAsync(ct);
    }
}
