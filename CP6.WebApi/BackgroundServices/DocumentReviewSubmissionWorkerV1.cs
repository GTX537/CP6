using CP6.Core.Services.Plm;
namespace CP6.WebApi.BackgroundServices;

internal sealed class DocumentReviewSubmissionWorkerV1(IServiceScopeFactory scopes,ILogger<DocumentReviewSubmissionWorkerV1> logger):BackgroundService
{
    private readonly Dictionary<Guid,(string,Guid?)> observed=[];
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5),stoppingToken);
                await TenantScopeRunner.ForEachTenantAsync(scopes,async(sp,tenant,ct)=>{
                    var result=await sp.GetRequiredService<DocumentReviewDeliveryV1>().RunAsync(tenant,ct);
                    var next=(result.State,result.OutboxId);
                    if(result.State is "OUTCOME_UNKNOWN" or "CONFLICT" or "BLOCKED" && (!observed.TryGetValue(tenant,out var prior)||prior!=next))
                        logger.LogWarning("Document review delivery needs recovery: tenant {Tenant}, outbox {Outbox}, state {State}",tenant,result.OutboxId,result.State);
                    observed[tenant]=next;
                },logger,stoppingToken);
            }
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception e){logger.LogError(e,"Document review tenant enumeration failed");}
        }
    }
}
