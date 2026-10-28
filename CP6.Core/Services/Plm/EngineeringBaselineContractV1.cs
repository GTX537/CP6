using System.Text.Json.Serialization;
using CP6.Core.Services.Oa;
namespace CP6.Core.Services.Plm;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringFreezeCommandV1(Guid OperationId,Guid IterationId,EngineeringSourceRefV1 SourceCandidate,
    Guid ReviewSubmissionId,Guid ReviewRoundId,Guid ApplicationReceiptId,EngineeringRevisionRefV1 Revision,
    EngineeringRevisionRefV1 EngineeringCandidate,Guid ExpectedEtag,EngineeringRevisionRefV1 Configuration,
    EngineeringSourceRefV1 SourceCheck,string TechnicalManifestDigest,string ReviewPackageDigest,string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringFreezeCancelV1(Guid OperationId,Guid TargetOperationId,string ExpectedState,string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringBaselineSuccessorV1(Guid OperationId,EngineeringSourceRefV1 Baseline,
    EngineeringRevisionRefV1 ExpectedRevision,Guid ExpectedEtag,string Reason);
public sealed record EngineeringFreezeReviewV1(EngineeringSourceRefV1 Iteration,Guid RoundId,Guid SubmissionId,
    EngineeringSourceRefV1 Package,string PackageOriginal,string SnapshotOriginal,string SubmissionOriginal,
    IterationReviewPolicyViewV1 Policy,IterationApprovalV1 Application,ApprovalApplicationReceiptV2 Receipt,
    ApprovalDecisionEnvelopeV2 Decision,string DecisionOriginalHash,string DecisionEvidenceOriginal,
    string ApplicationOriginal,string ReceiptOriginal,string DecisionOriginal,string ControlVersion,
    IterationCandidateSnapshotV1 Candidate,IterationReviewIssueV1[] Issues);
public sealed record EngineeringBaselineOriginalV1(string Schema,Guid EnvironmentId,Guid TenantId,Guid Id,string Version,
    Guid ActorId,DateTimeOffset FrozenAt,string EvidenceMode,EngineeringFreezeCommandV1 Command,
    EngineeringRevisionSnapshotV1 Revision,EngineeringCheckRunV1 Check,string CheckOriginal,
    EngineeringFreezeReviewV1 Review,EngineeringCheckManifestV1 Manifest,
    EngineeringSourceRefV1? OutputRelations,string? OutputRelationsOriginal,string OutputApplicability);
public sealed record EngineeringBaselineBindingV1(string Schema,OutputRelationsScopeV1 Scope,
    IterationReviewNativeRefV1 BaselineRef,IterationReviewNativeRefV1 OriginalManifestRef,
    IterationReviewNativeRefV1 OriginalManifestDigest,Guid RelationId,string RelationVersion,string RelationBodyDigest,
    Guid FreezeCommandId,IterationReviewNativeRefV1[] ConfigurationEvidence,string BodyDigest);
public sealed record EngineeringBaselineViewV1(EngineeringSourceRefV1 Reference,EngineeringBaselineOriginalV1 Original,
    string OriginalJson,EngineeringBaselineBindingV1? OutputBinding,string? OutputBindingOriginal,
    string SourceStatus,string[] Changes,string State="FROZEN",string Release="NOT_REQUESTED",string Verification="NOT_REQUESTED");
public sealed record EngineeringFreezeResultV1(Guid OperationId,EngineeringSourceRefV1 Baseline,Guid OriginalOperationId,
    string State,string TechnicalManifestDigest,string? OutputBindingDigest,Guid ResultIntentId);
public sealed record EngineeringFreezeOperationV1(Guid OperationId,string State,EngineeringFreezeResultV1? Result=null);
public sealed record EngineeringFreezeCancellationV1(Guid OperationId,Guid TargetOperationId,EngineeringFreezeOperationV1 Outcome);
public sealed record EngineeringBaselineSuccessorResultV1(Guid OperationId,EngineeringSourceRefV1 ParentBaseline,
    EngineeringRevisionRefV1 Revision,EngineeringRevisionRefV1 Candidate,Guid Etag);
public sealed record EngineeringBaselineSuccessorOperationV1(Guid OperationId,string State,EngineeringBaselineSuccessorResultV1? Result=null);
public sealed record EngineeringBaselineUsageV1(string Owner,string Status,EngineeringSourceRefV1[] References,string Reason);
public sealed record EngineeringBaselineDiffV1(EngineeringSourceRefV1 Before,EngineeringSourceRefV1 After,EngineeringWorkingChangeV1[] Changes);
public sealed record EngineeringFreezeChoiceV1(Guid IterationId,IterationReviewSubmissionV1 Submission,
    IterationApplicationContextV1 Application,IterationCandidateSnapshotV1[] Candidates);
