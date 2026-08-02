using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.Application;

public interface ISpacePlanningComparisonService
{
    Task<CreateSpacePlanningComparisonResponse> CreateComparisonAsync(
        Guid siteId,
        Guid comparisonId,
        CreateSpacePlanningComparisonRequest request,
        CancellationToken cancellationToken = default);

    Task<SpacePlanningComparisonDto> GetComparisonAsync(
        Guid siteId,
        Guid comparisonId,
        CancellationToken cancellationToken = default);

    Task<SpacePlanningComparisonListResponse> GetComparisonsAsync(
        Guid siteId,
        int limit,
        CancellationToken cancellationToken = default);

    Task<CreateSpacePlanningDecisionResponse> CreateDecisionAsync(
        Guid siteId,
        Guid comparisonId,
        Guid decisionId,
        CreateSpacePlanningDecisionRequest request,
        CancellationToken cancellationToken = default);

    Task<SpacePlanningDecisionDto> GetDecisionAsync(
        Guid siteId,
        Guid comparisonId,
        Guid decisionId,
        CancellationToken cancellationToken = default);

    Task<SpacePlanningDecisionListResponse> GetDecisionsAsync(
        Guid siteId,
        Guid comparisonId,
        int limit,
        CancellationToken cancellationToken = default);
}

public sealed record SpacePlanningComparisonThresholds(
    decimal MinimumDistanceCoveragePercent,
    decimal MaximumPeakCapacityUtilizationPercent,
    decimal MaximumCongestionTaskHours,
    decimal? MaximumTotalCost);

public sealed record SpacePlanningComparisonRunInput(
    Guid RunId,
    Guid BranchId,
    Guid ScenarioVersionId,
    long ScenarioContentRevision,
    string RunName,
    string RunResultHash,
    decimal DefaultQuantityCapacity,
    int DefaultConcurrentTaskCapacity,
    int LocationCapacityOverrideCount,
    decimal DistanceCoveragePercent,
    decimal TotalDistanceMeters,
    long CongestionTaskSeconds,
    int OverloadedLocationCount,
    decimal PeakCapacityUtilizationPercent,
    decimal AverageCompletedTasksPerHour,
    decimal PeakCompletedTasksPerHour,
    decimal TotalCost);

public sealed record SpacePlanningComparisonRiskResult(
    string Code,
    SpacePlanningRiskSeverity Severity);

public sealed record SpacePlanningComparisonEntryResult(
    int SequenceNo,
    SpacePlanningComparisonRunInput Run,
    bool IsBaseline,
    decimal CongestionTaskHours,
    decimal DistanceDeltaMeters,
    long CongestionTaskSecondsDelta,
    int OverloadedLocationCountDelta,
    decimal PeakCapacityUtilizationDeltaPercentagePoints,
    decimal AverageCompletedTasksPerHourDelta,
    decimal TotalCostDelta,
    IReadOnlyList<SpacePlanningComparisonRiskResult> Risks);

public sealed record SpacePlanningComparisonAnalysis(
    Guid BaselineRunId,
    IReadOnlyList<SpacePlanningComparisonEntryResult> Entries);

public sealed class SpacePlanningComparisonEngine
{
    public const string DefinitionVersion = "space-planning-comparison-v1";

    public SpacePlanningComparisonAnalysis Compare(
        Guid baselineRunId,
        IReadOnlyList<SpacePlanningComparisonRunInput> runs,
        SpacePlanningComparisonThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(thresholds);
        if (baselineRunId == Guid.Empty ||
            runs.Count is < 2 or > 10 ||
            runs.Any(value => value.RunId == Guid.Empty) ||
            runs.Select(value => value.RunId).Distinct().Count() != runs.Count ||
            runs.Select(value => value.BranchId).Distinct().Count() != runs.Count)
        {
            throw new ArgumentException(
                "Comparison requires two to ten distinct scenario runs.");
        }
        var baseline = runs.SingleOrDefault(value => value.RunId == baselineRunId)
            ?? throw new ArgumentException(
                "The baseline run must be included in the comparison.");
        ValidateThresholds(thresholds);
        foreach (var run in runs)
            ValidateRun(run);

        var ordered = new[] { baseline }
            .Concat(runs.Where(value => value.RunId != baselineRunId))
            .ToArray();
        var entries = ordered.Select((run, index) =>
        {
            var risks = BuildRisks(run, baseline, thresholds);
            return new SpacePlanningComparisonEntryResult(
                index + 1,
                run,
                run.RunId == baselineRunId,
                Round(run.CongestionTaskSeconds / 3600m),
                Round(run.TotalDistanceMeters - baseline.TotalDistanceMeters),
                run.CongestionTaskSeconds - baseline.CongestionTaskSeconds,
                run.OverloadedLocationCount - baseline.OverloadedLocationCount,
                Round(
                    run.PeakCapacityUtilizationPercent -
                    baseline.PeakCapacityUtilizationPercent),
                Round(
                    run.AverageCompletedTasksPerHour -
                    baseline.AverageCompletedTasksPerHour),
                Round(run.TotalCost - baseline.TotalCost),
                risks);
        }).ToArray();
        return new SpacePlanningComparisonAnalysis(baselineRunId, entries);
    }

