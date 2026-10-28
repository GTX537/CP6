using System.Text.Json.Serialization;
using CP6.Core.Services.Oa;

namespace CP6.Core.Services.Plm;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DocumentReviewRequirementV1(string Code, string Description, int RoleId, int MinimumApprovals);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DocumentReviewPolicyCommandV1(Guid OperationId, string State,
    DocumentReviewRequirementV1[] Requirements, string[] ExternalRequirements,
    [property: JsonRequired] bool SeparateAuthorAndReviewer, [property: JsonRequired] DateTimeOffset EffectiveFrom,
    [property: JsonRequired] DateTimeOffset EffectiveTo, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DocumentReviewSubmitV1(Guid OperationId, EngineeringSourceRefV1 Document,
    EngineeringSourceRefV1 Policy, string ReviewBasis, string[] DeclaredChangeRelations,
    Guid ReviewRoundId, Guid? ReplacesSubmissionId, string Reason);
public sealed record DocumentReviewAuthorityV1(string Resource, string Action, Guid ActorId,
    string PermissionVersion, string OrganizationVersion);
public sealed record DocumentReviewPolicyV1(Guid EnvironmentId, Guid TenantId, Guid DocumentId, Guid Id,
    Guid ActorId, DateTimeOffset RecordedAt, DocumentReviewAuthorityV1[] Authorities, DocumentReviewPolicyCommandV1 Body);
public sealed record DocumentReviewPolicyViewV1(EngineeringSourceRefV1 Reference, DocumentReviewPolicyV1 Fact);
public sealed record DocumentReviewPackageV1(EngineeringSourceRefV1 Document, EngineeringSourceRefV1? Previous,
    string ChangeDigest, string ReviewBasis, string[] DeclaredChangeRelations,
    DocumentSourceV1 Source, DocumentContentV1[] Contents);
public sealed record DocumentReviewSnapshotV1(EngineeringSourceRefV1 Document,EngineeringSourceRefV1? Previous,
    string ChangeDigest,EngineeringSourceRefV1 ReviewPackage,string SourceKind);
public sealed record DocumentReviewSubmissionV1(Guid SubmissionId, EngineeringSourceRefV1 Document,
    EngineeringSourceRefV1 Policy, Guid StarterId, DateTimeOffset FrozenAt, string SnapshotDigest,
    string State = "SUBMITTED", string TransportState = "PENDING", bool IsApproved = false);
public sealed record DocumentReviewContextV1(Guid TenantId, Guid DocumentId, string Etag,
    ControlledDocumentSnapshotV1 Document, DocumentReviewPolicyViewV1[] Policies, DocumentReviewSubmissionV1[] Submissions);
public sealed record DocumentReviewResultV1(Guid OperationId, Guid DocumentId, string Kind,
    EngineeringSourceRefV1 Reference, DocumentReviewSubmissionV1? Submission = null);
public sealed record DocumentReviewOperationV1(Guid OperationId, string State, DocumentReviewResultV1? Result = null);
