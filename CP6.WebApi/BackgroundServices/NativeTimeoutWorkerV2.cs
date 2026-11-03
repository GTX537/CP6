using CP6.Core.Services.Oa;

namespace CP6.WebApi.BackgroundServices;

public sealed class NativeTimeoutWorkerV2(IServiceScopeFactory scopes,NativeTimeoutOptionsV2 options,
    ILogger<NativeTimeoutWorkerV2> logger):BackgroundService
{
    public Task ScanOnceAsync(CancellationToken ct=default)
    {
        if(!options.Enabled)throw new InvalidOperationException("TIMEOUT_CONFIG_DISABLED");
        return TenantScopeRunner.ForEachTenantAsync(scopes,async(sp,tenant,token)=>
        {
            var owner=sp.GetRequiredService<NativeTimeoutOwnerV2>();
            foreach(var claimed in await owner.ClaimAsync(token))
            {
                // Each effect is a bounded, local transaction. Renew immediately before
                // its execution; final Owner checks reject an epoch that expires inside it.
                var claim=await owner.RenewAsync(claimed,token);
                var result=await owner.ExecuteAsync(claim,token);
                if(result.State is "BLOCKED_POLICY" or "BLOCKED_TARGET")
                    logger.LogWarning("Native timeout blocked: tenant {Tenant}, execution {Execution}, state {State}, reason {Reason}",tenant,result.ExecutionId,result.State,result.Reason);
            }
        },logger,ct);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!options.Enabled)return;
        if(options.ServiceId==Guid.Empty||string.IsNullOrWhiteSpace(options.HostKey))throw new InvalidOperationException("TIMEOUT_HOST_NOT_REGISTERED");
        while(!stoppingToken.IsCancellationRequested)
        {
            try{await ScanOnceAsync(stoppingToken);await Task.Delay(TimeSpan.FromSeconds(20),stoppingToken);}
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception e){logger.LogError(e,"Native timeout scan failed; no successful effect is inferred");await Task.Delay(TimeSpan.FromSeconds(20),stoppingToken);}
        }
    }
}
