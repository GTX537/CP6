using CP6.Space.Contracts;

namespace CP6.Space.Application;

public interface ISpaceDispatchRecommendationService
{
    Task<GenerateSpaceDispatchRecommendationResponse> GenerateAsync(
        Guid siteId,
        Guid recommendationId,
        GenerateSpaceDispatchRecommendationRequest request,
        CancellationToken cancellationToken = default);

    Task<SpaceDispatchRecommendationDto> GetAsync(
        Guid siteId,
        Guid recommendationId,
        CancellationToken cancellationToken = default);
}

public sealed record SpaceDispatchTaskInput(
    string TaskId,
    string TaskType,
    string Status,
    string? AssignedTo,
    int Priority,
    int ContractVersion,
    int ExecutionVersion,
    string RowVersion,
    string TargetLocationRole,
    bool TargetLocationResolved,
    Guid? LocationLogicalId,
    string? LocationCode,
    bool CodeMatches,
    Guid? FloorLogicalId,
    string? FloorCode,
    string? FloorName,
    int? FloorLevel,
    Guid? ZoneLogicalId,
    string? ZoneCode,
    Guid? RackLogicalId,
    string? RackCode,
    decimal? AnchorXMillimeters,
    decimal? AnchorYMillimeters,
    decimal Quantity,
    string? MaterialNumber);

public sealed record SpaceDispatchPersonInput(
    string PersonKey,
    string SourceId,
    string SourceKind,
    string PersonExternalId,
    bool IsSimulated,
    Guid? LocationLogicalId,
    Guid? FloorLogicalId,
    string? FloorCode,
    Guid? ZoneLogicalId,
    string? ZoneCode,
    decimal? AnchorXMillimeters,
    decimal? AnchorYMillimeters,
    string WorkState,
    DateTimeOffset? PositionOccurredAtUtc,
    DateTimeOffset? PositionReceivedAtUtc,
    DateTimeOffset? WorkStateOccurredAtUtc,
    DateTimeOffset? WorkStateReceivedAtUtc,
    bool PositionIsStale,
    bool WorkStateIsStale);

public sealed record SpaceDispatchAssignmentSet(
    int ExaminedTaskCount,
    int EligibleTaskCount,
    int ExaminedPersonCount,
    int EligiblePersonCount,
    int EligiblePairCount,
    int MatchableAssignmentCount,
    bool IsTruncated,
    SpaceDispatchRecommendationExclusionsDto Exclusions,
    bool ExclusionSamplesTruncated,
    IReadOnlyList<SpaceDispatchRecommendationExclusionSampleDto>
        ExclusionSamples,
    IReadOnlyList<SpaceDispatchRecommendationAssignmentDto> Assignments);

public sealed class SpaceDispatchPairLimitExceededException(int pairCount)
    : InvalidOperationException(
        $"The dispatch recommendation would examine {pairCount} personnel-task pairs.")
{
    public int PairCount { get; } = pairCount;
}

public sealed class SpaceDispatchRecommendationEngine
{
    public const int MaximumExclusionSampleCount = 100;
    public const int MaximumEvaluatedPairCount = 100_000;

