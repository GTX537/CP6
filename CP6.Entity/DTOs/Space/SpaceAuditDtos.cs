namespace CP6.Entity.DTOs.Space;

public sealed record SpaceAuditQueryDto(
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    string? Action = null,
    string? Outcome = null,
    Guid? CorrelationId = null,
    int Page = 1,
    int PageSize = 50);

public sealed record SpaceAuditEventDto(
    Guid EventId,
    Guid TenantId,
    DateTime OccurredAtUtc,
    string ActorType,
    string ActorId,
    string? ActorName,
    string Action,
    string ResourceType,
    string? ResourceId,
    string Outcome,
    string? ReasonCode,
    Guid CorrelationId,
    string TraceId,
    Guid? JobId,
    Guid? RunId,
    Guid? PublishAttemptId,
    int? AttemptNo,
    SpaceAuditEvidenceDto? AuthorizationEvidence);

public sealed record SpaceAuditEvidenceDto(
    string? PermissionCode,
    string? AuthorizationResult,
    int? ItemCount,
    string? Status,
    string? ExceptionType,
    string? ErrorFingerprint);

public sealed record SpaceAuditPageDto(
    IReadOnlyList<SpaceAuditEventDto> Items,
    int Page,
    int PageSize,
    int Total);

public sealed record SpaceAuditMetricsSnapshot(
    long Total,
    IReadOnlyDictionary<string, long> ByOutcome);

public sealed record SpaceAuditTimelineItemDto(
    string Kind,
    Guid TenantId,
    DateTime OccurredAtUtc,
    string Action,
    string ResourceType,
    string? ResourceId,
    string Outcome,
    string? SafeErrorCode,
    Guid CorrelationId,
    string? TraceId,
    Guid? JobId,
    Guid? RunId,
    Guid? PublishAttemptId,
    int? AttemptNo);

public sealed record SpacePublishEventDto(
    Guid Id,
    string HookName,
    string SourceNo,
    string TargetModule,
    string Status,
    int Attempts,
    DateTime CreateDate,
    Guid CorrelationId,
    Guid? JobId,
    Guid? PublishAttemptId,
    string? SafeErrorCode);
