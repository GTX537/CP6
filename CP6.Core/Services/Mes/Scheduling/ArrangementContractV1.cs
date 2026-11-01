using System.Text.Json.Serialization;
namespace CP6.Core.Services.Mes.Scheduling;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SchedDueV1([property:JsonConverter(typeof(SchedInstantConverterV1))]DateTimeOffset Value);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SchedDurationV1(SchedRefV1 Reference,string Microseconds);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SchedPlanningOperationV1(Guid OperationId,SchedDurationV1? TechnicalDuration,
    [property:JsonRequired]bool CalendarKnown,[property:JsonRequired]bool MaintenanceKnown,[property:JsonRequired]bool PredecessorsKnown);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SchedPlanningInputV1(string DataKind,SchedRefV1 Reference,Guid RootId,SchedDueV1? DeliveryDate,
    Guid[] PredecessorRootIds,SchedPlanningOperationV1[] Operations);
public sealed record ArrangementRootV1(SchedCaptureV1 Capture,SchedPlanningInputV1? Planning,IReadOnlyDictionary<Guid,string?> LegacyLeadTimes);
public sealed record ArrangementCaptureV1(Guid CaptureId,Guid SiteId,DateTimeOffset AsOf,DateTimeOffset ExpiresAt,
    ArrangementRootV1[] Roots,SchedBusyV1[] Unavailable,string Coverage,string ManifestDigest);
public sealed record SchedBusyV1(SchedRefV1 ResourceRef,SchedIntervalV1? Interval);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ArrangementRequestV1(Guid SiteId,Guid CaptureId,Guid[] OperationIds,
    [property:JsonConverter(typeof(SchedInstantConverterV1))]DateTimeOffset BaseTime,int DefaultDurationMinutes,
    string Algorithm,SchedRefV1[] ResourceScopeRefs,[property:JsonRequired]bool KeepLocked,string Reason);
public sealed record ArrangementItemV1(Guid RootId,Guid OperationId,SchedRefV1 OccurrenceRef,SchedRefV1? ResourceRef,
    SchedIntervalV1? OldInterval,SchedIntervalV1? NewInterval,string DurationBasis,int Rank,string Inclusion,string[] ReasonCodes);
public sealed record ArrangementGroupV1(RootApplyGroupV1 Group,SchedCaptureV1 Capture,SchedRefV1? PlanningInputRef,
    Guid[] PredecessorGroupIds,bool Eligible,string[] ReasonCodes);
public sealed record ArrangementPreviewV1(Guid PreviewId,string Revision,string Digest,DateTimeOffset ExpiresAt,
    ArrangementRequestV1 Input,string AlgorithmVersion,string[] Assumptions,ArrangementItemV1[] Items,int ManifestCount,
    string ManifestDigest,ArrangementGroupV1[] AtomicGroups,string Coverage);
public sealed record ArrangementPageV1(Guid PreviewId,string Revision,string Digest,DateTimeOffset ExpiresAt,
    ArrangementRequestV1 Input,string AlgorithmVersion,string[] Assumptions,ArrangementItemV1[] Items,int ManifestCount,
    string ManifestDigest,ArrangementGroupV1[] AtomicGroups,string Coverage,int? NextCursor);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ArrangementApplyV1(Guid CommandId,Guid PreviewId,string ExpectedPreviewRevision,string ExpectedPreviewDigest,
    Guid[] ExplicitAtomicGroupIds,string ExpectedManifestDigest,string Reason);
public sealed record ArrangementBatchV1(Guid BatchId,Guid ActorId,ArrangementApplyV1 Input,string InputDigest,DateTimeOffset CreatedAt);
public sealed record ArrangementGroupResultV1(Guid GroupId,Guid RootId,string State,string? ErrorCode,RootScheduleGroupResultV1? Result);
public sealed record ArrangementBatchResultV1(Guid BatchId,Guid CommandId,string Outcome,bool Replayed,int SubmittedCount,
    int CommittedCount,int RejectedCount,int UnknownCount,int WaitingCount,int CancelledCount,ArrangementGroupResultV1[] Groups);
