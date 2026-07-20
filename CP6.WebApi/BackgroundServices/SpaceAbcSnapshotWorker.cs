using CP6.Core.Services.Space;

namespace CP6.WebApi.BackgroundServices;

/// <summary>Hourly due-check that creates at most one scheduled ABC snapshot per site/day.</summary>
public sealed class SpaceAbcSnapshotWorker : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SpaceAbcSnapshotWorker> _logger;

    public SpaceAbcSnapshotWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<SpaceAbcSnapshotWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Space ABC snapshot worker started");
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Tenant enumeration and infrastructure failures must not silently kill the worker.
                    _logger.LogError(ex, "Space ABC snapshot cycle failed; the next hourly cycle will retry");
                }
                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            _logger.LogInformation("Space ABC snapshot worker stopped");
        }
    }

    public Task ProcessOnceAsync(CancellationToken ct = default) =>
        TenantScopeRunner.ForEachTenantAsync(_scopeFactory, async (services, tenantId, token) =>
        {
            var rebuilt = await services.GetRequiredService<ISpaceAnalyticsService>()
                .RebuildDueSnapshotsAsync(token);
            _logger.LogInformation(
                "Space ABC snapshot tenant {TenantId}: rebuilt {Count} due site(s)", tenantId, rebuilt);
        }, _logger, ct);
}
