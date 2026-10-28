using CP6.Core.Services.Plm;
namespace CP6.WebApi.BackgroundServices;

internal sealed class IterationReviewPipelineWorkerV1(IServiceScopeFactory scopes,ILogger<IterationReviewPipelineWorkerV1> logger):BackgroundService
{
    private readonly Dictionary<(string,Guid),(string,Guid?)> observed=[];
    private Task Run(string stage,Func<IServiceProvider,Guid,CancellationToken,Task<(string State,Guid? Id)>> run,CancellationToken ct)
        =>TenantScopeRunner.ForEachTenantAsync(scopes,async(sp,tenant,token)=>{
            var result=await run(sp,tenant,token);var key=(stage,tenant);
            if(result.State is "OUTCOME_UNKNOWN" or "CONFLICT" or "BLOCKED" && (!observed.TryGetValue(key,out var previous)||previous!=result))
                logger.LogWarning("Iteration review {Stage} needs recovery: tenant {Tenant}, operation {Operation}, state {State}",stage,tenant,result.Id,result.State);
            observed[key]=result;
        },logger,ct);
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while(!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5),ct);
                await Run("withdrawal",async(sp,t,c)=>{var r=await sp.GetRequiredService<IterationWithdrawalDeliveryV1>().RunAsync(t,c);return(r.State,r.EventId);},ct);
                await Run("decision",async(sp,t,c)=>{var r=await sp.GetRequiredService<IterationDecisionDeliveryV1>().RunAsync(t,c);return(r.State,r.EventId);},ct);
                await Run("application-receipt",async(sp,t,c)=>{var r=await sp.GetRequiredService<IterationApplicationDeliveryV1>().RunAsync(t,c);return(r.State,r.EventId);},ct);
                await Run("receipt-recovery",async(sp,t,c)=>{var r=await sp.GetRequiredService<IterationReceiptRecoveryV1>().RunAsync(t,c);return(r.State,r.ReceiptStreamId);},ct);
            }
            catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
            catch(Exception e){logger.LogError(e,"Iteration review pipeline tenant enumeration failed");}
        }
    }
}
