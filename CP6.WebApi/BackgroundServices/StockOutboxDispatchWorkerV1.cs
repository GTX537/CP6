using CP6.Core.Services.Integration.StockOutbox;

namespace CP6.WebApi.BackgroundServices;

internal sealed class StockOutboxDispatchWorkerV1(IServiceScopeFactory scopes,
    ILogger<StockOutboxDispatchWorkerV1> logger) : BackgroundService
{
    // Unknown original requests remain in PostgreSQL. Only log a change, not every unchanged poll.
    private readonly Dictionary<Guid, (string State, Guid? Request)> _observed = [];
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TenantScopeRunner.ForEachTenantAsync(scopes, async (sp, tenant, ct) =>
                {
                    var result = await sp.GetRequiredService<StockOutboxWorkerV1>().RunAsync(tenant, ct);
                    var next = (result.State, result.RequestId);
                    if (result.State == "OUTCOME_UNKNOWN" && (!_observed.TryGetValue(tenant, out var previous) || previous != next))
                        logger.LogWarning("Stock notification original needs recovery. Tenant {Tenant}, request {Request}", tenant, result.RequestId);
                    _observed[tenant] = next;
                }, logger, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Stock notification tenant enumeration failed"); }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
