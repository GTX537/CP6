using CP6.Core.Services.Wms;

namespace CP6.WebApi.Services;

public sealed class LocationRuleReviewSubmissionTransportV1:ILocationRuleReviewSubmissionTransportV1,IDisposable
{
    private readonly ToolApprovalDeliveryTransportV1 transport;
    public LocationRuleReviewSubmissionTransportV1(ToolApprovalDeliveryOptionsV1 options,CrmOidcOptions oidc,ApprovalIntakeRegistryV2 intake,bool development=false)
        =>transport=new(new ToolApprovalDeliveryRegistryV1(options,oidc,intake,development));
    internal LocationRuleReviewSubmissionTransportV1(ToolApprovalDeliveryRegistryV1 registry,HttpMessageHandler handler)
        =>transport=new(registry,handler);
    public ToolApprovalDeliveryRouteV1? Route(Guid tenant)=>transport.Route(tenant);
    public Task<ToolApprovalDeliveryResponseV1> SendAsync(ToolApprovalDeliveryRouteV1 route,Guid command,byte[] original,bool query,CancellationToken ct)
        =>transport.SendAsync(route,command,original,query,ct);
    public void Dispose()=>transport.Dispose();
}
