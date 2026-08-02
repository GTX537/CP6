using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;

namespace CP6.Space.Infrastructure;

public sealed class SpaceWmsRuntimeService : ISpaceWmsRuntimeService
{
    private const int QueryChunkSize = 500;
    private const int MaxLocationCount = 10_000;
    private const int MaxAbcMaterialCount = 10_000;
    private const string OccupiedLocationRateMethod =
        "PositivePhysicalInventoryLocations/ActivePublishedLocations";
    private const string CapacityUnavailableReason =
        "WMS_LOCATION_CAPACITY_NOT_AVAILABLE";
    private const string AbcTransactionTimeBasis =
        "CP6_WMS_TRANSACTION_DATE_COMPLETE_DAYS";
    private const string AbcRankingMethod =
        "OutboundQuantityPreviousCumulativeShare";

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
        var result = await QueryInventorySourceAsync(
            scope,
            locateCriteria: null,
            cancellationToken);

        var orderedItems = result.Items
            .OrderBy(value => value.SpaceLocationCode, StringComparer.Ordinal)
            .ThenBy(value => value.MaterialNumber, StringComparer.Ordinal)
            .ThenBy(value => value.LotNumber, StringComparer.Ordinal)
            .ThenBy(value => value.ContainerNumber, StringComparer.Ordinal)
            .ToArray();
        return new SpaceWmsRuntimeInventoryResponse(
            siteId,
            scope.PublishedVersionId,
            scope.WarehouseCode,
            ToDto(result.Source),
            orderedItems);
    }

    public async Task<SpaceWmsRuntimeInventoryLocateResponse> LocateInventoryAsync(
        Guid siteId,
        SpaceWmsInventoryLocateCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        var normalized = new SpaceWmsInventoryLocateCriteria(
            NormalizeLocateCriterion("materialNumber", criteria.MaterialNumber),
            NormalizeLocateCriterion("lotNumber", criteria.LotNumber),
            NormalizeLocateCriterion("containerNumber", criteria.ContainerNumber),
            NormalizeOwnerCriterion(criteria.OwnerId));
        if (normalized.MaterialNumber is null &&
            normalized.LotNumber is null &&
            normalized.ContainerNumber is null &&
            normalized.OwnerId is null)
        {
            throw Invalid(
                "criteria",
                "At least one owner, material, lot, or container criterion is required.");
        }

        var scope = await LoadScopeAsync(
            siteId,
            locationLogicalIds: null,
            cancellationToken);
        var result = await QueryInventorySourceAsync(
            scope,
            normalized,
            cancellationToken);
        var hits = result.Items
            .GroupBy(value => value.LocationLogicalId)
            .Select(group =>
            {
                var first = group.First();
                if (group
                    .Select(value => value.WmsLocationCode)
                    .Distinct(StringComparer.Ordinal)
                    .Count() != 1)
                {
                    throw ContractViolation(
                        "A located WMS identity returned multiple location codes.");
                }
                return new SpaceWmsRuntimeInventoryLocateHitDto(
                    first.LocationLogicalId,
                    first.WmsLogicalId,
                    first.SpaceLocationCode,
                    first.WmsLocationCode,
                    group.All(value => value.CodeMatches),
                    first.FloorLogicalId,
                    first.FloorCode,
                    first.FloorName,
                    first.FloorLevel,
                    group.Sum(value => value.PhysicalQuantity),
                    group.Sum(value => value.AllocatedQuantity),
                    LocateFacts(group.Select(value => value.MaterialNumber)),
                    LocateFacts(group.Select(value => value.LotNumber)),
                    LocateFacts(group.Select(value => value.ContainerNumber)),
                    LocateFacts(group.Select(value => value.OwnerId)));
            })
            .OrderBy(value => value.FloorLevel)
            .ThenBy(value => value.FloorCode, StringComparer.Ordinal)
            .ThenBy(value => value.SpaceLocationCode, StringComparer.Ordinal)
            .ThenBy(value => value.LocationLogicalId)
            .ToArray();

        return new SpaceWmsRuntimeInventoryLocateResponse(
            siteId,
            scope.PublishedVersionId,
            scope.WarehouseCode,
            ToDto(result.Source),
            new SpaceWmsRuntimeInventoryLocateCriteriaDto(
                normalized.MaterialNumber,
                normalized.LotNumber,
                normalized.ContainerNumber,
                normalized.OwnerId),
            hits.Length,
            hits.Select(value => value.FloorLogicalId).Distinct().Count(),
            hits);
    }

    public async Task<SpaceWmsRuntimeWarehouseOverviewResponse>
        GetWarehouseOverviewAsync(
            Guid siteId,
            int abcWindowDays = 90,
            CancellationToken cancellationToken = default)
    {
        if (abcWindowDays is < 1 or > 365)
        {
            throw Invalid(
                "abcWindowDays",
                "The ABC analysis window must be between 1 and 365 days.");
        }

        var capturedAtUtc = ReceiveTime();
        var windowEndDateExclusive = DateOnly.FromDateTime(
            capturedAtUtc.UtcDateTime);
        var windowStartDate = windowEndDateExclusive.AddDays(-abcWindowDays);
        var scope = await LoadScopeAsync(
            siteId,
            locationLogicalIds: null,
            cancellationToken);

        SpaceWmsRuntimeInventoryResponse inventory;
        try
        {
            inventory = await QueryInventoryAsync(
                siteId,
                locationLogicalIds: null,
                cancellationToken);
        }
        catch (SpaceProblemException problem) when (
            problem.Code == SpaceErrorCodes.WmsUnavailable)
        {
            inventory = new SpaceWmsRuntimeInventoryResponse(
                siteId,
                scope.PublishedVersionId,
                scope.WarehouseCode,
                UnavailableSourceDto(capturedAtUtc),
                []);
        }

        SpaceWmsRuntimeTaskResponse tasks;
        try
        {
            tasks = await QueryTasksAsync(
                siteId,
                locationLogicalIds: null,
                cancellationToken);
        }
        catch (SpaceProblemException problem) when (
            problem.Code == SpaceErrorCodes.WmsUnavailable)
        {
            tasks = new SpaceWmsRuntimeTaskResponse(
                siteId,
                scope.PublishedVersionId,
                scope.WarehouseCode,
                UnavailableSourceDto(capturedAtUtc),
                []);
        }

        var abc = await QueryAbcForOverviewAsync(
            scope,
            windowStartDate,
            windowEndDateExclusive,
            capturedAtUtc,
            cancellationToken);
        var floorMetrics = scope.Floors
            .Select(floor => new RuntimeFloorMetric(
                floor,
                FloorAreaSquareMeters(floor)))
            .OrderBy(value => value.Floor.Level)
            .ThenBy(value => value.Floor.Code, StringComparer.Ordinal)
            .ThenBy(value => value.Floor.LogicalId)
            .ToArray();
        var areaAvailableFloorCount = floorMetrics.Count(value =>
            value.AreaSquareMeters.HasValue);
        decimal? totalFloorAreaSquareMeters =
            floorMetrics.Length > 0 &&
            areaAvailableFloorCount == floorMetrics.Length
                ? floorMetrics.Sum(value => value.AreaSquareMeters!.Value)
                : null;
        var rackFootprintSquareMeters = RoundMetric(scope.Racks.Sum(value =>
            (decimal)value.WidthMillimeters * value.DepthMillimeters /
            1_000_000m));
        var rackFootprintRatePercent = totalFloorAreaSquareMeters > 0
            ? Percent(rackFootprintSquareMeters, totalFloorAreaSquareMeters.Value)
            : null;

        var inventoryAvailable = inventory.Source.IsAvailable;
        var taskAvailable = tasks.Source.IsAvailable;
        var abcAvailable = abc.Source.IsAvailable;
        var positiveInventory = inventoryAvailable
            ? inventory.Items.Where(value => value.PhysicalQuantity > 0).ToArray()
            : [];
        var occupiedLocationIds = positiveInventory
            .Select(value => value.LocationLogicalId)
            .Distinct()
            .ToHashSet();
        var activeLocationCount = scope.Locations.Count;
        var occupiedLocationCount = inventoryAvailable
            ? occupiedLocationIds.Count
            : (int?)null;
        var materialNumbers = positiveInventory
            .Where(value => !string.IsNullOrWhiteSpace(value.MaterialNumber))
            .Select(value => value.MaterialNumber!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var rankedAbc = abcAvailable
            ? RankAbc(abc.Items)
            : new Dictionary<string, RuntimeAbcRank>(StringComparer.Ordinal);
        var abcMaterialRows = inventoryAvailable && abcAvailable
            ? BuildAbcMaterialRows(
                materialNumbers,
                positiveInventory,
                rankedAbc)
            : [];
        var abcLocations = inventoryAvailable && abcAvailable
            ? BuildAbcLocations(positiveInventory, rankedAbc)
            : [];
        var rankCounts = abcMaterialRows
            .GroupBy(value => value.Rank, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var activeAlarms = await _context.DeviceAlarmStates
            .AsNoTracking()
            .Where(value => value.SiteId == siteId && value.IsActive)
            .Select(value => value.AlarmSeverity)
            .ToListAsync(cancellationToken);
        var codeMismatchLocationCount = inventoryAvailable && taskAvailable
            ? inventory.Items
                .Where(value => !value.CodeMatches)
                .Select(value => value.LocationLogicalId)
                .Concat(tasks.Items
                    .Where(value => !value.CodeMatches)
                    .Select(value => value.LocationLogicalId))
                .Distinct()
                .Count()
            : (int?)null;
        var overAllocatedInventoryLineCount = inventoryAvailable
            ? inventory.Items.Count(value =>
                value.AllocatedQuantity > value.PhysicalQuantity)
            : (int?)null;
        var areaMissingFloorCount = floorMetrics.Length - areaAvailableFloorCount;
        var unclassifiedMaterialCount = inventoryAvailable && abcAvailable
            ? rankCounts.GetValueOrDefault("Unclassified")
            : (int?)null;

        var floorRows = floorMetrics.Select(metric =>
        {
            var floorLocationIds = scope.Locations
                .Where(value => value.FloorLogicalId == metric.Floor.LogicalId)
                .Select(value => value.SpaceLogicalId)
                .ToHashSet();
            var floorOccupiedCount = inventoryAvailable
                ? floorLocationIds.Count(occupiedLocationIds.Contains)
                : (int?)null;
            var locationRanks = inventoryAvailable && abcAvailable
                ? abcLocations.Where(value =>
                    value.FloorLogicalId == metric.Floor.LogicalId).ToArray()
                : [];
            return new SpaceWmsRuntimeWarehouseFloorKpiDto(
                metric.Floor.LogicalId,
                metric.Floor.Code,
                metric.Floor.Name,
                metric.Floor.Level,
                metric.AreaSquareMeters,
                floorLocationIds.Count,
                floorOccupiedCount,
                floorOccupiedCount.HasValue
                    ? Percent(floorOccupiedCount.Value, floorLocationIds.Count)
                    : null,
                inventoryAvailable && abcAvailable
                    ? locationRanks.Count(value => value.Rank == "A")
                    : null,
                inventoryAvailable && abcAvailable
                    ? locationRanks.Count(value => value.Rank == "B")
                    : null,
                inventoryAvailable && abcAvailable
                    ? locationRanks.Count(value => value.Rank == "C")
                    : null,
                inventoryAvailable && abcAvailable
                    ? locationRanks.Count(value => value.Rank == "Unclassified")
                    : null);
        }).ToArray();

        return new SpaceWmsRuntimeWarehouseOverviewResponse(
            siteId,
            scope.PublishedVersionId,
            scope.WarehouseCode,
            capturedAtUtc,
            inventoryAvailable && taskAvailable && abcAvailable &&
            areaMissingFloorCount == 0,
            new SpaceWmsRuntimeWarehouseModelKpiDto(
                floorMetrics.Length,
                areaAvailableFloorCount,
                areaMissingFloorCount,
                totalFloorAreaSquareMeters,
                scope.ZoneCount,
                scope.Racks.Count,
                rackFootprintSquareMeters,
                rackFootprintRatePercent,
                activeLocationCount),
            new SpaceWmsRuntimeWarehouseInventoryKpiDto(
                inventory.Source,
                inventoryAvailable ? inventory.Items.Count : null,
                occupiedLocationCount,
                inventoryAvailable
                    ? activeLocationCount - occupiedLocationIds.Count
                    : null,
                occupiedLocationCount.HasValue
                    ? Percent(occupiedLocationCount.Value, activeLocationCount)
                    : null,
                OccupiedLocationRateMethod,
                null,
                "Unavailable",
                CapacityUnavailableReason,
                inventoryAvailable
                    ? DistinctFacts(inventory.Items.Select(value => value.OwnerId))
                    : null,
                inventoryAvailable
                    ? DistinctFacts(inventory.Items.Select(value => value.MaterialNumber))
                    : null,
                inventoryAvailable
                    ? DistinctFacts(inventory.Items.Select(value => value.LotNumber))
                    : null,
                inventoryAvailable
                    ? DistinctFacts(inventory.Items.Select(value => value.ContainerNumber))
                    : null),
            new SpaceWmsRuntimeWarehouseTaskKpiDto(
                tasks.Source,
                taskAvailable
                    ? tasks.Items.Select(value => value.TaskId)
                        .Distinct(StringComparer.Ordinal).Count()
                    : null,
                taskAvailable ? tasks.Items.Count : null),
            new SpaceWmsRuntimeWarehouseAnomalyKpiDto(
                activeAlarms.Count,
                activeAlarms.Count(value =>
                    value == SpaceDeviceAlarmSeverity.Critical),
                codeMismatchLocationCount,
                overAllocatedInventoryLineCount,
                areaMissingFloorCount,
                unclassifiedMaterialCount),
            new SpaceWmsRuntimeWarehouseAbcDto(
                ToDto(abc.Source),
                abcWindowDays,
                windowStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                windowEndDateExclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                AbcTransactionTimeBasis,
                AbcRankingMethod,
                80m,
                95m,
                inventoryAvailable && abcAvailable,
                inventoryAvailable && abcAvailable ? materialNumbers.Length : null,
                inventoryAvailable && abcAvailable ? rankCounts.GetValueOrDefault("A") : null,
                inventoryAvailable && abcAvailable ? rankCounts.GetValueOrDefault("B") : null,
                inventoryAvailable && abcAvailable ? rankCounts.GetValueOrDefault("C") : null,
                unclassifiedMaterialCount,
                abcMaterialRows,
                abcLocations),
            floorRows);
    }

    private async Task<RuntimeInventoryResult> QueryInventorySourceAsync(
        RuntimeScope scope,
        SpaceWmsInventoryLocateCriteria? locateCriteria,
        CancellationToken cancellationToken)
    {
        SpaceWmsSourceMetadata? observedSource = null;
        var items = new List<SpaceWmsRuntimeInventoryItemDto>();

        foreach (var logicalIds in scope.WmsLogicalIds.Chunk(QueryChunkSize))
        {
            SpaceWmsInventoryResult result;
            try
            {
                result = await _source.QueryInventoryAsync(
                    new SpaceWmsInventoryQuery(
                        scope.WmsContext,
                        logicalIds,
                        OwnerIds: locateCriteria?.OwnerId is null
                            ? null
                            : [locateCriteria.OwnerId],
                        LocateCriteria: locateCriteria),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                throw Unavailable();
            }

            result = ValidateInventoryResult(result);
            var requestedChunkIds = logicalIds.ToHashSet();
            observedSource = MergeSource(observedSource, result.Source);
            if (observedSource.Kind == SpaceWmsDataSourceKind.Unavailable)
            {
                return new RuntimeInventoryResult(observedSource, []);
            }

            ValidateInventoryItems(result.Items, scope, requestedChunkIds);
            if (locateCriteria is not null)
                ValidateLocateItems(result.Items, locateCriteria);
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

        observedSource ??= MergeSource(null, DeclaredSource());
        return new RuntimeInventoryResult(observedSource, items);
    }

    public async Task<SpaceWmsRuntimeTaskResponse> QueryTasksAsync(
        Guid siteId,
        IReadOnlyCollection<Guid>? locationLogicalIds = null,
        CancellationToken cancellationToken = default) =>
        await QueryTasksCoreAsync(
            siteId,
            locationLogicalIds,
            taskIds: null,
            cancellationToken);

    public async Task<SpaceWmsRuntimeTaskPathResponse> GetTaskPathAsync(
        Guid siteId,
        string taskId,
        CancellationToken cancellationToken = default)
    {
        var normalizedTaskId = NormalizeTaskId(taskId);
        var tasks = await QueryTasksCoreAsync(
            siteId,
            locationLogicalIds: null,
            [normalizedTaskId],
            cancellationToken);
        if (!tasks.Source.IsAvailable || tasks.Items.Count == 0)
        {
            return new SpaceWmsRuntimeTaskPathResponse(
                tasks.SiteId,
                tasks.PublishedVersionId,
                tasks.WarehouseCode,
                tasks.Source,
                normalizedTaskId,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                false,
                false,
                [],
                [],
                [],
                []);
        }

        var actualStops = tasks.Items
            .OrderBy(value => value.SequenceNo)
            .ThenBy(value => value.LocationLogicalId)
            .ToArray();
        if (actualStops
            .GroupBy(value => value.SequenceNo)
            .Any(group => group.Count() > 1))
        {
            throw ContractViolation(
                "A WMS task path returned duplicate sequence numbers.");
        }

        var floorIds = actualStops
            .Select(value => value.FloorLogicalId)
            .Distinct()
            .ToArray();
        var floorIdSet = floorIds.ToHashSet();
        var floorRevisions = await _context.FloorRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == tasks.PublishedVersionId &&
                value.LifecycleState == SpaceLifecycleState.Active &&
                floorIdSet.Contains(value.LogicalId))
            .ToListAsync(cancellationToken);
        if (floorRevisions.Count != floorIds.Length)
        {
            throw Conflict(
                SpaceErrorCodes.VersionStateInvalid,
                "Published task floor geometry is inconsistent.",
                "repair-published-version");
        }

        var floorDtos = floorRevisions
            .Select(floor =>
            {
                var stops = actualStops
                    .Where(value => value.FloorLogicalId == floor.LogicalId)
                    .ToArray();
                return new SpaceWmsRuntimeTaskFloorDto(
                    floor.LogicalId,
                    floor.FloorCode,
                    floor.Name,
                    floor.Level,
                    floor.Elevation,
                    floor.Height,
                    stops.Length,
                    stops.Sum(value => value.Quantity ?? 0));
            })
            .OrderBy(value => value.FloorLevel)
            .ThenBy(value => value.FloorCode, StringComparer.Ordinal)
            .ToArray();
        var workloads = actualStops
            .GroupBy(value => new
            {
                value.FloorLogicalId,
                value.FloorCode,
                value.ZoneLogicalId,
                value.ZoneCode,
            })
            .Select(group => new SpaceWmsRuntimeTaskWorkloadDto(
                group.Key.FloorLogicalId,
                group.Key.FloorCode,
                group.Key.ZoneLogicalId,
                group.Key.ZoneCode,
                group.Count(),
                group.Sum(value => value.Quantity ?? 0)))
            .OrderBy(value =>
                floorDtos.Single(floor =>
                    floor.FloorLogicalId == value.FloorLogicalId).FloorLevel)
            .ThenBy(value => value.FloorCode, StringComparer.Ordinal)
            .ThenBy(value => value.ZoneCode, StringComparer.Ordinal)
            .ToArray();

        var zones = await _context.ZoneRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == tasks.PublishedVersionId &&
                value.LifecycleState == SpaceLifecycleState.Active &&
                floorIdSet.Contains(value.FloorLogicalId))
            .ToListAsync(cancellationToken);
        var zoneById = zones.ToDictionary(value => value.LogicalId);
        var zoneIds = zoneById.Keys.ToArray();
        var aisleRevisions = await _context.AisleRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == tasks.PublishedVersionId &&
                value.LifecycleState == SpaceLifecycleState.Active &&
                zoneIds.Contains(value.ZoneLogicalId))
            .ToListAsync(cancellationToken);
        var aisles = aisleRevisions
            .Select(aisle => new SpaceWmsRuntimeTaskAisleDto(
                zoneById[aisle.ZoneLogicalId].FloorLogicalId,
                aisle.ZoneLogicalId,
                aisle.LogicalId,
                aisle.AisleCode,
                aisle.CenterlineJson))
            .OrderBy(value =>
                floorDtos.Single(floor =>
                    floor.FloorLogicalId == value.FloorLogicalId).FloorLevel)
            .ThenBy(value => value.AisleCode, StringComparer.Ordinal)
            .ThenBy(value => value.AisleLogicalId)
            .ToArray();

        var floorTransitions = CountTransitions(
            actualStops.Select(value => (Guid?)value.FloorLogicalId));
        var zoneTransitions = CountTransitions(
            actualStops.Select(value => value.ZoneLogicalId));
        var zoneCount = actualStops
            .Select(value => value.ZoneLogicalId)
            .Distinct()
            .Count();
        return new SpaceWmsRuntimeTaskPathResponse(
            tasks.SiteId,
            tasks.PublishedVersionId,
            tasks.WarehouseCode,
            tasks.Source,
            normalizedTaskId,
            actualStops.Length,
            actualStops.Count(value =>
                value.AnchorXMillimeters.HasValue &&
                value.AnchorYMillimeters.HasValue),
            floorDtos.Length,
            zoneCount,
            floorTransitions,
            zoneTransitions,
            actualStops.Sum(value => value.Quantity ?? 0),
            floorTransitions > 0,
            zoneTransitions > 0,
            actualStops,
            floorDtos,
            workloads,
            aisles);
    }

    private async Task<SpaceWmsRuntimeTaskResponse> QueryTasksCoreAsync(
        Guid siteId,
        IReadOnlyCollection<Guid>? locationLogicalIds,
        IReadOnlyCollection<string>? taskIds,
        CancellationToken cancellationToken)
    {
        var normalizedTaskIds = taskIds?
            .Select(NormalizeTaskId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var normalizedTaskIdSet = normalizedTaskIds?.ToHashSet(
            StringComparer.Ordinal);
        var scope = await LoadScopeAsync(
            siteId,
            locationLogicalIds,
            cancellationToken);
        SpaceWmsSourceMetadata? observedSource = null;
        var items = new List<SpaceWmsRuntimeTaskItemDto>();

        foreach (var logicalIds in scope.WmsLogicalIds.Chunk(QueryChunkSize))
        {
            SpaceWmsTaskResult result;
            try
            {
                result = await _source.QueryTasksAsync(
                    new SpaceWmsTaskQuery(
                        scope.WmsContext,
                        logicalIds,
                        normalizedTaskIds),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                throw Unavailable();
            }

            result = ValidateTaskResult(result);
            var requestedChunkIds = logicalIds.ToHashSet();
            observedSource = MergeSource(observedSource, result.Source);
            if (observedSource.Kind == SpaceWmsDataSourceKind.Unavailable)
            {
                return new SpaceWmsRuntimeTaskResponse(
                    siteId,
                    scope.PublishedVersionId,
                    scope.WarehouseCode,
                    ToDto(observedSource),
                    []);
            }

            ValidateTaskItems(result.Items, scope, requestedChunkIds);
            if (normalizedTaskIdSet is not null && result.Items.Any(item =>
                    !normalizedTaskIdSet.Contains(
                        item.TaskId.Trim().ToUpperInvariant())))
            {
                throw ContractViolation(
                    "A WMS task query returned an item outside the requested task filter.");
            }
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

        observedSource ??= MergeSource(null, DeclaredSource());

        var orderedItems = items
            .OrderBy(value => value.TaskId, StringComparer.Ordinal)
            .ThenBy(value => value.SequenceNo)
            .ThenBy(value => value.LocationLogicalId)
            .ToArray();
        return new SpaceWmsRuntimeTaskResponse(
            siteId,
            scope.PublishedVersionId,
            scope.WarehouseCode,
            ToDto(observedSource),
            orderedItems);
    }

    private static int CountTransitions(IEnumerable<Guid?> values)
    {
        using var enumerator = values.GetEnumerator();
        if (!enumerator.MoveNext())
            return 0;
        var previous = enumerator.Current;
        var count = 0;
        while (enumerator.MoveNext())
        {
            if (enumerator.Current != previous)
                count++;
            previous = enumerator.Current;
        }
        return count;
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
        var requestedIdSet = requestedIds?.ToHashSet();
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
        var selectedLocations = requestedIdSet is null
            ? allActiveLocations
            : allActiveLocations
                .Where(value => requestedIdSet.Contains(value.LogicalId))
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
            locations.ToDictionary(value => value.WmsLogicalId),
            floors.Select(value => new RuntimeFloor(
                value.LogicalId,
                value.FloorCode,
                value.Name,
                value.Level,
                value.BoundaryJson,
                value.CoordinateSystem)).ToArray(),
            zones.Count,
            racks.Select(value => new RuntimeRack(
                value.Width,
                value.Depth)).ToArray());
    }

    private async Task<SpaceWmsAbcResult> QueryAbcForOverviewAsync(
        RuntimeScope scope,
        DateOnly fromDateInclusive,
        DateOnly toDateExclusive,
        DateTimeOffset capturedAtUtc,
        CancellationToken cancellationToken)
    {
        SpaceWmsAbcResult result;
        try
        {
            result = await _source.QueryAbcAsync(
                new SpaceWmsAbcQuery(
                    scope.WmsContext,
                    fromDateInclusive,
                    toDateExclusive),
                cancellationToken);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new SpaceWmsAbcResult(
                new SpaceWmsSourceMetadata(
                    SpaceWmsDataSourceKind.Unavailable,
                    _source.RuntimeDataSourceId,
                    capturedAtUtc),
                []);
        }

        if (result is null || result.Source is null || result.Items is null ||
            result.Items.Any(value => value is null))
        {
            throw ContractViolation("The WMS ABC result is incomplete.");
        }
        var source = MergeSource(null, result.Source);
        if (!source.IsAvailable)
            return new SpaceWmsAbcResult(source, []);
        if (result.Items.Count > MaxAbcMaterialCount)
        {
            throw ContractViolation(
                $"The WMS ABC result exceeds {MaxAbcMaterialCount} materials.");
        }
        if (result.Items.Any(value =>
                string.IsNullOrWhiteSpace(value.MaterialNumber) ||
                value.MaterialNumber.Length > 100 ||
                value.OutboundMovementCount < 1 ||
                value.OutboundQuantity <= 0) ||
            result.Items
                .GroupBy(value => value.MaterialNumber, StringComparer.Ordinal)
                .Any(group => group.Count() > 1))
        {
            throw ContractViolation(
                "The WMS ABC aggregates contain invalid or duplicate materials.");
        }
        return new SpaceWmsAbcResult(source, result.Items);
    }

    private static IReadOnlyDictionary<string, RuntimeAbcRank> RankAbc(
        IReadOnlyList<SpaceWmsAbcAggregate> aggregates)
    {
        var ordered = aggregates
            .OrderByDescending(value => value.OutboundQuantity)
            .ThenBy(value => value.MaterialNumber, StringComparer.Ordinal)
            .ToArray();
        var total = ordered.Sum(value => value.OutboundQuantity);
        if (total <= 0)
            return new Dictionary<string, RuntimeAbcRank>(StringComparer.Ordinal);

        var cumulative = 0m;
        var result = new Dictionary<string, RuntimeAbcRank>(StringComparer.Ordinal);
        foreach (var aggregate in ordered)
        {
            var previousShare = Percent(cumulative, total) ?? 0m;
            var rank = previousShare < 80m
                ? "A"
                : previousShare < 95m
                    ? "B"
                    : "C";
            cumulative += aggregate.OutboundQuantity;
            result.Add(
                aggregate.MaterialNumber,
                new RuntimeAbcRank(
                    aggregate.OutboundMovementCount,
                    aggregate.OutboundQuantity,
                    previousShare,
                    Percent(cumulative, total) ?? 0m,
                    rank));
        }
        return result;
    }

    private static IReadOnlyList<SpaceWmsRuntimeWarehouseAbcMaterialDto>
        BuildAbcMaterialRows(
            IReadOnlyList<string> materialNumbers,
            IReadOnlyList<SpaceWmsRuntimeInventoryItemDto> inventory,
            IReadOnlyDictionary<string, RuntimeAbcRank> ranked)
    {
        return materialNumbers.Select(materialNumber =>
        {
            var rows = inventory.Where(value => string.Equals(
                value.MaterialNumber,
                materialNumber,
                StringComparison.Ordinal)).ToArray();
            if (!ranked.TryGetValue(materialNumber, out var rank))
            {
                return new SpaceWmsRuntimeWarehouseAbcMaterialDto(
                    materialNumber,
                    0,
                    0,
                    null,
                    null,
                    "Unclassified",
                    rows.Select(value => value.LocationLogicalId).Distinct().Count(),
                    rows.Select(value => value.FloorLogicalId).Distinct().Count());
            }
            return new SpaceWmsRuntimeWarehouseAbcMaterialDto(
                materialNumber,
                rank.OutboundMovementCount,
                rank.OutboundQuantity,
                rank.PreviousCumulativeSharePercent,
                rank.CumulativeSharePercent,
                rank.Rank,
                rows.Select(value => value.LocationLogicalId).Distinct().Count(),
                rows.Select(value => value.FloorLogicalId).Distinct().Count());
        })
        .OrderBy(value => AbcRankOrder(value.Rank))
        .ThenByDescending(value => value.OutboundQuantity)
        .ThenBy(value => value.MaterialNumber, StringComparer.Ordinal)
        .ToArray();
    }

    private static IReadOnlyList<SpaceWmsRuntimeWarehouseAbcLocationDto>
        BuildAbcLocations(
            IReadOnlyList<SpaceWmsRuntimeInventoryItemDto> inventory,
            IReadOnlyDictionary<string, RuntimeAbcRank> ranked)
    {
        return inventory
            .GroupBy(value => value.LocationLogicalId)
            .Select(group =>
            {
                var first = group.First();
                var materials = group
                    .Where(value => !string.IsNullOrWhiteSpace(value.MaterialNumber))
                    .Select(value => value.MaterialNumber!)
                    .Distinct(StringComparer.Ordinal)
                    .Select(materialNumber =>
                        new SpaceWmsRuntimeWarehouseAbcLocationMaterialDto(
                            materialNumber,
                            ranked.TryGetValue(materialNumber, out var materialRank)
                                ? materialRank.Rank
                                : "Unclassified"))
                    .OrderBy(value => AbcRankOrder(value.Rank))
                    .ThenBy(value => value.MaterialNumber, StringComparer.Ordinal)
                    .ToArray();
                var locationRank = materials.Length == 0
                    ? "Unclassified"
                    : materials.OrderBy(value => AbcRankOrder(value.Rank))
                        .First().Rank;
                return new SpaceWmsRuntimeWarehouseAbcLocationDto(
                    first.LocationLogicalId,
                    first.SpaceLocationCode,
                    first.FloorLogicalId,
                    first.FloorCode,
                    locationRank,
                    materials);
            })
            .OrderBy(value => value.FloorCode, StringComparer.Ordinal)
            .ThenBy(value => value.SpaceLocationCode, StringComparer.Ordinal)
            .ThenBy(value => value.LocationLogicalId)
            .ToArray();
    }

    private static int AbcRankOrder(string rank) => rank switch
    {
        "A" => 0,
        "B" => 1,
        "C" => 2,
        _ => 3,
    };

    private static decimal? FloorAreaSquareMeters(RuntimeFloor floor)
    {
        if (string.IsNullOrWhiteSpace(floor.CoordinateSystem) ||
            !floor.CoordinateSystem.Contains("MM", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        try
        {
            using var document = JsonDocument.Parse(floor.BoundaryJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("points", out var points) ||
                points.ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            var coordinates = points.EnumerateArray()
                .Select(point =>
                {
                    if (point.ValueKind != JsonValueKind.Array ||
                        point.GetArrayLength() < 2)
                    {
                        throw new JsonException("A floor boundary point is invalid.");
                    }
                    return (
                        X: point[0].GetDecimal(),
                        Y: point[1].GetDecimal());
                })
                .ToArray();
            if (coordinates.Length < 3)
                return null;
            var twiceArea = 0m;
            for (var index = 0; index < coordinates.Length; index++)
            {
                var current = coordinates[index];
                var next = coordinates[(index + 1) % coordinates.Length];
                twiceArea += current.X * next.Y - next.X * current.Y;
            }
            var area = Math.Abs(twiceArea) / 2m / 1_000_000m;
            return area > 0 ? RoundMetric(area) : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static int DistinctFacts(IEnumerable<string?> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.Ordinal)
            .Count();

    private static decimal? Percent(decimal numerator, decimal denominator) =>
        denominator > 0
            ? RoundMetric(numerator / denominator * 100m)
            : null;

    private static decimal RoundMetric(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private SpaceWmsRuntimeSourceDto UnavailableSourceDto(
        DateTimeOffset observedAtUtc) =>
        ToDto(new SpaceWmsSourceMetadata(
            SpaceWmsDataSourceKind.Unavailable,
            _source.RuntimeDataSourceId,
            observedAtUtc));

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

    private SpaceWmsSourceMetadata MergeSource(
        SpaceWmsSourceMetadata? current,
        SpaceWmsSourceMetadata next)
    {
        ValidateSource(next);
        var normalized = next with
        {
            ObservedAtUtc = next.ObservedAtUtc.ToUniversalTime(),
        };
        if (current is null)
            return normalized;
        if (!string.Equals(
                current.DataSourceId,
                normalized.DataSourceId,
                StringComparison.Ordinal))
        {
            throw ContractViolation(
                "WMS source provenance changed during the runtime query.");
        }
        var earliestObservation = current.ObservedAtUtc <= normalized.ObservedAtUtc
            ? current.ObservedAtUtc
            : normalized.ObservedAtUtc;
        if (normalized.Kind == SpaceWmsDataSourceKind.Unavailable)
        {
            return normalized with
            {
                ObservedAtUtc = earliestObservation,
            };
        }
        if (current.Kind != normalized.Kind)
        {
            throw ContractViolation(
                "WMS source provenance changed during the runtime query.");
        }

        return current with
        {
            ObservedAtUtc = earliestObservation,
        };
    }

    private void ValidateSource(SpaceWmsSourceMetadata source)
    {
        if (!Enum.IsDefined(typeof(SpaceWmsDataSourceKind), source.Kind))
            throw ContractViolation("The WMS source kind is invalid.");
        if (string.IsNullOrWhiteSpace(_source.RuntimeAdapterId) ||
            _source.RuntimeAdapterId.Length > 100)
        {
            throw ContractViolation("The WMS runtime adapter identity is invalid.");
        }
        var declaredKind = _source.RuntimeDataSourceKind;
        if (!Enum.IsDefined(typeof(SpaceWmsDataSourceKind), declaredKind))
            throw ContractViolation("The declared WMS source kind is invalid.");
        if (string.IsNullOrWhiteSpace(source.DataSourceId) ||
            source.DataSourceId.Length > 100)
        {
            throw ContractViolation("The WMS data source identity is invalid.");
        }
        if (source.ObservedAtUtc == default)
            throw ContractViolation("The WMS observation time is invalid.");
        if (source.Kind != declaredKind &&
            source.Kind != SpaceWmsDataSourceKind.Unavailable)
        {
            throw ContractViolation(
                "The returned WMS source kind does not match the declared source.");
        }
        if (!string.Equals(
                source.DataSourceId,
                _source.RuntimeDataSourceId,
                StringComparison.Ordinal))
        {
            throw ContractViolation(
                "The returned WMS data source does not match the declared source.");
        }
    }

    private static SpaceWmsInventoryResult ValidateInventoryResult(
        SpaceWmsInventoryResult? result)
    {
        if (result is null)
            throw ContractViolation("The WMS inventory result is missing.");
        if (result.Source is null)
            throw ContractViolation("The WMS inventory source is missing.");
        if (result.Items is null)
            throw ContractViolation("The WMS inventory collection is missing.");
        if (result.Items.Any(item => item is null))
            throw ContractViolation("The WMS inventory collection contains a null item.");
        return result;
    }

    private static SpaceWmsTaskResult ValidateTaskResult(
        SpaceWmsTaskResult? result)
    {
        if (result is null)
            throw ContractViolation("The WMS task result is missing.");
        if (result.Source is null)
            throw ContractViolation("The WMS task source is missing.");
        if (result.Items is null)
            throw ContractViolation("The WMS task collection is missing.");
        if (result.Items.Any(item => item is null))
            throw ContractViolation("The WMS task collection contains a null item.");
        return result;
    }

    private static void ValidateInventoryItems(
        IReadOnlyList<SpaceWmsInventoryItem> items,
        RuntimeScope scope,
        IReadOnlySet<Guid> requestedChunkIds)
    {
        foreach (var item in items)
        {
            ValidateLocationIdentity(
                item.LogicalId,
                item.LocationCode,
                scope,
                requestedChunkIds);
        }
    }

    private static void ValidateLocateItems(
        IReadOnlyList<SpaceWmsInventoryItem> items,
        SpaceWmsInventoryLocateCriteria criteria)
    {
        foreach (var item in items)
        {
            if (item.PhysicalQuantity <= 0 ||
                !MatchesLocate(item.MaterialNumber, criteria.MaterialNumber) ||
                !MatchesLocate(item.LotNumber, criteria.LotNumber) ||
                !MatchesLocate(item.ContainerNumber, criteria.ContainerNumber) ||
                !MatchesOwner(item.OwnerId, criteria.OwnerId))
            {
                throw ContractViolation(
                    "A returned WMS inventory item does not match the locate criteria.");
            }
        }
    }

    private static bool MatchesLocate(string? actual, string? expected) =>
        expected is null || string.Equals(actual, expected, StringComparison.Ordinal);

    private static bool MatchesOwner(string? actual, string? expected) =>
        expected is null ||
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private static void ValidateTaskItems(
        IReadOnlyList<SpaceWmsTaskItem> items,
        RuntimeScope scope,
        IReadOnlySet<Guid> requestedChunkIds)
    {
        foreach (var item in items)
        {
            ValidateLocationIdentity(
                item.LogicalId,
                item.LocationCode,
                scope,
                requestedChunkIds);
            if (string.IsNullOrWhiteSpace(item.TaskId) ||
                string.IsNullOrWhiteSpace(item.TaskType) ||
                string.IsNullOrWhiteSpace(item.Status) ||
                item.SequenceNo < 1)
            {
                throw ContractViolation("A returned WMS task is invalid.");
            }
        }
    }

    private static void ValidateLocationIdentity(
        Guid logicalId,
        string locationCode,
        RuntimeScope scope,
        IReadOnlySet<Guid> requestedChunkIds)
    {
        if (logicalId == Guid.Empty ||
            !requestedChunkIds.Contains(logicalId) ||
            !scope.LocationByWmsLogicalId.ContainsKey(logicalId))
        {
            throw ContractViolation(
                "A returned WMS location is outside the current query chunk.");
        }
        if (string.IsNullOrWhiteSpace(locationCode))
            throw ContractViolation("A returned WMS location code is invalid.");
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

    private SpaceWmsRuntimeSourceDto ToDto(
        SpaceWmsSourceMetadata source)
    {
        var receivedAtUtc = ReceiveTime();
        var observedAtUtc = source.ObservedAtUtc.ToUniversalTime();
        var age = receivedAtUtc - observedAtUtc;
        return new SpaceWmsRuntimeSourceDto(
            source.Kind.ToString(),
            _source.RuntimeAdapterId,
            source.DataSourceId,
            observedAtUtc,
            receivedAtUtc,
            age >= TimeSpan.Zero ? WholeMilliseconds(age) : 0,
            age < TimeSpan.Zero ? WholeMilliseconds(-age) : 0,
            source.IsSimulated,
            source.IsAvailable);
    }

    private DateTimeOffset ReceiveTime()
    {
        var now = _clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return new DateTimeOffset(now);
    }

    private static long WholeMilliseconds(TimeSpan value) =>
        (long)Math.Floor(value.TotalMilliseconds);

    private static string? NormalizeLocateCriterion(
        string field,
        string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
        if (normalized?.Length > 100)
            throw Invalid(field, "The value must not exceed 100 characters.");
        return normalized;
    }

    private static string? NormalizeOwnerCriterion(string? value)
    {
        var normalized = NormalizeLocateCriterion("ownerId", value);
        return normalized?.ToUpperInvariant();
    }

    private static string NormalizeTaskId(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToUpperInvariant();
        if (normalized is null)
            throw Invalid("taskId", "A task identity is required.");
        if (normalized.Length > 100)
            throw Invalid("taskId", "The value must not exceed 100 characters.");
        return normalized;
    }

    private static IReadOnlyList<string> LocateFacts(
        IEnumerable<string?> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

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

    private static SpaceProblemException ContractViolation(string detail) =>
        new(
            SpaceErrorCodes.WmsRuntimeContractViolation,
            502,
            "The WMS runtime response violated its contract.",
            detail,
            "verify-wms-adapter");

    private static SpaceProblemException Unavailable() =>
        new(
            SpaceErrorCodes.WmsUnavailable,
            503,
            "The WMS runtime source is unavailable.",
            "The WMS runtime adapter could not complete the query.",
            "retry-runtime-query",
            retryable: true);

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

    private sealed record RuntimeInventoryResult(
        SpaceWmsSourceMetadata Source,
        IReadOnlyList<SpaceWmsRuntimeInventoryItemDto> Items);

    private sealed record RuntimeScope(
        Guid PublishedVersionId,
        string WarehouseCode,
        SpaceWmsContext WmsContext,
        IReadOnlyList<RuntimeLocation> Locations,
        IReadOnlyList<Guid> WmsLogicalIds,
        IReadOnlyDictionary<Guid, RuntimeLocation> LocationByWmsLogicalId,
        IReadOnlyList<RuntimeFloor> Floors,
        int ZoneCount,
        IReadOnlyList<RuntimeRack> Racks);

    private sealed record RuntimeFloor(
        Guid LogicalId,
        string Code,
        string Name,
        int Level,
        string BoundaryJson,
        string CoordinateSystem);

    private sealed record RuntimeRack(
        int WidthMillimeters,
        int DepthMillimeters);

    private sealed record RuntimeFloorMetric(
        RuntimeFloor Floor,
        decimal? AreaSquareMeters);

    private sealed record RuntimeAbcRank(
        int OutboundMovementCount,
        decimal OutboundQuantity,
        decimal PreviousCumulativeSharePercent,
        decimal CumulativeSharePercent,
        string Rank);
}
