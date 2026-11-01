using System.Text.Json.Serialization;
namespace CP6.Core.Services.Mes.Scheduling;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RebindChoiceV1(SchedRefV1 ChoiceRef,SchedRefV1 ResourceRef,SchedRefV1 RequirementRef,SchedRefV1 TechnicalRef,
    SchedIntervalV1 UseInterval,bool Qualified,DateTimeOffset ExpiresAt,string Protocol);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RebindExecutionV1(SchedRefV1 WorkRef,string State);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RebindSimulationV1(string DataKind,Guid RootId,Guid OperationId,SchedRefV1[] OldClaimRefs,
    RebindExecutionV1[] OldExecutions,RebindChoiceV1[] Choices,string[] DeferredPhases);
public sealed record RebindContextV1(Guid ContextId,Guid ActorId,SchedCaptureV1 Capture,Guid OperationId,SchedRefV1 OldBindingRef,
    SchedRefV1[] OldClaims,RebindExecutionV1[] OldExecutions,RebindChoiceV1[] Choices,QueueHeadV1 OldQueue,QueueMemberV1 OldMember,
    SchedRefV1 SourceRef,string DataKind,bool CompleteForRebind);
public sealed record RebindPhaseKeyV1(string Phase,Guid Key);
public sealed record RebindPlanV1(RebindContextV1 Context,RebindChoiceV1 Choice,Guid NewBindingId,Guid NewClaimId,Guid NewQueueItemId,
    string QueuePolicy,RebindPhaseKeyV1[] PhaseKeys);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RebindActionV1(Guid CommandId,string ExpectedWorkRevision,string Reason);
public sealed record RebindStageExpectedV1(WoExpectedV1 Original,WoExpectedV1 Accepted,SchedRefV1? PredecessorReceipt,SchedRefV1[] OwnTransitions,string WorkRevision);
public sealed record RebindPhaseRequestV1(Guid WorkId,string Phase,Guid Key,string PayloadDigest,RebindStageExpectedV1 Expected,SchedPlanV1 Plan);
public sealed record RebindPhaseReceiptV1(string Phase,Guid Key,SchedRefV1 ReceiptRef,WoExpectedV1 Accepted,SchedRefV1? OwnTransition,
    string Outcome,SchedRefV1? ClaimRef,RootScheduleGroupResultV1? BindingResult,string? FailureCode=null);
public sealed record RebindWorkV1(Guid WorkId,Guid ActorId,Guid PlanId,Guid RootId,Guid OperationId,string Revision,string State,
    WoExpectedV1 Accepted,SchedRefV1[] OwnTransitions,RebindPhaseReceiptV1[] Receipts,string? PendingPhase,bool Stopped,
    bool NoNewExecution,RootScheduleGroupResultV1? BindingResult,string QueueMigrationState);
public sealed record RebindReplyV1(Guid CommandId,string Outcome,RebindWorkV1 Work,bool Replayed);
public sealed record RebindClaimV1(SchedRefV1 Reference,Guid WorkId,SchedRefV1 ResourceRef,SchedIntervalV1 UseInterval,string State,Guid? AssignmentId,string Responsibility);
public sealed record RebindMigrationV1(Guid WorkId,Guid RebindKey,Guid RootId,Guid OperationId,Guid SiteId,SchedRefV1 OldResource,
    QueueMemberV1 OldMember,SchedRefV1 NewResource,SchedRefV1 NewAssignment,Guid NewItemId,string State,SchedRefV1[] QueueReceipts);