    public SpaceDispatchAssignmentSet Generate(
        GenerateSpaceDispatchRecommendationRequest request,
        IReadOnlyCollection<SpaceDispatchTaskInput> tasks,
        IReadOnlyCollection<SpaceDispatchPersonInput> people)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(people);
        if (request.MaximumAssignments is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(request));
        if (tasks.GroupBy(value => value.TaskId, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Task identities must be unique.", nameof(tasks));
        }
        if (people.GroupBy(value => value.PersonKey, StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Person keys must be unique.", nameof(people));
        }

        var samples = new SampleCollector();
        var counts = new Counts();
        var eligibleTasks = tasks
            .OrderBy(value => value.TaskId, StringComparer.Ordinal)
            .Where(value => EligibleTask(value, request, counts, samples))
            .ToArray();
        var eligiblePeople = people
            .OrderBy(value => value.PersonKey, StringComparer.Ordinal)
            .Where(value => EligiblePerson(value, request, counts, samples))
            .ToArray();

        var potentialPairs = checked(eligibleTasks.Length * eligiblePeople.Length);
        if (potentialPairs > MaximumEvaluatedPairCount)
            throw new SpaceDispatchPairLimitExceededException(potentialPairs);

        var pairs = new List<Pair>(potentialPairs);
        foreach (var task in eligibleTasks)
        {
            foreach (var person in eligiblePeople)
            {
                var sameFloor = person.FloorLogicalId == task.FloorLogicalId;
                if (!sameFloor && !request.AllowCrossFloor)
                {
                    counts.CrossFloorPairsRejected++;
                    samples.Add(PairSample(
                        task,
                        person,
                        "CROSS_FLOOR_PAIR_REJECTED"));
                    continue;
                }

                var distance = sameFloor ? DistanceMeters(person, task) : null;
                if (request.MaximumTravelDistanceMeters.HasValue &&
                    !distance.HasValue)
                {
                    counts.DistanceUnverifiablePairsRejected++;
                    samples.Add(PairSample(
                        task,
                        person,
                        "DISTANCE_UNVERIFIABLE_PAIR_REJECTED"));
                    continue;
                }
                if (request.MaximumTravelDistanceMeters.HasValue &&
                    distance > request.MaximumTravelDistanceMeters.Value)
                {
                    counts.DistanceExceededPairsRejected++;
                    samples.Add(PairSample(
                        task,
                        person,
                        "DISTANCE_EXCEEDED_PAIR_REJECTED"));
                    continue;
                }

                var sameZone = sameFloor && person.ZoneLogicalId.HasValue &&
                    task.ZoneLogicalId.HasValue &&
                    person.ZoneLogicalId == task.ZoneLogicalId;
                pairs.Add(new Pair(task, person, sameFloor, sameZone, distance));
            }
        }

        var orderedPairs = pairs
            .OrderBy(value => value.Task.Priority)
            .ThenByDescending(value => value.SameZone)
            .ThenByDescending(value => value.SameFloor)
            .ThenBy(value => value.DistanceMeters.HasValue ? 0 : 1)
            .ThenBy(value => value.DistanceMeters ?? decimal.MaxValue)
            .ThenBy(value => value.Task.TaskId, StringComparer.Ordinal)
            .ThenBy(value => value.Person.PersonKey, StringComparer.Ordinal)
            .Select((value, index) => value with { CostRank = index })
            .ToArray();

        var maximum = MaximumMatching(
            eligibleTasks,
            eligiblePeople,
            orderedPairs);
        counts.EligibleTasksWithoutAssignment =
            eligibleTasks.Length - maximum.Count;
        counts.EligiblePeopleWithoutAssignment =
            eligiblePeople.Length - maximum.Count;
        for (var index = 0; index < eligibleTasks.Length; index++)
        {
            if (maximum.TaskToPerson[index] >= 0)
                continue;
            samples.Add(TaskSample(
                eligibleTasks[index],
                "ELIGIBLE_TASK_WITHOUT_COMPATIBLE_PERSON"));
        }
        var matchedPeople = maximum.TaskToPerson
            .Where(value => value >= 0)
            .ToHashSet();
        for (var index = 0; index < eligiblePeople.Length; index++)
        {
            if (matchedPeople.Contains(index))
                continue;
            samples.Add(PersonSample(
                eligiblePeople[index],
                "ELIGIBLE_PERSON_WITHOUT_COMPATIBLE_TASK"));
        }

        var desired = Math.Min(maximum.Count, request.MaximumAssignments);
        var selected = MinimumCostMatching(
                eligibleTasks,
                eligiblePeople,
                orderedPairs,
                desired)
            .OrderBy(value => value.CostRank)
            .Select((value, index) => Map(value, index + 1))
            .ToArray();

        return new SpaceDispatchAssignmentSet(
            tasks.Count,
            eligibleTasks.Length,
            people.Count,
            eligiblePeople.Length,
            orderedPairs.Length,
            maximum.Count,
            selected.Length < maximum.Count,
            counts.ToDto(),
            samples.IsTruncated,
            samples.Items,
            selected);
    }

    private static bool EligibleTask(
        SpaceDispatchTaskInput value,
        GenerateSpaceDispatchRecommendationRequest request,
        Counts counts,
        SampleCollector samples)
    {
        string? reason = null;
        if (string.IsNullOrWhiteSpace(value.TaskId) ||
            string.IsNullOrWhiteSpace(value.TaskType) ||
            string.IsNullOrWhiteSpace(value.Status) ||
            value.Priority is < 1 or > 4 ||
            value.ContractVersion < 1 ||
            value.ExecutionVersion < 0 ||
            string.IsNullOrWhiteSpace(value.RowVersion) ||
            value.Quantity < 0)
        {
            counts.InvalidTasks++;
            reason = "INVALID_DISPATCH_TASK";
        }
        else if (!value.TargetLocationResolved ||
                 !value.LocationLogicalId.HasValue ||
                 !value.FloorLogicalId.HasValue ||
                 string.IsNullOrWhiteSpace(value.LocationCode) ||
                 string.IsNullOrWhiteSpace(value.FloorCode) ||
                 string.IsNullOrWhiteSpace(value.FloorName) ||
                 !value.FloorLevel.HasValue)
        {
            counts.TaskTargetOutsidePublishedModel++;
            reason = "TASK_TARGET_OUTSIDE_PUBLISHED_MODEL";
        }
        else if (!value.CodeMatches)
        {
            counts.TaskLocationCodeMismatch++;
            reason = "TASK_LOCATION_CODE_MISMATCH";
        }
        else if (!Same(value.Status, "Pending"))
        {
            counts.TasksNotPending++;
            reason = "TASK_NOT_PENDING";
        }
        else if (!string.IsNullOrWhiteSpace(value.AssignedTo))
        {
            counts.TasksAlreadyAssigned++;
            reason = "TASK_ALREADY_ASSIGNED";
        }
        else if ((request.TaskType is not null &&
                  !Same(value.TaskType, request.TaskType)) ||
                 (request.TaskFloorLogicalId.HasValue &&
                  value.FloorLogicalId != request.TaskFloorLogicalId) ||
                 (request.TaskZoneLogicalId.HasValue &&
                  value.ZoneLogicalId != request.TaskZoneLogicalId))
        {
            counts.TasksOutsideRequestedScope++;
            reason = "TASK_OUTSIDE_REQUESTED_SCOPE";
        }
        if (reason is null)
            return true;
        samples.Add(TaskSample(value, reason));
        return false;
    }

    private static bool EligiblePerson(
        SpaceDispatchPersonInput value,
        GenerateSpaceDispatchRecommendationRequest request,
        Counts counts,
        SampleCollector samples)
    {
        string? reason = null;
        if (value.PositionIsStale ||
            !value.PositionOccurredAtUtc.HasValue ||
            !value.PositionReceivedAtUtc.HasValue)
        {
            counts.PeoplePositionStale++;
            reason = "PERSON_POSITION_STALE";
        }
        else if (value.WorkStateIsStale ||
                 !value.WorkStateOccurredAtUtc.HasValue ||
                 !value.WorkStateReceivedAtUtc.HasValue)
        {
            counts.PeopleWorkStateStale++;
            reason = "PERSON_WORK_STATE_STALE";
        }
        else if (!Same(value.WorkState, "Idle"))
        {
            counts.PeopleNotIdle++;
            reason = "PERSON_NOT_IDLE";
        }
        else if (value.IsSimulated && !request.IncludeSimulatedPersonnel)
        {
            counts.PeopleSimulatedExcluded++;
            reason = "SIMULATED_PERSON_EXCLUDED";
        }
        else if (!value.FloorLogicalId.HasValue ||
                 !value.AnchorXMillimeters.HasValue ||
                 !value.AnchorYMillimeters.HasValue ||
                 string.IsNullOrWhiteSpace(value.FloorCode))
        {
            counts.PeopleWithoutResolvablePosition++;
            reason = "PERSON_POSITION_UNRESOLVED";
        }
        if (reason is null)
            return true;
        samples.Add(PersonSample(value, reason));
        return false;
    }

    private static decimal? DistanceMeters(
        SpaceDispatchPersonInput person,
        SpaceDispatchTaskInput task)
    {
        if (!person.AnchorXMillimeters.HasValue ||
            !person.AnchorYMillimeters.HasValue ||
            !task.AnchorXMillimeters.HasValue ||
            !task.AnchorYMillimeters.HasValue)
        {
            return null;
        }
        var dx = person.AnchorXMillimeters.Value - task.AnchorXMillimeters.Value;
        var dy = person.AnchorYMillimeters.Value - task.AnchorYMillimeters.Value;
        return Math.Round(
            (decimal)Math.Sqrt((double)(dx * dx + dy * dy)) / 1_000m,
            3,
            MidpointRounding.AwayFromZero);
    }

    private static MaximumMatchingResult MaximumMatching(
        IReadOnlyList<SpaceDispatchTaskInput> tasks,
        IReadOnlyList<SpaceDispatchPersonInput> people,
        IReadOnlyList<Pair> pairs)
    {
        var taskIndex = tasks
            .Select((value, index) => (value.TaskId, index))
            .ToDictionary(value => value.TaskId, value => value.index,
                StringComparer.OrdinalIgnoreCase);
        var personIndex = people
            .Select((value, index) => (value.PersonKey, index))
            .ToDictionary(value => value.PersonKey, value => value.index,
                StringComparer.Ordinal);
        var adjacency = Enumerable.Range(0, tasks.Count)
            .Select(_ => new List<int>())
            .ToArray();
        foreach (var pair in pairs)
            adjacency[taskIndex[pair.Task.TaskId]].Add(personIndex[pair.Person.PersonKey]);

        var taskToPerson = Enumerable.Repeat(-1, tasks.Count).ToArray();
        var personToTask = Enumerable.Repeat(-1, people.Count).ToArray();
        var distance = new int[tasks.Count];
        var count = 0;
        while (Bfs())
        {
            for (var task = 0; task < tasks.Count; task++)
            {
                if (taskToPerson[task] < 0 && Dfs(task))
                    count++;
            }
        }
        return new MaximumMatchingResult(count, taskToPerson);

        bool Bfs()
        {
            var queue = new Queue<int>();
            for (var task = 0; task < tasks.Count; task++)
            {
                distance[task] = taskToPerson[task] < 0 ? 0 : -1;
                if (taskToPerson[task] < 0)
                    queue.Enqueue(task);
            }
            var found = false;
            while (queue.Count > 0)
            {
                var task = queue.Dequeue();
                foreach (var person in adjacency[task])
                {
                    var nextTask = personToTask[person];
                    if (nextTask < 0)
                    {
                        found = true;
                    }
                    else if (distance[nextTask] < 0)
                    {
                        distance[nextTask] = distance[task] + 1;
                        queue.Enqueue(nextTask);
                    }
                }
            }
            return found;
        }

        bool Dfs(int task)
        {
            foreach (var person in adjacency[task])
            {
                var nextTask = personToTask[person];
                if (nextTask < 0 ||
                    distance[nextTask] == distance[task] + 1 && Dfs(nextTask))
                {
                    taskToPerson[task] = person;
                    personToTask[person] = task;
                    return true;
                }
            }
            distance[task] = -1;
            return false;
        }
    }

    private static IReadOnlyList<Pair> MinimumCostMatching(
        IReadOnlyList<SpaceDispatchTaskInput> tasks,
        IReadOnlyList<SpaceDispatchPersonInput> people,
        IReadOnlyList<Pair> pairs,
        int desiredFlow)
    {
        if (desiredFlow == 0)
            return [];
        var source = 0;
        var firstTask = 1;
        var firstPerson = firstTask + tasks.Count;
        var sink = firstPerson + people.Count;
        var graph = Enumerable.Range(0, sink + 1)
            .Select(_ => new List<FlowEdge>())
            .ToArray();
        var taskIndex = tasks
            .Select((value, index) => (value.TaskId, index))
            .ToDictionary(value => value.TaskId, value => value.index,
                StringComparer.OrdinalIgnoreCase);
        var personIndex = people
            .Select((value, index) => (value.PersonKey, index))
            .ToDictionary(value => value.PersonKey, value => value.index,
                StringComparer.Ordinal);
        for (var index = 0; index < tasks.Count; index++)
            AddEdge(source, firstTask + index, 1, 0, null);
        foreach (var pair in pairs.OrderBy(value => value.CostRank))
        {
            AddEdge(
                firstTask + taskIndex[pair.Task.TaskId],
                firstPerson + personIndex[pair.Person.PersonKey],
                1,
                pair.CostRank,
                pair);
        }
        for (var index = 0; index < people.Count; index++)
            AddEdge(firstPerson + index, sink, 1, 0, null);

        var flow = 0;
        while (flow < desiredFlow)
        {
            var distance = Enumerable.Repeat(long.MaxValue, graph.Length).ToArray();
            var previousNode = Enumerable.Repeat(-1, graph.Length).ToArray();
            var previousEdge = Enumerable.Repeat(-1, graph.Length).ToArray();
            var queued = new bool[graph.Length];
            var queue = new Queue<int>();
            distance[source] = 0;
            queue.Enqueue(source);
            queued[source] = true;
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                queued[node] = false;
                for (var index = 0; index < graph[node].Count; index++)
                {
                    var edge = graph[node][index];
                    if (edge.Capacity == 0 || distance[node] == long.MaxValue)
                        continue;
                    var candidate = distance[node] + edge.Cost;
                    if (candidate >= distance[edge.To])
                        continue;
                    distance[edge.To] = candidate;
                    previousNode[edge.To] = node;
                    previousEdge[edge.To] = index;
                    if (!queued[edge.To])
                    {
                        queued[edge.To] = true;
                        queue.Enqueue(edge.To);
                    }
                }
            }
            if (previousNode[sink] < 0)
                throw new InvalidOperationException(
                    "Maximum-cardinality dispatch matching could not be reconstructed.");
            for (var node = sink; node != source; node = previousNode[node])
            {
                var edge = graph[previousNode[node]][previousEdge[node]];
                edge.Capacity--;
                graph[node][edge.Reverse].Capacity++;
            }
            flow++;
        }
        return graph
            .SelectMany(edges => edges)
            .Where(edge => edge.Pair is not null && edge.Capacity == 0)
            .Select(edge => edge.Pair!)
            .ToArray();

        void AddEdge(int from, int to, int capacity, int cost, Pair? pair)
        {
            var forward = new FlowEdge(to, graph[to].Count, capacity, cost, pair);
            var reverse = new FlowEdge(from, graph[from].Count, 0, -cost, null);
            graph[from].Add(forward);
            graph[to].Add(reverse);
        }
    }

