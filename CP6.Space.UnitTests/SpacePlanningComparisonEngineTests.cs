using CP6.Space.Application;
using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpacePlanningComparisonEngineTests
{
    private static readonly Guid BaselineRun =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CandidateRun =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Computes_baseline_deltas_and_explicit_threshold_risks()
    {
        var baseline = Run(
            BaselineRun,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            coverage: 100m,
            distance: 1_000m,
            congestionSeconds: 0,
            overloaded: 0,
            utilization: 90m,
            throughput: 20m,
            cost: 80m);
        var candidate = Run(
            CandidateRun,
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            coverage: 90m,
            distance: 900m,
            congestionSeconds: 3_600,
            overloaded: 1,
            utilization: 120m,
            throughput: 24m,
            cost: 120m,
            quantityCapacity: 200m);

        var result = new SpacePlanningComparisonEngine().Compare(
            BaselineRun,
            [candidate, baseline],
            new SpacePlanningComparisonThresholds(95m, 100m, 0.25m, 100m));

        Assert.Equal(BaselineRun, result.Entries[0].Run.RunId);
        Assert.True(result.Entries[0].IsBaseline);
        var compared = result.Entries[1];
        Assert.Equal(-100m, compared.DistanceDeltaMeters);
        Assert.Equal(3_600, compared.CongestionTaskSecondsDelta);
        Assert.Equal(1, compared.OverloadedLocationCountDelta);
        Assert.Equal(30m,
            compared.PeakCapacityUtilizationDeltaPercentagePoints);
        Assert.Equal(4m, compared.AverageCompletedTasksPerHourDelta);
        Assert.Equal(40m, compared.TotalCostDelta);
        Assert.Equal(1m, compared.CongestionTaskHours);
        Assert.Equal(
            [
                "DISTANCE_COVERAGE_BELOW_THRESHOLD",
                "PEAK_CAPACITY_ABOVE_THRESHOLD",
                "OVERLOADED_LOCATIONS_PRESENT",
                "CONGESTION_ABOVE_THRESHOLD",
                "TOTAL_COST_ABOVE_THRESHOLD",
                "CAPACITY_ASSUMPTIONS_DIFFER_FROM_BASELINE",
            ],
            compared.Risks.Select(value => value.Code));
    }

    [Fact]
    public void Preserves_caller_order_after_baseline_without_ranking()
    {
        var first = Run(
            CandidateRun,
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            distance: 2_000m,
            cost: 200m);
        var second = Run(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            distance: 500m,
            cost: 50m);
        var baseline = Run(
            BaselineRun,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

        var result = new SpacePlanningComparisonEngine().Compare(
            BaselineRun,
            [first, baseline, second],
            new SpacePlanningComparisonThresholds(0m, 1_000m, 1_000m, null));

        Assert.Equal(
            [BaselineRun, CandidateRun, second.RunId],
            result.Entries.Select(value => value.Run.RunId));
    }

    [Fact]
    public void Rejects_multiple_runs_from_the_same_scenario_branch()
    {
        var branch = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        Assert.Throws<ArgumentException>(() =>
            new SpacePlanningComparisonEngine().Compare(
                BaselineRun,
                [Run(BaselineRun, branch), Run(CandidateRun, branch)],
                new SpacePlanningComparisonThresholds(0m, 100m, 0m, null)));
    }

    [Fact]
    public void Decision_requires_human_rationale_and_outcome_consistency()
    {
        var tenantId = Guid.NewGuid();
        var comparisonId = Guid.NewGuid();
        var selected = SpacePlanningDecisionRecord.Create(
            tenantId,
            Guid.NewGuid(),
            new SpacePlanningDecisionRecordData(
                Guid.NewGuid(),
                comparisonId,
                BaselineRun,
                null,
                SpacePlanningDecisionOutcome.Selected,
                "Choose the baseline because the capacity evidence is stable.",
                new string('a', 64),
                new string('b', 64),
                SpacePlanningComparisonEngine.DefinitionVersion));

        Assert.Equal(BaselineRun, selected.SelectedRunId);
        Assert.Equal(SpacePlanningDecisionOutcome.Selected, selected.Outcome);
        Assert.Throws<ArgumentException>(() =>
            SpacePlanningDecisionRecord.Create(
                tenantId,
                Guid.NewGuid(),
                new SpacePlanningDecisionRecordData(
                    Guid.NewGuid(),
                    comparisonId,
                    null,
                    null,
                    SpacePlanningDecisionOutcome.Selected,
                    "Missing selected run.",
                    new string('a', 64),
                    new string('b', 64),
                    SpacePlanningComparisonEngine.DefinitionVersion)));
    }

    private static SpacePlanningComparisonRunInput Run(
        Guid runId,
        Guid branchId,
        decimal coverage = 100m,
        decimal distance = 1_000m,
        long congestionSeconds = 0,
        int overloaded = 0,
        decimal utilization = 90m,
        decimal throughput = 20m,
        decimal cost = 80m,
        decimal quantityCapacity = 100m) =>
        new(
            runId,
            branchId,
            Guid.NewGuid(),
            1,
            $"Run {runId:N}",
            new string('a', 64),
            quantityCapacity,
            2,
            0,
            coverage,
            distance,
            congestionSeconds,
            overloaded,
            utilization,
            throughput,
            throughput * 2,
            cost);
}
