using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
namespace CP6.Core.Services.Plm;

public interface IIterationDecisionTransportV1:IApprovalDecisionTransportV2 {}
public interface IIterationReceiptChainTransportV1:IApprovalReceiptChainTransportV2 {}
public sealed class IterationDecisionDeliveryV1(CP6Context db,ScopeAuthorizationFenceV1 scope,IIterationDecisionTransportV1 transport)
{
    public Task<ApprovalDecisionDeliveryRunV2> RunAsync(Guid tenant,CancellationToken ct=default)
        =>new ApprovalDecisionDeliveryV2(db,scope,transport,ApprovalBusinessProfileV2.IterationOwner).RunAsync(tenant,ct);
}
public sealed class IterationReceiptRecoveryV1(CP6Context db,ScopeAuthorizationFenceV1 scope,IIterationReceiptChainTransportV1 transport)
{
    public Task<ApprovalReceiptRecoveryRunV2> RunAsync(Guid tenant,CancellationToken ct=default)
        =>new ApprovalReceiptRecoveryV2(db,scope,transport,ApprovalBusinessProfileV2.IterationOwner).RunAsync(tenant,ct);
}
