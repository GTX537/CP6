using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Space;
using CP6.WebApi.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Space;

public sealed class SpaceAnalyticsService : ISpaceAnalyticsService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly CP6Context _db;
    private readonly IWmsStockQuery _stock;
    private readonly IWmsAnalyticsQuery _analytics;
    private readonly ILogger<SpaceAnalyticsService> _logger;
    private readonly ITenantClock _tenantClock;
    private readonly TimeProvider _timeProvider;

    public SpaceAnalyticsService(
        CP6Context db,
        IWmsStockQuery stock,
        IWmsAnalyticsQuery analytics,
        ILogger<SpaceAnalyticsService> logger,
        ITenantClock tenantClock,
        TimeProvider timeProvider)
    {
        _db = db;
        _stock = stock;
        _analytics = analytics;
        _logger = logger;
        _tenantClock = tenantClock;
        _timeProvider = timeProvider;
    }

    public async Task<SpaceAnalyticsConfigDto> GetConfigAsync(CancellationToken ct = default)
    {
        var config = await _db.Space_AnalyticsConfigs.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted, ct);
        return MapConfig(config ?? new Space_AnalyticsConfig());
    }

    public async Task<SpaceAnalyticsConfigDto> UpdateConfigAsync(
        SpaceAnalyticsConfigUpdate request, string? user, CancellationToken ct = default)
    {
        ValidateConfig(request);
        var config = await _db.Space_AnalyticsConfigs.FirstOrDefaultAsync(x => !x.IsDeleted, ct);
        var created = config == null;
        if (config == null)
        {
            config = new Space_AnalyticsConfig
            {
                Id = Guid.NewGuid(),
                Creator = user,
                CreateDate = ServerNow,
            };
            _db.Space_AnalyticsConfigs.Add(config);
        }
        else
        {
            config.Modifier = user;
            config.ModifyDate = ServerNow;
        }

        ApplyConfig(config, request);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (created)
        {
            _db.Entry(config).State = EntityState.Detached;
            config = await _db.Space_AnalyticsConfigs.FirstOrDefaultAsync(x => !x.IsDeleted, ct);
            if (config is null) throw;
            config.Modifier = user;
            config.ModifyDate = ServerNow;
            ApplyConfig(config, request);
            await _db.SaveChangesAsync(ct);
        }
        return MapConfig(config);
    }

    public async Task<SpaceAbcSnapshotMetaDto> RebuildAbcAsync(
        Guid siteId, string trigger, string? user, CancellationToken ct = default)
    {
        var site = await _db.Space_Sites.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == siteId && !x.IsDeleted, ct)
            ?? throw new BizException("E-SPACE-001");
        var warehouseCd = ResolveWarehouse(site);
        var config = await GetConfigAsync(ct);
        var now = ServerNow;
        var scheduledDate = string.Equals(trigger, "manual", StringComparison.OrdinalIgnoreCase)
            ? (DateOnly?)null
            : TenantToday;
        var from = now.AddDays(-config.WindowDays);
        var aggregates = await _analytics.GetOutboundAggregatesAsync(warehouseCd, from, now, ct);
        var products = AbcClassifier.Classify(
                aggregates.Select(x => new AbcInputRow(x.ProductCd, x.OutCount, x.OutQty)),
                AbcClassifier.ParseMetric(config.Metric),
                config.ThresholdA,
                config.ThresholdB)
            .Select(x => new SpaceAbcProductDto
            {
                ProductCd = x.ProductCd,
                OutCount = x.OutCount,
                OutQty = x.OutQty,
                Score = x.Score,
                CumulativeRatio = x.CumulativeRatio,
                AbcRank = x.AbcRank,
            })
            .ToList();

        var snapshot = new Space_AbcSnapshot
        {
            Id = Guid.NewGuid(),
            SiteId = site.Id,
            WarehouseCd = warehouseCd,
            WindowFrom = from,
            WindowTo = now,
            CalculatedAt = now,
            ScheduledDate = scheduledDate,
            WindowDays = config.WindowDays,
            Metric = config.Metric,
            ThresholdA = config.ThresholdA,
            ThresholdB = config.ThresholdB,
            ItemCount = products.Count,
            Trigger = string.Equals(trigger, "manual", StringComparison.OrdinalIgnoreCase) ? "manual" : "scheduled",
            ResultJson = JsonSerializer.Serialize(products, Json),
            Creator = user,
            CreateDate = now,
        };
        _db.Space_AbcSnapshots.Add(snapshot);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (scheduledDate.HasValue)
        {
            _db.Entry(snapshot).State = EntityState.Detached;
            var existing = await _db.Space_AbcSnapshots.AsNoTracking()
                .Where(x => x.SiteId == siteId && x.ScheduledDate == scheduledDate && !x.IsDeleted)
                .OrderByDescending(x => x.CalculatedAt)
                .FirstOrDefaultAsync(ct);
            if (existing is null) throw;
            return MapSnapshot(existing);
        }
        return MapSnapshot(snapshot);
    }

    public async Task<int> RebuildDueSnapshotsAsync(CancellationToken ct = default)
    {
        var config = await GetConfigAsync(ct);
        var tenantNow = TenantNow;
        if (!config.EnableScheduledSnapshot || tenantNow.Hour < config.ScheduledHourLocal) return 0;

        var sites = await _db.Space_Sites.AsNoTracking()
            .Where(x => !x.IsDeleted && x.Enable)
            .Select(x => x.Id)
            .ToListAsync(ct);
        var today = DateOnly.FromDateTime(tenantNow);
        var completed = (await _db.Space_AbcSnapshots.AsNoTracking()
                .Where(x => !x.IsDeleted && x.ScheduledDate == today && sites.Contains(x.SiteId))
                .Select(x => x.SiteId)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();

        var rebuilt = 0;
        foreach (var siteId in sites.Where(x => !completed.Contains(x)))
        {
            try
            {
                await RebuildAbcAsync(siteId, "scheduled", "SpaceAbcSnapshotWorker", ct);
                rebuilt++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Space ABC snapshot failed for site {SiteId}; continuing with remaining sites", siteId);
            }
        }
        return rebuilt;
    }

    public async Task<SpaceUtilizationResponse> GetUtilizationAsync(Guid floorId, CancellationToken ct = default)
    {
        var context = await LoadFloorContextAsync(floorId, ct);
        IReadOnlyList<WmsStockDto> stockRows;
        var stockAvailable = true;
        try
        {
            stockRows = await _stock.GetStockByLocationsAsync(context.Locations.Select(x => x.LocationCode).ToList(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "WMS stock query failed for Space floor {FloorId}", floorId);
            stockRows = Array.Empty<WmsStockDto>();
            stockAvailable = false;
        }

        return BuildUtilization(context, stockRows, stockAvailable, ServerNow);
    }

    private SpaceUtilizationResponse BuildUtilization(
        FloorContext context,
        IReadOnlyList<WmsStockDto> stockRows,
        bool stockAvailable,
        DateTime timestamp)
    {
        var stockByCode = stockRows.ToDictionary(x => x.LocationCode, StringComparer.Ordinal);
        var response = new SpaceUtilizationResponse
        {
            FloorId = context.Floor.Id,
            SiteId = context.Site.Id,
            WarehouseCd = ResolveWarehouse(context.Site),
            Timestamp = timestamp,
            StockAvailable = stockAvailable,
        };
        if (!stockAvailable)
        {
            response.Warnings.Add(new SpaceAnalyticsWarningDto
            {
                Code = "W-SPACE-701",
                Message = "WMS stock is temporarily unavailable; the last client snapshot may be shown.",
                Severity = "error",
            });
        }

        foreach (var location in context.Locations)
        {
            stockByCode.TryGetValue(location.LocationCode, out var stock);
            var capacity = stockAvailable ? ResolveCapacity(location, stock) : CapacityResolution.Unavailable;
            var qty = stockAvailable ? stock?.Qty ?? 0m : (decimal?)null;
            var utilization = capacity.Included && qty.HasValue && capacity.Capacity > 0m
                ? qty.Value / capacity.Capacity.Value
                : (decimal?)null;
            var item = new SpaceUtilizationItemDto
            {
                LocationId = location.LocationId,
                LocationCode = location.LocationCode,
                RackId = location.RackId,
                RackCode = location.RackCode,
                ZoneId = location.ZoneId,
                ZoneCode = location.ZoneCode,
                ZoneName = location.ZoneName,
                ZoneType = location.ZoneType,
                Qty = qty,
                Capacity = capacity.Capacity,
                CapacityUom = capacity.Uom,
                CapacitySource = capacity.Source,
                Utilization = utilization,
                BinStatus = stockAvailable ? stock?.BinStatus ?? 0 : null,
                StockAvailable = stockAvailable,
                IncludedInAggregate = capacity.Included,
                WarningCode = capacity.WarningCode,
            };
            response.Items.Add(item);
            if (capacity.WarningCode != null)
            {
                response.Warnings.Add(new SpaceAnalyticsWarningDto
                {
                    Code = capacity.WarningCode,
                    LocationCode = location.LocationCode,
                    Message = capacity.WarningMessage ?? "Capacity metadata is incomplete.",
                });
            }
        }

        response.Racks = AggregateUtilization(
            response.Items,
            x => x.RackId,
            x => x.RackCode ?? "unassigned",
            x => x.RackCode ?? "Unassigned rack");
        response.Zones = AggregateUtilization(
            response.Items,
            x => x.ZoneId,
            x => x.ZoneCode ?? "unassigned",
            x => x.ZoneName ?? "Unassigned zone");
        return response;
    }

    public async Task<SpaceStorageTypeResponse> GetStorageTypesAsync(Guid floorId, CancellationToken ct = default)
    {
        var context = await LoadFloorContextAsync(floorId, ct);
        var response = new SpaceStorageTypeResponse { FloorId = floorId };
        foreach (var location in context.Locations)
        {
            var type = ZoneType(location.ZoneType ?? 0);
            response.Items.Add(new SpaceStorageTypeItemDto
            {
                LocationId = location.LocationId,
                LocationCode = location.LocationCode,
                ZoneId = location.ZoneId,
                ZoneCode = location.ZoneCode,
                ZoneName = location.ZoneName,
                ZoneType = type.Id,
                TypeKey = type.Key,
                Color = type.Color,
            });
        }
        response.TotalLocations = response.Items.Count;
        response.Summary = response.Items
            .GroupBy(x => new { x.ZoneType, x.TypeKey, x.Color })
            .Select(g => new SpaceStorageTypeSummaryDto
            {
                ZoneType = g.Key.ZoneType,
                TypeKey = g.Key.TypeKey,
                Color = g.Key.Color,
                LocationCount = g.Count(),
                Percentage = response.TotalLocations == 0
                    ? 0m
                    : Math.Round((decimal)g.Count() / response.TotalLocations * 100m, 2),
            })
            .OrderByDescending(x => x.LocationCount)
            .ThenBy(x => x.ZoneType)
            .ToList();
        return response;
    }

    public async Task<SpaceAbcResponse> GetAbcAsync(
        Guid floorId, CancellationToken ct = default, bool includeProducts = true)
    {
        var context = await LoadFloorContextAsync(floorId, ct);
        var config = await GetConfigAsync(ct);
        var snapshot = await _db.Space_AbcSnapshots.AsNoTracking()
            .Where(x => x.SiteId == context.Site.Id && !x.IsDeleted)
            .OrderByDescending(x => x.CalculatedAt)
            .FirstOrDefaultAsync(ct);
        var snapshotReadable = TryDeserializeProducts(snapshot?.ResultJson, out var products);
        var productRank = products.ToDictionary(x => x.ProductCd, x => x.AbcRank, StringComparer.Ordinal);

        IReadOnlyList<WmsStockDto> stockRows;
        var stockAvailable = true;
        try
        {
            stockRows = await _stock.GetStockByLocationsAsync(context.Locations.Select(x => x.LocationCode).ToList(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "WMS stock query failed for Space ABC floor {FloorId}", floorId);
            stockRows = Array.Empty<WmsStockDto>();
            stockAvailable = false;
        }
        var stockByCode = stockRows.ToDictionary(x => x.LocationCode, StringComparer.Ordinal);
        var stale = snapshot == null || ServerNow - snapshot.CalculatedAt > TimeSpan.FromHours(config.StaleAfterHours);
        var response = new SpaceAbcResponse
        {
            FloorId = floorId,
            SiteId = context.Site.Id,
            HasSnapshot = snapshot != null,
            IsStale = stale,
            StockAvailable = stockAvailable,
            Snapshot = snapshot == null ? null : MapSnapshot(snapshot),
            Products = includeProducts ? products : new List<SpaceAbcProductDto>(),
        };
        if (stale)
        {
            response.Warnings.Add(new SpaceAnalyticsWarningDto
            {
                Code = "W-SPACE-704",
                Message = snapshot == null ? "ABC snapshot has not been calculated." : "ABC snapshot is stale.",
            });
        }
        if (!stockAvailable)
        {
            response.Warnings.Add(new SpaceAnalyticsWarningDto
            {
                Code = "W-SPACE-701",
                Message = "WMS stock is temporarily unavailable.",
                Severity = "error",
            });
        }

        foreach (var location in context.Locations)
        {
            stockByCode.TryGetValue(location.LocationCode, out var stock);
            var productCodes = stock?.ProductCodes ?? new List<string>();
            string? rank = null;
            if (stockAvailable && snapshotReadable && (stock?.Qty ?? 0m) > 0m)
            {
                rank = productCodes
                    .Select(p => productRank.TryGetValue(p, out var value) ? value : "C")
                    .OrderBy(RankOrder)
                    .FirstOrDefault() ?? "C";
            }
            response.Items.Add(new SpaceAbcLocationDto
            {
                LocationId = location.LocationId,
                LocationCode = location.LocationCode,
                Qty = stockAvailable ? stock?.Qty ?? 0m : null,
                ProductCodes = productCodes,
                AbcRank = rank,
                AbsX = location.AbsX,
                AbsY = location.AbsY,
            });
        }

        response.ShippingTargets = await LoadShippingTargetsAsync(floorId, ct);
        var aLocations = response.Items
            .Where(x => x.AbcRank == "A" && x.AbsX.HasValue && x.AbsY.HasValue)
            .ToList();
        if (aLocations.Count > 0 && response.ShippingTargets.Count > 0)
        {
            response.AverageAShippingDistanceMm = Math.Round(
                (decimal)aLocations.Average(item => response.ShippingTargets.Min(target =>
                    Math.Sqrt(Math.Pow(item.AbsX!.Value - target.X, 2) + Math.Pow(item.AbsY!.Value - target.Y, 2)))), 2);
            response.DistanceMethod = "euclidean-fallback";
        }
        return response;
    }

    public async Task<SpaceControlTowerDto> GetControlTowerAsync(Guid siteId, CancellationToken ct = default)
    {
        var site = await _db.Space_Sites.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == siteId && !x.IsDeleted, ct)
            ?? throw new BizException("E-SPACE-001");
        var floors = await _db.Space_Floors.AsNoTracking()
            .Where(x => x.SiteId == siteId && !x.IsDeleted)
            .OrderBy(x => x.Level)
            .ToListAsync(ct);
        var floorContexts = await LoadSiteFloorContextsAsync(site, floors, ct);
        IReadOnlyList<WmsStockDto> siteStock;
        var siteStockAvailable = true;
        try
        {
            siteStock = await _stock.GetStockByLocationsAsync(
                floorContexts.SelectMany(x => x.Locations).Select(x => x.LocationCode).ToList(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "WMS stock query failed for Space control tower site {SiteId}", siteId);
            siteStock = Array.Empty<WmsStockDto>();
            siteStockAvailable = false;
        }

        var stockByFloor = siteStock
            .Join(
                floorContexts.SelectMany(x => x.Locations.Select(location => new
                {
                    x.Floor.Id,
                    location.LocationCode,
                })),
                stock => stock.LocationCode,
                location => location.LocationCode,
                (stock, location) => new { FloorId = location.Id, Stock = stock })
            .GroupBy(x => x.FloorId)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<WmsStockDto>)x.Select(y => y.Stock).ToList());
        var utilizationTimestamp = ServerNow;
        var floorResults = new List<(Space_Floor Floor, SpaceUtilizationResponse Utilization)>();
        foreach (var context in floorContexts)
        {
            stockByFloor.TryGetValue(context.Floor.Id, out var floorStock);
            floorResults.Add((context.Floor, BuildUtilization(
                context, floorStock ?? Array.Empty<WmsStockDto>(), siteStockAvailable, utilizationTimestamp)));
        }

        var allItems = floorResults.SelectMany(x => x.Utilization.Items).ToList();
        var alerts = floorResults.SelectMany(x => x.Utilization.Warnings).ToList();
        var stockAvailable = floorResults.All(x => x.Utilization.StockAvailable);
        var tower = new SpaceControlTowerDto
        {
            SiteId = site.Id,
            SiteCode = site.SiteCode,
            SiteName = site.SiteName,
            WarehouseCd = ResolveWarehouse(site),
            GeneratedAt = ServerNow,
            StockAvailable = stockAvailable,
            TotalLocations = allItems.Count,
            OccupiedLocations = allItems.Count(x => x.Qty > 0m),
            EmptyLocations = allItems.Count(x => x.StockAvailable && x.Qty == 0m),
            FullOrOverCapacityLocations = allItems.Count(x => x.BinStatus == 2 || x.Utilization >= 1m),
        };

        tower.UtilizationByUom = allItems
            .Where(x => x.IncludedInAggregate && x.CapacityUom.HasValue && x.Qty.HasValue && x.Capacity.HasValue)
            .GroupBy(x => x.CapacityUom!.Value)
            .Select(g => new SpaceTowerUtilizationDto
            {
                CapacityUom = g.Key,
                Qty = g.Sum(x => x.Qty!.Value),
                Capacity = g.Sum(x => x.Capacity!.Value),
                Utilization = g.Sum(x => x.Capacity!.Value) == 0m
                    ? 0m
                    : g.Sum(x => x.Qty!.Value) / g.Sum(x => x.Capacity!.Value),
                LocationCount = g.Count(),
            })
            .OrderByDescending(x => x.LocationCount)
            .ToList();

        tower.Floors = floorResults.Select(x => new SpaceTowerFloorDto
        {
            FloorId = x.Floor.Id,
            FloorCode = x.Floor.FloorCode,
            FloorName = x.Floor.FloorName,
            Level = x.Floor.Level,
            TotalLocations = x.Utilization.Items.Count,
            OccupiedLocations = x.Utilization.Items.Count(i => i.Qty > 0m),
            AlertCount = x.Utilization.Warnings.Count
                         + x.Utilization.Items.Count(i => i.Utilization > 1m),
            Locations = x.Utilization.Items.Select(i => new SpaceTowerLocationUtilizationDto
            {
                LocationCode = i.LocationCode,
                Utilization = i.Utilization,
            }).ToList(),
        }).ToList();

        try
        {
            var activityWindow = TenantDayWindowInServerTime();
            var activity = await _analytics.GetActivitySummaryAsync(
                tower.WarehouseCd, activityWindow.From, activityWindow.To, ct);
            tower.TodayInboundCount = activity.InboundCount;
            tower.TodayOutboundCount = activity.OutboundCount;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "WMS activity query failed for Space control tower site {SiteId}", siteId);
            alerts.Add(new SpaceAnalyticsWarningDto
            {
                Code = "W-SPACE-701",
                Message = "Today's WMS activity is temporarily unavailable.",
                Severity = "error",
            });
            tower.StockAvailable = false;
        }

        var snapshot = await _db.Space_AbcSnapshots.AsNoTracking()
            .Where(x => x.SiteId == siteId && !x.IsDeleted)
            .OrderByDescending(x => x.CalculatedAt)
            .FirstOrDefaultAsync(ct);
        TryDeserializeProducts(snapshot?.ResultJson, out var abcProducts);
        tower.AbcSnapshot = snapshot == null ? null : MapSnapshot(snapshot);
        tower.AbcProductCounts = new Dictionary<string, int>
        {
            ["A"] = abcProducts.Count(x => x.AbcRank == "A"),
            ["B"] = abcProducts.Count(x => x.AbcRank == "B"),
            ["C"] = abcProducts.Count(x => x.AbcRank == "C"),
        };
        var config = await GetConfigAsync(ct);
        if (snapshot == null || ServerNow - snapshot.CalculatedAt > TimeSpan.FromHours(config.StaleAfterHours))
        {
            alerts.Add(new SpaceAnalyticsWarningDto
            {
                Code = "W-SPACE-704",
                Message = snapshot == null ? "ABC snapshot has not been calculated." : "ABC snapshot is stale.",
            });
        }

        foreach (var over in allItems.Where(x => x.Utilization > 1m))
        {
            alerts.Add(new SpaceAnalyticsWarningDto
            {
                Code = "W-SPACE-703",
                LocationCode = over.LocationCode,
                Message = $"Location utilization is {over.Utilization:P0}.",
                Severity = "error",
            });
        }
        var distinctAlerts = alerts
            .GroupBy(x => new { x.Code, x.LocationCode, x.Message })
            .Select(x => x.First())
            .ToList();
        tower.AnomalyCount = distinctAlerts.Count;
        tower.Alerts = distinctAlerts
            .Take(200)
            .ToList();
        return tower;
    }

    private async Task<FloorContext> LoadFloorContextAsync(Guid floorId, CancellationToken ct)
    {
        var floor = await _db.Space_Floors.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == floorId && !x.IsDeleted, ct)
            ?? throw new BizException("E-SPACE-001");
        var site = await _db.Space_Sites.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == floor.SiteId && !x.IsDeleted, ct)
            ?? throw new BizException("E-SPACE-001");

        var locations = await (
            from l in _db.Space_Locations.AsNoTracking()
            join rackEntity in _db.Space_Racks.AsNoTracking()
                on l.RackId equals (Guid?)rackEntity.Id into rackJoin
            from rack in rackJoin.DefaultIfEmpty()
            join zoneEntity in _db.Space_Zones.AsNoTracking()
                on (Guid?)(rack == null ? null : rack.ZoneId) equals (Guid?)zoneEntity.Id into zoneJoin
            from zone in zoneJoin.DefaultIfEmpty()
            where l.FloorId == floorId && l.Placed && l.LocationCode != null && !l.IsDeleted
            select new LocationAnalyticsRow
            {
                LocationId = l.Id,
                LocationCode = l.LocationCode!,
                RackId = l.RackId,
                RackCode = rack == null ? null : rack.RackCode,
                ZoneId = zone == null ? null : zone.Id,
                ZoneCode = zone == null ? null : zone.ZoneCode,
                ZoneName = zone == null ? null : zone.ZoneName,
                ZoneType = zone == null ? null : zone.ZoneType,
                SpaceCapacity = l.Capacity,
                SpaceCapacityUom = l.CapacityUom,
                AbsX = l.AbsX,
                AbsY = l.AbsY,
            }).ToListAsync(ct);
        return new FloorContext(floor, site, locations);
    }

    private async Task<List<FloorContext>> LoadSiteFloorContextsAsync(
        Space_Site site,
        IReadOnlyList<Space_Floor> floors,
        CancellationToken ct)
    {
        var floorIds = floors.Select(x => x.Id).ToList();
        if (floorIds.Count == 0) return new List<FloorContext>();
        var locations = await (
            from l in _db.Space_Locations.AsNoTracking()
            join rackEntity in _db.Space_Racks.AsNoTracking()
                on l.RackId equals (Guid?)rackEntity.Id into rackJoin
            from rack in rackJoin.DefaultIfEmpty()
            join zoneEntity in _db.Space_Zones.AsNoTracking()
                on (Guid?)(rack == null ? null : rack.ZoneId) equals (Guid?)zoneEntity.Id into zoneJoin
            from zone in zoneJoin.DefaultIfEmpty()
            where l.FloorId.HasValue && floorIds.Contains(l.FloorId.GetValueOrDefault())
                  && l.Placed && l.LocationCode != null && !l.IsDeleted
            select new
            {
                FloorId = l.FloorId.GetValueOrDefault(),
                Row = new LocationAnalyticsRow
                {
                    LocationId = l.Id,
                    LocationCode = l.LocationCode!,
                    RackId = l.RackId,
                    RackCode = rack == null ? null : rack.RackCode,
                    ZoneId = zone == null ? null : zone.Id,
                    ZoneCode = zone == null ? null : zone.ZoneCode,
                    ZoneName = zone == null ? null : zone.ZoneName,
                    ZoneType = zone == null ? null : zone.ZoneType,
                    SpaceCapacity = l.Capacity,
                    SpaceCapacityUom = l.CapacityUom,
                    AbsX = l.AbsX,
                    AbsY = l.AbsY,
                },
            }).ToListAsync(ct);
        var byFloor = locations.GroupBy(x => x.FloorId)
            .ToDictionary(x => x.Key, x => x.Select(y => y.Row).ToList());
        return floors.Select(floor => new FloorContext(
            floor,
            site,
            byFloor.TryGetValue(floor.Id, out var rows) ? rows : new List<LocationAnalyticsRow>()))
            .ToList();
    }

    private async Task<List<SpacePointDto>> LoadShippingTargetsAsync(Guid floorId, CancellationToken ct)
    {
        var polygons = await _db.Space_Zones.AsNoTracking()
            .Where(x => x.FloorId == floorId && x.ZoneType == 3 && x.Enable && !x.IsDeleted)
            .Select(x => x.Polygon)
            .ToListAsync(ct);
        var result = new List<SpacePointDto>();
        foreach (var polygon in polygons)
        {
            try
            {
                using var doc = JsonDocument.Parse(polygon);
                var points = doc.RootElement.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.Array && x.GetArrayLength() >= 2)
                    .Select(x => new SpacePointDto
                    {
                        X = x[0].GetDouble(),
                        Y = x[1].GetDouble(),
                    })
                    .ToList();
                if (points.Count > 0)
                    result.Add(new SpacePointDto { X = points.Average(x => x.X), Y = points.Average(x => x.Y) });
            }
            catch (JsonException)
            {
                // Invalid zone polygons are already guarded by Space master validation; ignore legacy drift here.
            }
        }
        return result;
    }

    private static CapacityResolution ResolveCapacity(LocationAnalyticsRow location, WmsStockDto? stock)
    {
        var spaceValid = location.SpaceCapacity is > 0
                         && IsValidUom(location.SpaceCapacityUom);
        if (stock?.Capacity is > 0 && stock.CapacitySource == "wms-bin")
        {
            if (IsValidUom(stock.CapacityUom))
            {
                if (location.SpaceCapacityUom.HasValue && location.SpaceCapacityUom != stock.CapacityUom)
                    return CapacityResolution.Invalid(
                        "W-SPACE-703",
                        "WMS and Space capacity units conflict; this location is excluded from utilization.");
                return CapacityResolution.Valid(stock.Capacity.Value, stock.CapacityUom!.Value, "wms-bin");
            }
            if (spaceValid)
                return CapacityResolution.Valid(
                    location.SpaceCapacity!.Value,
                    location.SpaceCapacityUom!.Value,
                    "space-fallback",
                    "W-SPACE-703",
                    "WMS capacity has no unit; Space capacity is used as fallback.");
            return CapacityResolution.Invalid(
                "W-SPACE-703",
                "WMS capacity has no valid unit and no Space fallback is configured.");
        }
        if (spaceValid)
            return CapacityResolution.Valid(
                location.SpaceCapacity!.Value,
                location.SpaceCapacityUom!.Value,
                "space-fallback");
        return CapacityResolution.Invalid(
            "W-SPACE-703",
            "Capacity or capacity unit is missing; this location is excluded from utilization.");
    }

    private static bool IsValidUom(int? uom) => uom is >= 1 and <= 4;

    private static List<SpaceUtilizationAggregateDto> AggregateUtilization(
        IEnumerable<SpaceUtilizationItemDto> items,
        Func<SpaceUtilizationItemDto, Guid?> id,
        Func<SpaceUtilizationItemDto, string> code,
        Func<SpaceUtilizationItemDto, string> name)
    {
        return items
            .Where(x => x.IncludedInAggregate && x.CapacityUom.HasValue && x.Qty.HasValue && x.Capacity.HasValue)
            .GroupBy(x => new { Id = id(x), Code = code(x), Name = name(x), Uom = x.CapacityUom!.Value })
            .Select(g => new SpaceUtilizationAggregateDto
            {
                EntityId = g.Key.Id,
                Code = g.Key.Code,
                Name = g.Key.Name,
                CapacityUom = g.Key.Uom,
                LocationCount = g.Count(),
                Qty = g.Sum(x => x.Qty!.Value),
                Capacity = g.Sum(x => x.Capacity!.Value),
                Utilization = g.Sum(x => x.Capacity!.Value) == 0m
                    ? 0m
                    : g.Sum(x => x.Qty!.Value) / g.Sum(x => x.Capacity!.Value),
                OverCapacityCount = g.Count(x => x.Utilization > 1m),
            })
            .OrderByDescending(x => x.Utilization)
            .ToList();
    }

    private static void ValidateConfig(SpaceAnalyticsConfigDto request)
    {
        if (request.WindowDays is < 1 or > 365)
            throw new ArgumentOutOfRangeException(nameof(request.WindowDays));
        if (!string.Equals(request.Metric, "quantity", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.Metric, "frequency", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Metric must be quantity or frequency.", nameof(request.Metric));
        if (request.ThresholdA <= 0m || request.ThresholdA >= request.ThresholdB || request.ThresholdB > 1m)
            throw new ArgumentException("ABC thresholds must satisfy 0 < A < B <= 1.");
        if (request.StaleAfterHours is < 1 or > 720)
            throw new ArgumentOutOfRangeException(nameof(request.StaleAfterHours));
        if (request.ScheduledHourLocal is < 0 or > 23)
            throw new ArgumentOutOfRangeException(nameof(request.ScheduledHourLocal));
    }

    private static void ApplyConfig(Space_AnalyticsConfig config, SpaceAnalyticsConfigDto request)
    {
        config.WindowDays = request.WindowDays;
        config.Metric = AbcClassifier.ToValue(AbcClassifier.ParseMetric(request.Metric));
        config.ThresholdA = request.ThresholdA;
        config.ThresholdB = request.ThresholdB;
        config.StaleAfterHours = request.StaleAfterHours;
        config.ScheduledHourLocal = request.ScheduledHourLocal;
        config.EnableScheduledSnapshot = request.EnableScheduledSnapshot;
    }

    private DateTime ServerNow => _timeProvider.GetLocalNow().DateTime;

    private DateTime TenantNow =>
        TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), _tenantClock.GetTenantTimeZone()).DateTime;

    private DateOnly TenantToday => DateOnly.FromDateTime(TenantNow);

    private (DateTime From, DateTime To) TenantDayWindowInServerTime()
    {
        var tenantZone = _tenantClock.GetTenantTimeZone();
        var tenantDate = DateOnly.FromDateTime(TenantNow);
        var tenantStart = tenantDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var tenantEnd = tenantDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var utcStart = TimeZoneInfo.ConvertTimeToUtc(tenantStart, tenantZone);
        var utcEnd = TimeZoneInfo.ConvertTimeToUtc(tenantEnd, tenantZone);
        return (
            TimeZoneInfo.ConvertTimeFromUtc(utcStart, _timeProvider.LocalTimeZone),
            TimeZoneInfo.ConvertTimeFromUtc(utcEnd, _timeProvider.LocalTimeZone));
    }

    private static SpaceAnalyticsConfigDto MapConfig(Space_AnalyticsConfig x) => new()
    {
        WindowDays = x.WindowDays,
        Metric = AbcClassifier.ToValue(AbcClassifier.ParseMetric(x.Metric)),
        ThresholdA = x.ThresholdA,
        ThresholdB = x.ThresholdB,
        StaleAfterHours = x.StaleAfterHours,
        ScheduledHourLocal = x.ScheduledHourLocal,
        EnableScheduledSnapshot = x.EnableScheduledSnapshot,
    };

    private static SpaceAbcSnapshotMetaDto MapSnapshot(Space_AbcSnapshot x) => new()
    {
        SnapshotId = x.Id,
        SiteId = x.SiteId,
        WarehouseCd = x.WarehouseCd,
        CalculatedAt = x.CalculatedAt,
        WindowFrom = x.WindowFrom,
        WindowTo = x.WindowTo,
        WindowDays = x.WindowDays,
        Metric = x.Metric,
        ThresholdA = x.ThresholdA,
        ThresholdB = x.ThresholdB,
        ItemCount = x.ItemCount,
        Trigger = x.Trigger,
    };

    private static bool TryDeserializeProducts(string? json, out List<SpaceAbcProductDto> products)
    {
        products = new List<SpaceAbcProductDto>();
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            products = JsonSerializer.Deserialize<List<SpaceAbcProductDto>>(json, Json)
                       ?? new List<SpaceAbcProductDto>();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string ResolveWarehouse(Space_Site site)
    {
        var warehouse = string.IsNullOrWhiteSpace(site.WarehouseCd) ? site.SiteCode : site.WarehouseCd;
        if (warehouse.Length > 10) throw new BizException("E-SPACE-405");
        return warehouse;
    }

    private static int RankOrder(string value) => value switch { "A" => 0, "B" => 1, _ => 2 };

    private static (int Id, string Key, string Color) ZoneType(int value) => value switch
    {
        1 => (1, "storage", "#3b82f6"),
        2 => (2, "receiving", "#22c55e"),
        3 => (3, "shipping", "#f97316"),
        4 => (4, "picking", "#a855f7"),
        5 => (5, "passage", "#64748b"),
        6 => (6, "inspection", "#06b6d4"),
        7 => (7, "return", "#f43f5e"),
        8 => (8, "frozen", "#0ea5e9"),
        _ => (0, "unassigned", "#94a3b8"),
    };

    private sealed class LocationAnalyticsRow
    {
        public Guid LocationId { get; set; }
        public string LocationCode { get; set; } = string.Empty;
        public Guid? RackId { get; set; }
        public string? RackCode { get; set; }
        public Guid? ZoneId { get; set; }
        public string? ZoneCode { get; set; }
        public string? ZoneName { get; set; }
        public int? ZoneType { get; set; }
        public int? SpaceCapacity { get; set; }
        public int? SpaceCapacityUom { get; set; }
        public int? AbsX { get; set; }
        public int? AbsY { get; set; }
    }

    private sealed record FloorContext(
        Space_Floor Floor,
        Space_Site Site,
        List<LocationAnalyticsRow> Locations);

    private sealed record CapacityResolution(
        decimal? Capacity,
        int? Uom,
        string? Source,
        bool Included,
        string? WarningCode,
        string? WarningMessage)
    {
        public static readonly CapacityResolution Unavailable = new(null, null, null, false, null, null);
        public static CapacityResolution Valid(
            decimal capacity, int uom, string source,
            string? warningCode = null, string? warningMessage = null)
            => new(capacity, uom, source, true, warningCode, warningMessage);
        public static CapacityResolution Invalid(string code, string message)
            => new(null, null, null, false, code, message);
    }
}
