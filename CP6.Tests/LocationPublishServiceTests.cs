using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Space;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace CP6.Tests;

/// <summary>
/// LocationPublishService 测试（ch04 D-3/D-4/D-5）。[InMemory 仅测逻辑]
/// </summary>
public class LocationPublishServiceTests
{
    private static CP6Context Db() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static LocationPublishService MakePublishSvc(
        CP6Context db,
        IWmsStockQuery? stock = null,
        ISpaceBridgeHook? hook = null)
    {
        var t = new TenantContext();
        var code = new CodeEngineService(db);
        hook ??= new SpaceBridgeHook(db, NullLogger<SpaceBridgeHook>.Instance, new NoOpWmsLocationConsumer());
        stock ??= new StubWmsStockQuery();
        return new LocationPublishService(db, t, code, hook, stock);
    }

    // ── D-3: 整层发布 ──────────────────────────────────────────────────────

    private static string ValidSegmentsJson() => JsonSerializer.Serialize(new[]
    {
        new CodeSegmentDef { Key = "zone", Source = "zone-code", Sep = "-" },
        new CodeSegmentDef { Key = "col",  Source = "col",       Sep = "" }
    });

    [Fact]
    public async Task Publish_GatePassed_FlipsStatusAndEmitsEvent()
    {
        using var db = Db();
        var floorId = Guid.NewGuid();
        var rackId = Guid.NewGuid();
        // Seed hierarchy to satisfy PrecheckAsync + BuildItemAsync
        var site = new Space_Site { Id = Guid.NewGuid(), SiteCode = "S1", SiteName = "S1" };
        var floor = new Space_Floor { Id = floorId, SiteId = site.Id, Level = 1, FloorCode = "F1", FloorName = "F1" };
        var zone = new Space_Zone { Id = Guid.NewGuid(), FloorId = floorId, ZoneCode = "Z1", ZoneName = "Z1" };
        var rack = new Space_Rack { Id = rackId, ZoneId = zone.Id, FloorId = floorId, RackCode = "R1", Cols = 1, Levels = 1, CellW = 1000, CellH = 1000, CellD = 1000 };
        // Seed valid code rule (ScopeType=0 tenant default) so PrecheckAsync passes
        db.Space_CodeRules.Add(new Space_CodeRule
        {
            Id = Guid.NewGuid(), RuleName = "default", ScopeType = 0, IsDefault = true,
            Segments = ValidSegmentsJson()
        });
        db.Space_Sites.Add(site);
        db.Space_Floors.Add(floor);
        db.Space_Zones.Add(zone);
        db.Space_Racks.Add(rack);
        db.Space_Locations.Add(new Space_Location
        {
            Id = Guid.NewGuid(), FloorId = floorId, RackId = rackId,
            Placed = true, Status = 0, CodeOrigin = 1, LocationCode = "A-01-01-01",
            Col = 1, Level = 1, Depth = 1
        });
        await db.SaveChangesAsync();

        var svc = MakePublishSvc(db);
        var n = await svc.PublishFloorAsync(floorId, zoneId: null, user: "u");
        Assert.Equal(1, n);

        var loc = await db.Space_Locations.SingleAsync();
        Assert.Equal(1, loc.Status);
        Assert.Equal(1, loc.Version);

        var evt = await db.IntegrationEvents.SingleAsync();
        Assert.Equal("SPACE", evt.SourceModule);
        var payload = JsonSerializer.Deserialize<JsonElement>(evt.PayloadJson);
        var firstOp = payload.GetProperty("Items")[0].GetProperty("Op").GetString();
        Assert.Equal("UPSERT", firstOp);
    }