    private static SpaceDispatchRecommendationAssignmentDto Map(
        Pair value,
        int rank)
    {
        var rules = new List<string>
        {
            "TASK_IS_PENDING_AND_UNASSIGNED",
            "TASK_CONCURRENCY_EVIDENCE_CAPTURED",
            "PERSON_POSITION_AND_WORK_STATE_ARE_FRESH",
            "PERSON_STATE_IS_IDLE",
            "ONE_PERSON_ONE_TASK_IN_RECOMMENDATION",
            "TASK_PRIORITY_USED_BEFORE_PROXIMITY",
        };
        if (value.SameZone)
            rules.Add("PERSON_AND_TARGET_ARE_IN_SAME_ZONE");
        else if (value.SameFloor)
            rules.Add("PERSON_AND_TARGET_ARE_ON_SAME_FLOOR");
        else
            rules.Add("CROSS_FLOOR_EXPLICITLY_ALLOWED");
        if (value.DistanceMeters.HasValue)
            rules.Add("PUBLISHED_GEOMETRIC_DISTANCE_TO_FIRST_ACTION_LOCATION");

        var task = value.Task;
        var person = value.Person;
        return new SpaceDispatchRecommendationAssignmentDto(
            rank,
            task.TaskId,
            task.TaskType,
            task.Status,
            task.Priority,
            task.ContractVersion,
            task.ExecutionVersion,
            task.RowVersion,
            task.TargetLocationRole,
            task.LocationLogicalId!.Value,
            task.LocationCode!,
            task.FloorLogicalId!.Value,
            task.FloorCode!,
            task.FloorName!,
            task.FloorLevel!.Value,
            task.ZoneLogicalId,
            task.ZoneCode,
            task.RackLogicalId,
            task.RackCode,
            task.Quantity,
            task.MaterialNumber,
            person.PersonKey,
            person.SourceId,
            person.SourceKind,
            person.PersonExternalId,
            person.LocationLogicalId,
            person.FloorLogicalId!.Value,
            person.ZoneLogicalId,
            person.PositionOccurredAtUtc!.Value,
            person.PositionReceivedAtUtc!.Value,
            person.WorkStateOccurredAtUtc!.Value,
            person.WorkStateReceivedAtUtc!.Value,
            value.SameFloor,
            value.SameZone,
            value.DistanceMeters,
            rules);
    }

