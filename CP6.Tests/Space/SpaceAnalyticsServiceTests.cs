using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Space;
using CP6.Core.Services.Wms;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Space;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CP6.Tests.Space;

public class SpaceAnalyticsServiceTests
{
    [Fact]
    public void AbcClassifier_UsesInclusiveBoundaries_AndDeterministicOrder()
    {
        var rows = AbcClassifier.Classify(new[]
        {
            new AbcInputRow("PA", 8, 80m),
            new AbcInputRow("PB", 2, 15m),
            new AbcInputRow("PC", 1, 5m),
        }, AbcMetric.Quantity);

        Assert.Equal(new[] { "A", "B", "C" }, rows.Select(x => x.AbcRank));
        Assert.Equal(0.80m, rows[0].CumulativeRatio);
        Assert.Equal(0.95m, rows[1].CumulativeRatio);

        var frequency = AbcClassifier.Classify(new[]
        {
            new AbcInputRow("HIGH_QTY", 1, 1000m),
            new AbcInputRow("HIGH_FREQ", 9, 9m),
        }, AbcMetric.Frequency);
        Assert.Equal("HIGH_FREQ", frequency[0].ProductCd);
    }

    [Fact]
    public async Task Utilization_ExcludesUnitConflict_AndFallsBackToSpaceCapacity()
    {
        using var db = NewDb();
        var ids = await SeedFloorAsync(db, locationCount: 2);
        var locations = await db.Space_Locations.OrderBy(x => x.LocationCode).ToListAsync();
        locations[0].Capacity = 10;
        locations[0].CapacityUom = 3;
        locations[1].Capacity = 20;
        locations[1].CapacityUom = 1;
        await db.SaveChangesAsync();

        var stock = new FakeStockQuery(new[]
        {
            new WmsStockDto
            {
                LocationCode = locations[0].LocationCode!, Qty = 5m, Capacity = 10m,
                CapacityUom = 2, CapacitySource = "wms-bin", BinStatus = 1,
            },
            new WmsStockDto
            {
                LocationCode = locations[1].LocationCode!, Qty = 5m, Capacity = 99m,
                CapacitySource = "wms-location", BinStatus = 1,
            },
        });
        var service = Service(db, stock);

        var result = await service.GetUtilizationAsync(ids.FloorId);

        var conflict = result.Items.Single(x => x.LocationCode == locations[0].LocationCode);
        Assert.False(conflict.IncludedInAggregate);
        Assert.Equal("W-SPACE-703", conflict.WarningCode);

        var fallback = result.Items.Single(x => x.LocationCode == locations[1].LocationCode);
        Assert.True(fallback.IncludedInAggregate);
        Assert.Equal(20m, fallback.Capacity);
        Assert.Equal(1, fallback.CapacityUom);
        Assert.Equal("space-fallback", fallback.CapacitySource);
        Assert.Equal(0.25m, fallback.Utilization);
        Assert.Single(result.Zones);
    }

    [Fact]
    public async Task RebuildSnapshot_MapsMixedLocationToHighestAbcClass()
    {
        using var db = NewDb();
        var ids = await SeedFloorAsync(db, locationCount: 1, addShippingZone: true);
        var location = await db.Space_Locations.SingleAsync();
        var stock = new FakeStockQuery(new[]
        {
            new WmsStockDto
            {
                LocationCode = location.LocationCode!, Qty = 10m, BinStatus = 1,
                ProductCodes = new List<string> { "PC", "PA" }, ProductKinds = 2,
            },
        });
        var analytics = new FakeAnalyticsQuery(new[]
        {
            new WmsOutboundAggregate { ProductCd = "PA", OutCount = 8, OutQty = 80m },
            new WmsOutboundAggregate { ProductCd = "PB", OutCount = 2, OutQty = 15m },
            new WmsOutboundAggregate { ProductCd = "PC", OutCount = 1, OutQty = 5m },
        });
        var service = Service(db, stock, analytics);

        var meta = await service.RebuildAbcAsync(ids.SiteId, "manual", "tester");
        var result = await service.GetAbcAsync(ids.FloorId);
        var compact = await service.GetAbcAsync(ids.FloorId, includeProducts: false);

        Assert.Equal("manual", meta.Trigger);
        Assert.Equal(3, meta.ItemCount);
        Assert.True(result.HasSnapshot);
        Assert.Equal(3, result.Products.Count);
        Assert.Empty(compact.Products);
        Assert.Equal("A", Assert.Single(compact.Items).AbcRank);
        Assert.Equal("A", Assert.Single(result.Items).AbcRank);
        Assert.NotEmpty(result.ShippingTargets);
        Assert.NotNull(result.AverageAShippingDistanceMm);
    }

