namespace CP6.Space.Contracts;

public sealed record CreateSpacePlanningComparisonRequest(
    string Name,
    Guid BaselineRunId,
    IReadOnlyList<Guid> RunIds,
    decimal MinimumDistanceCoveragePercent,
    decimal MaximumPeakCapacityUtilizationPercent,
    decimal MaximumCongestionTaskHours,
    decimal? MaximumTotalCost);

public sealed record SpacePlanningComparisonThresholdsDto(
    decimal MinimumDistanceCoveragePercent,
    decimal MaximumPeakCapacityUtilizationPercent,
    decimal MaximumCongestionTaskHours,
    decimal? MaximumTotalCost);

public sealed record SpacePlanningComparisonMetricsDto(
    decimal DistanceCoveragePercent,
    decimal TotalDistanceMeters,
    long CongestionTaskSeconds,
    decimal CongestionTaskHours,
    int OverloadedLocationCount,
    decimal PeakCapacityUtilizationPercent,
    decimal AverageCompletedTasksPerHour,
    decimal PeakCompletedTasksPerHour,
    decimal TotalCost);

public sealed record SpacePlanningComparisonDeltaDto(
    decimal DistanceMeters,
    long CongestionTaskSeconds,
    int OverloadedLocationCount,
    decimal PeakCapacityUtilizationPercentagePoints,
    decimal AverageCompletedTasksPerHour,
    decimal TotalCost);

public sealed record SpacePlanningComparisonRiskDto(
    string Code,
    string Severity);

public sealed record SpacePlanningComparisonEntryDto(
    int SequenceNo,
    Guid RunId,
    Guid BranchId,
    Guid ScenarioVersionId,
    long ScenarioContentRevision,
    string RunName,
    string RunResultHash,
    bool IsBaseline,
    SpacePlanningComparisonMetricsDto Metrics,
    SpacePlanningComparisonDeltaDto DeltaFromBaseline,
    IReadOnlyList<SpacePlanningComparisonRiskDto> Risks);

public sealed record SpacePlanningComparisonDto(
    Guid ComparisonId,
    Guid SiteId,
    Guid ModelId,
    Guid BasePublishedVersionId,
    Guid BaselineRunId,
    string Name,
    string Status,
    string DefinitionVersion,
    string RequestHash,
    string ComparisonHash,
    string SourceDatasetHash,
    string CurrencyCode,
    DateTimeOffset HistoricalFromUtc,
    DateTimeOffset HistoricalToUtc,
    SpacePlanningComparisonThresholdsDto Thresholds,
    IReadOnlyList<SpacePlanningComparisonEntryDto> Entries,
    bool AutomatedRanking,
    bool ProductionWriteAllowed,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedBy,
    IReadOnlyList<string> Limitations);

public sealed record SpacePlanningComparisonSummaryDto(
    Guid ComparisonId,
    Guid BaselineRunId,
    string Name,
    string CurrencyCode,
    int RunCount,
    int RiskCount,
    DateTimeOffset CreatedAtUtc);

public sealed record CreateSpacePlanningComparisonResponse(
    string Outcome,
    SpacePlanningComparisonDto Comparison);

public sealed record SpacePlanningComparisonListResponse(
    IReadOnlyList<SpacePlanningComparisonSummaryDto> Items,
    bool IsTruncated);

public sealed record CreateSpacePlanningDecisionRequest(
    string Outcome,
    Guid? SelectedRunId,
    string Rationale,
    Guid? SupersedesDecisionId);

public sealed record SpacePlanningDecisionDto(
    Guid DecisionId,
    Guid SiteId,
    Guid ComparisonId,
    Guid? SelectedRunId,
    Guid? SupersedesDecisionId,
    string Outcome,
    string Rationale,
    string ComparisonHash,
    string DefinitionVersion,
    bool HumanDecision,
    bool AutomatedRecommendation,
    bool ProductionWriteAllowed,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedBy);

public sealed record CreateSpacePlanningDecisionResponse(
    string Outcome,
    SpacePlanningDecisionDto Decision);

public sealed record SpacePlanningDecisionListResponse(
    IReadOnlyList<SpacePlanningDecisionDto> Items,
    bool IsTruncated);
