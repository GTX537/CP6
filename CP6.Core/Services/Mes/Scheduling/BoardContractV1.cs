using System.Text.Json.Serialization;
namespace CP6.Core.Services.Mes.Scheduling;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BoardOperationSourceV1(Guid OperationId,SchedRefV1? ProcessRef,SchedRefV1? WorkgroupRef,SchedRefV1? LegacyResourceRef);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BoardSourceV1(string DataKind,SchedRefV1 Reference,Guid RootId,SchedRefV1 SiteRef,string ZoneId,
    string WorkOrderState,string? DeliveryInstant,BoardOperationSourceV1[] Operations);
public sealed record BoardQueryV1(Guid SiteId,SchedIntervalV1 Window,string ZoneId,SchedRefV1? ProcessRef,SchedRefV1? WorkgroupRef,
    SchedRefV1? ResourceRef,string[] Statuses,string Sort="START_ASC");
public sealed record BoardSiteV1(SchedRefV1 SiteRef,string ZoneId,SchedIntervalV1 DefaultWindow,SchedRefV1[] Processes,SchedRefV1[] Workgroups,SchedRefV1[] Resources);
public sealed record BoardContextV1(Guid TenantId,Guid EnvironmentId,Guid ActorId,BoardSiteV1[] Sites)
{public string[] ActorCapabilities {get;init;}=[];}
public sealed record BoardConflictV1(Guid? OperationId,SchedIntervalV1 Interval,string Coverage);
public sealed record BoardBarV1(Guid OperationId,Guid WoId,string WorkOrderNo,string ProductLabel,string ProcessLabel,int RouteSortOrder,
    SchedRefV1? OccurrenceRef,SchedRefV1? SnapshotRef,WoExpectedV1? WoExpected,string? ScheduleRevision,SchedRefV1[] QueueHeads,
    SchedRefV1? ProcessRef,SchedRefV1? WorkgroupRef,SchedRefV1? ResourceRef,SchedRefV1? ResourceAssignmentRef,
    SchedIntervalV1? PlannedInterval,SchedIntervalV1? ActualInterval,string ActualCoverage,DateTimeOffset? DeliveryDate,
    SchedQuantityV1? PlanQty,SchedQuantityV1? GoodQty,SchedQuantityV1? DefectQty,string State,string WorkOrderState,string Priority,
    string Coverage,string Mode,bool Delayed,BoardConflictV1[] Conflicts)
{
    public string[] Capabilities {get;init;}=[];
    public SchedGateV1[] Gates {get;init;}=[];
    public IReadOnlyDictionary<string,string> OwnerLinks {get;init;}=new Dictionary<string,string>();
}
public sealed record BoardMetricV1(string? Value,string Unit,string Coverage);
public sealed record BoardKpiV1(BoardMetricV1 TotalOrders,BoardMetricV1 InProgressOrders,BoardMetricV1 CompletedOrders,
    BoardMetricV1 DelayedOrders,BoardMetricV1 CompletionRate,BoardMetricV1 AvgDelayDays,BoardMetricV1 Denominator,
    BoardMetricV1 DoneCount,BoardMetricV1 CancelledCount,BoardMetricV1 UnknownCount,string FormulaVersion);
internal sealed record BoardCaptureV1(Guid CaptureId,Guid ActorId,BoardQueryV1 Query,string QueryScopeHash,DateTimeOffset AsOf,
    DateTimeOffset ExpiresAt,Guid[] VisibleRoots,BoardBarV1[] Bars,BoardKpiV1 Kpi,string Coverage,string[] Warnings);
public sealed record BoardPageV1(Guid CaptureId,string QueryScopeHash,DateTimeOffset AsOf,DateTimeOffset ExpiresAt,string ZoneId,
    BoardBarV1[] Bars,int? NextCursor,BoardKpiV1 Kpi,string Coverage,string[] Warnings,string DataKind="SIMULATED_UPSTREAM");
public sealed record BoardDetailV1(BoardBarV1 Operation,SchedRootV1? RootScheduleContext,bool CompleteForRoot,string ZoneId,DateTimeOffset AsOf);
