using CP6.Core.Services.Plm;
using System.Text.Json.Serialization;
using CP6.Core.Services.Oa;

namespace CP6.Core.Services.Wms;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LocationRuleReviewRequirementV1(string Code, string Description, int RoleId, int MinimumApprovals);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LocationRuleReviewPolicyCommandV1(Guid OperationId, string State,
    LocationRuleReviewRequirementV1[] Requirements, string[] ExternalRequirements,
    [property: JsonRequired] bool SeparateAuthorAndReviewer, [property: JsonRequired] DateTimeOffset EffectiveFrom,
    [property: JsonRequired] DateTimeOffset EffectiveTo, string Reason, string RejectionDisposition);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LocationRuleReviewSubmitV1(Guid OperationId, EngineeringSourceRefV1 LocationRule,
    EngineeringSourceRefV1 Policy, string ReviewBasis, string[] DeclaredChangeRelations,
    Guid ReviewRoundId, Guid? ReplacesSubmissionId, string Reason);
public sealed record LocationRuleReviewAuthorityV1(string Resource, string Action, Guid ActorId,
    string PermissionVersion, string OrganizationVersion);
public sealed record LocationRuleReviewPolicyV1(Guid EnvironmentId, Guid TenantId, Guid LocationRuleId, Guid Id,
    Guid ActorId, DateTimeOffset RecordedAt, LocationRuleReviewAuthorityV1[] Authorities, LocationRuleReviewPolicyCommandV1 Body);
public sealed record LocationRuleReviewPolicyViewV1(EngineeringSourceRefV1 Reference, LocationRuleReviewPolicyV1 Fact);
public sealed record LocationRuleReviewPackageV1(EngineeringSourceRefV1 LocationRule, EngineeringSourceRefV1? Previous,
    string ChangeDigest, string ReviewBasis, string[] DeclaredChangeRelations,
    System.Text.Json.JsonElement Body);
public sealed record LocationRuleReviewSnapshotV1(EngineeringSourceRefV1 LocationRule,EngineeringSourceRefV1? Previous,
    string ChangeDigest,EngineeringSourceRefV1 ReviewPackage);
public sealed record LocationRuleReviewSubmissionV1(Guid SubmissionId, EngineeringSourceRefV1 LocationRule,
    EngineeringSourceRefV1 Policy, Guid StarterId, DateTimeOffset FrozenAt, string SnapshotDigest,
    string State = "SUBMITTED", string TransportState = "PENDING", bool IsApproved = false);
public sealed record LocationRuleReviewContextV1(Guid TenantId, Guid LocationRuleId, string Etag,
    LocationRuleReviewSourceViewV1 LocationRule, LocationRuleReviewPolicyViewV1[] Policies, LocationRuleReviewSubmissionV1[] Submissions);
public sealed record LocationRuleReviewResultV1(Guid OperationId, Guid LocationRuleId, string Kind,
    EngineeringSourceRefV1 Reference, LocationRuleReviewSubmissionV1? Submission = null);
public sealed record LocationRuleReviewOperationV1(Guid OperationId, string State, LocationRuleReviewResultV1? Result = null);
