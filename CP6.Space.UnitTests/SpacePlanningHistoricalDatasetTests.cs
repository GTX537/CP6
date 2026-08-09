using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpacePlanningHistoricalDatasetTests
{
    private static readonly Guid TenantId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset HistoricalFrom =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset HistoricalTo =
        HistoricalFrom.AddHours(4);
    private static readonly DateTimeOffset ReplayStart =
        new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Replay_clock_maps_boundaries_deterministically()
    {
        var clock = SpaceReplayClock.Create(
            HistoricalFrom,
            HistoricalTo,
            ReplayStart,
            8m);

        Assert.Equal(ReplayStart, clock.Map(HistoricalFrom));
        Assert.Equal(ReplayStart.AddMinutes(15), clock.Map(
            HistoricalFrom.AddHours(2)));
        Assert.Equal(ReplayStart.AddMinutes(30), clock.Map(HistoricalTo));
        Assert.Equal(clock.ReplayEndUtc, clock.Map(HistoricalTo));
    }

    [Fact]
    public void Replay_clock_rejects_non_utc_out_of_window_and_invalid_speed()
    {
        Assert.Throws<ArgumentException>(() =>
            SpaceReplayClock.Create(
                HistoricalFrom.ToOffset(TimeSpan.FromHours(1)),
                HistoricalTo,
                ReplayStart,
                1m));
        Assert.Throws<ArgumentException>(() =>
            SpaceReplayClock.Create(
                HistoricalFrom,
                HistoricalTo,
                ReplayStart,
                0m));
        Assert.Throws<ArgumentException>(() =>
            SpaceReplayClock.Create(
                HistoricalFrom,
                HistoricalTo,
                ReplayStart,
                1.00001m));

        var clock = SpaceReplayClock.Create(
            HistoricalFrom,
            HistoricalTo,
            ReplayStart,
            1m);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            clock.Map(HistoricalTo.AddTicks(1)));
    }

    [Fact]
    public void Dataset_and_tasks_require_tokenized_identifiers_and_valid_enums()
    {
        var dataset = CreateDataset();
        var valid = new SpacePlanningHistoricalTaskData(
            1,
            new string('a', 64),
            new string('b', 64),
            SpacePlanningTaskType.Pick,
            SpacePlanningTaskOutcome.Completed,
            HistoricalFrom.AddMinutes(10),
            HistoricalFrom.AddMinutes(20),
            ReplayStart.AddMinutes(10),
            ReplayStart.AddMinutes(20),
            null,
            Guid.NewGuid(),
            2m);

        var task = SpacePlanningHistoricalTask.Create(dataset, valid);

        Assert.Equal(new string('a', 64), task.TaskToken);
        Assert.Throws<ArgumentException>(() =>
            SpacePlanningHistoricalTask.Create(
                dataset,
                valid with { TaskToken = "raw-order-123" }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SpacePlanningHistoricalTask.Create(
                dataset,
                valid with { TaskType = (SpacePlanningTaskType)999 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SpacePlanningHistoricalTask.Create(
                dataset,
                valid with { Outcome = (SpacePlanningTaskOutcome)999 }));
    }

    private static SpacePlanningHistoricalDataset CreateDataset() =>
        SpacePlanningHistoricalDataset.Create(
            TenantId,
            Guid.NewGuid(),
            new SpacePlanningHistoricalDatasetData(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "January replay",
                HistoricalFrom,
                HistoricalTo,
                ReplayStart,
                1m,
                1,
                new string('c', 64),
                new string('d', 64),
                "space-planning-historical-dataset-v1",
                "sha256-upstream-token-v1"));
}
