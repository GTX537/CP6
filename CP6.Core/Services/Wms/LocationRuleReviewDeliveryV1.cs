using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;

namespace CP6.Core.Services.Wms;
public interface ILocationRuleReviewSubmissionTransportV1:IToolApprovalDeliveryTransportV1 { }
public sealed class LocationRuleReviewDeliveryV1(CP6Context db,ScopeAuthorizationFenceV1 scope,ILocationRuleReviewSubmissionTransportV1 transport)
{
    // The existing public transport DTO names are retained for compatibility; its closed
    // profile chooses the rule Owner's immutable journal, registration and delivery state throughout.
    public Task<ToolApprovalDeliveryRunV1> RunAsync(Guid tenant,CancellationToken ct=default)
        =>new ToolApprovalDeliveryV1(db,scope,transport,ApprovalBusinessProfileV2.LocationRuleOwner).RunAsync(tenant,ct);
}
