using CP6.Core.Services.Wms;
namespace CP6.WebApi.BackgroundServices;

internal sealed class LocationRuleReviewPipelineWorkerV1(IServiceScopeFactory scopes,ILogger<LocationRuleReviewPipelineWorkerV1> logger):BackgroundService
{
    private readonly Dictionary<(string,Guid),(string,Guid?)> observed=[];
    private Task Run(string stage,Func<IServiceProvider,Guid,CancellationToken,Task<(string State,Guid? Id)>> run,CancellationToken ct)
        =>TenantScopeRunner.ForEachTenantAsync(scopes,async(sp,tenant,token)=>{
            var result=await run(sp,tenant,token);var key=(stage,tenant);
            if(result.State is "OUTCOME_UNKNOWN" or "CONFLICT" or "BLOCKED" && (!observed.TryGetValue(key,out var previous)||previous!=result))
                logger.LogWarning("LocationRule review {Stage} needs recovery: tenant {Tenant}, operation {Operation}, state {State}",stage,tenant,result.Id,result.State);
            observed[key]=result;
        },logger,ct);
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while(!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5),ct);
                await Run("decision",async(sp,t,c)=>{var r=await sp.GetRequiredService<LocationRuleDecisionDeliveryV1>().RunAsync(t,c);return(r.State,r.EventId);},ct);
                await Run("application-receipt",async(sp,t,c)=>{var r=await sp.GetRequiredService<LocationRuleApplicationDeliveryV1>().RunAsync(t,c);return(r.State,r.EventId);},ct);
                await Run("receipt-recovery",async(sp,t,c)=>{var r=await sp.GetRequiredService<LocationRuleReceiptRecoveryV1>().RunAsync(t,c);return(r.State,r.ReceiptStreamId);},ct);
            }
            catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
            catch(Exception e){logger.LogError(e,"LocationRule review pipeline tenant enumeration failed");}
        }
    }
}
