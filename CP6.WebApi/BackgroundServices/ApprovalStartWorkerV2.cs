using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.WebApi.Services;
namespace CP6.WebApi.BackgroundServices;

public sealed class ApprovalStartOptionsV2
{
    public bool Enabled { get; set; }
}

internal sealed class ApprovalStartWorkerV2(IServiceScopeFactory scopes, ApprovalStartOptionsV2 options,
    ApprovalIntakeRegistryV2 registry, ILogger<ApprovalStartWorkerV2> logger) : BackgroundService
{
    private readonly Dictionary<Guid,string> _observed=[];
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!options.Enabled)return;
        if(!registry.Enabled)throw new InvalidOperationException("APP_START_INTAKE_REQUIRED");
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5),stoppingToken);
                await TenantScopeRunner.ForEachTenantAsync(scopes,async(sp,tenant,ct)=>{
                    string state;
                    try { state=(await sp.GetRequiredService<ApprovalStartOwnerV2>().RunAsync(tenant,ct)).State; }
                    catch(ScopeAuthorizationExceptionV1 e) { state=e.Message; }
                    if(state is not("IDLE" or "RUNNING" or "SUSPENDED") && (!_observed.TryGetValue(tenant,out var old)||old!=state))
                        logger.LogWarning("Approval start requires recovery: tenant {Tenant}, state {State}",tenant,state);
                    _observed[tenant]=state;
                },logger,stoppingToken);
            }
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception e){logger.LogError(e,"Approval start tenant enumeration failed");}
        }
    }
}
