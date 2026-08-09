namespace CP6.Space.Contracts;

public sealed record SpaceDispatchEvaluationEvidenceDto(
    DateTimeOffset RecommendationGeneratedAtUtc,
    DateTimeOffset ApprovalRequestedAtUtc,
    DateTimeOffset? ApprovalDecidedAtUtc,
    DateTimeOffset? AssignmentAppliedAtUtc,
    DateTimeOffset ExecutionObservedAtUtc,
    string RecommendationDefinitionVersion,
    string EvaluationDefinitionVersion,
    string AdapterId);

public sealed record SpaceDispatchEvaluationFunnelDto(
    int RecommendedCount,
    int SelectedCount,
    int AssignmentReceiptCount,
    int StartedCount,
    int CompletedCount,
    int AttentionCount,
    int CompensatedCount,
    decimal SelectionRatePercent,
    decimal AssignmentSuccessRatePercent,
    decimal StartRatePercent,
    decimal CompletionRatePercent);

public sealed record SpaceDispatchEvaluationTimingDto(
    decimal? ApprovalLeadTimeSeconds,
    decimal? AssignmentLeadTimeSeconds,
    int AssignmentToStartSampleCount,
    decimal? AverageAssignmentToStartSeconds,
    int ExecutionSampleCount,
    decimal? AverageExecutionSeconds,
    int AssignmentToCompletionSampleCount,
    decimal? AverageAssignmentToCompletionSeconds);

public sealed record SpaceDispatchPlannedDistanceComparisonDto(
    string Status,
    string Basis,
    int CohortCount,
    decimal? StableOrderBaselineMeters,
    decimal? OptimizedMeters,
    decimal? DifferenceMeters,
    decimal? DifferencePercent,
    string? Outcome,
    string? UnavailableReason);

public sealed record SpaceDispatchBenefitBoundaryDto(
    bool ActualTravelDistanceAvailable,
    string ActualTravelDistanceReason,
    bool ThroughputUpliftAvailable,
    string ThroughputUpliftReason,
    bool MonetaryBenefitAvailable,
    string MonetaryBenefitReason);

public sealed record SpaceDispatchOutcomeEvaluationDto(
    Guid ApprovalRequestId,
    Guid SiteId,
    Guid RecommendationId,
    Guid PublishedVersionId,
    string WarehouseCode,
    string ApprovalStatus,
    string ExecutionStatus,
    DateTimeOffset EvaluatedAtUtc,
    SpaceDispatchEvaluationEvidenceDto Evidence,
    SpaceDispatchEvaluationFunnelDto Funnel,
    SpaceDispatchEvaluationTimingDto Timing,
    SpaceDispatchPlannedDistanceComparisonDto PlannedDistance,
    SpaceDispatchBenefitBoundaryDto BenefitBoundary,
    IReadOnlyList<string> Limitations);
