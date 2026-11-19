using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;

namespace CP6.WebApi.BackgroundServices;

public sealed class NativeTimeoutWorkerV2(IServiceScopeFactory scopes, NativeTimeoutOptionsV2 options,
    ILogger<NativeTimeoutWorkerV2> logger) : BackgroundService
{
    public Task ScanOnceAsync(CancellationToken ct = default)
    {
        if (!options.Enabled) throw new InvalidOperationException("TIMEOUT_CONFIG_DISABLED");
        return TenantScopeRunner.ForEachTenantAsync(scopes, async (services, _, token) =>
        {
            var owner = services.GetRequiredService<NativeTimeoutOwnerV2>();
            foreach (var claim in await owner.ClaimAsync(true, token))
                await ProcessAsync(owner, claim, token);
        }, logger, ct);
    }

    internal async Task ProcessAsync(NativeTimeoutOwnerV2 owner, NativeTimeoutClaimV2 claim, CancellationToken ct)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var renewed = await owner.RenewAsync(claim, ct);
                await owner.ExecuteAsync(renewed, ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                var kind = NativeTimeoutOwnerV2.Classify(error);
                var retry = kind == NativeTimeoutFailureKindV2.Retryable && attempt < maxAttempts;
                if (error is not ScopeAuthorizationExceptionV1 { Message: "TIMEOUT_OUTCOME_UNKNOWN" })
                    await owner.ObserveWorkerFailureAsync(claim, kind, retry, ct);
                if (!retry)
                {
                    logger.LogWarning("原生超时操作 {OperationId} 的尝试 {AttemptId} 已停止，类别 {Kind}。",
                        claim.ExecutionId, claim.ClaimId, kind);
                    return;
                }
            }
            await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), ct);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled) return;
        if (options.ServiceId == Guid.Empty || string.IsNullOrWhiteSpace(options.HostKey))
            throw new InvalidOperationException("TIMEOUT_HOST_NOT_REGISTERED");
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ScanOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogError(e, "原生超时扫描失败。"); }
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        }
    }
}
