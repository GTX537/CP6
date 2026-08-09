using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.BackgroundServices;

/// <summary>
/// Deletes raw scan audit rows after their tenant/warehouse retention window.
/// Task business events are intentionally not affected and remain long-lived.
/// </summary>
public sealed class WmsScanRetentionCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WmsScanRetentionCleanupService> _logger;

    public WmsScanRetentionCleanupService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<WmsScanRetentionCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalHours = Math.Clamp(
            _configuration.GetValue<int?>("Wms:ScanCleanupIntervalHours") ?? 24,
            1,
            168);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await CleanupAsync(stoppingToken);
            try
            {
                await Task.Delay(TimeSpan.FromHours(intervalHours), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CP6Context>();
            var now = DateTime.UtcNow;
            var deleted = await db.MobileTaskScanLogs
                .IgnoreQueryFilters()
                .Where(x => x.RetainUntil <= now)
                .ExecuteDeleteAsync(ct);
            if (deleted > 0)
                _logger.LogInformation(
                    "Deleted {Count} expired WMS raw scan audit records.",
                    deleted);
        }
        catch (OperationCanceledException)
        {
            // Normal host shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WMS raw scan retention cleanup failed.");
        }
    }
}
