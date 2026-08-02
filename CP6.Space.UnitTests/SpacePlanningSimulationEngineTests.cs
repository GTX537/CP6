using CP6.Space.Application;
using CP6.Space.Domain;
using System.Text.Json;

namespace CP6.Space.UnitTests;

public sealed class SpacePlanningSimulationEngineTests
{
    private static readonly DateTimeOffset From =
        new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid A =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid B =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Floor =
        Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    [Fact]
    public void Computes_distance_congestion_capacity_throughput_and_cost()
    {
        var locations = new Dictionary<Guid,
            SpacePlanningSimulationLocationInput>
        {
            [A] = new(A, Floor, 0, 0, 2m, 1),
            [B] = new(B, Floor, 3_000, 4_000, 3m, 1),
        };
        var tasks = new[]
        {
            Task(1, new string('a', 64), SpacePlanningTaskOutcome.Completed,
                0, 60, A, B, 2m),
            Task(2, new string('a', 64), SpacePlanningTaskOutcome.Completed,
                30, 90, A, B, 2m),
            Task(3, null, SpacePlanningTaskOutcome.Failed,
                60, 120, B, A, 1m),
        };

        var result = new SpacePlanningSimulationEngine().Analyze(
            tasks,
            locations,
            new SpacePlanningSimulationParameters(
                From,
                From.AddHours(4),
                60,
                2m,
                10m,
                4m));

        Assert.Equal(3, result.TaskCount);
        Assert.Equal(2, result.CompletedTaskCount);
        Assert.Equal(4m, result.CompletedQuantity);
        Assert.Equal(3, result.DistanceEligibleTaskCount);
        Assert.Equal(15m, result.TotalDistanceMeters);
        Assert.Equal(100m, result.DistanceCoveragePercent);
        Assert.Equal(2, result.PeakConcurrentTasks);
        Assert.Equal(1_800, result.CongestionSeconds);
        Assert.Equal(1_800, result.CongestionTaskSeconds);
        Assert.Equal(1, result.OverloadedLocationCount);
        Assert.Equal(133.3333m, result.PeakCapacityUtilizationPercent);
        Assert.Equal(4m, result.HistoricalWindowHours);
        Assert.Equal(0.5m, result.AverageCompletedTasksPerHour);
        Assert.Equal(2m, result.PeakCompletedTasksPerHour);
        Assert.Equal(1m, result.AverageCompletedQuantityPerHour);
        Assert.Equal(4m, result.PeakCompletedQuantityPerHour);
        Assert.Equal(2.5m, result.LaborHours);
        Assert.Equal(30m, result.DistanceCost);
        Assert.Equal(25m, result.LaborCost);
        Assert.Equal(2m, result.CongestionCost);
        Assert.Equal(57m, result.TotalCost);

        var overloaded = Assert.Single(
            result.Locations,
            value => value.LocationLogicalId == B);
        Assert.True(overloaded.IsOverloaded);
        Assert.Equal(2, overloaded.PeakConcurrentTasks);
        Assert.Equal(4m, overloaded.PeakConcurrentQuantity);
        Assert.Equal(1_800, overloaded.CongestionSeconds);
    }

    [Fact]
    public void Distance_is_unknown_across_floors_but_same_location_is_zero()
    {
        var otherFloor = Guid.NewGuid();
        var locations = new Dictionary<Guid,
            SpacePlanningSimulationLocationInput>
        {
            [A] = new(A, Floor, null, null, 10m, 2),
            [B] = new(B, otherFloor, 3_000, 4_000, 10m, 2),
        };
        var result = new SpacePlanningSimulationEngine().Analyze(
            [
                Task(2, null, SpacePlanningTaskOutcome.Completed,
                    10, 20, A, B, 1m),
                Task(1, null, SpacePlanningTaskOutcome.Completed,
                    0, 10, A, A, 1m),
                Task(3, null, SpacePlanningTaskOutcome.Completed,
                    20, 30, null, B, 1m),
            ],
            locations,
            new SpacePlanningSimulationParameters(
                From,
                From.AddHours(1),
                15,
                0,
                0,
                0));

        Assert.Equal(1, result.DistanceEligibleTaskCount);
        Assert.Equal(0m, result.TotalDistanceMeters);
        Assert.Equal(33.3333m, result.DistanceCoveragePercent);
    }

    [Fact]
    public void Analysis_is_deterministic_for_input_order()
    {
        var locations = new Dictionary<Guid,
            SpacePlanningSimulationLocationInput>
        {
            [A] = new(A, Floor, 0, 0, 10m, 1),
            [B] = new(B, Floor, 3_000, 4_000, 10m, 1),
        };
        var first = Task(1, null, SpacePlanningTaskOutcome.Completed,
            0, 30, A, B, 1m);
        var second = Task(2, null, SpacePlanningTaskOutcome.Completed,
            15, 45, B, A, 1m);
        var parameters = new SpacePlanningSimulationParameters(
            From,
            From.AddHours(1),
            15,
            1,
            1,
            1);
        var engine = new SpacePlanningSimulationEngine();

        var left = engine.Analyze([first, second], locations, parameters);
        var right = engine.Analyze([second, first], locations, parameters);

        Assert.Equal(
            JsonSerializer.Serialize(left),
            JsonSerializer.Serialize(right));
    }

    [Fact]
    public void Rejects_subsecond_historical_windows()
    {
        var locations = new Dictionary<Guid,
            SpacePlanningSimulationLocationInput>
        {
            [A] = new(A, Floor, 0, 0, 10m, 1),
        };
        var task = new SpacePlanningSimulationTaskInput(
            1,
            null,
            SpacePlanningTaskOutcome.Completed,
            From,
            From.AddMilliseconds(500),
            A,
            A,
            1);

        Assert.Throws<ArgumentException>(() =>
            new SpacePlanningSimulationEngine().Analyze(
                [task],
                locations,
                new SpacePlanningSimulationParameters(
                    From,
                    From.AddMilliseconds(500),
                    1,
                    0,
                    0,
                    0)));
    }

    private static SpacePlanningSimulationTaskInput Task(
        int sequence,
        string? worker,
        SpacePlanningTaskOutcome outcome,
        int fromMinute,
        int toMinute,
        Guid? fromLocation,
        Guid toLocation,
        decimal quantity) =>
        new(
            sequence,
            worker,
            outcome,
            From.AddMinutes(fromMinute),
            From.AddMinutes(toMinute),
            fromLocation,
            toLocation,
            quantity);
}
