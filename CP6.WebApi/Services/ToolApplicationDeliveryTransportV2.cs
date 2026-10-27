using CP6.Core.Services.Wms;

namespace CP6.WebApi.Services;

/// <summary>Separate opt-in receipt sender; shares the registered C02 credential exchange and fixed-origin rules.</summary>
public sealed class ToolApplicationDeliveryTransportV2 : IToolApplicationTransportV2, IDisposable
{
    private readonly ToolApprovalDeliveryTransportV1 _transport;
    public ToolApplicationDeliveryTransportV2(ToolApprovalDeliveryOptionsV1 options, CrmOidcOptions oidc,
        ApprovalIntakeRegistryV2 intake, bool development = false)
    { _transport = new(new ToolApprovalDeliveryRegistryV1(options, oidc, intake, development)); }
    internal ToolApplicationDeliveryTransportV2(ToolApprovalDeliveryOptionsV1 options, CrmOidcOptions oidc,
        ApprovalIntakeRegistryV2 intake, HttpMessageHandler handler)
    { _transport = new(new ToolApprovalDeliveryRegistryV1(options, oidc, intake, true), handler); }
    public ToolApprovalDeliveryRouteV1? Route(Guid tenant) => _transport.Route(tenant);
    public Task<ToolApprovalDeliveryResponseV1> SendAsync(ToolApprovalDeliveryRouteV1 route, Guid eventId, byte[] original, bool query, CancellationToken ct)
        => _transport.SendReceiptAsync(route, eventId, original, query, ct);
    public void Dispose() => _transport.Dispose();
}
