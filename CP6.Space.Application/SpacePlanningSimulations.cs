using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.Application;

public interface ISpacePlanningSimulationService
{
    Task<CreateSpacePlanningSimulationRunResponse> CreateAsync(
        Guid siteId,
        Guid branchId,
        Guid runId,
        CreateSpacePlanningSimulationRunRequest request,
        CancellationToken cancellationToken = default);

    Task<SpacePlanningSimulationRunDto> GetAsync(
        Guid siteId,
        Guid branchId,
        Guid runId,
        CancellationToken cancellationToken = default);

    Task<SpacePlanningSimulationRunListResponse> GetListAsync(
        Guid siteId,
        Guid branchId,
        int limit,
        CancellationToken cancellationToken = default);
}

public sealed record SpacePlanningSimulationTaskInput(
    int SequenceNo,
    string? WorkerToken,
    SpacePlanningTaskOutcome Outcome,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CompletedAtUtc,
    Guid? FromLocationLogicalId,
    Guid ToLocationLogicalId,
    decimal Quantity);

public sealed record SpacePlanningSimulationLocationInput(
    Guid LocationLogicalId,
    Guid FloorLogicalId,
    decimal? XMillimeters,
    decimal? YMillimeters,
    decimal QuantityCapacity,
    int ConcurrentTaskCapacity);

public sealed record SpacePlanningSimulationParameters(
    DateTimeOffset HistoricalFromUtc,
    DateTimeOffset HistoricalToUtc,
    int ThroughputWindowMinutes,
    decimal DistanceCostPerMeter,
    decimal LaborCostPerHour,
    decimal CongestionCostPerTaskHour);

public sealed record SpacePlanningSimulationAnalysisLocation(
    Guid LocationLogicalId,
    int TaskCount,
    int CompletedTaskCount,
    decimal TotalQuantity,
    int DistanceEligibleTaskCount,
    decimal TotalDistanceMeters,
    decimal QuantityCapacity,
    int ConcurrentTaskCapacity,
    int PeakConcurrentTasks,
    decimal PeakConcurrentQuantity,
    decimal CapacityUtilizationPercent,
    long CongestionSeconds,
    long CongestionTaskSeconds,
    bool IsOverloaded);

public sealed record SpacePlanningSimulationAnalysis(
    int TaskCount,
    int CompletedTaskCount,
    decimal CompletedQuantity,
    int DistanceEligibleTaskCount,
    decimal TotalDistanceMeters,
    decimal DistanceCoveragePercent,
    int PeakConcurrentTasks,
    long CongestionSeconds,
    long CongestionTaskSeconds,
    int OverloadedLocationCount,
    decimal PeakCapacityUtilizationPercent,
    decimal HistoricalWindowHours,
    decimal AverageCompletedTasksPerHour,
    decimal PeakCompletedTasksPerHour,
    decimal AverageCompletedQuantityPerHour,
    decimal PeakCompletedQuantityPerHour,
    decimal LaborHours,
    decimal DistanceCost,
    decimal LaborCost,
    decimal CongestionCost,
    decimal TotalCost,
    IReadOnlyList<SpacePlanningSimulationAnalysisLocation> Locations);

public sealed class SpacePlanningSimulationEngine
{
    public const string DefinitionVersion = "space-planning-simulation-v1";
    public const string GeometryBasis = "rack-cell-straight-line-v1";

