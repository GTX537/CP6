using CP6.Core.Services.Erp;

namespace CP6.WebApi.BackgroundServices;

internal sealed class BusinessPartnerMappingWorkerV1(IServiceScopeFactory scopes, ILogger<BusinessPartnerMappingWorkerV1> logger)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.WhenAll(
            Pump((workspace, ct) => workspace.RunMappingDeliveryOnce(ct), stoppingToken),
            Pump((workspace, ct) => workspace.RunMappingProjectionOnce(ct), stoppingToken),
            Pump((workspace, ct) => workspace.RunMappingProjectionOnce(ct), stoppingToken));

    private async Task Pump(Func<BusinessPartnerWorkspaceV1, CancellationToken, Task> run, CancellationToken ct)
    {
        using var ticker = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await ticker.WaitForNextTickAsync(ct))
        {
            try
            {
                await TenantScopeRunner.ForEachTenantAsync(scopes, (sp, _, token) =>
                    run(sp.GetRequiredService<BusinessPartnerWorkspaceV1>(), token), logger, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception error) { logger.LogError(error, "枚举伙伴映射租户失败"); }
        }
    }
}