    private static SpaceDispatchRecommendationExclusionSampleDto TaskSample(
        SpaceDispatchTaskInput value,
        string reason) =>
        new(
            "Task",
            reason,
            value.TaskId,
            null,
            value.LocationCode,
            value.FloorLogicalId,
            value.FloorCode,
            value.ZoneLogicalId,
            value.ZoneCode);

    private static SpaceDispatchRecommendationExclusionSampleDto PersonSample(
        SpaceDispatchPersonInput value,
        string reason) =>
        new(
            "Person",
            reason,
            null,
            value.PersonKey,
            null,
            value.FloorLogicalId,
            value.FloorCode,
            value.ZoneLogicalId,
            value.ZoneCode);

    private static SpaceDispatchRecommendationExclusionSampleDto PairSample(
        SpaceDispatchTaskInput task,
        SpaceDispatchPersonInput person,
        string reason) =>
        new(
            "Pair",
            reason,
            task.TaskId,
            person.PersonKey,
            task.LocationCode,
            task.FloorLogicalId,
            task.FloorCode,
            task.ZoneLogicalId,
            task.ZoneCode);

    private static bool Same(string? first, string? second) =>
        string.Equals(
            first?.Trim(),
            second?.Trim(),
            StringComparison.OrdinalIgnoreCase);

