using System.Text.Json.Serialization;
namespace CP6.Core.Services.Mes.Scheduling;

public sealed class SchedulingSimulationOptionsV1 { public bool Enabled { get; set; } }
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SchedRefV1(string Owner,Guid Id,string Version,string Digest);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WoExpectedV1(string Revision,string ControlEpoch,string EvidenceVersion,string BindingRegistryRevision,Guid SnapshotId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SchedIntervalV1([property:JsonConverter(typeof(SchedInstantConverterV1))]DateTimeOffset Start,
    [property:JsonConverter(typeof(SchedInstantConverterV1))]DateTimeOffset End);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RootScheduleV1(SchedIntervalV1? Interval,string Priority,string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SchedQuantityV1(string Value,SchedRefV1 UnitRef,SchedRefV1 BasisRef);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SchedOperationV1(Guid OperationId,SchedRefV1 OccurrenceRef,int RouteSortOrder,string ProcessLabel,
    string ScheduleRevision,SchedIntervalV1? Interval,SchedRefV1? AssignmentRef,SchedRefV1? ResourceRef,
    SchedQuantityV1? PlanQty,SchedQuantityV1? GoodQty,SchedQuantityV1? DefectQty);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SchedGuardV1(Guid OperationId,SchedRefV1 SourceWindowRef,SchedIntervalV1 SourceWindow,
    SchedRefV1 ClaimRef,SchedIntervalV1 UseInterval,[property:JsonRequired]bool ClaimValid,
    [property:JsonRequired]bool PurposeAllowed,string ExecutionState,SchedIntervalV1? ActualInterval,
    SchedRefV1[] PredecessorOccurrences,SchedIntervalV1[] UnavailableIntervals);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SchedObservationV1(string DataKind,SchedRefV1 Reference,Guid RootId,SchedGuardV1[] Guards);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SchedOpeningV1(string DataKind,Guid RootId,SchedRefV1 SiteRef,string ZoneId,SchedRefV1 SnapshotRef,
    WoExpectedV1 Expected,RootScheduleV1 RootSchedule,SchedOperationV1[] Operations,SchedObservationV1 Observation);
public sealed record SchedRootV1(Guid RootId,string WorkOrderNo,SchedRefV1 SiteRef,string ZoneId,SchedRefV1 SnapshotRef,
    WoExpectedV1 Expected,RootScheduleV1 Schedule,SchedOperationV1[] Operations,SchedRefV1[] QueueHeads);
public sealed record SchedCaptureV1(Guid CaptureId,DateTimeOffset AsOf,DateTimeOffset ExpiresAt,string DataKind,
    SchedRootV1 Root,SchedObservationV1 Observation,SchedRefV1 OperationSetRef,string RootPolicy,bool CompleteForRoot);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SchedChangeRequestV1(Guid CaptureId,Guid OperationId,string ExpectedScheduleRevision,WoExpectedV1 WoExpected,
    string Kind,[property:JsonConverter(typeof(SchedInstantConverterV1))]DateTimeOffset ProposedStart,
    [property:JsonConverter(typeof(SchedInstantConverterV1))]DateTimeOffset ProposedEnd,string? ProposedPriority,SchedRefV1? ResourceChoiceRef,string Reason);
public sealed record SchedOperationChangeV1(Guid OperationId,SchedRefV1 OccurrenceRef,string ExpectedScheduleRevision,
    SchedIntervalV1? OldInterval,SchedIntervalV1 NewInterval,SchedRefV1? AssignmentRef,SchedRefV1 SourceWindowRef);
public sealed record RootApplyGroupV1(Guid GroupId,Guid RootWoId,WoExpectedV1 OriginalWoExpected,RootScheduleV1 OldRootSchedule,
    RootScheduleV1 NewRootSchedule,string RootPolicy,SchedRefV1 OldOperationSetRef,SchedRefV1 NewOperationManifestRef,
    SchedOperationChangeV1[] ChangedOperations,SchedRefV1[] UnchangedOperationRefs,SchedRefV1[] ExpectedQueueHeads,string GroupDigest,Guid ApplyKey);
public sealed record SchedGateV1(string Code,Guid OperationId,string State);
public sealed record SchedPlanV1(Guid PlanId,string Revision,string Digest,DateTimeOffset ExpiresAt,SchedChangeRequestV1 Input,
    SchedCaptureV1 Capture,RootApplyGroupV1 RootApplyGroup,SchedGateV1[] Gates,bool Eligible,RebindPlanV1? Rebind=null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SchedApplyV1(Guid CommandId,Guid PlanId,string ExpectedPlanRevision,string ExpectedPlanDigest,string Reason);
public sealed record SchedOperationResultV1(Guid OperationId,string OldScheduleRevision,string NewScheduleRevision,
    SchedRefV1 ScheduleRef,SchedIntervalV1 AcceptedInterval);
public sealed record RootScheduleGroupResultV1(SchedRefV1 ApplicationRef,Guid ApplyKey,string GroupDigest,SchedRefV1 RootWoRef,
    WoExpectedV1 OldWoExpected,WoExpectedV1 NewWoExpected,RootScheduleV1 OldRootSchedule,RootScheduleV1 NewRootSchedule,
    SchedOperationResultV1[] OperationResults,SchedRefV1 UnchangedOperationSetRef,SchedRefV1[] QueueResults,SchedRefV1 OwnTransitionRef,SchedRefV1 AuditRef);
public sealed record SchedReplyV1(Guid CommandId,string Outcome,Guid WorkId,RootScheduleGroupResultV1 Result,bool Replayed);
