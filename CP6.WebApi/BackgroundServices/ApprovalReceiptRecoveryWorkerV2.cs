using CP6.Core.Services.Oa;
namespace CP6.WebApi.BackgroundServices;

internal sealed class ApprovalReceiptRecoveryWorkerV2(IServiceScopeFactory scopes,ILogger<ApprovalReceiptRecoveryWorkerV2> logger):BackgroundService
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
                    var result=await sp.GetRequiredService<ApprovalReceiptRecoveryV2>().RunAsync(tenant,ct);
                    var observed=(result.State,result.ReceiptStreamId);
                    if(result.State is not ("IDLE_OR_DISABLED" or "RECOVERED" or "PREFIX_RECORDED") && (!_observed.TryGetValue(tenant,out var previous)||previous!=observed))
                        logger.LogWarning("Receipt recovery needs recovery: tenant {Tenant}, event {EventId}, state {State}",tenant,result.ReceiptStreamId,result.State);
                    _observed[tenant]=observed;
                },logger,stoppingToken);
            }
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception e){logger.LogError(e,"Receipt recovery tenant enumeration failed");}
        }
    }
}
