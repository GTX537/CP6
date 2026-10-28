using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Oa;
namespace CP6.Core.Services.Plm;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IterationReviewRequirementV1(string Code,string Description,int RoleId,int MinimumApprovals);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IterationReviewPolicyCommandV1(Guid OperationId,string State,IterationReviewRequirementV1[] Requirements,
    string RejectDisposition,[property:JsonRequired] bool SeparateAuthorAndReviewer,
    [property:JsonRequired] DateTimeOffset EffectiveFrom,[property:JsonRequired] DateTimeOffset EffectiveTo,string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IterationReviewSubmitV1(Guid OperationId,EngineeringSourceRefV1 Iteration,Guid IterationEtag,
    string InputVersion,EngineeringSourceRefV1[] Candidates,EngineeringSourceRefV1 Policy,Guid[] Reviewers,string ReviewBasis,string Reason);
public sealed record IterationReviewAuthorityV1(string Resource,string Action,Guid ActorId,string PermissionVersion,string OrganizationVersion);
public sealed record IterationReviewPolicyV1(Guid EnvironmentId,Guid TenantId,Guid IterationId,Guid Id,Guid ActorId,
    DateTimeOffset RecordedAt,IterationReviewAuthorityV1[] Authorities,IterationReviewPolicyCommandV1 Body);
public sealed record IterationReviewPolicyViewV1(EngineeringSourceRefV1 Reference,IterationReviewPolicyV1 Fact);
public sealed record IterationReviewContractPinV1(string ContractId,string Version,string SchemaSha256);
public sealed record IterationReviewNativeRefV1(IterationReviewContractPinV1 Contract,string CanonicalReferenceBase64,string RawSha256,int ByteLength);
public sealed record IterationReviewMemberV1(string CandidateId,string CandidateVersion,IterationReviewNativeRefV1 TechnicalReference,
    IterationReviewNativeRefV1 OriginalDigest);
public sealed record IterationReviewAggregateV1(string Schema,ApprovalScopeV2 Scope,string IterationId,string RoundId,
    Guid AggregateId,string AggregateVersion,IterationReviewMemberV1[] Members,string AggregateDigest);
public sealed record IterationReviewBindingV1(string Schema,string MappingVersion,ApprovalScopeV2 Scope,
    IterationReviewNativeRefV1 OwnerRegistrationRef,Guid RoundBindingId,string RoundId,string IterationId,
    Guid AggregateId,string AggregateVersion,string AggregateDigest,IterationReviewNativeRefV1 OriginalReviewPackageRef,
    ApprovalDigestV2 ReviewPackageDigest,IterationReviewNativeRefV1 OriginalPolicyRef,string PolicyVersion,
    ApprovalDigestV2 PolicyDigest,ApprovalDigestV2? TechnicalManifestDigest,string RejectDisposition,
    Guid SubmissionId,string ApplicationUnit,string OaRoundKey);
public sealed record IterationReviewSnapshotV1(string Schema,IterationReviewAggregateV1 Aggregate,IterationReviewBindingV1 Binding);
public sealed record IterationReviewPackageV1(string Schema,ApprovalScopeV2 Scope,Guid RoundId,
    EngineeringSourceRefV1 Iteration,string IterationOriginal,EngineeringSourceRefV1 Policy,Guid[] Reviewers,
    string ReviewBasis,IterationCandidateSnapshotV1[] Candidates,JsonElement[] Issues);
public sealed record IterationReviewRoundLinkV1(Guid RoundId,Guid BindingId,Guid SubmissionId,EngineeringSourceRefV1 Package);
public sealed record IterationReviewSubmissionV1(Guid SubmissionId,Guid RoundId,EngineeringSourceRefV1 Iteration,
    EngineeringSourceRefV1 Package,EngineeringSourceRefV1 Policy,string SnapshotDigest,string DispatchState,
    string BusinessStage="IN_REVIEW",string ApplicationState="NOT_RECEIVED");
public sealed record IterationReviewDetailV1(IterationReviewSubmissionV1 Submission,IterationReviewPackageV1 Package,
    IterationReviewSnapshotV1 Snapshot,string OriginalSnapshot,string OriginalSubmission);
public sealed record IterationReviewContextV1(Guid IterationId,string Etag,EngineeringIterationSnapshotV1 Iteration,
    IterationCandidateSnapshotV1[] Candidates,IterationReviewPolicyViewV1[] Policies,IterationReviewSubmissionV1[] Submissions);
public sealed record IterationReviewResultV1(Guid OperationId,Guid IterationId,string Kind,EngineeringSourceRefV1 Reference,
    IterationReviewSubmissionV1? Submission=null);
public sealed record IterationReviewOperationV1(Guid OperationId,string State,IterationReviewResultV1? Result=null);
