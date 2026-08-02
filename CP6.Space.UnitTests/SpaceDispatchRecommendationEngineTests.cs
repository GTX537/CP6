using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Space.UnitTests;

public sealed class SpaceDispatchRecommendationEngineTests
{
    private static readonly Guid FloorA = Guid.NewGuid();
    private static readonly Guid FloorB = Guid.NewGuid();
    private static readonly Guid ZoneA = Guid.NewGuid();
    private static readonly DateTimeOffset Now =
        new(2026, 8, 2, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Generate_preserves_maximum_cardinality_when_greedy_would_fail()
    {
        var result = new SpaceDispatchRecommendationEngine().Generate(
            new GenerateSpaceDispatchRecommendationRequest(
                MaximumTravelDistanceMeters: 2,
                MaximumAssignments: 10),
            [
                Task("TASK-A", 0, priority: 2),
                Task("TASK-B", 3_000, priority: 1),
            ],
            [
                Person("person-1", 1_000),
                Person("person-2", 4_000),
            ]);

        Assert.Equal(3, result.EligiblePairCount);
        Assert.Equal(2, result.MatchableAssignmentCount);
        Assert.False(result.IsTruncated);
        Assert.Equal(2, result.Assignments.Count);
        Assert.Contains(result.Assignments, value =>
            value.TaskId == "TASK-A" && value.PersonKey == "person-1");
        Assert.Contains(result.Assignments, value =>
            value.TaskId == "TASK-B" && value.PersonKey == "person-2");
        Assert.Equal(
            result.Assignments.Count,
            result.Assignments.Select(value => value.TaskId)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(
            result.Assignments.Count,
            result.Assignments.Select(value => value.PersonKey)
                .Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Generate_excludes_non_dispatchable_facts_with_independent_reasons()
    {
        var result = new SpaceDispatchRecommendationEngine().Generate(
            new GenerateSpaceDispatchRecommendationRequest(
                MaximumAssignments: 10),
            [
                Task("NOT-PENDING", 0) with { Status = "InProgress" },
                Task("ASSIGNED", 0) with { AssignedTo = "USER-1" },
                Task("ELIGIBLE", 0),
            ],
            [
                Person("position-stale", 0) with { PositionIsStale = true },
                Person("work-stale", 0) with { WorkStateIsStale = true },
                Person("busy", 0) with { WorkState = "Busy" },
                Person("simulated", 0) with { IsSimulated = true },
                Person("idle", 0),
            ]);

        Assert.Single(result.Assignments);
        Assert.Equal("ELIGIBLE", result.Assignments[0].TaskId);
        Assert.Equal("idle", result.Assignments[0].PersonKey);
        Assert.Equal(1, result.Exclusions.TasksNotPending);
        Assert.Equal(1, result.Exclusions.TasksAlreadyAssigned);
        Assert.Equal(1, result.Exclusions.PeoplePositionStale);
        Assert.Equal(1, result.Exclusions.PeopleWorkStateStale);
        Assert.Equal(1, result.Exclusions.PeopleNotIdle);
        Assert.Equal(1, result.Exclusions.PeopleSimulatedExcluded);
        Assert.Contains(
            "TASK_CONCURRENCY_EVIDENCE_CAPTURED",
            result.Assignments[0].RuleHits);
        Assert.Equal("AQIDBA==", result.Assignments[0].TaskRowVersion);
    }

    [Fact]
    public void Generate_reports_matching_capacity_separately_from_return_limit()
    {
        var result = new SpaceDispatchRecommendationEngine().Generate(
            new GenerateSpaceDispatchRecommendationRequest(
                MaximumAssignments: 1),
            [Task("TASK-1", 0), Task("TASK-2", 2_000)],
            [Person("person-1", 0), Person("person-2", 2_000)]);

        Assert.Equal(2, result.MatchableAssignmentCount);
        Assert.Single(result.Assignments);
        Assert.True(result.IsTruncated);
    }

    [Fact]
    public void Generate_fails_before_evaluating_more_than_pair_limit()
    {
        var tasks = Enumerable.Range(1, 317)
            .Select(index => Task($"TASK-{index:000}", 0))
            .ToArray();
        var people = Enumerable.Range(1, 316)
            .Select(index => Person($"person-{index:000}", 0))
            .ToArray();

        var error = Assert.Throws<SpaceDispatchPairLimitExceededException>(() =>
            new SpaceDispatchRecommendationEngine().Generate(
                new GenerateSpaceDispatchRecommendationRequest(),
                tasks,
                people));

        Assert.Equal(100_172, error.PairCount);
    }

    private static SpaceDispatchTaskInput Task(
        string taskId,
        decimal x,
        int priority = 2) =>
        new(
            TaskId: taskId,
            TaskType: "Pick",
            Status: "Pending",
            AssignedTo: null,
            Priority: priority,
            ContractVersion: 1,
            ExecutionVersion: 0,
            RowVersion: "AQIDBA==",
            TargetLocationRole: "Source",
            TargetLocationResolved: true,
            LocationLogicalId: Guid.NewGuid(),
            LocationCode: $"{taskId}-L1",
            CodeMatches: true,
            FloorLogicalId: FloorA,
            FloorCode: "F1",
            FloorName: "Floor 1",
            FloorLevel: 1,
            ZoneLogicalId: ZoneA,
            ZoneCode: "Z1",
            RackLogicalId: Guid.NewGuid(),
            RackCode: "R1",
            AnchorXMillimeters: x,
            AnchorYMillimeters: 0,
            Quantity: 1,
            MaterialNumber: "SKU-1");

    private static SpaceDispatchPersonInput Person(
        string personKey,
        decimal x) =>
        new(
            PersonKey: personKey,
            SourceId: "PDA-01",
            SourceKind: "Real",
            PersonExternalId: personKey,
            IsSimulated: false,
            LocationLogicalId: null,
            FloorLogicalId: FloorA,
            FloorCode: "F1",
            ZoneLogicalId: ZoneA,
            ZoneCode: "Z1",
            AnchorXMillimeters: x,
            AnchorYMillimeters: 0,
            WorkState: "Idle",
            PositionOccurredAtUtc: Now.AddSeconds(-10),
            PositionReceivedAtUtc: Now.AddSeconds(-9),
            WorkStateOccurredAtUtc: Now.AddSeconds(-8),
            WorkStateReceivedAtUtc: Now.AddSeconds(-7),
            PositionIsStale: false,
            WorkStateIsStale: false);
}
