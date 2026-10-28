using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
namespace CP6.Core.Services.Plm;

public interface IRequirementSetDecisionTransportV1:IApprovalDecisionTransportV2 {}
public interface IRequirementSetReceiptChainTransportV1:IApprovalReceiptChainTransportV2 {}
public sealed class RequirementSetDecisionDeliveryV1(CP6Context db,ScopeAuthorizationFenceV1 scope,IRequirementSetDecisionTransportV1 transport)
{
    public Task<ApprovalDecisionDeliveryRunV2> RunAsync(Guid tenant,CancellationToken ct=default)
        =>new ApprovalDecisionDeliveryV2(db,scope,transport,ApprovalBusinessProfileV2.RequirementSetOwner).RunAsync(tenant,ct);
}
public sealed class RequirementSetReceiptRecoveryV1(CP6Context db,ScopeAuthorizationFenceV1 scope,IRequirementSetReceiptChainTransportV1 transport)
{
    public Task<ApprovalReceiptRecoveryRunV2> RunAsync(Guid tenant,CancellationToken ct=default)
        =>new ApprovalReceiptRecoveryV2(db,scope,transport,ApprovalBusinessProfileV2.RequirementSetOwner).RunAsync(tenant,ct);
}
