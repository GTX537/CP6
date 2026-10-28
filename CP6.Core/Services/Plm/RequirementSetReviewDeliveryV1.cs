using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;

namespace CP6.Core.Services.Plm;
public interface IRequirementSetReviewSubmissionTransportV1:IToolApprovalDeliveryTransportV1 { }
public sealed class RequirementSetReviewDeliveryV1(CP6Context db,ScopeAuthorizationFenceV1 scope,IRequirementSetReviewSubmissionTransportV1 transport)
{
    // The existing public transport DTO names are retained for compatibility; its closed
    // profile chooses DOC's immutable journal, registration and delivery state throughout.
    public Task<ToolApprovalDeliveryRunV1> RunAsync(Guid tenant,CancellationToken ct=default)
        =>new ToolApprovalDeliveryV1(db,scope,transport,ApprovalBusinessProfileV2.RequirementSetOwner).RunAsync(tenant,ct);
}