    [Fact]
    public async Task AnalyticsConfig_IsTenantIsolated()
    {
        var tenant = new TenantContext { CurrentTenantId = Guid.NewGuid() };
        var tenantA = tenant.CurrentTenantId;
        var tenantB = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var db = new CP6Context(options, tenant);
        var service = Service(db, new FakeStockQuery(Array.Empty<WmsStockDto>()));

        await service.UpdateConfigAsync(new SpaceAnalyticsConfigUpdate { WindowDays = 30 }, "A");
        tenant.CurrentTenantId = tenantB;
        Assert.Equal(90, (await service.GetConfigAsync()).WindowDays);
        await service.UpdateConfigAsync(new SpaceAnalyticsConfigUpdate { WindowDays = 60 }, "B");

        tenant.CurrentTenantId = tenantA;
        Assert.Equal(30, (await service.GetConfigAsync()).WindowDays);
        tenant.CurrentTenantId = tenantB;
        Assert.Equal(60, (await service.GetConfigAsync()).WindowDays);
    }

    [Fact]
    public async Task ScheduledSnapshot_UsesTenantDate_AndManualSnapshotDoesNotSuppressIt()
    {
        using var db = NewDb();
        var ids = await SeedFloorAsync(db, locationCount: 1);
        var tenantZone = TimeZoneInfo.CreateCustomTimeZone("UTC+09-test", TimeSpan.FromHours(9), "UTC+09", "UTC+09");
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 7, 19, 0, 30, 0, TimeSpan.Zero));
        var service = Service(db, new FakeStockQuery(Array.Empty<WmsStockDto>()),
            clock: new FixedTenantClock(tenantZone), timeProvider: time);
        await service.UpdateConfigAsync(new SpaceAnalyticsConfigUpdate
        {
            ScheduledHourLocal = 9,
            EnableScheduledSnapshot = true,
        }, "tester");
        await service.RebuildAbcAsync(ids.SiteId, "manual", "tester");

        Assert.Equal(1, await service.RebuildDueSnapshotsAsync());
        Assert.Equal(0, await service.RebuildDueSnapshotsAsync());

        var snapshots = await db.Space_AbcSnapshots.OrderBy(x => x.Trigger).ToListAsync();
        Assert.Equal(2, snapshots.Count);
        Assert.Null(snapshots.Single(x => x.Trigger == "manual").ScheduledDate);
        Assert.Equal(new DateOnly(2026, 7, 19), snapshots.Single(x => x.Trigger == "scheduled").ScheduledDate);
        var scheduledIndex = db.Model.FindEntityType(typeof(Space_AbcSnapshot))!.GetIndexes()
            .Single(x => x.Properties.Any(p => p.Name == nameof(Space_AbcSnapshot.ScheduledDate)));
        Assert.True(scheduledIndex.IsUnique);
        Assert.Equal("[ScheduledDate] IS NOT NULL", scheduledIndex.GetFilter());
    }

    [Fact]
    public async Task AbcWithoutSnapshot_DoesNotMislabelOccupiedLocationAsC()
    {
        using var db = NewDb();
        var ids = await SeedFloorAsync(db, locationCount: 1);
        var location = await db.Space_Locations.SingleAsync();
        var service = Service(db, new FakeStockQuery(new[]
        {
            new WmsStockDto
            {
                LocationCode = location.LocationCode!, Qty = 5m,
                ProductCodes = new List<string> { "UNKNOWN" },
            },
        }));

        var result = await service.GetAbcAsync(ids.FloorId);

        Assert.False(result.HasSnapshot);
        Assert.Null(Assert.Single(result.Items).AbcRank);
    }

    [Fact]
    public async Task ControlTower_AnomalyCountIsNotCappedByAlertDetailLimit()
    {
        using var db = NewDb();
        var ids = await SeedFloorAsync(db, locationCount: 201);
        var rows = await db.Space_Locations.Select(x => new WmsStockDto
        {
            LocationCode = x.LocationCode!, Qty = 2m, Capacity = 1m,
            CapacityUom = 1, CapacitySource = "wms-bin", BinStatus = 2,
        }).ToListAsync();
        var service = Service(db, new FakeStockQuery(rows));

        var tower = await service.GetControlTowerAsync(ids.SiteId);

        Assert.Equal(200, tower.Alerts.Count);
        Assert.Equal(202, tower.AnomalyCount); // 201 over-capacity + one missing-ABC warning
    }

    [Fact]
    public async Task ControlTower_UsesTenantLocalDayForActivityWindow()
    {
        using var db = NewDb();
        var ids = await SeedFloorAsync(db, locationCount: 0);
        var tenantZone = TimeZoneInfo.CreateCustomTimeZone("UTC+09-activity", TimeSpan.FromHours(9), "UTC+09", "UTC+09");
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 7, 19, 18, 0, 0, TimeSpan.Zero));
        var analytics = new FakeAnalyticsQuery(Array.Empty<WmsOutboundAggregate>());
        var service = Service(db, new FakeStockQuery(Array.Empty<WmsStockDto>()), analytics,
            new FixedTenantClock(tenantZone), time);

        await service.GetControlTowerAsync(ids.SiteId);

        Assert.Equal(new DateTime(2026, 7, 19, 15, 0, 0), analytics.LastActivityFrom);
        Assert.Equal(new DateTime(2026, 7, 20, 15, 0, 0), analytics.LastActivityTo);
    }

    private static CP6Context NewDb() => TestHelper.CreateInMemoryContext();

    private static SpaceAnalyticsService Service(
        CP6Context db,
        IWmsStockQuery stock,
        IWmsAnalyticsQuery? analytics = null,
        ITenantClock? clock = null,
        TimeProvider? timeProvider = null) =>
        new(db, stock, analytics ?? new FakeAnalyticsQuery(Array.Empty<WmsOutboundAggregate>()),
            NullLogger<SpaceAnalyticsService>.Instance,
            clock ?? new FixedTenantClock(TimeZoneInfo.Utc),
            timeProvider ?? TimeProvider.System);

    private static async Task<(Guid SiteId, Guid FloorId)> SeedFloorAsync(
        CP6Context db, int locationCount, bool addShippingZone = false)
    {
        var siteId = Guid.NewGuid();
        var floorId = Guid.NewGuid();
        var zoneId = Guid.NewGuid();
        var rackId = Guid.NewGuid();
        db.Space_Sites.Add(new Space_Site
        {
            Id = siteId, SiteCode = "W1", SiteName = "Site", WarehouseCd = "W1",
        });
        db.Space_Floors.Add(new Space_Floor
        {
            Id = floorId, SiteId = siteId, FloorCode = "F1", FloorName = "Floor", Level = 1,
        });
        db.Space_Zones.Add(new Space_Zone
        {
            Id = zoneId, FloorId = floorId, ZoneCode = "ST", ZoneName = "Storage", ZoneType = 1,
        });
        db.Space_Racks.Add(new Space_Rack
        {
            Id = rackId, FloorId = floorId, ZoneId = zoneId, RackCode = "R1",
            Cols = locationCount, Levels = 1, CellW = 1000, CellH = 1000, CellD = 1000,
        });
        for (var i = 0; i < locationCount; i++)
        {
            db.Space_Locations.Add(new Space_Location
            {
                Id = Guid.NewGuid(), FloorId = floorId, RackId = rackId, Placed = true, Status = 1,
                LocationCode = $"L-{i + 1}", AbsX = 100 + i * 100, AbsY = 100,
            });
        }
        if (addShippingZone)
        {
            db.Space_Zones.Add(new Space_Zone
            {
                Id = Guid.NewGuid(), FloorId = floorId, ZoneCode = "SHIP", ZoneName = "Shipping",
                ZoneType = 3, Polygon = "[[1000,0],[1200,0],[1200,200],[1000,200]]",
            });
        }
        await db.SaveChangesAsync();
        return (siteId, floorId);
    }

    private sealed class FakeStockQuery : IWmsStockQuery
    {
        private readonly IReadOnlyList<WmsStockDto> _rows;
        public FakeStockQuery(IReadOnlyList<WmsStockDto> rows) => _rows = rows;
        public Task<IReadOnlyList<WmsStockDto>> GetStockByLocationsAsync(
            IReadOnlyCollection<string> locationCodes, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<WmsStockDto>>(
                _rows.Where(x => locationCodes.Contains(x.LocationCode)).ToList());
        public Task<IReadOnlyList<WmsLocationHit>> FindLocationsAsync(
            StockLocateQuery query, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<WmsLocationHit>>(Array.Empty<WmsLocationHit>());
        public Task<decimal> GetStockQtyAsync(
            string locationCode, string? warehouseCd = null, CancellationToken ct = default) =>
            Task.FromResult(0m);
    }

    private sealed class FakeAnalyticsQuery : IWmsAnalyticsQuery
    {
        private readonly IReadOnlyList<WmsOutboundAggregate> _rows;
        public FakeAnalyticsQuery(IReadOnlyList<WmsOutboundAggregate> rows) => _rows = rows;
        public DateTime? LastActivityFrom { get; private set; }
        public DateTime? LastActivityTo { get; private set; }
        public Task<IReadOnlyList<WmsOutboundAggregate>> GetOutboundAggregatesAsync(
            string warehouseCd, DateTime fromInclusive, DateTime toExclusive, CancellationToken ct = default) =>
            Task.FromResult(_rows);
        public Task<WmsActivitySummary> GetActivitySummaryAsync(
            string warehouseCd, DateTime fromInclusive, DateTime toExclusive, CancellationToken ct = default)
        {
            LastActivityFrom = fromInclusive;
            LastActivityTo = toExclusive;
            return Task.FromResult(new WmsActivitySummary());
        }
    }

    private sealed class FixedTenantClock(TimeZoneInfo zone) : ITenantClock
    {
        public TimeZoneInfo GetTenantTimeZone() => zone;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => now;
    }
}
