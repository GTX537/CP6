using CP6.Core.Services.Wms;
namespace CP6.WebApi.BackgroundServices;

internal sealed class ToolApprovalDeliveryWorkerV1(IServiceScopeFactory scopes,ILogger<ToolApprovalDeliveryWorkerV1> logger):BackgroundService
{
    private readonly Dictionary<Guid,(string,Guid?)> _observed=[];
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5),stoppingToken);
                await TenantScopeRunner.ForEachTenantAsync(scopes,async(sp,tenant,ct)=>{
                    var result=await sp.GetRequiredService<ToolApprovalDeliveryV1>().RunAsync(tenant,ct);
                    var observed=(result.State,result.OutboxId);
                    if(result.State is "OUTCOME_UNKNOWN" or "CONFLICT" or "BLOCKED" && (!_observed.TryGetValue(tenant,out var previous)||previous!=observed))
                        logger.LogWarning("Approval delivery needs recovery: tenant {Tenant}, outbox {Outbox}, state {State}",tenant,result.OutboxId,result.State);
                    _observed[tenant]=observed;
                },logger,stoppingToken);
            }
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception e){logger.LogError(e,"Approval delivery tenant enumeration failed");}
        }
    }
}
