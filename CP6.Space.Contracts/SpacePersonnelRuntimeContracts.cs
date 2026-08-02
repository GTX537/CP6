namespace CP6.Space.Contracts;

public sealed record SpacePersonnelCurrentPageDto(
    Guid SiteId,
    DateTimeOffset AsOfUtc,
    int FreshnessThresholdSeconds,
    IReadOnlyList<SpacePersonnelCurrentDto> Items,
    string? NextCursor);

public sealed record SpacePersonnelCurrentDto(
    string SourceId,
    string SourceKind,
    string PersonExternalId,
    string WorkState,
    Guid? FloorLogicalId,
    Guid? LocationLogicalId,
    decimal? XMillimeters,
    decimal? YMillimeters,
    decimal? ZMillimeters,
    decimal? AccuracyMillimeters,
    DateTimeOffset? PositionOccurredAtUtc,
    DateTimeOffset? PositionReceivedAtUtc,
    Guid? PositionEventId,
    string? PositionSourceEventId,
    DateTimeOffset? WorkStateOccurredAtUtc,
    DateTimeOffset? WorkStateReceivedAtUtc,
    Guid? WorkStateEventId,
    string? WorkStateSourceEventId,
    long? PositionAgeMilliseconds,
    long? WorkStateAgeMilliseconds,
    bool HasPosition,
    bool PositionIsStale,
    bool WorkStateIsStale,
    bool IsSimulated);

public sealed record SpacePersonnelTrajectoryResponse(
    Guid SiteId,
    string SourceId,
    string SourceKind,
    string PersonExternalId,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    DateTimeOffset RetentionCutoffUtc,
    IReadOnlyList<SpacePersonnelTrajectoryPointDto> Items,
    string? NextCursor);

public sealed record SpacePersonnelTrajectoryPointDto(
    Guid EventId,
    string SourceEventId,
    Guid? FloorLogicalId,
    Guid? LocationLogicalId,
    decimal? XMillimeters,
    decimal? YMillimeters,
    decimal? ZMillimeters,
    decimal? AccuracyMillimeters,
    long? SourceSequence,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset ReceivedAtUtc,
    long IngestDelayMilliseconds);
