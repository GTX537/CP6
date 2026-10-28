using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
namespace CP6.Core.Services.Wms;

public interface ILocationRuleDecisionTransportV1:IApprovalDecisionTransportV2 {}
public interface ILocationRuleReceiptChainTransportV1:IApprovalReceiptChainTransportV2 {}
public sealed class LocationRuleDecisionDeliveryV1(CP6Context db,ScopeAuthorizationFenceV1 scope,ILocationRuleDecisionTransportV1 transport)
{
    public Task<ApprovalDecisionDeliveryRunV2> RunAsync(Guid tenant,CancellationToken ct=default)
        =>new ApprovalDecisionDeliveryV2(db,scope,transport,ApprovalBusinessProfileV2.LocationRuleOwner).RunAsync(tenant,ct);
}
public sealed class LocationRuleReceiptRecoveryV1(CP6Context db,ScopeAuthorizationFenceV1 scope,ILocationRuleReceiptChainTransportV1 transport)
{
    public Task<ApprovalReceiptRecoveryRunV2> RunAsync(Guid tenant,CancellationToken ct=default)
        =>new ApprovalReceiptRecoveryV2(db,scope,transport,ApprovalBusinessProfileV2.LocationRuleOwner).RunAsync(tenant,ct);
}
