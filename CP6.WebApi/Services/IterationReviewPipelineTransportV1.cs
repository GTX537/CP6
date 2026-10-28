using CP6.Core.Services.Oa;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Wms;
namespace CP6.WebApi.Services;

public sealed class IterationDecisionTransportV1:IIterationDecisionTransportV1,IDisposable
{
    private readonly ApprovalDecisionDeliveryTransportV2 transport;
    public IterationDecisionTransportV1(ApprovalDecisionDeliveryOptionsV2 options,CrmOidcOptions oidc,ApprovalFactReceiverRegistryV2 receiver,bool development=false)
        =>transport=new(new ApprovalDecisionDeliveryRegistryV2(options,oidc,receiver,development,ApprovalBusinessProfileV2.IterationOwner));
    internal IterationDecisionTransportV1(ApprovalDecisionDeliveryRegistryV2 registry,HttpMessageHandler handler)=>transport=new(registry,handler);
    public ApprovalDecisionDeliveryRouteV2? Route(Guid tenant)=>transport.Route(tenant);
    public Task<ApprovalDecisionTransportResponseV2> SendAsync(ApprovalDecisionDeliveryRouteV2 route,Guid eventId,byte[] original,bool query,CancellationToken ct)
        =>transport.SendAsync(route,eventId,original,query,ct);
    public void Dispose()=>transport.Dispose();
}
public sealed class IterationApplicationTransportV1:IIterationApplicationTransportV1,IDisposable
{
    private readonly ToolApplicationDeliveryTransportV2 transport;
    public IterationApplicationTransportV1(ToolApprovalDeliveryOptionsV1 options,CrmOidcOptions oidc,ApprovalIntakeRegistryV2 intake,bool development=false)
        =>transport=new(options,oidc,intake,development);
    internal IterationApplicationTransportV1(ToolApprovalDeliveryOptionsV1 options,CrmOidcOptions oidc,ApprovalIntakeRegistryV2 intake,HttpMessageHandler handler)
        =>transport=new(options,oidc,intake,handler);
    public ToolApprovalDeliveryRouteV1? Route(Guid tenant)=>transport.Route(tenant);
    public Task<ToolApprovalDeliveryResponseV1> SendAsync(ToolApprovalDeliveryRouteV1 route,Guid eventId,byte[] original,bool query,CancellationToken ct)
        =>transport.SendAsync(route,eventId,original,query,ct);
    public void Dispose()=>transport.Dispose();
}
public sealed class IterationReceiptChainTransportV1:IIterationReceiptChainTransportV1,IDisposable
{
    private readonly ApprovalDecisionDeliveryTransportV2 transport;
    public IterationReceiptChainTransportV1(ApprovalDecisionDeliveryOptionsV2 options,CrmOidcOptions oidc,ApprovalFactReceiverRegistryV2 receiver,bool development=false)
        =>transport=new(new ApprovalDecisionDeliveryRegistryV2(options,oidc,receiver,development,ApprovalBusinessProfileV2.IterationOwner));
    internal IterationReceiptChainTransportV1(ApprovalDecisionDeliveryRegistryV2 registry,HttpMessageHandler handler)=>transport=new(registry,handler);
    public ApprovalDecisionDeliveryRouteV2? Route(Guid tenant)=>transport.Route(tenant);
    public Task<ApprovalDecisionTransportResponseV2> ReadAsync(ApprovalDecisionDeliveryRouteV2 route,Guid stream,string afterRevision,int limit,string? cursor,CancellationToken ct)
        =>transport.ReadChainAsync(route,stream,afterRevision,limit,cursor,ct);
    public void Dispose()=>transport.Dispose();
}
public sealed class IterationWithdrawalTransportV1:IIterationWithdrawalTransportV1,IDisposable
{
    private readonly ToolApprovalDeliveryTransportV1 transport;
    public IterationWithdrawalTransportV1(ToolApprovalDeliveryOptionsV1 options,CrmOidcOptions oidc,ApprovalIntakeRegistryV2 intake,bool development=false)
        =>transport=new(new ToolApprovalDeliveryRegistryV1(options,oidc,intake,development));
    internal IterationWithdrawalTransportV1(ToolApprovalDeliveryRegistryV1 registry,HttpMessageHandler handler)=>transport=new(registry,handler);
    public ToolApprovalDeliveryRouteV1? Route(Guid tenant)=>transport.Route(tenant);
    public Task<ToolApprovalDeliveryResponseV1> QueryAsync(ToolApprovalDeliveryRouteV1 route,Guid command,CancellationToken ct)=>transport.QueryWithdrawalAsync(route,command,ct);
    public Task<ToolApprovalDeliveryResponseV1> ContextAsync(ToolApprovalDeliveryRouteV1 route,Guid submission,CancellationToken ct)=>transport.WithdrawalContextAsync(route,submission,ct);
    public Task<ToolApprovalDeliveryResponseV1> SendAsync(ToolApprovalDeliveryRouteV1 route,Guid submission,string rowVersion,byte[] original,CancellationToken ct)
        =>transport.SendWithdrawalAsync(route,submission,rowVersion,original,ct);
    public void Dispose()=>transport.Dispose();
}
