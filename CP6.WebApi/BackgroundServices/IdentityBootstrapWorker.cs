using CP6.Core.Services.CrmIdentity;

namespace CP6.WebApi.BackgroundServices;

public sealed class IdentityBootstrapWorker(IServiceScopeFactory scopes, CrmIdentityRuntime runtime,
    ILogger<IdentityBootstrapWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pending = runtime.Options.Tenants.Keys.ToHashSet();
        while (pending.Count > 0 && !stoppingToken.IsCancellationRequested)
        {
            foreach (var tenant in pending.ToArray())
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IdentityBootstrapService>().InitializeAsync(tenant, stoppingToken);
                    pending.Remove(tenant);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception) { logger.LogWarning("C02 identity bootstrap remains unavailable; management baseline is closed."); }
            }
            if (pending.Count > 0) await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