    private static IReadOnlyList<SpacePlanningComparisonRiskResult> BuildRisks(
        SpacePlanningComparisonRunInput run,
        SpacePlanningComparisonRunInput baseline,
        SpacePlanningComparisonThresholds thresholds)
    {
        var risks = new List<SpacePlanningComparisonRiskResult>();
        if (run.DistanceCoveragePercent <
            thresholds.MinimumDistanceCoveragePercent)
        {
            risks.Add(new(
                "DISTANCE_COVERAGE_BELOW_THRESHOLD",
                SpacePlanningRiskSeverity.Warning));
        }
        if (run.PeakCapacityUtilizationPercent >
            thresholds.MaximumPeakCapacityUtilizationPercent)
        {
            risks.Add(new(
                "PEAK_CAPACITY_ABOVE_THRESHOLD",
                SpacePlanningRiskSeverity.Critical));
        }
        if (run.OverloadedLocationCount > 0)
        {
            risks.Add(new(
                "OVERLOADED_LOCATIONS_PRESENT",
                SpacePlanningRiskSeverity.Critical));
        }
        if (Round(run.CongestionTaskSeconds / 3600m) >
            thresholds.MaximumCongestionTaskHours)
        {
            risks.Add(new(
                "CONGESTION_ABOVE_THRESHOLD",
                SpacePlanningRiskSeverity.Warning));
        }
        if (thresholds.MaximumTotalCost.HasValue &&
            run.TotalCost > thresholds.MaximumTotalCost.Value)
        {
            risks.Add(new(
                "TOTAL_COST_ABOVE_THRESHOLD",
                SpacePlanningRiskSeverity.Warning));
        }
        if (run.DefaultQuantityCapacity != baseline.DefaultQuantityCapacity ||
            run.DefaultConcurrentTaskCapacity !=
                baseline.DefaultConcurrentTaskCapacity ||
            run.LocationCapacityOverrideCount !=
                baseline.LocationCapacityOverrideCount)
        {
            risks.Add(new(
                "CAPACITY_ASSUMPTIONS_DIFFER_FROM_BASELINE",
                SpacePlanningRiskSeverity.Information));
        }
        return risks;
    }

    private static void ValidateThresholds(
        SpacePlanningComparisonThresholds value)
    {
        if (value.MinimumDistanceCoveragePercent is < 0 or > 100 ||
            decimal.Round(value.MinimumDistanceCoveragePercent, 4) !=
                value.MinimumDistanceCoveragePercent ||
            value.MaximumPeakCapacityUtilizationPercent < 0 ||
            decimal.Round(value.MaximumPeakCapacityUtilizationPercent, 4) !=
                value.MaximumPeakCapacityUtilizationPercent ||
            value.MaximumCongestionTaskHours < 0 ||
            decimal.Round(value.MaximumCongestionTaskHours, 6) !=
                value.MaximumCongestionTaskHours ||
            value.MaximumTotalCost is < 0 ||
            value.MaximumTotalCost.HasValue &&
            decimal.Round(value.MaximumTotalCost.Value, 6) !=
                value.MaximumTotalCost.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    private static void ValidateRun(SpacePlanningComparisonRunInput value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.BranchId == Guid.Empty ||
            value.ScenarioVersionId == Guid.Empty ||
            value.ScenarioContentRevision < 0 ||
            string.IsNullOrWhiteSpace(value.RunName) ||
            value.RunResultHash?.Length != 64 ||
            value.DefaultQuantityCapacity <= 0 ||
            value.DefaultConcurrentTaskCapacity < 1 ||
            value.LocationCapacityOverrideCount < 0 ||
            value.DistanceCoveragePercent is < 0 or > 100 ||
            value.TotalDistanceMeters < 0 ||
            value.CongestionTaskSeconds < 0 ||
            value.OverloadedLocationCount < 0 ||
            value.PeakCapacityUtilizationPercent < 0 ||
            value.AverageCompletedTasksPerHour < 0 ||
            value.PeakCompletedTasksPerHour < 0 ||
            value.TotalCost < 0)
        {
            throw new ArgumentException("Simulation comparison input is invalid.");
        }
    }

    private static decimal Round(decimal value) =>
        decimal.Round(value, 6, MidpointRounding.ToEven);
}