    public SpacePlanningSimulationAnalysis Analyze(
        IReadOnlyList<SpacePlanningSimulationTaskInput> tasks,
        IReadOnlyDictionary<Guid, SpacePlanningSimulationLocationInput>
            locations,
        SpacePlanningSimulationParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(locations);
        ArgumentNullException.ThrowIfNull(parameters);
        if (tasks.Count is < 1 or > 10_000 ||
            parameters.HistoricalFromUtc.Offset != TimeSpan.Zero ||
            parameters.HistoricalToUtc.Offset != TimeSpan.Zero ||
            parameters.HistoricalFromUtc >= parameters.HistoricalToUtc ||
            parameters.HistoricalToUtc - parameters.HistoricalFromUtc <
                TimeSpan.FromSeconds(1) ||
            parameters.ThroughputWindowMinutes is < 1 or > 1_440 ||
            parameters.DistanceCostPerMeter < 0 ||
            parameters.LaborCostPerHour < 0 ||
            parameters.CongestionCostPerTaskHour < 0)
        {
            throw new ArgumentException("The simulation input is invalid.");
        }

        var ordered = tasks
            .OrderBy(value => value.SequenceNo)
            .ToArray();
        if (ordered.Select(value => value.SequenceNo).Distinct().Count() !=
            ordered.Length ||
            ordered.Any(value =>
                value.SequenceNo < 1 ||
                value.CreatedAtUtc.Offset != TimeSpan.Zero ||
                value.CompletedAtUtc.Offset != TimeSpan.Zero ||
                value.CreatedAtUtc < parameters.HistoricalFromUtc ||
                value.CompletedAtUtc > parameters.HistoricalToUtc ||
                value.CreatedAtUtc > value.CompletedAtUtc ||
                value.ToLocationLogicalId == Guid.Empty ||
                value.Quantity <= 0 ||
                !locations.ContainsKey(value.ToLocationLogicalId)))
        {
            throw new ArgumentException("A simulation task is invalid.");
        }

        var distanceBySequence = ordered.ToDictionary(
            value => value.SequenceNo,
            value => Distance(value, locations));
        var locationResults = ordered
            .GroupBy(value => value.ToLocationLogicalId)
            .Select(group => AnalyzeLocation(
                locations[group.Key],
                group.OrderBy(value => value.SequenceNo).ToArray(),
                distanceBySequence))
            .OrderByDescending(value => value.IsOverloaded)
            .ThenByDescending(value => value.CongestionTaskSeconds)
            .ThenByDescending(value => value.CapacityUtilizationPercent)
            .ThenBy(value => value.LocationLogicalId)
            .ToArray();

        var completed = ordered
            .Where(value => value.Outcome == SpacePlanningTaskOutcome.Completed)
            .ToArray();
        var eligibleDistances = distanceBySequence.Values
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        var totalDistance = Round(eligibleDistances.Sum(), 6);
        var coverage = Round(
            eligibleDistances.Length * 100m / ordered.Length,
            4);
        var exactWindowHours =
            (parameters.HistoricalToUtc -
                parameters.HistoricalFromUtc).Ticks /
            (decimal)TimeSpan.TicksPerHour;
        var windowHours = Round(exactWindowHours, 9);
        var completedQuantity = Round(
            completed.Sum(value => value.Quantity),
            4);
        var throughput = Throughput(completed, parameters);
        var laborHours = Round(LaborSeconds(ordered) / 3_600m, 6);
        var congestionTaskSeconds = locationResults.Sum(
            value => value.CongestionTaskSeconds);
        var distanceCost = Money(
            totalDistance * parameters.DistanceCostPerMeter);
        var laborCost = Money(laborHours * parameters.LaborCostPerHour);
        var congestionCost = Money(
            congestionTaskSeconds / 3_600m *
            parameters.CongestionCostPerTaskHour);

        return new SpacePlanningSimulationAnalysis(
            ordered.Length,
            completed.Length,
            completedQuantity,
            eligibleDistances.Length,
            totalDistance,
            coverage,
            locationResults.Max(value => value.PeakConcurrentTasks),
            locationResults.Sum(value => value.CongestionSeconds),
            congestionTaskSeconds,
            locationResults.Count(value => value.IsOverloaded),
            locationResults.Max(value => value.CapacityUtilizationPercent),
            windowHours,
            Round(completed.Length / exactWindowHours, 6),
            throughput.PeakTaskRate,
            Round(completedQuantity / exactWindowHours, 6),
            throughput.PeakQuantityRate,
            laborHours,
            distanceCost,
            laborCost,
            congestionCost,
            Money(distanceCost + laborCost + congestionCost),
            locationResults);
    }

