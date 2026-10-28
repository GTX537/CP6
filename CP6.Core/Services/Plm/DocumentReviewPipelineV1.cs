using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
namespace CP6.Core.Services.Plm;

public interface IDocumentDecisionTransportV1:IApprovalDecisionTransportV2 {}
public interface IDocumentReceiptChainTransportV1:IApprovalReceiptChainTransportV2 {}
public sealed class DocumentDecisionDeliveryV1(CP6Context db,ScopeAuthorizationFenceV1 scope,IDocumentDecisionTransportV1 transport)
{
    public Task<ApprovalDecisionDeliveryRunV2> RunAsync(Guid tenant,CancellationToken ct=default)
        =>new ApprovalDecisionDeliveryV2(db,scope,transport,ApprovalBusinessProfileV2.DocumentOwner).RunAsync(tenant,ct);
}
public sealed class DocumentReceiptRecoveryV1(CP6Context db,ScopeAuthorizationFenceV1 scope,IDocumentReceiptChainTransportV1 transport)
{
    public Task<ApprovalReceiptRecoveryRunV2> RunAsync(Guid tenant,CancellationToken ct=default)
        =>new ApprovalReceiptRecoveryV2(db,scope,transport,ApprovalBusinessProfileV2.DocumentOwner).RunAsync(tenant,ct);
}
