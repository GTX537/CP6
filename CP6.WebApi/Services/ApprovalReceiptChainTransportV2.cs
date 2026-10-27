using CP6.Core.Services.Oa;

namespace CP6.WebApi.Services;

public sealed class ApprovalReceiptChainTransportV2 : IApprovalReceiptChainTransportV2, IDisposable
{
    private readonly ApprovalDecisionDeliveryTransportV2 _transport;
    public ApprovalReceiptChainTransportV2(ApprovalDecisionDeliveryOptionsV2 options, CrmOidcOptions oidc,
        ApprovalFactReceiverRegistryV2 receiver, bool development = false)
    { _transport = new(new ApprovalDecisionDeliveryRegistryV2(options, oidc, receiver, development)); }
    internal ApprovalReceiptChainTransportV2(ApprovalDecisionDeliveryOptionsV2 options, CrmOidcOptions oidc,
        ApprovalFactReceiverRegistryV2 receiver, HttpMessageHandler handler)
    { _transport = new(new ApprovalDecisionDeliveryRegistryV2(options, oidc, receiver, true), handler); }
    public ApprovalDecisionDeliveryRouteV2? Route(Guid tenant) => _transport.Route(tenant);
    public Task<ApprovalDecisionTransportResponseV2> ReadAsync(ApprovalDecisionDeliveryRouteV2 route, Guid stream,
        string afterRevision, int limit, string? cursor, CancellationToken ct)
        => _transport.ReadChainAsync(route, stream, afterRevision, limit, cursor, ct);
    public void Dispose() => _transport.Dispose();
}
