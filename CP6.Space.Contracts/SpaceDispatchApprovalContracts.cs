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
