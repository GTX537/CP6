using CP6.Space.Contracts;

namespace CP6.Space.Application;

public sealed record SpaceOperationsDiagnosticPoint(
    string PersonKey,
    Guid EventId,
    string SourceId,
    string SourceKind,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset ReceivedAtUtc,
    Guid? FloorLogicalId,
    Guid? LocationLogicalId,
    decimal? XMillimeters,
    decimal? YMillimeters,
    bool IsEligible);

public sealed record SpaceOperationsDiagnosticLocation(
    Guid LocationLogicalId,
    string SpaceLocationCode,
    Guid FloorLogicalId,
    string FloorCode,
    string FloorName,
    int FloorLevel);

public sealed record SpaceOperationsPeopleAnalysis(
    SpaceOperationsPathDiagnosisDto Path,
    SpaceOperationsCongestionDiagnosisDto Congestion,
    SpaceOperationsDwellDiagnosisDto Dwell);

public interface ISpaceOperationsDiagnosticService
{
    Task<SpaceOperationsDiagnosticResponse> GetAsync(
        Guid siteId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);
}

public sealed class SpaceOperationsDiagnosticEngine
{
    public const int MaximumReturnedFindingCount = 100;

    public SpaceOperationsPeopleAnalysis Analyze(
        IReadOnlyList<SpaceOperationsDiagnosticPoint> points,
        IReadOnlyDictionary<Guid, SpaceOperationsDiagnosticLocation> locations,
        SpaceOperationsDiagnosticThresholdsDto thresholds)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(locations);
        ArgumentNullException.ThrowIfNull(thresholds);

