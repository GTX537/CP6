using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Space.UnitTests;

public sealed class SpaceOperationsDiagnosticEngineTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid FloorA =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FloorB =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid LocationA =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid LocationB =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void Analysis_keeps_path_backtrack_dwell_and_copresence_distinct()
    {
        var engine = new SpaceOperationsDiagnosticEngine();
        var points = new[]
        {
            Point("P1", 0, LocationA, FloorA, 0),
            Point("P1", 60, LocationB, FloorA, 10_000),
            Point("P1", 120, LocationA, FloorA, 0),
            Point("P1", 600, LocationA, FloorA, 0),
            Point("P1", 900, LocationA, FloorA, 0),
            Point("P2", 630, LocationA, FloorA, 0),
            Point("P2", 900, LocationA, FloorA, 0),
        };

        var result = engine.Analyze(points, Locations(), Thresholds());

        Assert.Equal(2, result.Path.PersonCount);
        Assert.Equal(5, result.Path.ObservedTransitionCount);
        Assert.Equal(4, result.Path.KnownDistanceSegmentCount);
        Assert.Equal(1, result.Path.UnknownDistanceSegmentCount);
        Assert.Equal(20m, result.Path.ObservedDistanceMeters);
        var backtrack = Assert.Single(result.Path.Backtracks);
        Assert.Equal(180m, backtrack.TurnAngleDegrees);
        Assert.Equal(10m, backtrack.ReturnSegmentMeters);

        var dwell = Assert.Single(result.Dwell.Hotspots);
        Assert.Equal(1, dwell.EpisodeCount);
        Assert.Equal(300, dwell.TotalDwellSeconds);
        Assert.Equal(1, dwell.PersonCount);

        var congestion = Assert.Single(result.Congestion.Hotspots);
        Assert.Equal(2, congestion.PeakConcurrentPeople);
        Assert.Equal(270, congestion.ConcurrentSeconds);
        Assert.Equal(2, congestion.ObservedPersonCount);
    }

    [Fact]
    public void Ineligible_cross_floor_sparse_and_unlocated_segments_are_unknown()
    {
        var engine = new SpaceOperationsDiagnosticEngine();
        var points = new[]
        {
            Point("P1", 0, LocationA, FloorA, null),
            Point("P1", 60, LocationA, FloorA, null),
            Point("P1", 120, LocationB, FloorA, 1_000, eligible: false),
            Point("P1", 180, LocationB, FloorA, 2_000),
            Point("P1", 240, LocationB, FloorB, 3_000),
            Point("P1", 600, LocationB, FloorB, 4_000),
        };

        var result = engine.Analyze(points, Locations(), Thresholds());

        Assert.Equal(1, result.Path.KnownDistanceSegmentCount);
        Assert.Equal(4, result.Path.UnknownDistanceSegmentCount);
        Assert.Equal(0m, result.Path.ObservedDistanceMeters);
        Assert.Empty(result.Path.Backtracks);
        Assert.Empty(result.Dwell.Hotspots);
        Assert.Empty(result.Congestion.Hotspots);
    }

    [Fact]
    public void Half_open_presence_intervals_do_not_overlap_at_shared_boundary()
    {
        var engine = new SpaceOperationsDiagnosticEngine();
        var points = new[]
        {
            Point("P1", 0, LocationA, FloorA, 0),
            Point("P1", 300, LocationA, FloorA, 0),
            Point("P2", 300, LocationA, FloorA, 0),
            Point("P2", 600, LocationA, FloorA, 0),
        };

        var result = engine.Analyze(points, Locations(), Thresholds());

        Assert.Equal(2, result.Dwell.EpisodeCount);
        Assert.Empty(result.Congestion.Hotspots);
        Assert.Equal(0, result.Congestion.ConcurrentSeconds);
    }

    [Fact]
    public void Backtrack_evidence_is_stably_ordered_and_truncated_at_one_hundred()
    {
        var engine = new SpaceOperationsDiagnosticEngine();
        var points = Enumerable.Range(0, 101)
            .SelectMany(index => new[]
            {
                Point($"P{index:000}", index * 3, LocationA, FloorA, 0),
                Point($"P{index:000}", index * 3 + 1, LocationA, FloorA, 10_000),
                Point($"P{index:000}", index * 3 + 2, LocationA, FloorA, 0),
            })
            .ToArray();

        var result = engine.Analyze(points, Locations(), Thresholds());

        Assert.Equal(101, result.Path.BacktrackCount);
        Assert.True(result.Path.BacktracksTruncated);
        Assert.Equal(100, result.Path.Backtracks.Count);
        Assert.Equal(
            result.Path.Backtracks.OrderBy(value => value.OccurredAtUtc),
            result.Path.Backtracks);
    }

    private static SpaceOperationsDiagnosticPoint Point(
        string person,
        int seconds,
        Guid? location,
        Guid floor,
        decimal? x,
        bool eligible = true) =>
        new(
            person,
            Guid.NewGuid(),
            "PDA-01",
            "Real",
            Start.AddSeconds(seconds),
            Start.AddSeconds(seconds + 1),
            floor,
            location,
            x,
            x.HasValue ? 0 : null,
            eligible);

    private static IReadOnlyDictionary<Guid, SpaceOperationsDiagnosticLocation>
        Locations() =>
        new Dictionary<Guid, SpaceOperationsDiagnosticLocation>
        {
            [LocationA] = new(
                LocationA,
                "F1-L01",
                FloorA,
                "F1",
                "Floor 1",
                1),
            [LocationB] = new(
                LocationB,
                "F1-L02",
                FloorA,
                "F1",
                "Floor 1",
                1),
        };

    private static SpaceOperationsDiagnosticThresholdsDto Thresholds() =>
        new(300, 1_000, 150, 300, 2, 85, 95);
}
