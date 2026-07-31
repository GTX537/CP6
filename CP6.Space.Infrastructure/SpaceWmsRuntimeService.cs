using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class SpaceWmsRuntimeService : ISpaceWmsRuntimeService
{
    private const int QueryChunkSize = 500;
    private const int MaxLocationCount = 10_000;

    private readonly SpaceContext _context;
    private readonly ISpaceExecutionContext _execution;
    private readonly ISpaceClock _clock;
    private readonly ISpaceDesignAccessEvaluator _access;
    private readonly ISpaceWarehouseResolver _warehouses;
    private readonly ISpaceWmsRuntimeSource _source;

    public SpaceWmsRuntimeService(
        SpaceContext context,
        ISpaceExecutionContext execution,
        ISpaceClock clock,
        ISpaceDesignAccessEvaluator access,
        ISpaceWarehouseResolver warehouses,
        ISpaceWmsRuntimeSource source)
    {
        _context = context;
        _execution = execution;
        _clock = clock;
        _access = access;
        _warehouses = warehouses;
        _source = source;
    }

    public async Task<SpaceWmsRuntimeInventoryResponse> QueryInventoryAsync(
        Guid siteId,
        IReadOnlyCollection<Guid>? locationLogicalIds = null,
        CancellationToken cancellationToken = default)
    {
        var scope = await LoadScopeAsync(
            siteId,
            locationLogicalIds,
            cancellationToken);
        SpaceWmsSourceMetadata? observedSource = null;
        var items = new List<SpaceWmsRuntimeInventoryItemDto>();

        foreach (var logicalIds in scope.WmsLogicalIds.Chunk(QueryChunkSize))
        {
            var result = await _source.QueryInventoryAsync(
                new SpaceWmsInventoryQuery(scope.WmsContext, logicalIds),
                cancellationToken);
            observedSource ??= result.Source;
            foreach (var item in result.Items)
            {
                var location = scope.LocationByWmsLogicalId[item.LogicalId];
                items.Add(new SpaceWmsRuntimeInventoryItemDto(
                    location.SpaceLogicalId,
                    item.LogicalId,
                    location.SpaceLocationCode,
                    item.LocationCode,
                    string.Equals(
                        location.SpaceLocationCode,
                        item.LocationCode,
                        StringComparison.Ordinal),
                    location.FloorLogicalId,
                    location.FloorCode,
                    location.FloorName,
                    location.FloorLevel,
                    item.PhysicalQuantity,
                    item.AllocatedQuantity,
                    item.MaterialNumber,
                    item.LotNumber,
                    item.ContainerNumber,
                    item.OwnerId));
            }
        }

        return new SpaceWmsRuntimeInventoryResponse(
            siteId,
            scope.PublishedVersionId,
            scope.WarehouseCode,
            ToDto(observedSource ?? DeclaredSource()),
            items
                .OrderBy(value => value.SpaceLocationCode, StringComparer.Ordinal)
                .ThenBy(value => value.MaterialNumber, StringComparer.Ordinal)
                .ThenBy(value => value.LotNumber, StringComparer.Ordinal)
                .ThenBy(value => value.ContainerNumber, StringComparer.Ordinal)
                .ToArray());
    }

    public async Task<SpaceWmsRuntimeTaskResponse> QueryTasksAsync(
        Guid siteId,
        IReadOnlyCollection<Guid>? locationLogicalIds = null,
        CancellationToken cancellationToken = default)
    {
        var scope = await LoadScopeAsync(
            siteId,
            locationLogicalIds,
            cancellationToken);
        SpaceWmsSourceMetadata? observedSource = null;
        var items = new List<SpaceWmsRuntimeTaskItemDto>();

        foreach (var logicalIds in scope.WmsLogicalIds.Chunk(QueryChunkSize))
        {
            var result = await _source.QueryTasksAsync(
                new SpaceWmsTaskQuery(scope.WmsContext, logicalIds),
                cancellationToken);
            observedSource ??= result.Source;
            foreach (var item in result.Items)
            {
                var location = scope.LocationByWmsLogicalId[item.LogicalId];
                items.Add(new SpaceWmsRuntimeTaskItemDto(
                    item.TaskId,
                    item.TaskType,
                    item.Status,
                    item.SequenceNo,
                    location.SpaceLogicalId,
                    item.LogicalId,
                    location.SpaceLocationCode,
                    item.LocationCode,
                    string.Equals(
                        location.SpaceLocationCode,
                        item.LocationCode,
                        StringComparison.Ordinal),
                    location.FloorLogicalId,
                    location.FloorCode,
                    location.FloorName,
                    location.FloorLevel,
                    location.ZoneLogicalId,
                    location.ZoneCode,
                    location.RackLogicalId,
                    location.RackCode,
                    location.AnchorXMillimeters,
                    location.AnchorYMillimeters,
                    location.AnchorZMillimeters,
                    item.Quantity,
                    item.MaterialNumber));
            }
        }

        return new SpaceWmsRuntimeTaskResponse(
            siteId,
            scope.PublishedVersionId,
            scope.WarehouseCode,
            ToDto(observedSource ?? DeclaredSource()),
            items
                .OrderBy(value => value.TaskId, StringComparer.Ordinal)
                .ThenBy(value => value.SequenceNo)
                .ThenBy(value => value.LocationLogicalId)
                .ToArray());
    }

    private async Task<RuntimeScope> LoadScopeAsync(
        Guid siteId,
        IReadOnlyCollection<Guid>? locationLogicalIds,
        CancellationToken cancellationToken)
    {
        EnsureExecutionContext();
        if (siteId == Guid.Empty)
            throw Invalid("siteId", "A non-empty site identity is required.");
        if (locationLogicalIds?.Any(value => value == Guid.Empty) == true)
        {
            throw Invalid(
                "locationLogicalIds",
                "Location identities must be non-empty.");
        }
        var requestedIds = locationLogicalIds?
            .Distinct()
            .ToArray();
        if (requestedIds?.Length > MaxLocationCount)
        {
            throw Invalid(
                "locationLogicalIds",
                $"No more than {MaxLocationCount} locations may be queried.");
        }

        _access.EnsureSiteAccess(siteId, write: false);

        var model = await _context.Models
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.SiteId == siteId,
                cancellationToken)
            ?? throw NotFound(
                SpaceErrorCodes.ModelNotFound,
                "The Space model was not found.");
        if (!model.CurrentPublishedVersionId.HasValue)
        {
            throw Conflict(
                SpaceErrorCodes.VersionStateInvalid,
                "The Space model has no current Published version.",
                "publish-version");
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
            throw Conflict(
                SpaceErrorCodes.VersionStateInvalid,
                "The current runtime version is not Published.",
                "publish-version");
        }

        var allActiveLocations = await _context.LocationRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == publishedVersionId &&
                value.LifecycleState == SpaceLifecycleState.Active)
            .OrderBy(value => value.LogicalId)
            .ToListAsync(cancellationToken);
        var selectedLocations = requestedIds is null
            ? allActiveLocations
            : allActiveLocations
                .Where(value => requestedIds.Contains(value.LogicalId))
                .ToList();
        if (requestedIds is not null && selectedLocations.Count != requestedIds.Length)
        {
            throw NotFound(
                SpaceErrorCodes.LogicalIdNotFound,
                "A requested location was not found in the current Published version.");
        }
        if (selectedLocations.Count > MaxLocationCount)
        {
            throw Invalid(
                "locationLogicalIds",
                $"No more than {MaxLocationCount} locations may be queried.");
        }

        var floors = await _context.FloorRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == publishedVersionId &&
                value.LifecycleState == SpaceLifecycleState.Active)
            .ToListAsync(cancellationToken);
        var racks = await _context.RackRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == publishedVersionId &&
                value.LifecycleState == SpaceLifecycleState.Active)
            .ToListAsync(cancellationToken);
        var zones = await _context.ZoneRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == publishedVersionId &&
                value.LifecycleState == SpaceLifecycleState.Active)
            .ToListAsync(cancellationToken);
        var rackLevels = await _context.RackLevelRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == publishedVersionId &&
                value.LifecycleState == SpaceLifecycleState.Active)
            .ToListAsync(cancellationToken);
        var floorById = floors.ToDictionary(value => value.LogicalId);
        var rackById = racks.ToDictionary(value => value.LogicalId);
        var zoneById = zones.ToDictionary(value => value.LogicalId);
        var rackLevelByPosition = rackLevels.ToDictionary(
            value => (value.RackLogicalId, value.LevelNo));

        var activeLocationIds = allActiveLocations
            .Select(value => value.LogicalId)
            .ToHashSet();
        var adoptions = await _context.WmsAdoptions
            .AsNoTracking()
            .Where(value =>
                value.SiteId == siteId &&
                value.AdapterId == _source.RuntimeAdapterId &&
                value.LocationLogicalId != null)
            .ToListAsync(cancellationToken);
        var currentAdoptions = adoptions
            .Where(value => activeLocationIds.Contains(value.LocationLogicalId!.Value))
            .ToArray();
        if (currentAdoptions
            .GroupBy(value => value.LocationLogicalId!.Value)
            .Any(group => group.Count() > 1))
        {
            throw Conflict(
                SpaceErrorCodes.WmsAdoptionDuplicate,
                "A Space location has multiple WMS bindings.",
                "repair-wms-adoption");
        }
        if (currentAdoptions
            .GroupBy(value => value.WmsLogicalId)
            .Any(group => group.Select(value => value.LocationLogicalId).Distinct().Count() > 1))
        {
            throw Conflict(
                SpaceErrorCodes.WmsAdoptionDuplicate,
                "A WMS location is mapped to multiple Space locations.",
                "repair-wms-adoption");
        }
        var adoptionBySpaceId = currentAdoptions
            .ToDictionary(value => value.LocationLogicalId!.Value);
        var allCurrentWmsLogicalIds = allActiveLocations
            .Select(value => adoptionBySpaceId.TryGetValue(value.LogicalId, out var adoption)
                ? adoption.WmsLogicalId
                : value.LogicalId)
            .ToArray();
        if (allCurrentWmsLogicalIds.Distinct().Count() != allCurrentWmsLogicalIds.Length)
        {
            throw Conflict(
                SpaceErrorCodes.WmsAdoptionDuplicate,
                "A WMS location is mapped to multiple Space locations.",
                "repair-wms-adoption");
        }

        var locations = new List<RuntimeLocation>(selectedLocations.Count);
        foreach (var location in selectedLocations)
        {
            if (!floorById.TryGetValue(location.FloorLogicalId, out var floor) ||
                string.IsNullOrWhiteSpace(location.LocationCode))
            {
                throw Conflict(
                    SpaceErrorCodes.VersionStateInvalid,
                    "Published location geometry is inconsistent.",
                    "repair-published-version");
            }

            Guid? zoneLogicalId = null;
            string? zoneCode = null;
            string? rackCode = null;
            double? anchorX = null;
            double? anchorY = null;
            double? anchorZ = null;
            if (location.RackLogicalId.HasValue)
            {
                if (!rackById.TryGetValue(location.RackLogicalId.Value, out var rack) ||
                    rack.FloorLogicalId != location.FloorLogicalId ||
                    !zoneById.TryGetValue(rack.ZoneLogicalId, out var zone) ||
                    zone.FloorLogicalId != location.FloorLogicalId ||
                    !rackLevelByPosition.TryGetValue(
                        (rack.LogicalId, location.LevelNo),
                        out var rackLevel))
                {
                    throw Conflict(
                        SpaceErrorCodes.VersionStateInvalid,
                        "Published rack-backed location geometry is inconsistent.",
                        "repair-published-version");
                }

                zoneLogicalId = zone.LogicalId;
                zoneCode = zone.ZoneCode;
                rackCode = rack.RackCode;
                var radians = (double)rack.RotationZ * Math.PI / 180d;
                var localX = (location.ColumnNo - 0.5d) * rackLevel.CellWidth;
                var localY = (location.DepthNo - 0.5d) * rackLevel.CellDepth;
                anchorX = rack.X + localX * Math.Cos(radians) - localY * Math.Sin(radians);
                anchorY = rack.Y + localX * Math.Sin(radians) + localY * Math.Cos(radians);
                anchorZ = rack.Z + rackLevel.BottomZ +
                    rackLevel.BeamHeight + rackLevel.ClearHeight / 2d;
            }

            var wmsLogicalId = adoptionBySpaceId.TryGetValue(
                location.LogicalId,
                out var adoption)
                ? adoption.WmsLogicalId
                : location.LogicalId;
            locations.Add(new RuntimeLocation(
                location.LogicalId,
                wmsLogicalId,
                location.LocationCode,
                floor.LogicalId,
                floor.FloorCode,
                floor.Name,
                floor.Level,
                zoneLogicalId,
                zoneCode,
                location.RackLogicalId,
                rackCode,
                anchorX,
                anchorY,
                anchorZ));
        }

        var wmsLogicalIds = locations
            .Select(value => value.WmsLogicalId)
            .Distinct()
            .ToArray();
        if (wmsLogicalIds.Length != locations.Count)
        {
            throw Conflict(
                SpaceErrorCodes.WmsAdoptionDuplicate,
                "A WMS location is mapped to multiple Space locations.",
                "repair-wms-adoption");
        }
        if (wmsLogicalIds.Length > MaxLocationCount)
        {
            throw Invalid(
                "locationLogicalIds",
                $"No more than {MaxLocationCount} mapped WMS locations may be queried.");
        }

        var warehouse = await _warehouses.ResolveAsync(siteId, cancellationToken)
            ?? throw NotFound(
                SpaceErrorCodes.ModelNotFound,
                "The CP6 runtime site was not found.");
        var correlationId = _execution is ISpaceCorrelationContext correlation &&
            correlation.CorrelationId != Guid.Empty
                ? correlation.CorrelationId
                : Guid.NewGuid();
        var wmsContext = new SpaceWmsContext(
            _execution.TenantId,
            siteId,
            warehouse.WarehouseCode,
            correlationId);
        return new RuntimeScope(
            publishedVersionId,
            warehouse.WarehouseCode,
            wmsContext,
            locations,
            wmsLogicalIds,
            locations.ToDictionary(value => value.WmsLogicalId));
    }

    private void EnsureExecutionContext()
    {
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

    private SpaceWmsSourceMetadata DeclaredSource()
    {
        var now = _clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return new SpaceWmsSourceMetadata(
            _source.RuntimeDataSourceKind,
            _source.RuntimeDataSourceId,
            new DateTimeOffset(now));
    }

    private static SpaceWmsRuntimeSourceDto ToDto(
        SpaceWmsSourceMetadata source) =>
        new(
            source.Kind.ToString(),
            source.DataSourceId,
            source.ObservedAtUtc.ToUniversalTime(),
            source.IsSimulated,
            source.IsAvailable);

    private static SpaceProblemException Invalid(
        string field,
        string detail) =>
        new(
            SpaceErrorCodes.RequestInvalid,
            400,
            "The runtime query is invalid.",
            $"{field}: {detail}",
            "correct-request");

    private static SpaceProblemException NotFound(
        string code,
        string title) =>
        new(code, 404, title, recoveryAction: "verify-resource");

    private static SpaceProblemException Conflict(
        string code,
        string title,
        string recoveryAction) =>
        new(code, 409, title, recoveryAction: recoveryAction);

    private sealed record RuntimeLocation(
        Guid SpaceLogicalId,
        Guid WmsLogicalId,
        string SpaceLocationCode,
        Guid FloorLogicalId,
        string FloorCode,
        string FloorName,
        int FloorLevel,
        Guid? ZoneLogicalId,
        string? ZoneCode,
        Guid? RackLogicalId,
        string? RackCode,
        double? AnchorXMillimeters,
        double? AnchorYMillimeters,
        double? AnchorZMillimeters);

    private sealed record RuntimeScope(
        Guid PublishedVersionId,
        string WarehouseCode,
        SpaceWmsContext WmsContext,
        IReadOnlyList<RuntimeLocation> Locations,
        IReadOnlyList<Guid> WmsLogicalIds,
        IReadOnlyDictionary<Guid, RuntimeLocation> LocationByWmsLogicalId);
}
