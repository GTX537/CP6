using System.Data.Common;
using CP6.WebApi.Services;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.BackgroundServices;

internal sealed class CrmOidcFamilyLogoutRecoveryWorkerV1(IServiceScopeFactory scopes,
    ILogger<CrmOidcFamilyLogoutRecoveryWorkerV1> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        var after = Guid.Empty;
        var afterSource = Guid.Empty;
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                if (scope.ServiceProvider.GetRequiredService<ICrmOidcGrantStore>() is not SqlCrmOidcGrantStore store) return;
                var batch = await store.ReadPendingFamilyLogoutsAsync(after, stoppingToken);
                foreach (var accepted in batch)
                {
                    try
                    {
                        await store.CompleteAcceptedFamilyAsync(accepted, stoppingToken);
                    }
                    catch (Exception error) when (error is CrmOidcFamilyLogoutUnavailableException or DbException or DbUpdateException)
                    {
                        logger.LogWarning("CRM OIDC logout remains pending for tenant {TenantId}, browser family {FamilyId}",
                            accepted.TenantId, accepted.FamilyId);
                    }
                    after = accepted.FamilyId;
                }
                if (batch.Count < 32) after = Guid.Empty;

                var sources = await store.ReadPendingSourceRetirementsAsync(afterSource, stoppingToken);
                foreach (var accepted in sources)
                {
                    try
                    {
                        await store.CompleteSourceRetirementAsync(accepted, stoppingToken);
                    }
                    catch (Exception error) when (error is CrmOidcFamilyLogoutUnavailableException or DbException or DbUpdateException)
                    {
                        logger.LogWarning("CRM authentication retirement remains pending for tenant {TenantId}, operation {OperationId}",
                            accepted.TenantId, accepted.OperationId);
                    }
                    afterSource = accepted.OperationId;
                }
                if (sources.Count < 32) afterSource = Guid.Empty;
            }
            catch (DbException)
            {
                logger.LogWarning("Could not enumerate accepted CRM OIDC family logouts");
            }
        }
    }
}
