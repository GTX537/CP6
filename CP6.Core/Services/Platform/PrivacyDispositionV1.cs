using System.Text.Json.Serialization;
namespace CP6.Core.Services.Platform;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PrivacyDispositionPolicyCommandV1(Guid OperationId,Guid PolicyId,Guid TargetTenantId,
    [property:JsonRequired] Guid? ExpectedVersion,Guid BasisRef,[property:JsonRequired] bool Enabled);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PrivacyDispositionTargetV1(Guid TargetTenantId,Guid SubjectId,Guid PolicyId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PrivacyDispositionPlanCommandV1(Guid OperationId,Guid RequestRef,PrivacyDispositionTargetV1 Target,string ExpectedScopeDigest);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PrivacyDispositionDecisionV1(Guid OperationId,Guid ExpectedVersion,string Decision,Guid DecisionRef);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PrivacyDispositionExecuteV1(Guid OperationId,Guid ExpectedVersion,string ExpectedScopeDigest,Guid ConfirmationRef);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PrivacyDispositionHoldCommandV1(Guid OperationId,Guid TargetTenantId,Guid SubjectId,
    [property:JsonRequired] Guid? ExpectedVersion,[property:JsonRequired] bool Active,Guid BasisRef,[property:JsonRequired] DateTimeOffset? ReviewAt);
public sealed record PrivacyDispositionPolicyV1(Guid PolicyId,Guid Version,Guid TargetTenantId,Guid BasisRef,bool Enabled,Guid OwnerId,DateTimeOffset At);
public sealed record PrivacyDispositionHoldV1(Guid Version,bool Active,Guid BasisRef,DateTimeOffset? ReviewAt,Guid OwnerId,Guid OwnerTenantId,DateTimeOffset At);
public sealed record PrivacyDispositionScopeV1(Guid EnvironmentId,Guid TargetTenantId,Guid SubjectId,Guid TargetVersion,
    Guid PolicyId,Guid PolicyVersion,Guid BasisRef,Guid? HoldVersion,string Action,string[] Fields,string[] Effects,string[] UncoveredOwners);
public sealed record PrivacyDispositionPreviewV1(PrivacyDispositionScopeV1 Scope,string ScopeDigest,PrivacyDispositionHoldV1? Hold,bool HoldReviewOverdue,string[] Blockers);
public sealed record PrivacyDispositionPlanV1(Guid PlanId,Guid RequestRef,Guid RequesterId,DateTimeOffset CreatedAt,
    Guid Version,string State,Guid? ApproverId,Guid? DecisionRef,PrivacyDispositionScopeV1 Scope,string ScopeDigest,
    PrivacyDispositionHoldV1? CurrentHold,bool HoldReviewOverdue,string[] CurrentBlockers,bool ApprovalCurrent,PrivacyDispositionExecutionV1? Execution=null);
public sealed record PrivacyDispositionExecutionV1(Guid PlanId,Guid OperationId,Guid EnvironmentId,Guid OperatorTenantId,Guid OperatorId,
    Guid RequesterId,Guid ApproverId,Guid ConfirmationRef,Guid TargetTenantId,Guid SubjectId,string Owner,string Action,
    Guid PolicyVersion,Guid BeforeVersion,Guid AfterVersion,DateTimeOffset OccurredAt,string OwnerOutcome,string State,string[] Fields,string[] UncoveredOwners);
public sealed record PrivacyDispositionOperationV1(Guid OperationId,string State,string Kind,Guid? PlanId,Guid? Version,
    Guid? PolicyId=null,Guid? TargetTenantId=null,Guid? SubjectId=null,PrivacyDispositionExecutionV1? Execution=null);
public sealed record PrivacyDispositionContextV1(Guid EnvironmentId,Guid ActorTenantId,Guid ActorId,string[] Actions,
    string Action,string[] Fields,string[] Effects,string[] UncoveredOwners);
public sealed class PrivacyDispositionExceptionV1(string code,int status=409,Exception? inner=null):InvalidOperationException(code,inner)
{public int Status{get;}=status;}
