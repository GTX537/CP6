using System.Text.Json.Serialization;
using CP6.Core.Services.Oa;

namespace CP6.Core.Services.Plm;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementSetReviewRequirementV1(string Code, string Description, int RoleId, int MinimumApprovals);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementSetReviewPolicyCommandV1(Guid OperationId, string State,
    RequirementSetReviewRequirementV1[] Requirements, string[] ExternalRequirements,
    [property: JsonRequired] bool SeparateAuthorAndReviewer, [property: JsonRequired] DateTimeOffset EffectiveFrom,
    [property: JsonRequired] DateTimeOffset EffectiveTo, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementSetReviewSubmitV1(Guid OperationId, EngineeringSourceRefV1 RequirementSet,
    EngineeringSourceRefV1 Policy, string ReviewBasis, string[] DeclaredChangeRelations,
    Guid ReviewRoundId, Guid? ReplacesSubmissionId, string Reason);
public sealed record RequirementSetReviewAuthorityV1(string Resource, string Action, Guid ActorId,
    string PermissionVersion, string OrganizationVersion);
public sealed record RequirementSetReviewPolicyV1(Guid EnvironmentId, Guid TenantId, Guid RequirementSetId, Guid Id,
    Guid ActorId, DateTimeOffset RecordedAt, RequirementSetReviewAuthorityV1[] Authorities, RequirementSetReviewPolicyCommandV1 Body);
public sealed record RequirementSetReviewPolicyViewV1(EngineeringSourceRefV1 Reference, RequirementSetReviewPolicyV1 Fact);
public sealed record RequirementSetReviewPackageV1(EngineeringSourceRefV1 RequirementSet, EngineeringSourceRefV1? Previous,
    string ChangeDigest, string ReviewBasis, string[] DeclaredChangeRelations,
    RequirementSetBodyV1 Body);
public sealed record RequirementSetReviewSnapshotV1(EngineeringSourceRefV1 RequirementSet,EngineeringSourceRefV1? Previous,
    string ChangeDigest,EngineeringSourceRefV1 ReviewPackage);
public sealed record RequirementSetReviewSubmissionV1(Guid SubmissionId, EngineeringSourceRefV1 RequirementSet,
    EngineeringSourceRefV1 Policy, Guid StarterId, DateTimeOffset FrozenAt, string SnapshotDigest,
    string State = "SUBMITTED", string TransportState = "PENDING", bool IsApproved = false);
public sealed record RequirementSetReviewContextV1(Guid TenantId, Guid RequirementSetId, string Etag,
    RequirementSetSnapshotV1 RequirementSet, RequirementSetReviewPolicyViewV1[] Policies, RequirementSetReviewSubmissionV1[] Submissions);
public sealed record RequirementSetReviewResultV1(Guid OperationId, Guid RequirementSetId, string Kind,
    EngineeringSourceRefV1 Reference, RequirementSetReviewSubmissionV1? Submission = null);
public sealed record RequirementSetReviewOperationV1(Guid OperationId, string State, RequirementSetReviewResultV1? Result = null);