    [Fact]
    public async Task Publish_EmptyCode_Throws_E307_NoEvent()
    {
        using var db = Db();
        var floorId = Guid.NewGuid();
        // Zone without a code rule → PrecheckAsync returns PrecheckErrors, triggering E-307
        // Alternatively, add a location with null code → EmptyCodeCount > 0 → E-307
        var zone = new Space_Zone { Id = Guid.NewGuid(), FloorId = floorId, ZoneCode = "Z1", ZoneName = "Z1" };
        db.Space_Zones.Add(zone);
        db.Space_Locations.Add(new Space_Location
        {
            Id = Guid.NewGuid(), FloorId = floorId, RackId = Guid.NewGuid(),
            Placed = true, Status = 0, CodeOrigin = 1, LocationCode = null
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => MakePublishSvc(db).PublishFloorAsync(floorId, null, "u"));
        Assert.StartsWith("E-SPACE-307", ex.Message);
        Assert.Equal(0, await db.IntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task Publish_NoLocations_Returns0()
    {
        using var db = Db();
        var floorId = Guid.NewGuid();
        var n = await MakePublishSvc(db).PublishFloorAsync(floorId, null, "u");
        Assert.Equal(0, n);
    }

    // ── D-4: 停用 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Deactivate_StockZero_Success_EmitsDeactivateEvent()
    {
        using var db = Db();
        var floorId = Guid.NewGuid();
        var rackId = Guid.NewGuid();
        var site = new Space_Site { Id = Guid.NewGuid(), SiteCode = "S1", SiteName = "S1" };
        var floor = new Space_Floor { Id = floorId, SiteId = site.Id, Level = 1, FloorCode = "F1", FloorName = "F1" };
        var zone = new Space_Zone { Id = Guid.NewGuid(), FloorId = floorId, ZoneCode = "Z1", ZoneName = "Z1" };
        var rack = new Space_Rack { Id = rackId, ZoneId = zone.Id, FloorId = floorId, RackCode = "R1", Cols = 1, Levels = 1, CellW = 1000, CellH = 1000, CellD = 1000 };
        db.Space_Sites.Add(site);
        db.Space_Floors.Add(floor);
        db.Space_Zones.Add(zone);
        db.Space_Racks.Add(rack);
        var locId = Guid.NewGuid();
        db.Space_Locations.Add(new Space_Location
        {
            Id = locId, FloorId = floorId, RackId = rackId,
            Placed = true, Status = 1, CodeOrigin = 1, LocationCode = "A-01-01-01",
            Col = 1, Level = 1, Depth = 1, Version = 1
        });
        await db.SaveChangesAsync();

        await MakePublishSvc(db).DeactivateAsync(locId, "u");

        var loc = await db.Space_Locations.SingleAsync();
        Assert.Equal(2, loc.Status);
        Assert.Equal(2, loc.Version);

        var evt = await db.IntegrationEvents.SingleAsync();
        var payload = JsonSerializer.Deserialize<JsonElement>(evt.PayloadJson);
        var firstOp = payload.GetProperty("Items")[0].GetProperty("Op").GetString();
        Assert.Equal("DEACTIVATE", firstOp);
    }

    [Fact]
    public async Task Deactivate_StockPositive_Throws_E401()
    {
        using var db = Db();
        var locId = Guid.NewGuid();
        db.Space_Locations.Add(new Space_Location
        {
            Id = locId, FloorId = Guid.NewGuid(), RackId = Guid.NewGuid(),
            Placed = true, Status = 1, CodeOrigin = 1, LocationCode = "X-01-01-01",
            Col = 1, Level = 1, Depth = 1, Version = 1
        });
        await db.SaveChangesAsync();

        var stockStub = new FixedStockQuery(5);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => MakePublishSvc(db, stock: stockStub).DeactivateAsync(locId, "u"));
        Assert.StartsWith("E-SPACE-401", ex.Message);
        Assert.Equal(0, await db.IntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task Deactivate_NotPublished_Throws_E004()
    {
        using var db = Db();
        var locId = Guid.NewGuid();
        db.Space_Locations.Add(new Space_Location
        {
            Id = locId, FloorId = Guid.NewGuid(), Status = 0, LocationCode = "Y-01-01-01"
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => MakePublishSvc(db).DeactivateAsync(locId, "u"));
        Assert.StartsWith("E-SPACE-004", ex.Message);
    }

    // ── D-5: 采纳导入 ──────────────────────────────────────────────────────

    [Fact]
    public async Task Adopt_NewCode_Creates_Status1_CodeOrigin2_PlacedFalse()
    {
        using var db = Db();
        var svc = MakePublishSvc(db);
        var items = new List<(string, Dictionary<string, object?>?)>
        {
            ("EXT-001", null),
            ("EXT-002", null)
        };
        var (imported, skipped) = await svc.AdoptAsync(items, "u");
        Assert.Equal(2, imported);
        Assert.Empty(skipped);
        var locs = await db.Space_Locations.ToListAsync();
        Assert.Equal(2, locs.Count);
        Assert.All(locs, l =>
        {
            Assert.Equal(1, l.Status);
            Assert.Equal(2, l.CodeOrigin);
            Assert.False(l.Placed);
            Assert.Null(l.RackId);
        });
        Assert.Equal(0, await db.IntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task Adopt_DuplicateCode_Skipped()
    {
        using var db = Db();
        db.Space_Locations.Add(new Space_Location
        {
            Id = Guid.NewGuid(), Status = 1, LocationCode = "EXT-001", CodeOrigin = 2
        });
        await db.SaveChangesAsync();

        var svc = MakePublishSvc(db);
        var items = new List<(string, Dictionary<string, object?>?)>
        {
            ("EXT-001", null),
            ("EXT-003", null)
        };
        var (imported, skipped) = await svc.AdoptAsync(items, "u");
        Assert.Equal(1, imported);
        Assert.Single(skipped);
        Assert.Contains("EXT-001", skipped);
    }

    private sealed class FixedStockQuery : IWmsStockQuery
    {
        private readonly int _qty;
        public FixedStockQuery(int qty) => _qty = qty;
        public Task<int> GetStockQtyAsync(string locationCode) => Task.FromResult(_qty);
    }
}
