using CP6.Core.Services.CrmIdentity;
using CP6.WebApi.Services;

namespace CP6.WebApi.BackgroundServices;

public sealed class InquiryServiceIdentityWorkerV1(IServiceScopeFactory scopes, CrmIdentityRuntime runtime,
    ILogger<InquiryServiceIdentityWorkerV1> logger) : ServiceIdentityPublicationWorker<InquiryServiceIdentityPublisherV1>(scopes, runtime, logger);

public sealed class AccountC03ServiceIdentityWorker(IServiceScopeFactory scopes, CrmIdentityRuntime runtime,
    ILogger<AccountC03ServiceIdentityWorker> logger) : ServiceIdentityPublicationWorker<AccountC03ServiceIdentityPublisher>(scopes, runtime, logger);

/// <summary>按已启用的来源分别发布。每个来源只重试尚未完成登记的租户。</summary>
public abstract class ServiceIdentityPublicationWorker<T>(IServiceScopeFactory scopes, CrmIdentityRuntime runtime,
    ILogger logger) : BackgroundService where T : class, IServiceIdentityPublisher
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pending = runtime.Options.Tenants.Keys.ToHashSet();
        while (pending.Count > 0 && !stoppingToken.IsCancellationRequested)
        {
            foreach (var tenant in pending.ToArray())
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<T>().PublishAsync(tenant, stoppingToken);
                    pending.Remove(tenant);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception) { logger.LogWarning("C02 服务主体来源 {Publisher} 尚不能发布。", typeof(T).Name); }
            }
            if (pending.Count > 0) await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
