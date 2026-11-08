using System.Text.Json.Serialization;
namespace CP6.Core.Services.Platform;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PayloadDispositionPolicyCommandV1(Guid OperationId,Guid PolicyId,Guid TargetTenantId,
    [property:JsonRequired] Guid? ExpectedVersion,Guid BasisRef,[property:JsonRequired] bool Enabled,
    [property:JsonRequired] int MinimumRetentionDays,[property:JsonRequired] bool RequireAccessClosed);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PayloadDispositionTargetV1(Guid ArtifactId,Guid PolicyId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PayloadDispositionRequestV1(Guid OperationId,Guid RequestRef,PayloadDispositionTargetV1 Target,string ExpectedScopeDigest);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PayloadDispositionDecisionV1(Guid OperationId,Guid ExpectedVersion,string Decision,Guid DecisionRef);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PayloadDispositionExecuteV1(Guid OperationId,Guid ExpectedVersion,string ExpectedScopeDigest,Guid ConfirmationRef);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PayloadDispositionHoldV1(Guid OperationId,[property:JsonRequired] Guid? ExpectedVersion,
    [property:JsonRequired] bool Active,Guid BasisRef,[property:JsonRequired] DateTimeOffset? ReviewAt);
public sealed record PayloadDispositionContextV1(Guid EnvironmentId,Guid ActorTenantId,Guid ActorId,string Owner,string Action,string[] Actions,int MaximumRetentionDays,string[] UncoveredOwners);
public sealed record PayloadDispositionPolicyV1(Guid PolicyId,Guid Version,Guid TargetTenantId,Guid BasisRef,bool Enabled,int MinimumRetentionDays,bool RequireAccessClosed,Guid OwnerId,DateTimeOffset At);
public sealed record PayloadDispositionScopeV1(Guid EnvironmentId,Guid OperatorTenantId,Guid ArtifactId,Guid TargetTenantId,Guid SubjectId,
    Guid? PayloadVersion,long? PayloadLength,string OriginalSha256,long OriginalLength,string Purpose,DateTimeOffset CapturedAt,DateTimeOffset AccessExpiresAt,
    Guid PolicyId,Guid PolicyVersion,Guid BasisRef,int MinimumRetentionDays,bool RequireAccessClosed,DateTimeOffset EarliestDispositionAt,Guid? HoldVersion);
public sealed record PayloadDispositionPreviewV1(PayloadDispositionScopeV1 Scope,string ScopeDigest,PrivacyDispositionHoldV1? Hold,bool HoldReviewOverdue,string AccessState,string[] Blockers);
public sealed record PayloadDispositionPlanV1(Guid PlanId,Guid RequestRef,Guid RequesterId,DateTimeOffset CreatedAt,Guid Version,string State,
    Guid? ApproverId,Guid? DecisionRef,PayloadDispositionScopeV1 Scope,string ScopeDigest,PrivacyDispositionHoldV1? CurrentHold,string AccessState,string[] CurrentBlockers,bool ApprovalCurrent,PayloadDispositionExecutionV1? Execution);
public sealed record PayloadDispositionExecutionV1(Guid PlanId,Guid OperationId,Guid ConfirmationRef,string Owner,string OwnerOutcome,string AggregateState,
    Guid EnvironmentId,Guid OperatorTenantId,Guid OperatorId,Guid RequesterId,Guid ApproverId,Guid ArtifactId,Guid TargetTenantId,Guid SubjectId,
    Guid PolicyId,Guid PolicyVersion,Guid BeforeVersion,long RemovedBytes,string ScopeDigest,DateTimeOffset OccurredAt,string[] UncoveredOwners);
public sealed record PayloadDispositionOperationV1(Guid OperationId,string State,string Kind,Guid? PlanId=null,Guid? Version=null,Guid? PolicyId=null,Guid? ArtifactId=null,PayloadDispositionExecutionV1? Execution=null);