    private static SpacePlanningSimulationAnalysisLocation AnalyzeLocation(
        SpacePlanningSimulationLocationInput location,
        IReadOnlyList<SpacePlanningSimulationTaskInput> tasks,
        IReadOnlyDictionary<int, decimal?> distanceBySequence)
    {
        if (location.QuantityCapacity <= 0 ||
            location.ConcurrentTaskCapacity < 1)
        {
            throw new ArgumentException("A location capacity is invalid.");
        }

        var changes = tasks
            .Where(value => value.CompletedAtUtc > value.CreatedAtUtc)
            .SelectMany(value => new[]
            {
                new LoadChange(
                    value.CreatedAtUtc,
                    value.SequenceNo,
                    value.Quantity,
                    IsStart: true),
                new LoadChange(
                    value.CompletedAtUtc,
                    value.SequenceNo,
                    value.Quantity,
                    IsStart: false),
            })
            .GroupBy(value => value.AtUtc)
            .OrderBy(value => value.Key)
            .ToArray();
        var activeCount = 0;
        var activeQuantity = 0m;
        var peakCount = 0;
        var peakQuantity = 0m;
        decimal congestionSeconds = 0;
        decimal congestionTaskSeconds = 0;
        DateTimeOffset? previous = null;
        foreach (var group in changes)
        {
            if (previous.HasValue)
            {
                var seconds = (decimal)(group.Key - previous.Value)
                    .TotalSeconds;
                if (activeCount > location.ConcurrentTaskCapacity)
                {
                    congestionSeconds += seconds;
                    congestionTaskSeconds +=
                        (activeCount - location.ConcurrentTaskCapacity) *
                        seconds;
                }
            }

            foreach (var ending in group.Where(value => !value.IsStart))
            {
                activeCount--;
                activeQuantity -= ending.Quantity;
            }
            foreach (var starting in group.Where(value => value.IsStart))
            {
                activeCount++;
                activeQuantity += starting.Quantity;
            }
            if (activeCount < 0 || activeQuantity < 0)
                throw new InvalidOperationException("The load timeline is invalid.");
            peakCount = Math.Max(peakCount, activeCount);
            peakQuantity = Math.Max(peakQuantity, activeQuantity);
            previous = group.Key;
        }

        var eligible = tasks
            .Select(value => distanceBySequence[value.SequenceNo])
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        var utilization = Round(
            peakQuantity * 100m / location.QuantityCapacity,
            4);
        var roundedCongestionSeconds = WholeSeconds(congestionSeconds);
        var roundedCongestionTaskSeconds = WholeSeconds(
            congestionTaskSeconds);
        var overloaded =
            peakCount > location.ConcurrentTaskCapacity ||
            peakQuantity > location.QuantityCapacity;
        return new SpacePlanningSimulationAnalysisLocation(
            location.LocationLogicalId,
            tasks.Count,
            tasks.Count(value =>
                value.Outcome == SpacePlanningTaskOutcome.Completed),
            Round(tasks.Sum(value => value.Quantity), 4),
            eligible.Length,
            Round(eligible.Sum(), 6),
            location.QuantityCapacity,
            location.ConcurrentTaskCapacity,
            peakCount,
            Round(peakQuantity, 4),
            utilization,
            roundedCongestionSeconds,
            roundedCongestionTaskSeconds,
            overloaded);
    }

    private static decimal? Distance(
        SpacePlanningSimulationTaskInput task,
        IReadOnlyDictionary<Guid, SpacePlanningSimulationLocationInput>
            locations)
    {
        if (!task.FromLocationLogicalId.HasValue ||
            !locations.TryGetValue(task.FromLocationLogicalId.Value,
                out var from) ||
            !locations.TryGetValue(task.ToLocationLogicalId, out var to) ||
            from.FloorLogicalId != to.FloorLogicalId)
        {
            return null;
        }
        if (task.FromLocationLogicalId.Value == task.ToLocationLogicalId)
            return 0;
        if (
            !from.XMillimeters.HasValue ||
            !from.YMillimeters.HasValue ||
            !to.XMillimeters.HasValue ||
            !to.YMillimeters.HasValue)
        {
            return null;
        }

        var dx = (double)(to.XMillimeters.Value - from.XMillimeters.Value);
        var dy = (double)(to.YMillimeters.Value - from.YMillimeters.Value);
        return Round((decimal)(Math.Sqrt(dx * dx + dy * dy) / 1_000d), 6);
    }

