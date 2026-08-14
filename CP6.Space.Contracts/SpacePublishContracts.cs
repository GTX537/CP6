namespace CP6.Space.Contracts;

public sealed record CreateSpacePublishAttemptRequest(
    Guid? ExpectedPublishedVersionId,
    Guid ValidationRunId,
    string PlanHash,
    string? ApprovalReference,
    string? WarningAcknowledgementHash = null);

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
    int BatchAttemptNo,
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
    Guid? JobId,
    string JobType,
    string JobStatus,
    int JobAttemptCount,
    int JobMaxAttempts,
    DateTime? NextAttemptAtUtc,
    DateTime? LockExpiresAtUtc,
    int ManualRetryCount,
    DateTime? LastRetriedAtUtc,
    Guid? LastRetriedBy,
    int OpenReconciliationIssueCount,
    IReadOnlyList<SpacePublishBatchDto> Batches,
    IReadOnlyList<SpacePublishAuditEventDto> AuditEvents);

public sealed record SpacePublishAttemptSummaryDto(
    Guid Id,
    Guid SiteId,
    Guid TargetVersionId,
    string TargetVersionNo,
    string TargetVersionName,
    Guid? BaseVersionId,
    string Status,
    string CurrentStep,
    DateTime StartedAtUtc,
    DateTime? FinishedAtUtc,
    string? ApprovalReference,
    string? LastErrorCode,
    string? Summary,
    Guid? JobId,
    string JobStatus,
    int JobAttemptCount,
    int JobMaxAttempts,
    DateTime? NextAttemptAtUtc,
    int OpenReconciliationIssueCount,
    Guid? HistoricalRepublishId,
    Guid? HistoricalVersionId);

public sealed record SpacePublishAuditEventDto(
    Guid Id,
    int EventNo,
    string EventType,
    string AttemptStatus,
    string Step,
    Guid JobId,
    Guid? BatchId,
    Guid ActorId,
    Guid CorrelationId,
    DateTime OccurredAtUtc,
    string Summary,
    string? ErrorCode,
    string EvidenceHash,
    string? PreviousEventHash,
    string EventHash);

public sealed record CreateSpacePublishAttemptResponse(
    SpacePublishAttemptDto Attempt,
    bool IdempotentReplay);

public sealed record RetrySpacePublishAttemptRequest(
    string Reason,
    string? Resolution);

public sealed record RetrySpacePublishAttemptResponse(
    SpacePublishAttemptDto Attempt,
    bool IdempotentReplay);

public sealed record StartSpaceHistoricalRepublishRequest(
    Guid ExpectedPublishedVersionId,
    string Reason,
    string? ApprovalReference,
    string? NewVersionName);

public sealed record SpaceHistoricalRepublishDto(
    Guid Id,
    Guid SiteId,
    Guid HistoricalVersionId,
    Guid ExpectedPublishedVersionId,
    Guid TargetVersionId,
    string TargetVersionNo,
    string TargetVersionStatus,
    string Status,
    string Reason,
    string? ApprovalReference,
    Guid RequestedBy,
    DateTime RequestedAtUtc,
    Guid CorrelationId,
    Guid JobId,
    string JobStatus,
    Guid? ValidationRunId,
    Guid? PublishAttemptId,
    string? PublishAttemptStatus);

public sealed record StartSpaceHistoricalRepublishResponse(
    SpaceHistoricalRepublishDto Republish,
    bool IdempotentReplay);
