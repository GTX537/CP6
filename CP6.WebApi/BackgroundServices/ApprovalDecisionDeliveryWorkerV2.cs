using CP6.Core.Services.Oa;
namespace CP6.WebApi.BackgroundServices;

internal sealed class ApprovalDecisionDeliveryWorkerV2(IServiceScopeFactory scopes,ILogger<ApprovalDecisionDeliveryWorkerV2> logger):BackgroundService
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
                    var result=await sp.GetRequiredService<ApprovalDecisionDeliveryV2>().RunAsync(tenant,ct);
                    var observed=(result.State,result.EventId);
                    if(result.State is "OUTCOME_UNKNOWN" or "CONFLICT" or "BLOCKED" && (!_observed.TryGetValue(tenant,out var previous)||previous!=observed))
                        logger.LogWarning("Decision delivery needs recovery: tenant {Tenant}, event {EventId}, state {State}",tenant,result.EventId,result.State);
                    _observed[tenant]=observed;
                },logger,stoppingToken);
            }
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception e){logger.LogError(e,"Decision delivery tenant enumeration failed");}
        }
    }
}
