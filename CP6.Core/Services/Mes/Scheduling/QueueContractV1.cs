using System.Text.Json.Serialization;
namespace CP6.Core.Services.Mes.Scheduling;
public sealed record QueueMemberV1(Guid ItemId,Guid RootId,Guid OperationId,SchedRefV1 OccurrenceRef,SchedRefV1 AssignmentRef,int RouteSortOrder);
public sealed record QueueHeadV1(Guid QueueId,Guid SiteId,SchedRefV1 ResourceRef,string Revision,QueueMemberV1[] Members);
public sealed record QueueItemV1(QueueMemberV1 Member,int QueueOrdinal,string State,SchedRefV1 SourceScheduleRef,
    SchedIntervalV1? Interval,SchedRefV1[] RoutePredecessorRefs,string ExecutionGateSummary,bool ReassessRequired,SchedRefV1 ObservationRef);
public sealed record QueueCaptureV1(Guid CaptureId,Guid ActorId,QueueHeadV1 Head,QueueItemV1[] Items,Guid[] VisibleRoots,
    DateTimeOffset AsOf,DateTimeOffset ExpiresAt,string ManifestDigest,bool CompleteForWrite,SchedIntervalV1? Window);
public sealed record QueueViewV1(Guid CaptureId,Guid QueueId,Guid SiteId,SchedRefV1 ResourceRef,string QueueRevision,QueueItemV1[] Items,
    string ManifestDigest,int? TotalCount,bool CompleteForWrite,string AuthorizationScope,DateTimeOffset AsOf,DateTimeOffset ExpiresAt,int? NextCursor);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QueuePlanRequestV1(SchedRefV1 ResourceRef,string ExpectedQueueRevision,Guid CaptureId,Guid MoveItemId,
    string Position,Guid? BeforeItemId,Guid? AfterItemId,string ExpectedManifestDigest,string Reason);
public sealed record QueuePlanV1(Guid PlanId,string Revision,string Digest,DateTimeOffset ExpiresAt,QueuePlanRequestV1 Input,
    QueueCaptureV1 Capture,Guid[] OldOrder,Guid[] NewOrder,Guid[] AffectedItems);
public sealed record QueueReplyV1(Guid CommandId,string Outcome,Guid PlanId,SchedRefV1 QueueRef,SchedRefV1 AuditRef,bool Replayed);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QueueExecutionEventV1(string DataKind,Guid RootId,Guid OperationId,SchedRefV1 FactRef,string State,
    SchedIntervalV1? ActualInterval,[property:JsonConverter(typeof(SchedInstantConverterV1))]DateTimeOffset OccurredAt);
public sealed record QueueExecutionResultV1(SchedRefV1 FactRef,string Disposition,bool ReassessRequired);
