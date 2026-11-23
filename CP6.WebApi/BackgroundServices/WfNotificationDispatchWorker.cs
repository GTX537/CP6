using CP6.Core.Services.Oa;
using CP6.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CP6.WebApi.BackgroundServices;

/// <summary>按已采用的任务 Outbox 所属租户派送。发送结果独立于原任务事务。</summary>
public sealed class WfNotificationDispatchWorker(IServiceScopeFactory scopes, ILogger<WfNotificationDispatchWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await DispatchBatchAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    internal Task DispatchBatchAsync(CancellationToken ct) =>
        TenantScopeRunner.ForEachTenantAsync(scopes, async (services, _, token) =>
        {
            var owner = services.GetRequiredService<WorkflowNotificationDeliveryService>();
            if (!owner.IsAvailable) return;
            var hub = services.GetRequiredService<IHubContext<NotifyHub>>();
            await owner.ScanAsync(async (tenant, environment, recipient, cancellation) =>
            {
                try
                {
                    await hub.Clients.Group(NotifyHub.WorkflowRecipientGroup(tenant, environment, recipient))
                        .SendAsync("WfNotification", cancellation);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
                catch (Exception) { logger.LogWarning("站内通知刷新提示未送达。"); }
            }, token);
        }, logger, ct);
}
