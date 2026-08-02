using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class SpaceOperationsDiagnosticService
    : ISpaceOperationsDiagnosticService
{
    public const string DefinitionVersion =
        "space-operations-diagnostics-v1";
    public const int MaximumEvidenceEventCount = 100_000;
    public const string CapacityUnavailableReason =
        "WMS_LOCATION_CAPACITY_NOT_AVAILABLE";

    private static readonly SpaceOperationsDiagnosticThresholdsDto Thresholds =
        new(
            MaximumObservationGapSeconds: 300,
            MinimumBacktrackSegmentMillimeters: 1_000,
            BacktrackAngleDegrees: 150,
            DwellThresholdSeconds: 300,
            CongestionMinimumConcurrentPeople: 2,
            OccupancyWatchPercent: 85,
            OccupancyCriticalPercent: 95);

    private readonly SpaceContext _context;
    private readonly ISpaceWmsRuntimeService _runtime;
    private readonly ISpaceExecutionContext _execution;
    private readonly ISpaceClock _clock;
    private readonly ISpaceDesignAccessEvaluator _access;
    private readonly SpacePersonnelRuntimeOptions _personnelOptions;
    private readonly SpaceOperationsDiagnosticEngine _engine;

    public SpaceOperationsDiagnosticService(
        SpaceContext context,
        ISpaceWmsRuntimeService runtime,
        ISpaceExecutionContext execution,
        ISpaceClock clock,
        ISpaceDesignAccessEvaluator access,
        SpacePersonnelRuntimeOptions personnelOptions,
        SpaceOperationsDiagnosticEngine engine)
    {
        _context = context;
        _runtime = runtime;
        _execution = execution;
        _clock = clock;
        _access = access;
        _personnelOptions = personnelOptions;
        _engine = engine;
        _personnelOptions.Validate();
    }

    public async Task<SpaceOperationsDiagnosticResponse> GetAsync(
        Guid siteId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        var now = RequireUtcNow();
        ValidateWindow(fromUtc, toUtc, now);
        if (siteId == Guid.Empty)
            throw Invalid("siteId", "A non-empty site identity is required.");

        _access.EnsureSiteAccess(siteId, write: false);
        var scope = await LoadScopeAsync(siteId, cancellationToken);
        var from = fromUtc.UtcDateTime;
        var to = toUtc.UtcDateTime;
        var evidence = await _context.PersonnelEvents
            .AsNoTracking()
            .Where(value =>
                value.SiteId == siteId &&
                value.EventKind == SpacePersonnelEventKind.PositionObserved &&
                value.OccurredAtUtc >= from &&
                value.OccurredAtUtc < to)
            .OrderBy(value => value.SourceId)
            .ThenBy(value => value.PersonExternalId)
            .ThenBy(value => value.OccurredAtUtc)
            .ThenBy(value => value.ReceivedAtUtc)
            .ThenBy(value => value.Id)
            .Take(MaximumEvidenceEventCount + 1)
            .ToListAsync(cancellationToken);
        if (evidence.Count > MaximumEvidenceEventCount)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.OperationsDiagnosticsEvidenceLimit,
                422,
                "The operations diagnostics evidence limit was exceeded.",
                $"No more than {MaximumEvidenceEventCount} personnel position events may be analyzed.",
                "narrow-diagnostics-window");
        }

        var simulatedCount = evidence.Count(value =>
            value.SourceKind == SpacePersonnelSourceKind.Simulated);
        var realPoints = evidence
            .Where(value => value.SourceKind == SpacePersonnelSourceKind.Real)
            .Select(value => ToPoint(value, scope))
            .ToArray();
        var eligiblePoints = realPoints
            .Where(value => value.IsEligible)
            .ToArray();
        var excludedOutsideModel = realPoints.Length - eligiblePoints.Length;
        var personnelSource = BuildPersonnelSource(
            evidence.Count,
            eligiblePoints,
            simulatedCount,
            excludedOutsideModel);
        var people = _engine.Analyze(realPoints, scope.Locations, Thresholds);
        var capacityResult = await BuildCapacityAsync(
            siteId,
            scope,
            cancellationToken);

        var limitations = new List<string>
        {
            "PERSONNEL_REAL_POSITION_EVENTS_ONLY",
            "UNKNOWN_PATH_SEGMENTS_NOT_INTERPOLATED",
            "CONGESTION_IS_OBSERVED_LOCATION_COPRESENCE",
            "WMS_OCCUPANCY_IS_CURRENT_NOT_HISTORICAL_WINDOW",
            "CAPACITY_MASTER_NOT_AVAILABLE",
        };
        if (simulatedCount > 0)
            limitations.Add("SIMULATED_PERSONNEL_EVENTS_EXCLUDED");
        if (excludedOutsideModel > 0)
            limitations.Add("OUTSIDE_CURRENT_PUBLISHED_MODEL_EVENTS_EXCLUDED");
        if (!capacityResult.Capacity.IsAvailable)
            limitations.Add("WMS_OCCUPANCY_SOURCE_UNAVAILABLE");

        return new SpaceOperationsDiagnosticResponse(
            siteId,
            scope.PublishedVersionId,
            capacityResult.WarehouseCode,
            fromUtc,
            toUtc,
            new DateTimeOffset(now),
            DefinitionVersion,
            Thresholds,
            personnelSource,
            people.Path,
            people.Congestion,
            people.Dwell,
            capacityResult.Capacity,
            limitations);
    }

    private async Task<DiagnosticScope> LoadScopeAsync(
        Guid siteId,
        CancellationToken cancellationToken)
    {
        var model = await _context.Models
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.SiteId == siteId,
                cancellationToken)
            ?? throw new SpaceProblemException(
                SpaceErrorCodes.ModelNotFound,
                404,
                "The Space model was not found.",
                recoveryAction: "select-existing-site");
        if (!model.CurrentPublishedVersionId.HasValue)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.VersionStateInvalid,
                409,
                "The Space model has no current Published version.",
                recoveryAction: "publish-version");
        }

        var publishedVersionId = model.CurrentPublishedVersionId.Value;
        var published = await _context.Versions
            .AsNoTracking()
            .AnyAsync(
                value =>
                    value.Id == publishedVersionId &&
                    value.Status == SpaceVersionStatus.Published,
                cancellationToken);
        if (!published)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.VersionStateInvalid,
                409,
                "The current runtime version is not Published.",
                recoveryAction: "publish-version");
        }

        var floorRows = await _context.FloorRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == publishedVersionId &&
                value.LifecycleState == SpaceLifecycleState.Active)
            .OrderBy(value => value.Level)
            .ThenBy(value => value.FloorCode)
            .ThenBy(value => value.LogicalId)
            .ToListAsync(cancellationToken);
        var floors = floorRows.ToDictionary(
            value => value.LogicalId,
            value => new DiagnosticFloor(
                value.LogicalId,
                value.FloorCode,
                value.Name,
                value.Level));
        var locationRows = await _context.LocationRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == publishedVersionId &&
                value.LifecycleState == SpaceLifecycleState.Active)
            .OrderBy(value => value.LogicalId)
            .ToListAsync(cancellationToken);
        var locations = new Dictionary<Guid, SpaceOperationsDiagnosticLocation>();
        foreach (var value in locationRows)
        {
            if (!floors.TryGetValue(value.FloorLogicalId, out var floor) ||
                string.IsNullOrWhiteSpace(value.LocationCode))
            {
                throw new SpaceProblemException(
                    SpaceErrorCodes.VersionStateInvalid,
                    409,
                    "Published location geometry is inconsistent.",
                    recoveryAction: "repair-published-version");
            }
            locations.Add(
                value.LogicalId,
                new SpaceOperationsDiagnosticLocation(
                    value.LogicalId,
                    value.LocationCode,
                    floor.FloorLogicalId,
                    floor.FloorCode,
                    floor.FloorName,
                    floor.FloorLevel));
        }

        return new DiagnosticScope(
            publishedVersionId,
            floors,
            locations);
    }

    private static SpaceOperationsDiagnosticPoint ToPoint(
        SpacePersonnelEvent value,
        DiagnosticScope scope)
    {
        var eligible = true;
        Guid? floorLogicalId = value.FloorLogicalId;
        if (value.LocationLogicalId.HasValue)
        {
            if (!scope.Locations.TryGetValue(
                    value.LocationLogicalId.Value,
                    out var location) ||
                floorLogicalId.HasValue &&
                floorLogicalId.Value != location.FloorLogicalId)
            {
                eligible = false;
            }
            else
            {
                floorLogicalId = location.FloorLogicalId;
            }
        }
        if (!floorLogicalId.HasValue ||
            !scope.Floors.ContainsKey(floorLogicalId.Value))
        {
            eligible = false;
        }

        return new SpaceOperationsDiagnosticPoint(
            string.Concat(value.SourceId, "\u001f", value.PersonExternalId),
            value.Id,
            value.SourceId,
            value.SourceKind.ToString(),
            ToOffset(value.OccurredAtUtc),
            ToOffset(value.ReceivedAtUtc),
            floorLogicalId,
            value.LocationLogicalId,
            value.XMillimeters,
            value.YMillimeters,
            eligible);
    }

    private static SpaceOperationsPersonnelSourceDto BuildPersonnelSource(
        int evidenceEventCount,
        IReadOnlyList<SpaceOperationsDiagnosticPoint> eligible,
        int simulatedCount,
        int excludedOutsideModel)
    {
        var sources = eligible
            .GroupBy(value => (value.SourceId, value.SourceKind))
            .Select(group => new SpaceOperationsPersonnelSourceItemDto(
                group.Key.SourceId,
                group.Key.SourceKind,
                group.Count(),
                group.Select(value => value.PersonKey)
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                group.Min(value => value.OccurredAtUtc),
                group.Max(value => value.OccurredAtUtc),
                group.Max(value => value.ReceivedAtUtc)))
            .OrderBy(value => value.SourceId, StringComparer.Ordinal)
            .ThenBy(value => value.SourceKind, StringComparer.Ordinal)
            .ToArray();
        return new SpaceOperationsPersonnelSourceDto(
            evidenceEventCount,
            eligible.Count,
            simulatedCount,
            excludedOutsideModel,
            eligible.Select(value => value.PersonKey)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            sources.Length,
            eligible.Count == 0
                ? null
                : eligible.Min(value => value.OccurredAtUtc),
            eligible.Count == 0
                ? null
                : eligible.Max(value => value.OccurredAtUtc),
            eligible.Count == 0
                ? null
                : eligible.Max(value => value.ReceivedAtUtc),
            sources);
    }

    private async Task<CapacityResult> BuildCapacityAsync(
        Guid siteId,
        DiagnosticScope scope,
        CancellationToken cancellationToken)
    {
        SpaceWmsRuntimeInventoryResponse? inventory;
        try
        {
            inventory = await _runtime.QueryInventoryAsync(
                siteId,
                locationLogicalIds: null,
                cancellationToken);
        }
        catch (SpaceProblemException problem) when (
            problem.Code == SpaceErrorCodes.WmsUnavailable)
        {
            inventory = null;
        }

        if (inventory is not null &&
            (inventory.SiteId != siteId ||
             inventory.PublishedVersionId != scope.PublishedVersionId))
        {
            throw ContractViolation(
                "The WMS inventory scope does not match the diagnostics scope.");
        }

        var available = inventory?.Source.IsAvailable == true;
        if (!available && inventory?.Items.Count > 0)
        {
            throw ContractViolation(
                "An unavailable WMS inventory source returned data rows.");
        }
        var occupied = new HashSet<Guid>();
        if (available)
        {
            foreach (var item in inventory!.Items)
            {
                if (!scope.Locations.TryGetValue(
                        item.LocationLogicalId,
                        out var location) ||
                    location.FloorLogicalId != item.FloorLogicalId)
                {
                    throw ContractViolation(
                        "A WMS inventory row is outside the current Published model.");
                }
                if (item.PhysicalQuantity > 0)
                    occupied.Add(item.LocationLogicalId);
            }
        }

        var locationCount = scope.Locations.Count;
        var occupancyPercent = available
            ? Percent(occupied.Count, locationCount)
            : null;
        var pressure = OccupancyPressure(occupancyPercent, available);
        var floors = scope.Floors.Values
            .OrderBy(value => value.FloorLevel)
            .ThenBy(value => value.FloorCode, StringComparer.Ordinal)
            .ThenBy(value => value.FloorLogicalId)
            .Select(floor =>
            {
                var floorLocations = scope.Locations.Values
                    .Where(value => value.FloorLogicalId == floor.FloorLogicalId)
                    .Select(value => value.LocationLogicalId)
                    .ToArray();
                var floorOccupied = available
                    ? floorLocations.Count(occupied.Contains)
                    : (int?)null;
                var floorPercent = floorOccupied.HasValue
                    ? Percent(floorOccupied.Value, floorLocations.Length)
                    : null;
                return new SpaceOperationsFloorOccupancyDto(
                    floor.FloorLogicalId,
                    floor.FloorCode,
                    floor.FloorName,
                    floor.FloorLevel,
                    floorLocations.Length,
                    floorOccupied,
                    floorPercent,
                    OccupancyPressure(floorPercent, available));
            })
            .ToArray();
        return new CapacityResult(
            inventory?.WarehouseCode,
            new SpaceOperationsCapacityDiagnosisDto(
                inventory?.Source,
                available,
                "PositivePhysicalInventoryLocations/ActivePublishedLocations",
                locationCount,
                available ? occupied.Count : null,
                occupancyPercent,
                pressure,
                null,
                "Unavailable",
                CapacityUnavailableReason,
                floors));
    }

    private void EnsureExecutionContext()
    {
        if (_execution.IsExternal)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.OperationsDiagnosticsInternalOnly,
                403,
                "Operations diagnostics are available only to internal principals.",
                recoveryAction: "use-internal-space-principal");
        }
        if (_execution.TenantId == Guid.Empty ||
            _execution.ActorId == Guid.Empty ||
            _execution.TenantId != _context.CurrentTenantId)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.TenantScopeDenied,
                403,
                "The Space tenant scope was denied.",
                recoveryAction: "reauthenticate");
        }
    }

    private void ValidateWindow(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        DateTime now)
    {
        if (fromUtc.Offset != TimeSpan.Zero || toUtc.Offset != TimeSpan.Zero)
            throw Invalid("timeWindow", "fromUtc and toUtc must use UTC offset +00:00.");
        if (fromUtc >= toUtc)
            throw Invalid("timeWindow", "fromUtc must be earlier than toUtc.");
        if (toUtc.UtcDateTime > now)
            throw Invalid("toUtc", "toUtc cannot be in the future.");
        if (fromUtc.UtcDateTime < now - _personnelOptions.TrajectoryRetention)
        {
            throw Invalid(
                "fromUtc",
                $"fromUtc must be within the {_personnelOptions.TrajectoryRetention.TotalDays:G} day retention window.");
        }
        if (toUtc - fromUtc > _personnelOptions.MaximumTrajectoryWindow)
        {
            throw Invalid(
                "timeWindow",
                $"The diagnostics window cannot exceed {_personnelOptions.MaximumTrajectoryWindow.TotalHours:G} hours.");
        }
    }

    private DateTime RequireUtcNow()
    {
        var value = _clock.UtcNow;
        if (value.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return value;
    }

    private static decimal? Percent(int numerator, int denominator) =>
        denominator > 0
            ? Math.Round(
                (decimal)numerator / denominator * 100m,
                2,
                MidpointRounding.AwayFromZero)
            : null;

    private static string OccupancyPressure(decimal? percent, bool available)
    {
        if (!available)
            return "Unavailable";
        if (!percent.HasValue)
            return "NotApplicable";
        if (percent.Value >= Thresholds.OccupancyCriticalPercent)
            return "Critical";
        return percent.Value >= Thresholds.OccupancyWatchPercent
            ? "Watch"
            : "Normal";
    }

    private static DateTimeOffset ToOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static SpaceProblemException Invalid(string field, string detail) =>
        new(
            SpaceErrorCodes.RequestInvalid,
            400,
            "The operations diagnostics query is invalid.",
            $"{field}: {detail}",
            "correct-diagnostics-query");

    private static SpaceProblemException ContractViolation(string detail) =>
        new(
            SpaceErrorCodes.WmsRuntimeContractViolation,
            502,
            "The WMS runtime contract was violated.",
            detail,
            "verify-wms-adapter");

    private sealed record DiagnosticFloor(
        Guid FloorLogicalId,
        string FloorCode,
        string FloorName,
        int FloorLevel);

    private sealed record DiagnosticScope(
        Guid PublishedVersionId,
        IReadOnlyDictionary<Guid, DiagnosticFloor> Floors,
        IReadOnlyDictionary<Guid, SpaceOperationsDiagnosticLocation> Locations);

    private sealed record CapacityResult(
        string? WarehouseCode,
        SpaceOperationsCapacityDiagnosisDto Capacity);
}
