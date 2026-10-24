using CP6.Core.Services.CrmIdentity;
using CP6.WebApi.Services;

namespace CP6.WebApi.BackgroundServices;

/// <summary>Explicit source opt-in only. Configuration changes require coordinated drain and restart.</summary>
public sealed class InquiryServiceIdentityWorkerV1(IServiceScopeFactory scopes, CrmIdentityRuntime runtime,
    ILogger<InquiryServiceIdentityWorkerV1> logger) : BackgroundService
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
                    await scope.ServiceProvider.GetRequiredService<InquiryServiceIdentityPublisherV1>().PublishAsync(tenant, stoppingToken);
                    pending.Remove(tenant);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception) { logger.LogWarning("C02 service principal source publication remains unavailable."); }
            }
            if (pending.Count > 0) await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
