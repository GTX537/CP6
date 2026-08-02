using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class SpaceDispatchRecommendationService(
    SpaceContext context,
    ISpaceWmsRuntimeService runtime,
    ISpaceExecutionContext execution,
    ISpaceClock clock,
    ISpaceDesignAccessEvaluator access,
    SpacePersonnelRuntimeOptions personnelOptions,
    SpaceDispatchRecommendationEngine engine)
    : ISpaceDispatchRecommendationService
{
    public const string DefinitionVersion = "space-dispatch-v1";
    public const int MaximumAssignmentCount = 100;
    public const int MaximumTaskCount = 10_000;
    public const int MaximumPersonCount = 10_000;
    public const int MaximumPersonnelSourceCount = 100;

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    public async Task<GenerateSpaceDispatchRecommendationResponse>
        GenerateAsync(
            Guid siteId,
            Guid recommendationId,
            GenerateSpaceDispatchRecommendationRequest request,
            CancellationToken cancellationToken = default)
    {
        EnsureInternalExecution();
        EnsureIdentity(siteId, "siteId");
        EnsureIdentity(recommendationId, "recommendationId");
        ArgumentNullException.ThrowIfNull(request);
        personnelOptions.Validate();
        var normalized = Normalize(request);
        var requestHash = RequestHash(siteId, normalized);
        access.EnsureSiteAccess(siteId, write: false);

        var existing = await context.DispatchRecommendations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.Id == recommendationId,
                cancellationToken);
        if (existing is not null)
            return Duplicate(existing, siteId, requestHash);

        var tasks = await runtime.QueryDispatchTasksAsync(
            siteId,
            cancellationToken);
        EnsureAvailable(tasks.Source);
        if (tasks.SiteId != siteId ||
            tasks.PublishedVersionId == Guid.Empty ||
            string.IsNullOrWhiteSpace(tasks.WarehouseCode))
        {
            throw ContractViolation(
                "The dispatch-task snapshot does not identify the requested Published warehouse.");
        }
        if (tasks.Items.Count > MaximumTaskCount)
        {
            throw EvidenceLimit(
                $"More than {MaximumTaskCount} active dispatch tasks matched the site.");
        }

        var spatial = await LoadSpatialAsync(
            tasks.PublishedVersionId,
            cancellationToken);
        ValidateRequestedScope(normalized, spatial.Floors, spatial.Zones);
        var now = RequireUtcNow();
        var states = await context.PersonnelStates
            .AsNoTracking()
            .Where(value => value.SiteId == siteId)
            .OrderBy(value => value.SourceId)
            .ThenBy(value => value.PersonExternalId)
            .ThenBy(value => value.Id)
            .Take(MaximumPersonCount + 1)
            .ToArrayAsync(cancellationToken);
        if (states.Length > MaximumPersonCount)
        {
            throw EvidenceLimit(
                $"More than {MaximumPersonCount} current personnel states matched the site.");
        }

        var taskInputs = tasks.Items
            .Select(MapTask)
            .ToArray();
        var personInputs = states
            .Select(value => MapPerson(value, spatial, siteId, now))
            .ToArray();
        SpaceDispatchAssignmentSet assignmentSet;
        try
        {
            assignmentSet = engine.Generate(
                normalized,
                taskInputs,
                personInputs);
        }
        catch (SpaceDispatchPairLimitExceededException exception)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.DispatchRecommendationPairLimit,
                422,
                "The dispatch recommendation pair limit was exceeded.",
                $"The request would examine {exception.PairCount} pairs; the limit is {SpaceDispatchRecommendationEngine.MaximumEvaluatedPairCount}.",
                "narrow-dispatch-task-scope-or-distance");
        }
        ValidateAssignmentSet(assignmentSet);

        var sources = new SpaceDispatchRecommendationSourcesDto(
            tasks.Source,
            PersonnelSource(states, now));
        var limitations = Limitations(normalized, sources);
        var outcome = assignmentSet.Assignments.Count == 0
            ? "NoAssignment"
            : "AssignmentsGenerated";
        var entity = SpaceDispatchRecommendation.Create(
            execution.TenantId,
            recommendationId,
            new SpaceDispatchRecommendationData(
                siteId,
                tasks.PublishedVersionId,
                tasks.WarehouseCode,
                now,
                execution.ActorId,
                DefinitionVersion,
                outcome,
                assignmentSet.ExaminedTaskCount,
                assignmentSet.EligibleTaskCount,
                assignmentSet.ExaminedPersonCount,
                assignmentSet.EligiblePersonCount,
                assignmentSet.EligiblePairCount,
                assignmentSet.MatchableAssignmentCount,
                assignmentSet.Assignments.Count,
                assignmentSet.IsTruncated,
                assignmentSet.ExclusionSamplesTruncated,
                JsonSerializer.Serialize(normalized, Json),
                JsonSerializer.Serialize(sources, Json),
                JsonSerializer.Serialize(assignmentSet.Exclusions, Json),
                JsonSerializer.Serialize(assignmentSet.ExclusionSamples, Json),
                JsonSerializer.Serialize(assignmentSet.Assignments, Json),
                JsonSerializer.Serialize(limitations, Json),
                requestHash));
        context.DispatchRecommendations.Add(entity);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            context.Entry(entity).State = EntityState.Detached;
            var concurrent = await context.DispatchRecommendations
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    value => value.Id == recommendationId,
                    cancellationToken);
            if (concurrent is null)
                throw;
            return Duplicate(concurrent, siteId, requestHash);
        }
        return new GenerateSpaceDispatchRecommendationResponse(
            "Generated",
            Map(entity));
    }

    public async Task<SpaceDispatchRecommendationDto> GetAsync(
        Guid siteId,
        Guid recommendationId,
        CancellationToken cancellationToken = default)
    {
        EnsureInternalExecution();
        EnsureIdentity(siteId, "siteId");
        EnsureIdentity(recommendationId, "recommendationId");
        access.EnsureSiteAccess(siteId, write: false);
        var value = await context.DispatchRecommendations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == recommendationId && item.SiteId == siteId,
                cancellationToken)
            ?? throw NotFound();
        return Map(value);
    }

    private GenerateSpaceDispatchRecommendationResponse Duplicate(
        SpaceDispatchRecommendation existing,
        Guid siteId,
        string requestHash)
    {
        if (existing.SiteId != siteId ||
            !string.Equals(existing.RequestHash, requestHash,
                StringComparison.Ordinal))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.DispatchRecommendationConflict,
                409,
                "The dispatch recommendation identity is already in use.",
                recoveryAction: "use-new-recommendation-id");
        }
        return new GenerateSpaceDispatchRecommendationResponse(
            "Duplicate",
            Map(existing));
    }

    private static GenerateSpaceDispatchRecommendationRequest Normalize(
        GenerateSpaceDispatchRecommendationRequest request)
    {
        if (request.TaskFloorLogicalId == Guid.Empty)
            throw Invalid("taskFloorLogicalId", "Identity cannot be empty.");
        if (request.TaskZoneLogicalId == Guid.Empty)
            throw Invalid("taskZoneLogicalId", "Identity cannot be empty.");
        if (request.MaximumTravelDistanceMeters <= 0)
        {
            throw Invalid(
                "maximumTravelDistanceMeters",
                "Maximum travel distance must be positive.");
        }
        if (request.MaximumAssignments is < 1 or > MaximumAssignmentCount)
        {
            throw Invalid(
                "maximumAssignments",
                $"Maximum assignments must be between 1 and {MaximumAssignmentCount}.");
        }
        return request with
        {
            TaskType = OptionalIdentifier(request.TaskType, 100, "taskType"),
        };
    }

    private static void ValidateRequestedScope(
        GenerateSpaceDispatchRecommendationRequest request,
        IReadOnlyDictionary<Guid, FloorRow> floors,
        IReadOnlyDictionary<Guid, ZoneRow> zones)
    {
        if (request.TaskFloorLogicalId.HasValue &&
            !floors.ContainsKey(request.TaskFloorLogicalId.Value))
        {
            throw Invalid(
                "taskFloorLogicalId",
                "The requested floor is not active in the Published version.");
        }
        if (!request.TaskZoneLogicalId.HasValue)
            return;
        if (!zones.TryGetValue(request.TaskZoneLogicalId.Value, out var zone))
        {
            throw Invalid(
                "taskZoneLogicalId",
                "The requested zone is not active in the Published version.");
        }
        if (request.TaskFloorLogicalId.HasValue &&
            zone.FloorLogicalId != request.TaskFloorLogicalId.Value)
        {
            throw Invalid(
                "taskZoneLogicalId",
                "The requested zone is outside the requested floor.");
        }
    }

    private async Task<SpatialMap> LoadSpatialAsync(
        Guid publishedVersionId,
        CancellationToken cancellationToken)
    {
        var floors = await context.FloorRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == publishedVersionId &&
                value.LifecycleState == SpaceLifecycleState.Active)
            .Select(value => new FloorRow(
                value.LogicalId,
                value.FloorCode,
                value.Name,
                value.Level))
            .ToArrayAsync(cancellationToken);
        var zones = await context.ZoneRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == publishedVersionId &&
                value.LifecycleState == SpaceLifecycleState.Active)
            .Select(value => new ZoneRow(
                value.LogicalId,
                value.FloorLogicalId,
                value.ZoneCode))
            .ToArrayAsync(cancellationToken);
        var racks = await context.RackRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == publishedVersionId &&
                value.LifecycleState == SpaceLifecycleState.Active)
            .Select(value => new RackRow(
                value.LogicalId,
                value.FloorLogicalId,
                value.ZoneLogicalId,
                value.X,
                value.Y,
                value.RotationZ))
            .ToArrayAsync(cancellationToken);
        var levels = await context.RackLevelRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == publishedVersionId &&
                value.LifecycleState == SpaceLifecycleState.Active)
            .Select(value => new RackLevelRow(
                value.RackLogicalId,
                value.LevelNo,
                value.CellWidth,
                value.CellDepth))
            .ToArrayAsync(cancellationToken);
        var locations = await context.LocationRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == publishedVersionId &&
                value.LifecycleState == SpaceLifecycleState.Active)
            .Select(value => new LocationRow(
                value.LogicalId,
                value.FloorLogicalId,
                value.RackLogicalId,
                value.ColumnNo,
                value.LevelNo,
                value.DepthNo))
            .ToArrayAsync(cancellationToken);

        var floorById = floors.ToDictionary(value => value.LogicalId);
        var zoneById = zones.ToDictionary(value => value.LogicalId);
        var rackById = racks.ToDictionary(value => value.LogicalId);
        var levelByPosition = levels.ToDictionary(
            value => (value.RackLogicalId, value.LevelNo));
        var locationById = new Dictionary<Guid, LocationSpatialRow>();
        foreach (var value in locations)
        {
            if (!floorById.TryGetValue(value.FloorLogicalId, out var floor))
            {
                throw ContractViolation(
                    "An active location references a missing active floor.");
            }
            Guid? zoneId = null;
            string? zoneCode = null;
            decimal? x = null;
            decimal? y = null;
            if (value.RackLogicalId.HasValue)
            {
                if (!rackById.TryGetValue(value.RackLogicalId.Value, out var rack) ||
                    rack.FloorLogicalId != value.FloorLogicalId ||
                    !zoneById.TryGetValue(rack.ZoneLogicalId, out var zone) ||
                    zone.FloorLogicalId != value.FloorLogicalId ||
                    !levelByPosition.TryGetValue(
                        (rack.LogicalId, value.LevelNo), out var level))
                {
                    throw ContractViolation(
                        "An active location has inconsistent rack, level, zone, or floor metadata.");
                }
                zoneId = zone.LogicalId;
                zoneCode = zone.ZoneCode;
                var angle = (double)rack.RotationZ * Math.PI / 180d;
                var localX = (value.ColumnNo - 0.5m) * level.CellWidth;
                var localY = (value.DepthNo - 0.5m) * level.CellDepth;
                x = rack.X + localX * (decimal)Math.Cos(angle) -
                    localY * (decimal)Math.Sin(angle);
                y = rack.Y + localX * (decimal)Math.Sin(angle) +
                    localY * (decimal)Math.Cos(angle);
            }
            locationById.Add(
                value.LogicalId,
                new LocationSpatialRow(
                    value.LogicalId,
                    floor.LogicalId,
                    floor.FloorCode,
                    zoneId,
                    zoneCode,
                    x,
                    y));
        }
        return new SpatialMap(floorById, zoneById, locationById);
    }

    private SpaceDispatchPersonInput MapPerson(
        SpacePersonnelCurrentState value,
        SpatialMap spatial,
        Guid siteId,
        DateTime now)
    {
        var floorId = value.FloorLogicalId;
        var locationId = value.LocationLogicalId;
        Guid? zoneId = null;
        string? zoneCode = null;
        string? floorCode = null;
        decimal? x = value.XMillimeters;
        decimal? y = value.YMillimeters;
        var resolvable = false;
        if (floorId.HasValue &&
            spatial.Floors.TryGetValue(floorId.Value, out var floor))
        {
            resolvable = true;
            floorCode = floor.FloorCode;
        }
        if (locationId.HasValue)
        {
            if (!spatial.Locations.TryGetValue(locationId.Value, out var location) ||
                floorId.HasValue && floorId.Value != location.FloorLogicalId)
            {
                resolvable = false;
            }
            else
            {
                floorId ??= location.FloorLogicalId;
                floorCode = location.FloorCode;
                zoneId = location.ZoneLogicalId;
                zoneCode = location.ZoneCode;
                x ??= location.AnchorXMillimeters;
                y ??= location.AnchorYMillimeters;
                resolvable = true;
            }
        }
        if (!resolvable || !floorId.HasValue ||
            !spatial.Floors.ContainsKey(floorId.Value))
        {
            floorId = null;
            floorCode = null;
            zoneId = null;
            zoneCode = null;
            x = null;
            y = null;
        }
        var threshold = personnelOptions.CurrentFreshness;
        var positionStale = !value.PositionOccurredAtUtc.HasValue ||
            now - value.PositionOccurredAtUtc.Value > threshold;
        var workStateStale = !value.WorkStateOccurredAtUtc.HasValue ||
            now - value.WorkStateOccurredAtUtc.Value > threshold;
        return new SpaceDispatchPersonInput(
            PersonKey(siteId, value.SourceId, value.PersonExternalId),
            value.SourceId,
            value.SourceKind.ToString(),
            value.PersonExternalId,
            value.SourceKind == SpacePersonnelSourceKind.Simulated,
            locationId,
            floorId,
            floorCode,
            zoneId,
            zoneCode,
            x,
            y,
            value.WorkState.ToString(),
            Offset(value.PositionOccurredAtUtc),
            Offset(value.PositionReceivedAtUtc),
            Offset(value.WorkStateOccurredAtUtc),
            Offset(value.WorkStateReceivedAtUtc),
            positionStale,
            workStateStale);
    }

    private static SpaceDispatchTaskInput MapTask(
        SpaceWmsRuntimeDispatchTaskItemDto value) =>
        new(
            value.TaskId,
            value.TaskType,
            value.Status,
            value.AssignedTo,
            value.Priority,
            value.ContractVersion,
            value.ExecutionVersion,
            value.RowVersion,
            value.TargetLocationRole,
            value.TargetLocationResolved,
            value.LocationLogicalId,
            value.SpaceLocationCode,
            value.CodeMatches,
            value.FloorLogicalId,
            value.FloorCode,
            value.FloorName,
            value.FloorLevel,
            value.ZoneLogicalId,
            value.ZoneCode,
            value.RackLogicalId,
            value.RackCode,
            DecimalAnchor(value.AnchorXMillimeters),
            DecimalAnchor(value.AnchorYMillimeters),
            value.Quantity,
            value.MaterialNumber);

    private static decimal? DecimalAnchor(double? value)
    {
        if (!value.HasValue)
            return null;
        if (!double.IsFinite(value.Value))
            throw ContractViolation("A dispatch-task anchor is not finite.");
        try
        {
            return Convert.ToDecimal(value.Value, CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            throw ContractViolation("A dispatch-task anchor is outside decimal range.");
        }
    }

    private SpaceDispatchPersonnelSourceDto PersonnelSource(
        IReadOnlyCollection<SpacePersonnelCurrentState> states,
        DateTime now)
    {
        var groups = states
            .GroupBy(value => new { value.SourceId, value.SourceKind })
            .OrderBy(value => value.Key.SourceId, StringComparer.Ordinal)
            .ThenBy(value => value.Key.SourceKind)
            .Select(group => new SpaceDispatchPersonnelSourceItemDto(
                group.Key.SourceId,
                group.Key.SourceKind.ToString(),
                group.Count(),
                Offset(Max(group.Select(value => value.PositionOccurredAtUtc))),
                Offset(Max(group.Select(value => value.PositionReceivedAtUtc))),
                Offset(Max(group.Select(value => value.WorkStateOccurredAtUtc))),
                Offset(Max(group.Select(value => value.WorkStateReceivedAtUtc)))))
            .ToArray();
        return new SpaceDispatchPersonnelSourceDto(
            Utc(now),
            checked((int)personnelOptions.CurrentFreshness.TotalSeconds),
            states.Count,
            states.Count(value =>
                value.SourceKind == SpacePersonnelSourceKind.Real),
            states.Count(value =>
                value.SourceKind == SpacePersonnelSourceKind.Simulated),
            groups.Length > MaximumPersonnelSourceCount,
            groups.Take(MaximumPersonnelSourceCount).ToArray());
    }

    private static DateTime? Max(IEnumerable<DateTime?> values) =>
        values.Where(value => value.HasValue)
            .Select(value => value!.Value)
            .Cast<DateTime?>()
            .Max();

    private static IReadOnlyList<string> Limitations(
        GenerateSpaceDispatchRecommendationRequest request,
        SpaceDispatchRecommendationSourcesDto sources)
    {
        var values = new List<string>
        {
            "PERSON_SKILLS_CERTIFICATIONS_SHIFTS_AND_WORK_HOURS_ARE_NOT_AVAILABLE",
            "TASK_DUE_TIME_SERVICE_LEVEL_AND_DEVICE_ELIGIBILITY_ARE_NOT_USED",
            "TASK_AND_PERSON_OBSERVATIONS_ARE_NOT_ATOMIC",
            "PERSON_AND_TASK_STATE_CAN_CHANGE_AFTER_GENERATION",
            "DISTANCE_IS_SAME_FLOOR_PUBLISHED_GEOMETRY_NOT_ROUTE_OR_TRAVEL_TIME",
            "RECOMMENDATION_REQUIRES_REVALIDATION_BEFORE_APPROVAL_OR_EXECUTION",
            "RECOMMENDATION_DOES_NOT_APPROVE_ASSIGN_CLAIM_START_OR_WRITE_TASKS",
        };
        if (request.AllowCrossFloor)
            values.Add("CROSS_FLOOR_ASSIGNMENTS_HAVE_NO_VERTICAL_ROUTE_DISTANCE");
        if (request.IncludeSimulatedPersonnel)
            values.Add("SIMULATED_PERSONNEL_EXPLICITLY_INCLUDED");
        if (sources.DispatchTasks.IsSimulated)
            values.Add("SIMULATED_DISPATCH_TASK_SOURCE");
        if (sources.Personnel.SourcesTruncated)
            values.Add("PERSONNEL_SOURCE_GROUPS_TRUNCATED");
        return values;
    }

    private static string RequestHash(
        Guid siteId,
        GenerateSpaceDispatchRecommendationRequest request)
    {
        var values = new[]
        {
            DefinitionVersion,
            siteId.ToString("D"),
            request.TaskType ?? string.Empty,
            request.TaskFloorLogicalId?.ToString("D") ?? string.Empty,
            request.TaskZoneLogicalId?.ToString("D") ?? string.Empty,
            request.AllowCrossFloor ? "1" : "0",
            request.MaximumTravelDistanceMeters.HasValue
                ? request.MaximumTravelDistanceMeters.Value.ToString(
                    "G29", CultureInfo.InvariantCulture)
                : string.Empty,
            request.IncludeSimulatedPersonnel ? "1" : "0",
            request.MaximumAssignments.ToString(CultureInfo.InvariantCulture),
        };
        return Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(string.Join("\n", values))))
            .ToLowerInvariant();
    }

    private string PersonKey(
        Guid siteId,
        string sourceId,
        string personExternalId) =>
        Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(string.Join(
                        "\n",
                        execution.TenantId.ToString("D"),
                        siteId.ToString("D"),
                        sourceId,
                        personExternalId))))
            .ToLowerInvariant();

    private static SpaceDispatchRecommendationDto Map(
        SpaceDispatchRecommendation value)
    {
        var request = Deserialize<GenerateSpaceDispatchRecommendationRequest>(
            value.RequestJson, "request");
        var sources = Deserialize<SpaceDispatchRecommendationSourcesDto>(
            value.SourcesJson, "sources");
        var exclusions = Deserialize<SpaceDispatchRecommendationExclusionsDto>(
            value.ExclusionsJson, "exclusions");
        var samples = Deserialize<SpaceDispatchRecommendationExclusionSampleDto[]>(
            value.ExclusionSamplesJson, "exclusion samples");
        var assignments = Deserialize<SpaceDispatchRecommendationAssignmentDto[]>(
            value.AssignmentsJson, "assignments");
        var limitations = Deserialize<string[]>(
            value.LimitationsJson, "limitations");
        ValidatePersistedEvidence(value, exclusions, samples, assignments);
        return new SpaceDispatchRecommendationDto(
            value.Id,
            value.SiteId,
            value.PublishedVersionId,
            value.WarehouseCode,
            Utc(value.GeneratedAtUtc),
            value.GeneratedBy,
            value.DefinitionVersion,
            value.Outcome,
            request,
            sources,
            value.ExaminedTaskCount,
            value.EligibleTaskCount,
            value.ExaminedPersonCount,
            value.EligiblePersonCount,
            value.EligiblePairCount,
            value.MatchableAssignmentCount,
            value.ReturnedAssignmentCount,
            value.IsTruncated,
            exclusions,
            value.ExclusionSamplesTruncated,
            samples,
            assignments,
            limitations);
    }

    private static void ValidateAssignmentSet(SpaceDispatchAssignmentSet value)
    {
        ValidateEvidence(
            value.ExaminedTaskCount,
            value.EligibleTaskCount,
            value.ExaminedPersonCount,
            value.EligiblePersonCount,
            value.EligiblePairCount,
            value.MatchableAssignmentCount,
            value.Assignments.Count,
            value.IsTruncated,
            value.Exclusions,
            value.ExclusionSamplesTruncated,
            value.ExclusionSamples,
            value.Assignments);
    }

    private static void ValidatePersistedEvidence(
        SpaceDispatchRecommendation value,
        SpaceDispatchRecommendationExclusionsDto exclusions,
        IReadOnlyCollection<SpaceDispatchRecommendationExclusionSampleDto> samples,
        IReadOnlyCollection<SpaceDispatchRecommendationAssignmentDto> assignments)
    {
        if (assignments.Count != value.ReturnedAssignmentCount)
            throw ContractViolation("The persisted assignment count is invalid.");
        ValidateEvidence(
            value.ExaminedTaskCount,
            value.EligibleTaskCount,
            value.ExaminedPersonCount,
            value.EligiblePersonCount,
            value.EligiblePairCount,
            value.MatchableAssignmentCount,
            value.ReturnedAssignmentCount,
            value.IsTruncated,
            exclusions,
            value.ExclusionSamplesTruncated,
            samples,
            assignments);
    }

    private static void ValidateEvidence(
        int examinedTasks,
        int eligibleTasks,
        int examinedPeople,
        int eligiblePeople,
        int eligiblePairs,
        int matchable,
        int returned,
        bool truncated,
        SpaceDispatchRecommendationExclusionsDto exclusions,
        bool samplesTruncated,
        IReadOnlyCollection<SpaceDispatchRecommendationExclusionSampleDto> samples,
        IReadOnlyCollection<SpaceDispatchRecommendationAssignmentDto> assignments)
    {
        var taskExcluded = exclusions.TasksOutsideRequestedScope +
            exclusions.TasksNotPending + exclusions.TasksAlreadyAssigned +
            exclusions.InvalidTasks + exclusions.TaskTargetOutsidePublishedModel +
            exclusions.TaskLocationCodeMismatch;
        var peopleExcluded = exclusions.PeoplePositionStale +
            exclusions.PeopleWorkStateStale + exclusions.PeopleNotIdle +
            exclusions.PeopleSimulatedExcluded +
            exclusions.PeopleWithoutResolvablePosition;
        var rejectedPairs = exclusions.CrossFloorPairsRejected +
            exclusions.DistanceUnverifiablePairsRejected +
            exclusions.DistanceExceededPairsRejected;
        var sampleEvents = taskExcluded + peopleExcluded + rejectedPairs +
            exclusions.EligibleTasksWithoutAssignment +
            exclusions.EligiblePeopleWithoutAssignment;
        if (taskExcluded + eligibleTasks != examinedTasks ||
            peopleExcluded + eligiblePeople != examinedPeople ||
            rejectedPairs + eligiblePairs != checked(eligibleTasks * eligiblePeople) ||
            exclusions.EligibleTasksWithoutAssignment != eligibleTasks - matchable ||
            exclusions.EligiblePeopleWithoutAssignment != eligiblePeople - matchable ||
            matchable > eligibleTasks || matchable > eligiblePeople ||
            matchable > eligiblePairs || returned > matchable ||
            truncated != (returned < matchable) ||
            samples.Count > SpaceDispatchRecommendationEngine.MaximumExclusionSampleCount ||
            samples.Count > sampleEvents ||
            samplesTruncated != (samples.Count < sampleEvents) ||
            !assignments.Select(value => value.Rank)
                .SequenceEqual(Enumerable.Range(1, assignments.Count)) ||
            assignments.Select(value => value.TaskId)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != assignments.Count ||
            assignments.Select(value => value.PersonKey)
                .Distinct(StringComparer.Ordinal).Count() != assignments.Count)
        {
            throw ContractViolation(
                "The dispatch recommendation evidence does not reconcile.");
        }
    }

    private static T Deserialize<T>(string value, string field)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(value, Json)
                   ?? throw new JsonException($"{field} is missing.");
        }
        catch (JsonException)
        {
            throw ContractViolation(
                $"The persisted dispatch recommendation {field} is invalid.");
        }
    }

    private void EnsureInternalExecution()
    {
        if (execution.IsExternal)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.DispatchRecommendationsInternalOnly,
                403,
                "Dispatch recommendations are available to internal principals only.",
                recoveryAction: "use-internal-operations-principal");
        }
        if (execution.TenantId == Guid.Empty ||
            execution.ActorId == Guid.Empty ||
            execution.TenantId != context.CurrentTenantId)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.TenantScopeDenied,
                403,
                "The Space tenant scope was denied.",
                recoveryAction: "reauthenticate");
        }
    }

    private static void EnsureAvailable(SpaceWmsRuntimeSourceDto source)
    {
        if (!source.IsAvailable)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.WmsUnavailable,
                503,
                "The dispatch-task source is unavailable.",
                recoveryAction: "retry-dispatch-generation",
                retryable: true);
        }
    }

    private static void EnsureIdentity(Guid value, string field)
    {
        if (value == Guid.Empty)
            throw Invalid(field, "Identity is required.");
    }

    private static string? OptionalIdentifier(
        string? value,
        int maximumLength,
        string field)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;
        if (normalized.Length > maximumLength || normalized.Any(char.IsControl))
        {
            throw Invalid(
                field,
                $"The identifier must be at most {maximumLength} characters and contain no control characters.");
        }
        return normalized.ToUpperInvariant();
    }

    private DateTime RequireUtcNow()
    {
        var value = clock.UtcNow;
        if (value.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return value;
    }

    private static DateTimeOffset? Offset(DateTime? value) =>
        value.HasValue ? Utc(value.Value) : null;

    private static DateTimeOffset Utc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static SpaceProblemException Invalid(string field, string detail) =>
        new(
            SpaceErrorCodes.RequestInvalid,
            400,
            "The dispatch recommendation request is invalid.",
            $"{field}: {detail}",
            "correct-request");

    private static SpaceProblemException NotFound() =>
        new(
            SpaceErrorCodes.DispatchRecommendationNotFound,
            404,
            "The dispatch recommendation was not found.",
            recoveryAction: "generate-dispatch-recommendation");

    private static SpaceProblemException EvidenceLimit(string detail) =>
        new(
            SpaceErrorCodes.DispatchRecommendationEvidenceLimit,
            422,
            "The dispatch recommendation evidence limit was exceeded.",
            detail,
            "narrow-dispatch-scope");

    private static SpaceProblemException ContractViolation(string detail) =>
        new(
            SpaceErrorCodes.WmsRuntimeContractViolation,
            502,
            "The dispatch recommendation source violated its contract.",
            detail,
            "check-dispatch-sources");

    private sealed record FloorRow(
        Guid LogicalId,
        string FloorCode,
        string FloorName,
        int FloorLevel);

    private sealed record ZoneRow(
        Guid LogicalId,
        Guid FloorLogicalId,
        string ZoneCode);

    private sealed record RackRow(
        Guid LogicalId,
        Guid FloorLogicalId,
        Guid ZoneLogicalId,
        int X,
        int Y,
        decimal RotationZ);

    private sealed record RackLevelRow(
        Guid RackLogicalId,
        int LevelNo,
        int CellWidth,
        int CellDepth);

    private sealed record LocationRow(
        Guid LogicalId,
        Guid FloorLogicalId,
        Guid? RackLogicalId,
        int ColumnNo,
        int LevelNo,
        int DepthNo);

    private sealed record LocationSpatialRow(
        Guid LogicalId,
        Guid FloorLogicalId,
        string FloorCode,
        Guid? ZoneLogicalId,
        string? ZoneCode,
        decimal? AnchorXMillimeters,
        decimal? AnchorYMillimeters);

    private sealed record SpatialMap(
        IReadOnlyDictionary<Guid, FloorRow> Floors,
        IReadOnlyDictionary<Guid, ZoneRow> Zones,
        IReadOnlyDictionary<Guid, LocationSpatialRow> Locations);
}
