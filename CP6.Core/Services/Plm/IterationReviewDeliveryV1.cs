using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;

namespace CP6.Core.Services.Plm;
public interface IIterationReviewSubmissionTransportV1:IToolApprovalDeliveryTransportV1 { }
public sealed class IterationReviewDeliveryV1(CP6Context db,ScopeAuthorizationFenceV1 scope,IIterationReviewSubmissionTransportV1 transport)
{
    // The existing public transport DTO names are retained for compatibility; its closed
    // profile chooses ITER's immutable journal, registration and delivery state throughout.
    public Task<ToolApprovalDeliveryRunV1> RunAsync(Guid tenant,CancellationToken ct=default)
        =>new ToolApprovalDeliveryV1(db,scope,transport,ApprovalBusinessProfileV2.IterationOwner).RunAsync(tenant,ct);
}
