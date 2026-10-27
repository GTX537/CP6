using System.Text.Json.Serialization;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Plm;
namespace CP6.Core.Services.Wms;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolDispositionPolicyCommandV1(Guid OperationId, string State, EngineeringSourceRefV1 Site,
    string[] ReasonCodes, int RequiredHumanApprovals, [property: JsonRequired] bool SeparateRequesterAndHandler,
    [property: JsonRequired] DateTimeOffset EffectiveFrom, [property: JsonRequired] DateTimeOffset EffectiveTo, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolDispositionSubmitV1(Guid OperationId, ToolDispositionRefV1 Request,
    ToolDispositionRefV1 Policy, string ExpectedObligationVersion, string Reason);
public sealed record ToolDispositionPolicyV1(Guid EnvironmentId, Guid TenantId, Guid ToolId, Guid RecordId,
    Guid ActorId, DateTimeOffset RecordedAt, ToolMaintenanceAuthorityV1[] Authorities, ToolDispositionPolicyCommandV1 Body);
public sealed record ToolDispositionPolicyViewV1(ToolDispositionRefV1 Reference, ToolDispositionPolicyV1 Fact);
public sealed record ToolDispositionApprovalViewV1(Guid SubmissionId, Guid ToolId, string State,
    ApprovalSubjectV2 Subject, ApprovalPolicyV2 Policy, Guid StarterId, DateTimeOffset FrozenAt,
    string SnapshotDigest, string BusinessSubmittedRowVersion, Guid OwnerOutboxId,
    string TransportState = "PENDING", bool IsApproved = false, bool IsDiscarded = false);
public sealed record ToolDispositionApprovalContextV1(Guid TenantId, Guid ToolId, string Etag,
    ToolDispositionContextV1 Disposition, ToolDispositionPolicyViewV1? Policy, string PolicyKnowledge,
    ToolDispositionApprovalViewV1? Submission);
public sealed record ToolDispositionApprovalResultV1(Guid OperationId, Guid ObjectId, string Kind,
    ToolDispositionRefV1 Fact, ToolDispositionApprovalViewV1? Submission = null, bool IsDiscarded = false, bool IsUsePermit = false);
public sealed record ToolDispositionApprovalOperationV1(Guid OperationId, string State, ToolDispositionApprovalResultV1? Result = null);