    private sealed record Pair(
        SpaceDispatchTaskInput Task,
        SpaceDispatchPersonInput Person,
        bool SameFloor,
        bool SameZone,
        decimal? DistanceMeters,
        int CostRank = 0);

    private sealed record MaximumMatchingResult(
        int Count,
        int[] TaskToPerson);

    private sealed class FlowEdge(
        int to,
        int reverse,
        int capacity,
        int cost,
        Pair? pair)
    {
        public int To { get; } = to;
        public int Reverse { get; } = reverse;
        public int Capacity { get; set; } = capacity;
        public int Cost { get; } = cost;
        public Pair? Pair { get; } = pair;
    }

    private sealed class SampleCollector
    {
        private readonly List<SpaceDispatchRecommendationExclusionSampleDto>
            _items = [];
        private int _count;

        public IReadOnlyList<SpaceDispatchRecommendationExclusionSampleDto>
            Items => _items;
        public bool IsTruncated => _count > _items.Count;

        public void Add(SpaceDispatchRecommendationExclusionSampleDto value)
        {
            _count++;
            if (_items.Count < MaximumExclusionSampleCount)
                _items.Add(value);
        }
    }

    private sealed class Counts
    {
        public int TasksOutsideRequestedScope { get; set; }
        public int TasksNotPending { get; set; }
        public int TasksAlreadyAssigned { get; set; }
        public int InvalidTasks { get; set; }
        public int TaskTargetOutsidePublishedModel { get; set; }
        public int TaskLocationCodeMismatch { get; set; }
        public int EligibleTasksWithoutAssignment { get; set; }
        public int PeoplePositionStale { get; set; }
        public int PeopleWorkStateStale { get; set; }
        public int PeopleNotIdle { get; set; }
        public int PeopleSimulatedExcluded { get; set; }
        public int PeopleWithoutResolvablePosition { get; set; }
        public int EligiblePeopleWithoutAssignment { get; set; }
        public int CrossFloorPairsRejected { get; set; }
        public int DistanceUnverifiablePairsRejected { get; set; }
        public int DistanceExceededPairsRejected { get; set; }

        public SpaceDispatchRecommendationExclusionsDto ToDto() =>
            new(
                TasksOutsideRequestedScope,
                TasksNotPending,
                TasksAlreadyAssigned,
                InvalidTasks,
                TaskTargetOutsidePublishedModel,
                TaskLocationCodeMismatch,
                EligibleTasksWithoutAssignment,
                PeoplePositionStale,
                PeopleWorkStateStale,
                PeopleNotIdle,
                PeopleSimulatedExcluded,
                PeopleWithoutResolvablePosition,
                EligiblePeopleWithoutAssignment,
                CrossFloorPairsRejected,
                DistanceUnverifiablePairsRejected,
                DistanceExceededPairsRejected);
    }
}
