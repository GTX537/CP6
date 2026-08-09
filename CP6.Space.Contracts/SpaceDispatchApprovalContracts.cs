namespace CP6.Space.Contracts;

public sealed record SubmitSpaceDispatchApprovalRequest(
    IReadOnlyList<int> SelectedRanks,
    string Reason);

public sealed record SpaceDispatchApprovalSelectionDto(
    int Rank,
    string TaskId,
    string TaskType,
    string PersonSourceId,
    string PersonExternalId,
    string TargetLocationCode);

public sealed record SpaceDispatchTaskAdaptationReceiptDto(
    int Rank,
    string TaskId,
    string PersonExternalId,
    Guid OperationId,
    string Outcome);

public sealed record SpaceDispatchApprovalRequestDto(
    Guid ApprovalRequestId,
    Guid SiteId,
    Guid RecommendationId,
    Guid PublishedVersionId,
    string WarehouseCode,
    string RecommendationDefinitionVersion,
    string Status,
    string Reason,
    Guid RequestedBy,
    DateTimeOffset RequestedAtUtc,
    Guid FlowInstanceId,
    Guid? DecidedBy,
    DateTimeOffset? DecidedAtUtc,
    DateTimeOffset? AppliedAtUtc,
    string AdapterId,
    int SelectedCount,
    IReadOnlyList<SpaceDispatchApprovalSelectionDto> Selections,
    IReadOnlyList<SpaceDispatchTaskAdaptationReceiptDto> Receipts,
    string? FailureCode);

public sealed record SubmitSpaceDispatchApprovalResponse(
    string Outcome,
    SpaceDispatchApprovalRequestDto ApprovalRequest);

public sealed record SubmitSpaceDispatchExecutionActionRequest(string Reason);

public sealed record SpaceDispatchExecutionTaskDto(
    int Rank,
    string TaskId,
    string PersonSourceId,
    string PersonExternalId,
    Guid AssignmentOperationId,
    int WmsStatus,
    string State,
    int ExecutionVersion,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? DoneAtUtc,
    string? LastEventType,
    DateTimeOffset? LastEventAtUtc);

public sealed record SpaceDispatchExecutionActionDto(
    Guid ActionId,
    string ActionType,
    string Status,
    string Reason,
    Guid RequestedBy,
    DateTimeOffset RequestedAtUtc,
    string AdapterId,
    IReadOnlyList<SpaceDispatchTaskAdaptationReceiptDto> Receipts,
    string? FailureCode);

public sealed record SpaceDispatchExecutionDto(
    Guid ApprovalRequestId,
    Guid SiteId,
    Guid RecommendationId,
    string ApprovalStatus,
    string Status,
    DateTimeOffset ObservedAtUtc,
    int TotalCount,
    int AssignedCount,
    int ExecutingCount,
    int CompletedCount,
    int AttentionCount,
    bool CanRetry,
    int RetryAttemptCount,
    int RetryAttemptsRemaining,
    bool CanCompensate,
    string? CompensationBlockCode,
    DateTimeOffset? CompensatedAtUtc,
    IReadOnlyList<SpaceDispatchExecutionTaskDto> Tasks,
    IReadOnlyList<SpaceDispatchExecutionActionDto> Actions);

public sealed record SpaceDispatchExecutionActionResponse(
    string Outcome,
    SpaceDispatchExecutionActionDto Action,
    SpaceDispatchExecutionDto Execution);