    private static decimal LaborSeconds(
        IReadOnlyList<SpacePlanningSimulationTaskInput> tasks)
    {
        var withWorkers = tasks
            .Where(value => value.WorkerToken is not null)
            .GroupBy(value => value.WorkerToken!, StringComparer.Ordinal)
            .Sum(group => UnionSeconds(group.ToArray()));
        var withoutWorkers = tasks
            .Where(value => value.WorkerToken is null)
            .Sum(value =>
                (decimal)(value.CompletedAtUtc - value.CreatedAtUtc)
                    .TotalSeconds);
        return withWorkers + withoutWorkers;
    }

    private static decimal UnionSeconds(
        IReadOnlyList<SpacePlanningSimulationTaskInput> tasks)
    {
        var intervals = tasks
            .Where(value => value.CompletedAtUtc > value.CreatedAtUtc)
            .OrderBy(value => value.CreatedAtUtc)
            .ThenBy(value => value.CompletedAtUtc)
            .ToArray();
        if (intervals.Length == 0)
            return 0;

        var started = intervals[0].CreatedAtUtc;
        var ended = intervals[0].CompletedAtUtc;
        decimal seconds = 0;
        foreach (var interval in intervals.Skip(1))
        {
            if (interval.CreatedAtUtc <= ended)
            {
                ended = interval.CompletedAtUtc > ended
                    ? interval.CompletedAtUtc
                    : ended;
                continue;
            }
            seconds += (decimal)(ended - started).TotalSeconds;
            started = interval.CreatedAtUtc;
            ended = interval.CompletedAtUtc;
        }
        return seconds + (decimal)(ended - started).TotalSeconds;
    }

    private static ThroughputResult Throughput(
        IReadOnlyList<SpacePlanningSimulationTaskInput> completed,
        SpacePlanningSimulationParameters parameters)
    {
        if (completed.Count == 0)
            return new ThroughputResult(0, 0);

        var windowTicks = TimeSpan
            .FromMinutes(parameters.ThroughputWindowMinutes)
            .Ticks;
        var totalTicks = (parameters.HistoricalToUtc -
            parameters.HistoricalFromUtc).Ticks;
        var windowCount = checked((int)Math.Ceiling(
            totalTicks / (decimal)windowTicks));
        var buckets = new Dictionary<int, ThroughputBucket>();
        foreach (var task in completed)
        {
            var offsetTicks = (task.CompletedAtUtc -
                parameters.HistoricalFromUtc).Ticks;
            var index = Math.Min(
                checked((int)(offsetTicks / windowTicks)),
                windowCount - 1);
            buckets.TryGetValue(index, out var current);
            buckets[index] = new ThroughputBucket(
                current.TaskCount + 1,
                current.Quantity + task.Quantity);
        }
        var rateFactor = 60m / parameters.ThroughputWindowMinutes;
        return new ThroughputResult(
            Round(buckets.Values.Max(value => value.TaskCount) *
                rateFactor, 6),
            Round(buckets.Values.Max(value => value.Quantity) *
                rateFactor, 6));
    }

    private static long WholeSeconds(decimal seconds) =>
        decimal.ToInt64(decimal.Round(
            seconds,
            0,
            MidpointRounding.AwayFromZero));

    private static decimal Money(decimal value) => Round(value, 4);

    private static decimal Round(decimal value, int digits) =>
        Math.Round(value, digits, MidpointRounding.AwayFromZero);

    private sealed record LoadChange(
        DateTimeOffset AtUtc,
        int SequenceNo,
        decimal Quantity,
        bool IsStart);

    private readonly record struct ThroughputBucket(
        int TaskCount,
        decimal Quantity);

    private sealed record ThroughputResult(
        decimal PeakTaskRate,
        decimal PeakQuantityRate);
}