        var orderedPeople = points
            .GroupBy(value => value.PersonKey, StringComparer.Ordinal)
            .Select(group => group
                .OrderBy(value => value.OccurredAtUtc)
                .ThenBy(value => value.ReceivedAtUtc)
                .ThenBy(value => value.EventId)
                .ToArray())
            .Where(person => person.Any(value => value.IsEligible))
            .ToArray();
        var path = AnalyzePath(orderedPeople, locations, thresholds);
        var episodes = BuildObservedLocationEpisodes(
            orderedPeople,
            thresholds.MaximumObservationGapSeconds);
        var dwell = AnalyzeDwell(
            episodes,
            locations,
            thresholds.DwellThresholdSeconds);
        var congestion = AnalyzeCongestion(
            episodes,
            locations,
            thresholds.CongestionMinimumConcurrentPeople);
        return new SpaceOperationsPeopleAnalysis(path, congestion, dwell);
    }

    private static SpaceOperationsPathDiagnosisDto AnalyzePath(
        IReadOnlyList<SpaceOperationsDiagnosticPoint[]> people,
        IReadOnlyDictionary<Guid, SpaceOperationsDiagnosticLocation> locations,
        SpaceOperationsDiagnosticThresholdsDto thresholds)
    {
        var transitionCount = 0;
        var knownCount = 0;
        var unknownCount = 0;
        var distanceMillimeters = 0d;
        var backtracks = new List<SpaceOperationsBacktrackFindingDto>();

        foreach (var person in people)
        {
            transitionCount += Math.Max(0, person.Length - 1);
            for (var index = 1; index < person.Length; index++)
            {
                var segment = Segment(
                    person[index - 1],
                    person[index],
                    thresholds.MaximumObservationGapSeconds);
                if (segment.IsKnown)
                {
                    knownCount++;
                    distanceMillimeters += segment.DistanceMillimeters;
                }
                else
                {
                    unknownCount++;
                }
            }

            for (var index = 1; index + 1 < person.Length; index++)
            {
                var incoming = Segment(
                    person[index - 1],
                    person[index],
                    thresholds.MaximumObservationGapSeconds);
                var outgoing = Segment(
                    person[index],
                    person[index + 1],
                    thresholds.MaximumObservationGapSeconds);
                if (!incoming.HasVector ||
                    !outgoing.HasVector ||
                    incoming.DistanceMillimeters <
                    thresholds.MinimumBacktrackSegmentMillimeters ||
                    outgoing.DistanceMillimeters <
                    thresholds.MinimumBacktrackSegmentMillimeters)
                {
                    continue;
                }

                var denominator =
                    incoming.DistanceMillimeters *
                    outgoing.DistanceMillimeters;
                var cosine = Math.Clamp(
                    (incoming.Dx * outgoing.Dx +
                     incoming.Dy * outgoing.Dy) / denominator,
                    -1d,
                    1d);
                var angle = Math.Acos(cosine) * 180d / Math.PI;
                if (angle + 0.000001d < (double)thresholds.BacktrackAngleDegrees)
                    continue;

                var pivot = person[index];
                SpaceOperationsDiagnosticLocation? location = null;
                if (pivot.LocationLogicalId.HasValue)
                    locations.TryGetValue(pivot.LocationLogicalId.Value, out location);
                backtracks.Add(new SpaceOperationsBacktrackFindingDto(
                    pivot.FloorLogicalId!.Value,
                    location?.FloorCode,
                    pivot.LocationLogicalId,
                    location?.SpaceLocationCode,
                    pivot.XMillimeters!.Value,
                    pivot.YMillimeters!.Value,
                    pivot.OccurredAtUtc,
                    Round((decimal)angle, 4),
                    Round((decimal)(outgoing.DistanceMillimeters / 1_000d), 3)));
            }
        }

        var orderedBacktracks = backtracks
            .OrderByDescending(value => value.ReturnSegmentMeters)
            .ThenBy(value => value.OccurredAtUtc)
            .ThenBy(value => value.LocationLogicalId)
            .ThenBy(value => value.FloorLogicalId)
            .ToArray();
        var returned = orderedBacktracks
            .Take(MaximumReturnedFindingCount)
            .ToArray();
        return new SpaceOperationsPathDiagnosisDto(
            people.Count,
            transitionCount,
            knownCount,
            unknownCount,
            Round((decimal)(distanceMillimeters / 1_000d), 3),
            orderedBacktracks.Length,
            Round(orderedBacktracks.Sum(value => value.ReturnSegmentMeters), 3),
            orderedBacktracks.Length > returned.Length,
            returned);
    }

    private static SpaceOperationsDwellDiagnosisDto AnalyzeDwell(
        IReadOnlyList<ObservedLocationEpisode> episodes,
        IReadOnlyDictionary<Guid, SpaceOperationsDiagnosticLocation> locations,
        int thresholdSeconds)
    {
        var qualifying = episodes
            .Where(value => value.DurationSeconds >= thresholdSeconds)
            .ToArray();
        var hotspots = qualifying
            .GroupBy(value => value.LocationLogicalId)
            .Select(group =>
            {
                locations.TryGetValue(group.Key, out var location);
                var first = group.First();
                return new SpaceOperationsDwellHotspotDto(
                    group.Key,
                    location?.SpaceLocationCode,
                    location?.FloorLogicalId ?? first.FloorLogicalId,
                    location?.FloorCode,
                    group.Count(),
                    group.Select(value => value.PersonKey)
                        .Distinct(StringComparer.Ordinal)
                        .Count(),
                    group.Sum(value => value.DurationSeconds),
                    group.Max(value => value.DurationSeconds));
            })
            .OrderByDescending(value => value.TotalDwellSeconds)
            .ThenByDescending(value => value.MaximumDwellSeconds)
            .ThenBy(value => value.LocationLogicalId)
            .ToArray();
        var returned = hotspots.Take(MaximumReturnedFindingCount).ToArray();
        return new SpaceOperationsDwellDiagnosisDto(
            qualifying.Length,
            qualifying.Select(value => value.PersonKey)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            hotspots.Length,
            qualifying.Sum(value => value.DurationSeconds),
            hotspots.Length > returned.Length,
            returned);
    }

    private static SpaceOperationsCongestionDiagnosisDto AnalyzeCongestion(
        IReadOnlyList<ObservedLocationEpisode> episodes,
        IReadOnlyDictionary<Guid, SpaceOperationsDiagnosticLocation> locations,
        int minimumConcurrentPeople)
    {
        var hotspots = episodes
            .GroupBy(value => value.LocationLogicalId)
            .Select(group => Congestion(
                group.ToArray(),
                locations,
                minimumConcurrentPeople))
            .Where(value =>
                value.PeakConcurrentPeople >= minimumConcurrentPeople &&
                value.ConcurrentSeconds > 0)
            .OrderByDescending(value => value.ConcurrentSeconds)
            .ThenByDescending(value => value.PeakConcurrentPeople)
            .ThenBy(value => value.LocationLogicalId)
            .ToArray();
        var returned = hotspots.Take(MaximumReturnedFindingCount).ToArray();
        return new SpaceOperationsCongestionDiagnosisDto(
            hotspots.Length,
            hotspots.Length == 0
                ? 0
                : hotspots.Max(value => value.PeakConcurrentPeople),
            hotspots.Sum(value => value.ConcurrentSeconds),
            hotspots.Length > returned.Length,
            returned);
    }

    private static SpaceOperationsCongestionHotspotDto Congestion(
        IReadOnlyList<ObservedLocationEpisode> episodes,
        IReadOnlyDictionary<Guid, SpaceOperationsDiagnosticLocation> locations,
        int minimumConcurrentPeople)
    {
        var changes = episodes
            .SelectMany(value => new[]
            {
                new PresenceChange(value.StartedAtUtc, value.PersonKey, IsStart: true),
                new PresenceChange(value.EndedAtUtc, value.PersonKey, IsStart: false),
            })
            .GroupBy(value => value.AtUtc)
            .OrderBy(value => value.Key)
            .ToArray();
        var active = new HashSet<string>(StringComparer.Ordinal);
        DateTimeOffset? previous = null;
        var concurrentSeconds = 0d;
        var peak = 0;
        foreach (var change in changes)
        {
            if (previous.HasValue && active.Count >= minimumConcurrentPeople)
                concurrentSeconds += (change.Key - previous.Value).TotalSeconds;
            foreach (var ending in change.Where(value => !value.IsStart))
                active.Remove(ending.PersonKey);
            foreach (var starting in change.Where(value => value.IsStart))
                active.Add(starting.PersonKey);
            peak = Math.Max(peak, active.Count);
            previous = change.Key;
        }

        var first = episodes[0];
        locations.TryGetValue(first.LocationLogicalId, out var location);
        return new SpaceOperationsCongestionHotspotDto(
            first.LocationLogicalId,
            location?.SpaceLocationCode,
            location?.FloorLogicalId ?? first.FloorLogicalId,
            location?.FloorCode,
            peak,
            checked((int)Math.Round(
                concurrentSeconds,
                MidpointRounding.AwayFromZero)),
            episodes.Select(value => value.PersonKey)
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    private static IReadOnlyList<ObservedLocationEpisode>
        BuildObservedLocationEpisodes(
            IReadOnlyList<SpaceOperationsDiagnosticPoint[]> people,
            int maximumGapSeconds)
    {
        var result = new List<ObservedLocationEpisode>();
        foreach (var person in people)
        {
            ObservedLocationEpisode? current = null;
            for (var index = 1; index < person.Length; index++)
            {
                var left = person[index - 1];
                var right = person[index];
                var gap = (right.OccurredAtUtc - left.OccurredAtUtc)
                    .TotalSeconds;
                var continuous =
                    gap > 0 &&
                    gap <= maximumGapSeconds &&
                    left.IsEligible &&
                    right.IsEligible &&
                    left.LocationLogicalId.HasValue &&
                    left.LocationLogicalId == right.LocationLogicalId &&
                    left.FloorLogicalId.HasValue &&
                    left.FloorLogicalId == right.FloorLogicalId;
                if (!continuous)
                {
                    FlushEpisode(result, ref current);
                    continue;
                }

                if (current is not null &&
                    current.LocationLogicalId == left.LocationLogicalId!.Value &&
                    current.EndedAtUtc == left.OccurredAtUtc)
                {
                    current = current with { EndedAtUtc = right.OccurredAtUtc };
                }
                else
                {
                    FlushEpisode(result, ref current);
                    current = new ObservedLocationEpisode(
                        left.PersonKey,
                        left.LocationLogicalId!.Value,
                        left.FloorLogicalId!.Value,
                        left.OccurredAtUtc,
                        right.OccurredAtUtc);
                }
            }
            FlushEpisode(result, ref current);
        }
        return result;
    }

    private static void FlushEpisode(
        ICollection<ObservedLocationEpisode> result,
        ref ObservedLocationEpisode? current)
    {
        if (current is not null)
            result.Add(current);
        current = null;
    }

    private static ObservedSegment Segment(
        SpaceOperationsDiagnosticPoint left,
        SpaceOperationsDiagnosticPoint right,
        int maximumGapSeconds)
    {
        var gap = (right.OccurredAtUtc - left.OccurredAtUtc).TotalSeconds;
        if (gap <= 0 ||
            gap > maximumGapSeconds ||
            !left.IsEligible ||
            !right.IsEligible ||
            !left.FloorLogicalId.HasValue ||
            left.FloorLogicalId != right.FloorLogicalId)
        {
            return ObservedSegment.Unknown;
        }
        if (left.XMillimeters.HasValue &&
            left.YMillimeters.HasValue &&
            right.XMillimeters.HasValue &&
            right.YMillimeters.HasValue)
        {
            var dx = (double)(right.XMillimeters.Value - left.XMillimeters.Value);
            var dy = (double)(right.YMillimeters.Value - left.YMillimeters.Value);
            var distance = Math.Sqrt(dx * dx + dy * dy);
            return new ObservedSegment(
                true,
                distance > 0,
                dx,
                dy,
                distance);
        }
        if (left.LocationLogicalId.HasValue &&
            left.LocationLogicalId == right.LocationLogicalId)
        {
            return new ObservedSegment(true, false, 0, 0, 0);
        }
        return ObservedSegment.Unknown;
    }

    private static decimal Round(decimal value, int digits) =>
        Math.Round(value, digits, MidpointRounding.AwayFromZero);

    private sealed record ObservedLocationEpisode(
        string PersonKey,
        Guid LocationLogicalId,
        Guid FloorLogicalId,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset EndedAtUtc)
    {
        public int DurationSeconds => checked((int)Math.Round(
            (EndedAtUtc - StartedAtUtc).TotalSeconds,
            MidpointRounding.AwayFromZero));
    }

    private sealed record PresenceChange(
        DateTimeOffset AtUtc,
        string PersonKey,
        bool IsStart);

    private readonly record struct ObservedSegment(
        bool IsKnown,
        bool HasVector,
        double Dx,
        double Dy,
        double DistanceMillimeters)
    {
        public static ObservedSegment Unknown => new(false, false, 0, 0, 0);
    }
}
