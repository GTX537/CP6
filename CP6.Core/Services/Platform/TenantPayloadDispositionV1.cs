using System.Text.Json.Serialization;
namespace CP6.Core.Services.Platform;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TenantPayloadDispositionPolicyCommandV1(Guid OperationId,Guid PolicyId,Guid TargetTenantId,
    [property:JsonRequired] Guid? ExpectedVersion,Guid BasisRef,[property:JsonRequired] bool Enabled,
    [property:JsonRequired] int MinimumRetentionDays,[property:JsonRequired] bool RequireAccessClosed);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TenantPayloadDispositionTargetV1(Guid ArtifactId,Guid PolicyId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TenantPayloadDispositionRequestV1(Guid OperationId,Guid RequestRef,TenantPayloadDispositionTargetV1 Target,string ExpectedScopeDigest);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TenantPayloadDispositionDecisionV1(Guid OperationId,Guid ExpectedVersion,string Decision,Guid DecisionRef);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TenantPayloadDispositionExecuteV1(Guid OperationId,Guid ExpectedVersion,string ExpectedScopeDigest,Guid ConfirmationRef);
public sealed record TenantPayloadDispositionContextV1(Guid EnvironmentId,Guid ActorTenantId,Guid ActorId,string Owner,string Action,string[] Actions,int MaximumRetentionDays,string[] UncoveredOwners);
public sealed record TenantPayloadDispositionPolicyV1(Guid PolicyId,Guid Version,Guid TargetTenantId,Guid BasisRef,bool Enabled,int MinimumRetentionDays,bool RequireAccessClosed,Guid OwnerId,DateTimeOffset At);
public sealed record TenantPayloadDispositionScopeV1(Guid EnvironmentId,Guid OperatorTenantId,Guid ArtifactId,Guid TargetTenantId,
    Guid? PayloadVersion,long? PayloadLength,string OriginalSha256,long OriginalLength,string Purpose,DateTimeOffset CapturedAt,DateTimeOffset AccessExpiresAt,
    Guid PolicyId,Guid PolicyVersion,Guid BasisRef,int MinimumRetentionDays,bool RequireAccessClosed,DateTimeOffset EarliestDispositionAt,Guid? HoldVersion);
public sealed record TenantPayloadDispositionPreviewV1(TenantPayloadDispositionScopeV1 Scope,string ScopeDigest,TenantPayloadHoldV1 Hold,bool HoldReviewOverdue,string AccessState,string[] Blockers);
public sealed record TenantPayloadDispositionPlanV1(Guid PlanId,Guid RequestRef,Guid RequesterId,DateTimeOffset CreatedAt,Guid Version,string State,
    Guid? ApproverId,Guid? DecisionRef,TenantPayloadDispositionScopeV1 Scope,string ScopeDigest,TenantPayloadHoldV1 CurrentHold,string AccessState,string[] CurrentBlockers,bool ApprovalCurrent,TenantPayloadDispositionExecutionV1? Execution);
public sealed record TenantPayloadDispositionExecutionV1(Guid PlanId,Guid OperationId,Guid ConfirmationRef,string Owner,string OwnerOutcome,string AggregateState,
    Guid EnvironmentId,Guid OperatorTenantId,Guid OperatorId,Guid RequesterId,Guid ApproverId,Guid ArtifactId,Guid TargetTenantId,
    Guid PolicyId,Guid PolicyVersion,Guid BeforeVersion,long RemovedBytes,string ScopeDigest,DateTimeOffset OccurredAt,string[] UncoveredOwners);
public sealed record TenantPayloadDispositionOperationV1(Guid OperationId,string State,string Kind,Guid? PlanId=null,Guid? Version=null,Guid? PolicyId=null,Guid? ArtifactId=null,TenantPayloadDispositionExecutionV1? Execution=null);

public sealed record TenantPayloadHoldV1(Guid? Version,int ActiveCount,int OverdueCount);
