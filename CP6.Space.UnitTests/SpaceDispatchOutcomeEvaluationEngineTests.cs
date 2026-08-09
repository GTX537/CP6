using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Space.UnitTests;

public sealed class SpaceDispatchOutcomeEvaluationEngineTests
{
    private static readonly Guid SiteId = Guid.Parse(
        "10000000-0000-0000-0000-000000000001");
    private static readonly Guid RecommendationId = Guid.Parse(
        "20000000-0000-0000-0000-000000000001");
    private static readonly Guid ApprovalId = Guid.Parse(
        "30000000-0000-0000-0000-000000000001");
    private static readonly Guid VersionId = Guid.Parse(
        "40000000-0000-0000-0000-000000000001");
    private static readonly Guid FloorId = Guid.Parse(
        "50000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherFloorId = Guid.Parse(
        "50000000-0000-0000-0000-000000000002");
    private static readonly Guid TargetOne = Guid.Parse(
        "60000000-0000-0000-0000-000000000001");
    private static readonly Guid TargetTwo = Guid.Parse(
        "60000000-0000-0000-0000-000000000002");
    private static readonly Guid PersonOneLocation = Guid.Parse(
        "70000000-0000-0000-0000-000000000001");
    private static readonly Guid PersonTwoLocation = Guid.Parse(
        "70000000-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset GeneratedAt =
        new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly SpaceDispatchOutcomeEvaluationEngine _engine = new();

    [Fact]
    public void Complete_cohort_returns_honest_funnel_timing_and_improvement()
    {
        var fixture = Fixture();

        var result = _engine.Evaluate(
            fixture.Recommendation,
            fixture.Approval,
            fixture.Execution,
            fixture.Anchors);

        Assert.Equal("Completed", result.ExecutionStatus);
        Assert.Equal(100m, result.Funnel.SelectionRatePercent);
        Assert.Equal(100m, result.Funnel.AssignmentSuccessRatePercent);
        Assert.Equal(100m, result.Funnel.StartRatePercent);
        Assert.Equal(100m, result.Funnel.CompletionRatePercent);
        Assert.Equal(30m, result.Timing.ApprovalLeadTimeSeconds);
        Assert.Equal(90m, result.Timing.AssignmentLeadTimeSeconds);
        Assert.Equal(2, result.Timing.AssignmentToStartSampleCount);
        Assert.Equal(90m, result.Timing.AverageAssignmentToStartSeconds);
        Assert.Equal(330m, result.Timing.AverageExecutionSeconds);
        Assert.Equal(420m, result.Timing.AverageAssignmentToCompletionSeconds);
        Assert.Equal("Available", result.PlannedDistance.Status);
        Assert.Equal(20m, result.PlannedDistance.StableOrderBaselineMeters);
        Assert.Equal(0m, result.PlannedDistance.OptimizedMeters);
        Assert.Equal(20m, result.PlannedDistance.DifferenceMeters);
        Assert.Equal(100m, result.PlannedDistance.DifferencePercent);
        Assert.Equal("Improved", result.PlannedDistance.Outcome);
        Assert.False(result.BenefitBoundary.ActualTravelDistanceAvailable);
        Assert.False(result.BenefitBoundary.ThroughputUpliftAvailable);
        Assert.False(result.BenefitBoundary.MonetaryBenefitAvailable);
    }

    [Fact]
    public void Geometry_can_report_regression_without_turning_it_into_a_gain()
    {
        var fixture = Fixture();
        var assignments = fixture.Recommendation.Assignments
            .Select(value => value with { GeometricDistanceMeters = 12.5m })
            .ToArray();
        var recommendation = fixture.Recommendation with
        {
            Assignments = assignments,
        };

        var result = _engine.Evaluate(
            recommendation, fixture.Approval, fixture.Execution, fixture.Anchors);

        Assert.Equal("Available", result.PlannedDistance.Status);
        Assert.Equal(25m, result.PlannedDistance.OptimizedMeters);
        Assert.Equal(-5m, result.PlannedDistance.DifferenceMeters);
        Assert.Equal(-25m, result.PlannedDistance.DifferencePercent);
        Assert.Equal("Regressed", result.PlannedDistance.Outcome);
    }

    [Fact]
    public void Single_assignment_has_no_pairing_counterfactual()
    {
        var fixture = Fixture(count: 1);

        var result = _engine.Evaluate(
            fixture.Recommendation,
            fixture.Approval,
            fixture.Execution,
            fixture.Anchors);

        Assert.Equal("Unavailable", result.PlannedDistance.Status);
        Assert.Equal("COHORT_TOO_SMALL", result.PlannedDistance.UnavailableReason);
        Assert.Null(result.PlannedDistance.DifferenceMeters);
        Assert.Contains("COHORT_TOO_SMALL", result.Limitations);
    }

    [Fact]
    public void Missing_person_location_rejects_partial_geometry_claim()
    {
        var fixture = Fixture();
        var assignments = fixture.Recommendation.Assignments.ToArray();
        assignments[0] = assignments[0] with { PersonLocationLogicalId = null };

        var result = _engine.Evaluate(
            fixture.Recommendation with { Assignments = assignments },
            fixture.Approval,
            fixture.Execution,
            fixture.Anchors);

        Assert.Equal("PERSON_LOCATION_ANCHOR_NOT_AVAILABLE",
            result.PlannedDistance.UnavailableReason);
        Assert.Null(result.PlannedDistance.OptimizedMeters);
    }

    [Fact]
    public void Stable_order_cross_floor_pair_rejects_comparison()
    {
        var fixture = Fixture();
        var anchors = fixture.Anchors.ToDictionary(value => value.Key,
            value => value.Value);
        anchors[PersonOneLocation] = anchors[PersonOneLocation] with
        {
            FloorLogicalId = OtherFloorId,
        };
        var assignments = fixture.Recommendation.Assignments.ToArray();
        assignments[1] = assignments[1] with
        {
            PersonFloorLogicalId = OtherFloorId,
        };

        var result = _engine.Evaluate(
            fixture.Recommendation with { Assignments = assignments },
            fixture.Approval,
            fixture.Execution,
            anchors);

        Assert.Equal("STABLE_ORDER_PAIR_CROSSES_FLOORS",
            result.PlannedDistance.UnavailableReason);
    }

    [Fact]
    public void Stable_order_pair_must_respect_original_distance_limit()
    {
        var fixture = Fixture();
        var recommendation = fixture.Recommendation with
        {
            Request = fixture.Recommendation.Request with
            {
                MaximumTravelDistanceMeters = 5m,
            },
        };

        var result = _engine.Evaluate(
            recommendation, fixture.Approval, fixture.Execution, fixture.Anchors);

        Assert.Equal("STABLE_ORDER_PAIR_DISTANCE_LIMIT_EXCEEDED",
            result.PlannedDistance.UnavailableReason);
    }

    [Fact]
    public void Invalid_or_incomplete_times_are_excluded_and_disclosed()
    {
        var fixture = Fixture();
        var tasks = fixture.Execution.Tasks.ToArray();
        tasks[0] = tasks[0] with
        {
            StartedAtUtc = GeneratedAt.AddMinutes(1),
            DoneAtUtc = GeneratedAt.AddSeconds(30),
        };
        tasks[1] = tasks[1] with
        {
            StartedAtUtc = null,
            DoneAtUtc = GeneratedAt.AddMinutes(8),
        };
        var approval = fixture.Approval with
        {
            DecidedAtUtc = GeneratedAt.AddSeconds(15),
        };

        var result = _engine.Evaluate(
            fixture.Recommendation,
            approval,
            fixture.Execution with { Tasks = tasks },
            fixture.Anchors);

        Assert.Null(result.Timing.ApprovalLeadTimeSeconds);
        Assert.Equal(0, result.Timing.AssignmentToStartSampleCount);
        Assert.Equal(0, result.Timing.ExecutionSampleCount);
        Assert.Equal(1, result.Timing.AssignmentToCompletionSampleCount);
        Assert.Contains("APPROVAL_TIMING_INVALID", result.Limitations);
        Assert.Contains("TASK_TIMING_EVIDENCE_INCOMPLETE", result.Limitations);
        Assert.Contains("TASK_TIMING_EVIDENCE_INVALID", result.Limitations);
    }

    [Fact]
    public void Mismatched_selection_fails_closed()
    {
        var fixture = Fixture();
        var selections = fixture.Approval.Selections.ToArray();
        selections[0] = selections[0] with { TaskId = "OTHER" };

        var exception = Assert.Throws<SpaceDispatchOutcomeEvaluationException>(() =>
            _engine.Evaluate(
                fixture.Recommendation,
                fixture.Approval with { Selections = selections },
                fixture.Execution,
                fixture.Anchors));

        Assert.Contains("selection", exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Mismatched_execution_aggregate_fails_closed()
    {
        var fixture = Fixture();

        var exception = Assert.Throws<SpaceDispatchOutcomeEvaluationException>(() =>
            _engine.Evaluate(
                fixture.Recommendation,
                fixture.Approval,
                fixture.Execution with { CompletedCount = 1 },
                fixture.Anchors));

        Assert.Contains("execution task", exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private static TestFixture Fixture(int count = 2)
    {
        var assignments = new[]
        {
            Assignment(1, "TASK-1", "PERSON-2", TargetOne,
                PersonTwoLocation, 0m),
            Assignment(2, "TASK-2", "PERSON-1", TargetTwo,
                PersonOneLocation, 0m),
        }.Take(count).ToArray();
        var recommendation = new SpaceDispatchRecommendationDto(
            RecommendationId,
            SiteId,
            VersionId,
            "WH1",
            GeneratedAt,
            Guid.Parse("80000000-0000-0000-0000-000000000001"),
            "space-dispatch-v1",
            "AssignmentsGenerated",
            new GenerateSpaceDispatchRecommendationRequest(
                AllowCrossFloor: false,
                MaximumAssignments: 20),
            null!,
            count,
            count,
            count,
            count,
            count * count,
            count,
            count,
            false,
            null!,
            false,
            [],
            assignments,
            []);
        var selections = assignments.Select(value =>
            new SpaceDispatchApprovalSelectionDto(
                value.Rank,
                value.TaskId,
                value.TaskType,
                value.PersonSourceId,
                value.PersonExternalId,
                value.TargetLocationCode)).ToArray();
        var receipts = assignments.Select(value =>
            new SpaceDispatchTaskAdaptationReceiptDto(
                value.Rank,
                value.TaskId,
                value.PersonExternalId,
                Guid.NewGuid(),
                "Applied")).ToArray();
        var approval = new SpaceDispatchApprovalRequestDto(
            ApprovalId,
            SiteId,
            RecommendationId,
            VersionId,
            "WH1",
            "space-dispatch-v1",
            "Applied",
            "approved",
            Guid.Parse("80000000-0000-0000-0000-000000000002"),
            GeneratedAt.AddSeconds(30),
            Guid.Parse("80000000-0000-0000-0000-000000000003"),
            Guid.Parse("80000000-0000-0000-0000-000000000004"),
            GeneratedAt.AddMinutes(1),
            GeneratedAt.AddMinutes(2),
            "cp6-mobile-task-assignment-v1",
            count,
            selections,
            receipts,
            null);
        var tasks = assignments.Select((value, index) =>
            new SpaceDispatchExecutionTaskDto(
                value.Rank,
                value.TaskId,
                value.PersonSourceId,
                value.PersonExternalId,
                receipts[index].OperationId,
                2,
                "Completed",
                0,
                GeneratedAt.AddMinutes(3 + index),
                GeneratedAt.AddMinutes(8 + index * 2),
                "Completed",
                GeneratedAt.AddMinutes(8 + index * 2))).ToArray();
        var execution = new SpaceDispatchExecutionDto(
            ApprovalId,
            SiteId,
            RecommendationId,
            "Applied",
            "Completed",
            GeneratedAt.AddMinutes(12),
            count,
            0,
            0,
            count,
            0,
            false,
            0,
            3,
            false,
            null,
            null,
            tasks,
            []);
        var anchors = new Dictionary<Guid, SpaceDispatchEvaluationLocationAnchor>
        {
            [TargetOne] = new(TargetOne, FloorId, 10_000m, 0m),
            [TargetTwo] = new(TargetTwo, FloorId, 0m, 0m),
            [PersonOneLocation] = new(PersonOneLocation, FloorId, 0m, 0m),
            [PersonTwoLocation] = new(PersonTwoLocation, FloorId, 10_000m, 0m),
        };
        return new TestFixture(recommendation, approval, execution, anchors);
    }

    private static SpaceDispatchRecommendationAssignmentDto Assignment(
        int rank,
        string taskId,
        string personId,
        Guid targetLocationId,
        Guid personLocationId,
        decimal distance) =>
        new(
            rank,
            taskId,
            "PICK",
            "Pending",
            2,
            2,
            0,
            "row-version",
            "From",
            targetLocationId,
            $"LOC-{rank}",
            FloorId,
            "F1",
            "Floor 1",
            1,
            null,
            null,
            null,
            null,
            1m,
            null,
            $"SOURCE:{personId}",
            "SOURCE",
            "Real",
            personId,
            personLocationId,
            FloorId,
            null,
            GeneratedAt.AddMinutes(-1),
            GeneratedAt.AddMinutes(-1),
            GeneratedAt.AddMinutes(-1),
            GeneratedAt.AddMinutes(-1),
            true,
            false,
            distance,
            []);

    private sealed record TestFixture(
        SpaceDispatchRecommendationDto Recommendation,
        SpaceDispatchApprovalRequestDto Approval,
        SpaceDispatchExecutionDto Execution,
        IReadOnlyDictionary<Guid, SpaceDispatchEvaluationLocationAnchor> Anchors);
}
