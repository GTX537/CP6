using CP6.Core.Services.Oa;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Wms;
namespace CP6.WebApi.Services;

public sealed class RequirementSetDecisionTransportV1:IRequirementSetDecisionTransportV1,IDisposable
{
    private readonly ApprovalDecisionDeliveryTransportV2 transport;
    public RequirementSetDecisionTransportV1(ApprovalDecisionDeliveryOptionsV2 options,CrmOidcOptions oidc,ApprovalFactReceiverRegistryV2 receiver,bool development=false)
        =>transport=new(new ApprovalDecisionDeliveryRegistryV2(options,oidc,receiver,development,ApprovalBusinessProfileV2.RequirementSetOwner));
    internal RequirementSetDecisionTransportV1(ApprovalDecisionDeliveryRegistryV2 registry,HttpMessageHandler handler)=>transport=new(registry,handler);
    public ApprovalDecisionDeliveryRouteV2? Route(Guid tenant)=>transport.Route(tenant);
    public Task<ApprovalDecisionTransportResponseV2> SendAsync(ApprovalDecisionDeliveryRouteV2 route,Guid eventId,byte[] original,bool query,CancellationToken ct)
        =>transport.SendAsync(route,eventId,original,query,ct);
    public void Dispose()=>transport.Dispose();
}
public sealed class RequirementSetApplicationTransportV1:IRequirementSetApplicationTransportV1,IDisposable
{
    private readonly ToolApplicationDeliveryTransportV2 transport;
    public RequirementSetApplicationTransportV1(ToolApprovalDeliveryOptionsV1 options,CrmOidcOptions oidc,ApprovalIntakeRegistryV2 intake,bool development=false)
        =>transport=new(options,oidc,intake,development);
    internal RequirementSetApplicationTransportV1(ToolApprovalDeliveryOptionsV1 options,CrmOidcOptions oidc,ApprovalIntakeRegistryV2 intake,HttpMessageHandler handler)
        =>transport=new(options,oidc,intake,handler);
    public ToolApprovalDeliveryRouteV1? Route(Guid tenant)=>transport.Route(tenant);
    public Task<ToolApprovalDeliveryResponseV1> SendAsync(ToolApprovalDeliveryRouteV1 route,Guid eventId,byte[] original,bool query,CancellationToken ct)
        =>transport.SendAsync(route,eventId,original,query,ct);
    public void Dispose()=>transport.Dispose();
}
public sealed class RequirementSetReceiptChainTransportV1:IRequirementSetReceiptChainTransportV1,IDisposable
{
    private readonly ApprovalDecisionDeliveryTransportV2 transport;
    public RequirementSetReceiptChainTransportV1(ApprovalDecisionDeliveryOptionsV2 options,CrmOidcOptions oidc,ApprovalFactReceiverRegistryV2 receiver,bool development=false)
        =>transport=new(new ApprovalDecisionDeliveryRegistryV2(options,oidc,receiver,development,ApprovalBusinessProfileV2.RequirementSetOwner));
    internal RequirementSetReceiptChainTransportV1(ApprovalDecisionDeliveryRegistryV2 registry,HttpMessageHandler handler)=>transport=new(registry,handler);
    public ApprovalDecisionDeliveryRouteV2? Route(Guid tenant)=>transport.Route(tenant);
    public Task<ApprovalDecisionTransportResponseV2> ReadAsync(ApprovalDecisionDeliveryRouteV2 route,Guid stream,string afterRevision,int limit,string? cursor,CancellationToken ct)
        =>transport.ReadChainAsync(route,stream,afterRevision,limit,cursor,ct);
    public void Dispose()=>transport.Dispose();
}
