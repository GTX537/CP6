namespace CP6.Space.Contracts;

public sealed record CreateSpacePublishAttemptRequest(
    Guid? ExpectedPublishedVersionId,
    Guid ValidationRunId,
    string PlanHash,
    string? ApprovalReference);

public sealed record SpacePublishReceiptDto(
    Guid LogicalId,
    string LocationCode,
    string Action,
    string Outcome,
    string? ExternalLocationId,
    string? ExternalVersion,
    string? ResponseHash,
    string? ErrorCode,
    DateTime ReceivedAtUtc);

public sealed record SpacePublishBatchDto(
    Guid Id,
    int BatchNo,
    string OperationKey,
    string PayloadHash,
    string Status,
    int AttemptCount,
    string? ExternalOperationId,
    DateTime? ObservedAtUtc,
    IReadOnlyList<SpacePublishReceiptDto> Receipts);

public sealed record SpacePublishAttemptDto(
    Guid Id,
    Guid SiteId,
    Guid PublishPlanId,
    Guid TargetVersionId,
    Guid? BaseVersionId,
    string AdapterId,
    string PlanHash,
    string Status,
    string CurrentStep,
    DateTime StartedAtUtc,
    DateTime? FinishedAtUtc,
    Guid RequestedBy,
    Guid? ApprovedBy,
    string? ApprovalReference,
    DateTime? WmsCommittedAtUtc,
    DateTime? RuntimeActivatedAtUtc,
    string? LastErrorCode,
    string? Summary,
    Guid CorrelationId,
    int OpenReconciliationIssueCount,
    IReadOnlyList<SpacePublishBatchDto> Batches);

public sealed record CreateSpacePublishAttemptResponse(
    SpacePublishAttemptDto Attempt,
    bool IdempotentReplay);
