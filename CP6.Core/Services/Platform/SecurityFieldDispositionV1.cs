using System.Text.Json.Serialization;
namespace CP6.Core.Services.Platform;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SecurityFieldPolicyCommandV1(Guid OperationId,Guid PolicyId,Guid TargetTenantId,[property:JsonRequired] Guid? ExpectedVersion,
    Guid BasisRef,[property:JsonRequired] bool Enabled,[property:JsonRequired] DateTimeOffset NotBeforeUtc);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SecurityFieldTargetV1(Guid LogId,Guid PolicyId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SecurityFieldRequestV1(Guid OperationId,Guid RequestRef,SecurityFieldTargetV1 Target,string ExpectedScopeDigest);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SecurityFieldDecisionV1(Guid OperationId,Guid ExpectedVersion,string Decision,Guid DecisionRef);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SecurityFieldExecuteV1(Guid OperationId,Guid ExpectedVersion,string ExpectedScopeDigest,Guid ConfirmationRef);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SecurityFieldHoldV1(Guid OperationId,[property:JsonRequired] Guid? ExpectedVersion,[property:JsonRequired] bool Active,Guid BasisRef,[property:JsonRequired] DateTimeOffset? ReviewAt);
public sealed record SecurityFieldContextV1(Guid EnvironmentId,Guid ActorTenantId,Guid ActorId,string Owner,string Action,string[] Actions,string[] Fields,string[] UncoveredOwners);
public sealed record SecurityFieldPolicyV1(Guid PolicyId,Guid Version,Guid TargetTenantId,Guid BasisRef,bool Enabled,DateTimeOffset NotBeforeUtc,Guid OwnerId,DateTimeOffset At);
public sealed record SecurityFieldScopeV1(Guid EnvironmentId,Guid OperatorTenantId,Guid LogId,Guid TargetTenantId,Guid SubjectId,int EventType,DateTime OriginalCreatedAt,
    Guid TargetVersion,string BeforeDigest,Guid PolicyId,Guid PolicyVersion,Guid BasisRef,DateTimeOffset NotBeforeUtc,Guid? HoldVersion,string[] Fields,string[] UncoveredOwners);
public sealed record SecurityFieldPreviewV1(SecurityFieldScopeV1 Scope,string ScopeDigest,int ClassifiedValueCount,PrivacyDispositionHoldV1? Hold,bool HoldReviewOverdue,string[] Blockers);
public sealed record SecurityFieldPlanV1(Guid PlanId,Guid RequestRef,Guid RequesterId,DateTimeOffset CreatedAt,Guid Version,string State,Guid? ApproverId,Guid? DecisionRef,
    SecurityFieldScopeV1 Scope,string ScopeDigest,PrivacyDispositionHoldV1? CurrentHold,string[] CurrentBlockers,bool ApprovalCurrent,int? CurrentClassifiedValueCount,SecurityFieldExecutionV1? Execution);
public sealed record SecurityFieldExecutionV1(Guid PlanId,Guid OperationId,Guid ConfirmationRef,string Owner,string Action,string OwnerOutcome,string AggregateState,
    Guid EnvironmentId,Guid OperatorTenantId,Guid OperatorId,Guid RequesterId,Guid ApproverId,Guid LogId,Guid TargetTenantId,Guid SubjectId,
    Guid PolicyId,Guid PolicyVersion,Guid BeforeVersion,Guid AfterVersion,string ScopeDigest,DateTimeOffset OccurredAt,int ClearedValueCount,string[] Fields,string[] UncoveredOwners);
public sealed record SecurityFieldOperationV1(Guid OperationId,string State,string Kind,Guid? PlanId=null,Guid? Version=null,Guid? PolicyId=null,Guid? LogId=null,SecurityFieldExecutionV1? Execution=null);
