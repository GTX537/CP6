using CP6.Core.Services.Wms;
using CP6.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CP6.WebApi.Services;

public sealed class SignalRMobileTaskNotifier : IMobileTaskNotifier
{
    private readonly IHubContext<WmsHub> _hub;

    public SignalRMobileTaskNotifier(IHubContext<WmsHub> hub) => _hub = hub;

    public Task NotifyAsync(
        Guid tenantId,
        string eventName,
        MobileTaskEvent payload,
        CancellationToken ct = default)
        => _hub.Clients.Group(WmsHub.TenantGroup(tenantId))
            .SendAsync(eventName, payload, ct);
}
