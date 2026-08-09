namespace CP6.Space.Contracts;

public sealed record CreateSpacePlanningHistoricalTaskRequest(
    string TaskToken,
    string? WorkerToken,
    string TaskType,
    string Outcome,
    DateTimeOffset OriginalCreatedAtUtc,
    DateTimeOffset OriginalCompletedAtUtc,
    Guid? FromLocationLogicalId,
    Guid ToLocationLogicalId,
    decimal Quantity);

public sealed record CreateSpacePlanningHistoricalDatasetRequest(
    string Name,
    DateTimeOffset HistoricalFromUtc,
    DateTimeOffset HistoricalToUtc,
    DateTimeOffset ReplayStartUtc,
    decimal ReplaySpeedFactor,
    string SourceDatasetHash,
    bool ConfirmDeidentified,
    IReadOnlyList<CreateSpacePlanningHistoricalTaskRequest> Tasks);

public sealed record SpacePlanningReplayClockDto(
    DateTimeOffset HistoricalFromUtc,
    DateTimeOffset HistoricalToUtc,
    DateTimeOffset ReplayStartUtc,
    DateTimeOffset ReplayEndUtc,
    decimal ReplaySpeedFactor,
    decimal HistoricalDurationSeconds,
    decimal ReplayDurationSeconds);

public sealed record SpacePlanningHistoricalTaskDto(
    int SequenceNo,
    string TaskToken,
    string? WorkerToken,
    string TaskType,
    string Outcome,
    DateTimeOffset OriginalCreatedAtUtc,
    DateTimeOffset OriginalCompletedAtUtc,
    DateTimeOffset ReplayCreatedAtUtc,
    DateTimeOffset ReplayCompletedAtUtc,
    Guid? FromLocationLogicalId,
    Guid ToLocationLogicalId,
    decimal Quantity);

public sealed record SpacePlanningHistoricalDatasetDto(
    Guid DatasetId,
    Guid BranchId,
    Guid SiteId,
    Guid ScenarioVersionId,
    string Name,
    int TaskCount,
    string SourceDatasetHash,
    string DefinitionVersion,
    string DeidentificationVersion,
    bool Deidentified,
    bool ProductionWriteAllowed,
    SpacePlanningReplayClockDto ReplayClock,
    IReadOnlyList<SpacePlanningHistoricalTaskDto> Tasks,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedBy,
    IReadOnlyList<string> Limitations);

public sealed record SpacePlanningHistoricalDatasetSummaryDto(
    Guid DatasetId,
    Guid BranchId,
    Guid ScenarioVersionId,
    string Name,
    int TaskCount,
    DateTimeOffset HistoricalFromUtc,
    DateTimeOffset HistoricalToUtc,
    DateTimeOffset ReplayStartUtc,
    DateTimeOffset ReplayEndUtc,
    decimal ReplaySpeedFactor,
    DateTimeOffset CreatedAtUtc);

public sealed record CreateSpacePlanningHistoricalDatasetResponse(
    string Outcome,
    SpacePlanningHistoricalDatasetDto Dataset);

public sealed record SpacePlanningHistoricalDatasetListResponse(
    IReadOnlyList<SpacePlanningHistoricalDatasetSummaryDto> Items,
    bool IsTruncated);
