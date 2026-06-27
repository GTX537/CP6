using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Oa;

// ── 列表项 ──
public record InboxPendingItem(Guid TaskId, Guid InstanceId, Guid? TokenId, string FlowKey, string? FlowName,
    string NodeId, string? NodeName, Guid StarterId, string StarterName, string? BizType, string? BizId,
    bool IsRead, DateTime SentAt);

public record InboxCcItem(Guid CcId, Guid InstanceId, string FlowKey, string? FlowName, string? AtNodeId,
    Guid StarterId, string StarterName, bool IsRead, DateTime CreateDate);

public record InboxRunningItem(Guid InstanceId, string FlowKey, string? FlowName, string CurrentNode,
    int Status, IReadOnlyList<string> CurrentHandlers, DateTime CreateDate);

public record InboxDoneItem(Guid InstanceId, string FlowKey, string? FlowName, Guid StarterId, string StarterName,
    int FormToStatus, DateTime DoneAt, int InstanceStatus);

// ── 仪表盘 ──
public record TrendPoint(string Date, int Count);
public record InboxStats(int PendingCount, int RunningCount, int DoneThisMonth, int RejectedBackToMe,
    IReadOnlyList<TrendPoint> Trend, IReadOnlyList<InboxPendingItem> RecentPending);

// ── 批量 ──
public record BatchActResultItem(Guid TaskId, bool Ok, string? Error);

// ── 详情（左读右签）──
public record TimelineRow(int StepSeq, Guid? TokenId, string NodeId, string? NodeName,
    Guid ExpectedHandlerId, string ExpectedHandlerName, Guid? ActualHandlerId, string? ActualHandlerName,
    Guid? OnBehalfOfId, string? OnBehalfOfName, int Status, string? Comment, DateTime SentAt, DateTime? HandledAt);

public record SnapshotRow(int StepSeq, string NodeId, string DataJson);
public record CcRow(Guid RecipientId, string RecipientName, string? AtNodeId, bool IsRead);

public record InboxDetail(Wf_FlowInstance Instance, string? FlowName, string? FormKey, string? FormSchemaJson,
    string CurrentDataJson, IReadOnlyList<TimelineRow> Timeline, IReadOnlyList<SnapshotRow> Snapshots,
    IReadOnlyList<ForecastStep> Forecast, IReadOnlyList<CcRow> Cc);

// ── 预计流程（ForecastService 产出，置此便于 InboxDetail 引用）──
public record ForecastStep(string NodeId, string? NodeName, string Type, IReadOnlyList<string> Approvers,
    bool Resolved, string? Note);
public record ForecastResult(IReadOnlyList<ForecastStep> Steps, bool Branched);
