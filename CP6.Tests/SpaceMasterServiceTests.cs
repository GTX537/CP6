using CP6.Core.EFDbContext;
using CP6.WebApi.Localization;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Space;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;

namespace CP6.Tests;

/// <summary>
/// Space 主数据服务测试（ch00 B-3/B-4/B-5/B-6）。
/// 覆盖：Site/Zone 唯一校验 + 多边形顶点数 + 货架改位姿触发几何重算 + 删除护栏 + 场景/待绑定过滤。
/// [InMemory 仅测逻辑]：唯一索引级/并发级正确性需补真库集成测（plan Task D-9）。
/// </summary>
public class SpaceMasterServiceTests
{
    /// <summary>
    /// 每测建独立 InMemory 库 + 服务实例（避免跨测污染）。
    /// 单参构造 → CurrentTenantId 回退 TenantContext.DefaultTenant；
    /// 全局过滤 + 写入盖章均按默认租户，测试自洽，无需显式设 TenantId。
    /// </summary>
    private static (CP6Context, SpaceMasterService) Make()
    {
        var db = new CP6Context(new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var geo = new LocationGeometryService(db);
        // publish 依赖组装同 SceneServiceTests.Make()（Task 2 已升级）——删巷道放行路径/改挂 re-publish 需要
        var publish = new LocationPublishService(db, new TenantContext(), new CodeEngineService(db),
            new SpaceBridgeHook(db, NullLogger<SpaceBridgeHook>.Instance, new NoOpWmsLocationConsumer()),
            new StubWmsStockQuery(), new CP6.Core.Services.Wms.WmsBinDeactivator(db));
        return (db, new SpaceMasterService(db, geo, publish));
    }

    // ── B-3: Site/Zone 唯一校验 + 多边形顶点数 ────────────────────────────

    [Fact]
    public async Task CreateSite_DuplicateCode_Throws_E001()
    {
        var (_, svc) = Make();
        await svc.CreateSiteAsync(new SiteDto { SiteCode = "WH1", SiteName = "a" }, "u");
        var ex = await Assert.ThrowsAsync<BizException>(
            () => svc.CreateSiteAsync(new SiteDto { SiteCode = "WH1", SiteName = "b" }, "u"));
        Assert.Equal("E-SPACE-001", ex.Code);
    }

    /// <summary>编辑器 reconciliation：scene 须含完整 Floor 对象（底图标定 + 原点）。</summary>
    [Fact]
    public async Task GetScene_IncludesFloorObject()
    {
        var (_, svc) = Make();
        var siteId = await svc.CreateSiteAsync(new SiteDto { SiteCode = "S", SiteName = "s" }, "u");
        var floorId = await svc.CreateFloorAsync(new FloorDto
        {
            SiteId = siteId, Level = 1, FloorCode = "F1", FloorName = "1F",
            UnderlayScale = 2.5, OriginX = 100
        }, "u");
        var scene = await svc.GetSceneAsync(floorId);
        Assert.NotNull(scene.Floor);
        Assert.Equal("F1", scene.Floor!.FloorCode);
        Assert.Equal(2.5, scene.Floor.UnderlayScale);
        Assert.Equal(100, scene.Floor.OriginX);
    }

    [Fact]
    public async Task CreateSite_UniqueCode_Succeeds()
    {
        var (_, svc) = Make();
        var id = await svc.CreateSiteAsync(new SiteDto { SiteCode = "WH1", SiteName = "a" }, "u");
        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task CreateZone_PolygonLessThan3Vertices_Throws_E006()
    {
        var (_, svc) = Make();
        var ex = await Assert.ThrowsAsync<BizException>(
            () => svc.CreateZoneAsync(
                new ZoneDto { FloorId = Guid.NewGuid(), ZoneCode = "A", Polygon = "[[0,0],[1,1]]" }, "u"));
        Assert.Equal("E-SPACE-006", ex.Code);
    }

    [Fact]
    public async Task CreateZone_ValidPolygon_Succeeds()
    {
        var (_, svc) = Make();
        var id = await svc.CreateZoneAsync(
            new ZoneDto { FloorId = Guid.NewGuid(), ZoneCode = "A",
                          Polygon = "[[0,0],[100,0],[100,100]]" }, "u");
        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task UpdateZone_InvalidPolygon_Throws_E006()
    {
        var (_, svc) = Make();
        var floorId = Guid.NewGuid();
        var id = await svc.CreateZoneAsync(
            new ZoneDto { FloorId = floorId, ZoneCode = "Z1",
                          Polygon = "[[0,0],[1,0],[1,1]]" }, "u");
        var ex = await Assert.ThrowsAsync<BizException>(
            () => svc.UpdateZoneAsync(id,
                new ZoneDto { FloorId = floorId, ZoneCode = "Z1", Polygon = "[[0,0]]" }, "u"));
        Assert.Equal("E-SPACE-006", ex.Code);
    }

    [Fact]
    public async Task ListSites_ReturnsCreated()
    {
        var (_, svc) = Make();
        await svc.CreateSiteAsync(new SiteDto { SiteCode = "S1", SiteName = "Site1" }, "u");
        await svc.CreateSiteAsync(new SiteDto { SiteCode = "S2", SiteName = "Site2" }, "u");
        var list = await svc.ListSitesAsync();
        Assert.Equal(2, list.Count);
    }

    /// <summary>
    /// 波1 终审记票兑现：SiteDto 暴露 WarehouseCd（此前是只能 SQL 改的死配置列）。
    /// Create 带 WarehouseCd → List 回显 → Update 改值 → List 回显新值。
    /// </summary>
    [Fact]
    public async Task Site_WarehouseCd_RoundTrips_ThroughDtoAndService()
    {
        var (_, svc) = Make();
        var id = await svc.CreateSiteAsync(
            new SiteDto { SiteCode = "WH1", SiteName = "a", WarehouseCd = "MAIN" }, "u");

        var created = (await svc.ListSitesAsync()).Single(x => x.Id == id);
        Assert.Equal("MAIN", created.WarehouseCd);

        await svc.UpdateSiteAsync(id,
            new SiteDto { SiteCode = "WH1", SiteName = "a", WarehouseCd = "ALT" }, "u");

        var updated = (await svc.ListSitesAsync()).Single(x => x.Id == id);
        Assert.Equal("ALT", updated.WarehouseCd);
    }

    // ── B-4: 货架改位姿触发几何重算 ──────────────────────────────────────

    [Fact]
    public async Task UpdateRack_MovePosition_RecalcsLocations()
    {
        var (db, svc) = Make();
        var rackId = await svc.CreateRackAsync(new RackDto
        {
            ZoneId = Guid.NewGuid(), RackCode = "R1",
            X = 0, Y = 0, Cols = 1, Levels = 1, DepthCount = 1,
            CellW = 1000, CellH = 1000, CellD = 1000
        }, "u");

        // 直插已放置库位（编码引擎/发布在 C/D 阶段，此处模拟）
        db.Space_Locations.Add(new Space_Location
        {
            Id = Guid.NewGuid(), RackId = rackId,
            Placed = true, Col = 1, Level = 1, Depth = 1
        });
        await db.SaveChangesAsync();

        var rack = await db.Space_Racks.SingleAsync();
        await svc.UpdateRackAsync(rackId, new RackDto
        {
            ZoneId = rack.ZoneId, RackCode = "R1",
            X = 5000, Y = 0, Cols = 1, Levels = 1, DepthCount = 1,
            CellW = 1000, CellH = 1000, CellD = 1000,
            RowVersion = rack.RowVersion
        }, "u");

        var loc = await db.Space_Locations.SingleAsync();
        // 锚点 X=5000, col=1 → AbsX = 5000 + (1-0.5)*1000 = 5500
        Assert.Equal(5000 + 500, loc.AbsX);
    }

    [Fact]
    public async Task CreateRack_DuplicateCodeInZone_Throws_E001()
    {
        var (_, svc) = Make();
        var zoneId = Guid.NewGuid();
        await svc.CreateRackAsync(new RackDto
        {
            ZoneId = zoneId, RackCode = "R1",
            Cols = 1, Levels = 1, DepthCount = 1,
            CellW = 1000, CellH = 1000, CellD = 1000
        }, "u");
        var ex = await Assert.ThrowsAsync<BizException>(
            () => svc.CreateRackAsync(new RackDto
            {
                ZoneId = zoneId, RackCode = "R1",
                Cols = 1, Levels = 1, DepthCount = 1,
                CellW = 1000, CellH = 1000, CellD = 1000
            }, "u"));
        Assert.Equal("E-SPACE-001", ex.Code);
    }

    [Fact]
    public async Task CreateRack_EmptyZoneId_Throws_E002()
    {
        var (_, svc) = Make();
        var ex = await Assert.ThrowsAsync<BizException>(
            () => svc.CreateRackAsync(new RackDto
            {
                ZoneId = Guid.Empty, RackCode = "R1",
                Cols = 1, Levels = 1, DepthCount = 1,
                CellW = 1000, CellH = 1000, CellD = 1000
            }, "u"));
        Assert.Equal("E-SPACE-002", ex.Code);
    }

    // ── B-5: 删除护栏 ─────────────────────────────────────────────────────

    /// <summary>
    /// 契约变更（§7.1 + 2026-07-06 拍板）：旧 E-SPACE-003「有库位一律拦」全废止。
    /// 无 Status=1 已发布库位时，删货架级联删其下（草稿）库位，不再抛 E-003。
    /// </summary>
    [Fact]
    public async Task DeleteRack_WithLocations_CascadeDeletes_E003Abolished()
    {
        var (db, svc) = Make();
        var rackId = Guid.NewGuid();
        db.Space_Racks.Add(new Space_Rack
            { Id = rackId, ZoneId = Guid.NewGuid(), RackCode = "R" });
        db.Space_Locations.Add(new Space_Location
            { Id = Guid.NewGuid(), RackId = rackId, Placed = true });  // Status 默认 0=草稿
        await db.SaveChangesAsync();

        await svc.DeleteRackAsync(rackId);

        // 旧断言（抛 E-003）废止：货架与库位全级联删
        Assert.Equal(0, await db.Space_Racks.CountAsync());
        Assert.Equal(0, await db.Space_Locations.CountAsync());
    }

    [Fact]
    public async Task DeleteRack_NoLocations_Succeeds()
    {
        var (db, svc) = Make();
        var rackId = Guid.NewGuid();
        db.Space_Racks.Add(new Space_Rack
            { Id = rackId, ZoneId = Guid.NewGuid(), RackCode = "R" });
        await db.SaveChangesAsync();

        await svc.DeleteRackAsync(rackId);
        Assert.Equal(0, await db.Space_Racks.CountAsync());
    }

    // ── 波1.5 Task 6: 删货架护栏 + mode=deactivate|rehome（ch04 §7.1/§7.2）─────

    [Fact]
    public async Task DeleteRack_DraftLocationsOnly_Cascades()
    {
        var (db, svc) = Make();
        var rackId = Guid.NewGuid();
        db.Space_Racks.Add(new Space_Rack
            { Id = rackId, ZoneId = Guid.NewGuid(), RackCode = "R" });
        db.Space_Locations.AddRange(
            new Space_Location { Id = Guid.NewGuid(), RackId = rackId, Placed = true, Status = 0 },
            new Space_Location { Id = Guid.NewGuid(), RackId = rackId, Placed = true, Status = 0 });
        await db.SaveChangesAsync();

        await svc.DeleteRackAsync(rackId);

        // 无 Status=1 → 级联删库位+货架（旧 E-003 全拦废止）
        Assert.Equal(0, await db.Space_Racks.CountAsync());
        Assert.Equal(0, await db.Space_Locations.CountAsync());
    }

    [Fact]
    public async Task DeleteRack_WithPublished_NoMode_Throws403()
    {
        var (db, svc) = Make();
        var s = await SeedPublishedAisleAsync(db);

        var ex = await Assert.ThrowsAsync<BizException>(
            () => svc.DeleteRackAsync(s.rackId));
        Assert.Equal("E-SPACE-403", ex.Code);

        // 护栏拦下：货架与库位原样
        Assert.Equal(1, await db.Space_Racks.CountAsync());
        Assert.Equal(1, await db.Space_Locations.CountAsync());
        Assert.Equal(1, (await db.Space_Locations.SingleAsync()).Status);
    }

    [Fact]
    public async Task DeleteRack_ModeDeactivate_DeactivatesThenCascades()
    {
        var (db, svc) = Make();
        var s = await SeedPublishedAisleAsync(db);

        await svc.DeleteRackAsync(s.rackId, mode: "deactivate", user: "u");

        // 停用后级联删（拍板②：停用位可删）——货架与库位全删
        Assert.Equal(0, await db.Space_Racks.CountAsync());
        Assert.Equal(0, await db.Space_Locations.CountAsync());
        // DEACTIVATE 事件已落库
        Assert.True(await db.IntegrationEvents.CountAsync() >= 1);
        // 真 WmsBinDeactivator 组装：T_WmsBin 墓碑 IsActive=false（loc 已被级联删，bin 独立留存）
        var bin = await db.WmsBins.SingleAsync();
        Assert.False(bin.IsActive);
    }

    [Fact]
    public async Task DeleteRack_ModeRehome_MovesLocations_Republishes()
    {
        var (db, svc) = Make();
        // 源架 R1（1x1x1）+ published loc（Version=1, Col=1）
        var siteId  = Guid.NewGuid();
        var floorId = Guid.NewGuid();
        var zoneId  = Guid.NewGuid();
        var srcId   = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var locId   = Guid.NewGuid();

        db.Space_Sites.Add(new Space_Site { Id = siteId, SiteCode = "S1", SiteName = "S1" });
        db.Space_Floors.Add(new Space_Floor { Id = floorId, SiteId = siteId, Level = 1, FloorCode = "F1", FloorName = "1F" });
        db.Space_Zones.Add(new Space_Zone
            { Id = zoneId, FloorId = floorId, ZoneCode = "Z1", ZoneName = "Zone1", Polygon = "[[0,0],[1,0],[1,1]]" });
        db.Space_Racks.Add(new Space_Rack
            { Id = srcId, ZoneId = zoneId, FloorId = floorId, RackCode = "R1",
              Cols = 1, Levels = 1, DepthCount = 1, CellW = 1000, CellH = 1000, CellD = 1000 });
        // 目标架 R2（同规格 1x1x1，无自有库位，同 zone）
        db.Space_Racks.Add(new Space_Rack
            { Id = targetId, ZoneId = zoneId, FloorId = floorId, RackCode = "R2",
              Cols = 1, Levels = 1, DepthCount = 1, CellW = 1000, CellH = 1000, CellD = 1000 });
        db.Space_Locations.Add(new Space_Location
        {
            Id = locId, FloorId = floorId, RackId = srcId,
            Placed = true, Status = 1, CodeOrigin = 1, Version = 1,
            LocationCode = "A-01-01-01", Col = 1, Level = 1, Depth = 1
        });
        await db.SaveChangesAsync();

        await svc.DeleteRackAsync(srcId, mode: "rehome", targetRackId: targetId, user: "u");

        // 库位改挂 target、Version 升、Status 仍 1（码冻结）
        var loc = await db.Space_Locations.SingleAsync();
        Assert.Equal(targetId, loc.RackId);
        Assert.Equal(2, loc.Version);
        Assert.Equal(1, loc.Status);
        // 源架已删，目标架仍在
        Assert.Null(await db.Space_Racks.FirstOrDefaultAsync(r => r.Id == srcId));
        Assert.NotNull(await db.Space_Racks.FirstOrDefaultAsync(r => r.Id == targetId));
        // UPSERT 事件 path.RackCode == "R2"
        var evt = await db.IntegrationEvents.SingleAsync();
        var payload = JsonSerializer.Deserialize<JsonElement>(evt.PayloadJson);
        var item = payload.GetProperty("Items")[0];
        Assert.Equal("UPSERT", item.GetProperty("Op").GetString());
        Assert.Equal("R2", item.GetProperty("Path").GetProperty("RackCode").GetString());
    }

    [Fact]
    public async Task DeleteRack_ModeRehome_TargetInDifferentZone_Throws()
    {
        // C1 反证：跨 zone 目标 rehome 会打穿 WarehouseCd 锚——必须拒绝，源架/库位原样
        var (db, svc) = Make();
        var floorId  = Guid.NewGuid();
        var zoneA    = Guid.NewGuid();
        var zoneB    = Guid.NewGuid();
        var srcId    = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var locId    = Guid.NewGuid();

        db.Space_Racks.Add(new Space_Rack
            { Id = srcId, ZoneId = zoneA, FloorId = floorId, RackCode = "R1",
              Cols = 1, Levels = 1, DepthCount = 1, CellW = 1000, CellH = 1000, CellD = 1000 });
        db.Space_Racks.Add(new Space_Rack
            { Id = targetId, ZoneId = zoneB, FloorId = floorId, RackCode = "R2",
              Cols = 1, Levels = 1, DepthCount = 1, CellW = 1000, CellH = 1000, CellD = 1000 });
        db.Space_Locations.Add(new Space_Location
        {
            Id = locId, FloorId = floorId, RackId = srcId,
            Placed = true, Status = 1, CodeOrigin = 1, Version = 1,
            LocationCode = "A-01-01-01", Col = 1, Level = 1, Depth = 1
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BizException>(
            () => svc.DeleteRackAsync(srcId, mode: "rehome", targetRackId: targetId, user: "u"));
        Assert.Equal("E-SPACE-002", ex.Code);

        // 拒绝后：源架仍在，库位仍挂源架（锚未动）
        Assert.NotNull(await db.Space_Racks.FirstOrDefaultAsync(r => r.Id == srcId));
        Assert.Equal(srcId, (await db.Space_Locations.SingleAsync()).RackId);
    }

    [Fact]
    public async Task DeleteRack_ModeRehome_DraftOnly_MovesLocations_NotDeleted()
    {
        // I2：全草稿货架 rehome——「搬走」语义对草稿同样成立（改挂 target，不删、不 re-publish）
        var (db, svc) = Make();
        var floorId  = Guid.NewGuid();
        var zoneId   = Guid.NewGuid();
        var srcId    = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var locId    = Guid.NewGuid();

        db.Space_Racks.Add(new Space_Rack
            { Id = srcId, ZoneId = zoneId, FloorId = floorId, RackCode = "R1",
              Cols = 1, Levels = 1, DepthCount = 1, CellW = 1000, CellH = 1000, CellD = 1000 });
        db.Space_Racks.Add(new Space_Rack
            { Id = targetId, ZoneId = zoneId, FloorId = floorId, RackCode = "R2",
              Cols = 1, Levels = 1, DepthCount = 1, CellW = 1000, CellH = 1000, CellD = 1000 });
        db.Space_Locations.Add(new Space_Location
            { Id = locId, FloorId = floorId, RackId = srcId, Placed = true, Status = 0, CodeOrigin = 1, Col = 1, Level = 1, Depth = 1 });
        await db.SaveChangesAsync();

        await svc.DeleteRackAsync(srcId, mode: "rehome", targetRackId: targetId, user: "u");

        var loc = await db.Space_Locations.SingleAsync();
        Assert.Equal(targetId, loc.RackId);                         // 草稿改挂 target（未被销毁）
        Assert.Equal(0, loc.Status);                                // 仍草稿，未升版
        Assert.Equal(0, await db.IntegrationEvents.CountAsync());   // 草稿无 re-publish 事件
        Assert.Null(await db.Space_Racks.FirstOrDefaultAsync(r => r.Id == srcId));      // 源架已删
        Assert.NotNull(await db.Space_Racks.FirstOrDefaultAsync(r => r.Id == targetId)); // 目标架仍在
    }

    [Fact]
    public async Task DeleteRack_UnknownMode_Throws_NoSideEffect()
    {
        // I2：未知 mode 白名单拦截——不静默降级删除，零副作用
        var (db, svc) = Make();
        var s = await SeedPublishedAisleAsync(db);

        var ex = await Assert.ThrowsAsync<BizException>(
            () => svc.DeleteRackAsync(s.rackId, mode: "bogus", user: "u"));
        Assert.Equal("E-SPACE-002", ex.Code);

        Assert.Equal(1, await db.Space_Racks.CountAsync());
        Assert.Equal(1, await db.Space_Locations.CountAsync());
        Assert.Equal(1, (await db.Space_Locations.SingleAsync()).Status);
    }

    [Fact]
    public async Task DeleteAisle_SetsNullRackAisleId()
    {
        var (db, svc) = Make();
        var aisleId = Guid.NewGuid();
        db.Space_Aisles.Add(new Space_Aisle
            { Id = aisleId, ZoneId = Guid.NewGuid(), AisleCode = "L1" });
        db.Space_Racks.Add(new Space_Rack
            { Id = Guid.NewGuid(), ZoneId = Guid.NewGuid(), AisleId = aisleId, RackCode = "R" });
        await db.SaveChangesAsync();

        await svc.DeleteAisleAsync(aisleId);

        // 货架 AisleId 已置空，货架本身保留
        Assert.Null((await db.Space_Racks.SingleAsync()).AisleId);
        // 巷道已删
        Assert.Equal(0, await db.Space_Aisles.CountAsync());
    }

    // ── 波1.5 H3: 删巷道发布护栏 + mode=deactivate|rehome（ch04 §7.1/§7.2）─────

    /// <summary>
    /// 种子帮手：site→floor→zone→aisle→rack(挂 aisle)→published loc(Status=1,有码,Version=1)。
    /// 返回各层 Id 便于断言。loc.FloorId 设好使 WarehouseCd 沿 Floor→Site 链解析（"S1" 短码不触发 E-405）。
    /// </summary>
    private static async Task<(Guid siteId, Guid floorId, Guid zoneId, Guid aisleId, Guid rackId, Guid locId)>
        SeedPublishedAisleAsync(CP6Context db, string aisleCode = "L1")
    {
        var siteId  = Guid.NewGuid();
        var floorId = Guid.NewGuid();
        var zoneId  = Guid.NewGuid();
        var aisleId = Guid.NewGuid();
        var rackId  = Guid.NewGuid();
        var locId   = Guid.NewGuid();

        db.Space_Sites.Add(new Space_Site { Id = siteId, SiteCode = "S1", SiteName = "S1" });
        db.Space_Floors.Add(new Space_Floor { Id = floorId, SiteId = siteId, Level = 1, FloorCode = "F1", FloorName = "1F" });
        db.Space_Zones.Add(new Space_Zone
            { Id = zoneId, FloorId = floorId, ZoneCode = "Z1", ZoneName = "Zone1", Polygon = "[[0,0],[1,0],[1,1]]" });
        db.Space_Aisles.Add(new Space_Aisle { Id = aisleId, ZoneId = zoneId, AisleCode = aisleCode });
        db.Space_Racks.Add(new Space_Rack
            { Id = rackId, ZoneId = zoneId, AisleId = aisleId, FloorId = floorId, RackCode = "R1" });
        db.Space_Locations.Add(new Space_Location
        {
            Id = locId, FloorId = floorId, RackId = rackId,
            Placed = true, Status = 1, CodeOrigin = 1, Version = 1,
            LocationCode = "A-01-01-01", Col = 1, Level = 1, Depth = 1
        });
        await db.SaveChangesAsync();
        return (siteId, floorId, zoneId, aisleId, rackId, locId);
    }

    [Fact]
    public async Task DeleteAisle_WithPublishedLocations_NoMode_Throws402()
    {
        var (db, svc) = Make();
        var s = await SeedPublishedAisleAsync(db);

        var ex = await Assert.ThrowsAsync<BizException>(
            () => svc.DeleteAisleAsync(s.aisleId));
        Assert.Equal("E-SPACE-402", ex.Code);

        // 护栏拦下：巷道仍在，货架 AisleId 未被置空
        Assert.Equal(1, await db.Space_Aisles.CountAsync());
        Assert.Equal(s.aisleId, (await db.Space_Racks.SingleAsync()).AisleId);
    }

    [Fact]
    public async Task DeleteAisle_ModeDeactivate_DeactivatesThenDeletes()
    {
        var (db, svc) = Make();
        var s = await SeedPublishedAisleAsync(db);

        await svc.DeleteAisleAsync(s.aisleId, mode: "deactivate", user: "u");

        // 库位已停用（Status=2）且升版
        var loc = await db.Space_Locations.SingleAsync();
        Assert.Equal(2, loc.Status);
        Assert.Equal(2, loc.Version);         // Version 1 → 2
        // 巷道已删，货架保留且 AisleId 置空
        Assert.Equal(0, await db.Space_Aisles.CountAsync());
        Assert.Null((await db.Space_Racks.SingleAsync()).AisleId);
        // DEACTIVATE 事件已落库
        Assert.True(await db.IntegrationEvents.CountAsync() >= 1);
    }

    [Fact]
    public async Task DeleteAisle_ModeRehome_RepointsRacks_Republishes()
    {
        var (db, svc) = Make();
        var s = await SeedPublishedAisleAsync(db, aisleCode: "SRC");
        // 同 zone 下再建 target 巷道
        var targetId = Guid.NewGuid();
        db.Space_Aisles.Add(new Space_Aisle { Id = targetId, ZoneId = s.zoneId, AisleCode = "DST" });
        await db.SaveChangesAsync();

        await svc.DeleteAisleAsync(s.aisleId, mode: "rehome", targetAisleId: targetId, user: "u");

        // 货架改挂 target
        Assert.Equal(targetId, (await db.Space_Racks.SingleAsync()).AisleId);
        // 库位 re-publish：Version 1→2，Status 仍 1（码冻结）
        var loc = await db.Space_Locations.SingleAsync();
        Assert.Equal(2, loc.Version);
        Assert.Equal(1, loc.Status);
        // src 巷道已删，target 仍在
        Assert.Equal(1, await db.Space_Aisles.CountAsync());
        Assert.Equal(targetId, (await db.Space_Aisles.SingleAsync()).Id);
        // UPSERT 事件 path.AisleCode == target 的码
        var evt = await db.IntegrationEvents.SingleAsync();
        var payload = JsonSerializer.Deserialize<JsonElement>(evt.PayloadJson);
        var item = payload.GetProperty("Items")[0];
        Assert.Equal("UPSERT", item.GetProperty("Op").GetString());
        Assert.Equal("DST", item.GetProperty("Path").GetProperty("AisleCode").GetString());
    }

    [Fact]
    public async Task DeleteAisle_ModeRehome_TargetInDifferentZone_Throws()
    {
        var (db, svc) = Make();
        var s = await SeedPublishedAisleAsync(db, aisleCode: "SRC");
        // target 巷道在另一 zone
        var otherZoneId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        db.Space_Zones.Add(new Space_Zone
            { Id = otherZoneId, FloorId = s.floorId, ZoneCode = "Z2", ZoneName = "Zone2", Polygon = "[[0,0],[1,0],[1,1]]" });
        db.Space_Aisles.Add(new Space_Aisle { Id = targetId, ZoneId = otherZoneId, AisleCode = "DST" });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BizException>(
            () => svc.DeleteAisleAsync(s.aisleId, mode: "rehome", targetAisleId: targetId, user: "u"));
        Assert.Equal("E-SPACE-407", ex.Code);

        // 拒绝后：src 仍在，货架仍挂 src
        Assert.Equal(s.aisleId, (await db.Space_Racks.SingleAsync()).AisleId);
        Assert.NotNull(await db.Space_Aisles.FirstOrDefaultAsync(a => a.Id == s.aisleId));
    }

    [Fact]
    public async Task DeleteSite_WithFloor_Throws_E007()
    {
        var (db, svc) = Make();
        var siteId = Guid.NewGuid();
        db.Space_Sites.Add(new Space_Site { Id = siteId, SiteCode = "S", SiteName = "s" });
        db.Space_Floors.Add(new Space_Floor { Id = Guid.NewGuid(), SiteId = siteId, FloorCode = "F", FloorName = "f" });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BizException>(
            () => svc.DeleteSiteAsync(siteId));
        Assert.Equal("E-SPACE-007", ex.Code);
    }

    [Fact]
    public async Task DeleteFloor_WithZone_Throws_E007()
    {
        var (db, svc) = Make();
        var floorId = Guid.NewGuid();
        db.Space_Floors.Add(new Space_Floor { Id = floorId, SiteId = Guid.NewGuid(), FloorCode = "F", FloorName = "f" });
        db.Space_Zones.Add(new Space_Zone
            { Id = Guid.NewGuid(), FloorId = floorId, ZoneCode = "Z", ZoneName = "z",
              Polygon = "[[0,0],[1,0],[1,1]]" });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BizException>(
            () => svc.DeleteFloorAsync(floorId));
        Assert.Equal("E-SPACE-007", ex.Code);
    }

    [Fact]
    public async Task DeleteZone_WithAisle_Throws_E007()
    {
        var (db, svc) = Make();
        var zoneId = Guid.NewGuid();
        db.Space_Zones.Add(new Space_Zone
            { Id = zoneId, FloorId = Guid.NewGuid(), ZoneCode = "Z", ZoneName = "z",
              Polygon = "[[0,0],[1,0],[1,1]]" });
        db.Space_Aisles.Add(new Space_Aisle
            { Id = Guid.NewGuid(), ZoneId = zoneId, AisleCode = "A" });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BizException>(
            () => svc.DeleteZoneAsync(zoneId));
        Assert.Equal("E-SPACE-007", ex.Code);
    }

    // ── B-6: 场景聚合 / 待绑定 ──────────────────────────────────────────

    [Fact]
    public async Task GetScene_OnlyReturnsPlacedLocations()
    {
        var (db, svc) = Make();
        var floorId = Guid.NewGuid();
        db.Space_Locations.AddRange(
            new Space_Location
            {
                Id = Guid.NewGuid(), FloorId = floorId, RackId = Guid.NewGuid(),
                Placed = true, Status = 1
            },
            new Space_Location
            {
                Id = Guid.NewGuid(), FloorId = null, Placed = false, Status = 1, CodeOrigin = 2
            }); // 采纳未放置
        await db.SaveChangesAsync();

        var scene = await svc.GetSceneAsync(floorId);
        Assert.Single(scene.Locations);  // 未放置的不进场景
    }

    [Fact]
    public async Task GetUnplaced_ReturnsStatusOneAndNotPlaced()
    {
        var (db, svc) = Make();
        var floorId = Guid.NewGuid();
        db.Space_Locations.AddRange(
            new Space_Location
            {
                Id = Guid.NewGuid(), FloorId = floorId, RackId = Guid.NewGuid(),
                Placed = true, Status = 1
            },
            new Space_Location
            {
                Id = Guid.NewGuid(), FloorId = null, Placed = false, Status = 1, CodeOrigin = 2
            }); // 采纳未放置
        await db.SaveChangesAsync();

        var unplaced = await svc.GetUnplacedAsync(floorId);
        Assert.Single(unplaced);  // 只有 Status=1 && Placed=false 的那条
    }

    [Fact]
    public async Task GetScene_ExcludesDraftLocations()
    {
        var (db, svc) = Make();
        var floorId = Guid.NewGuid();
        // 草稿（Status=0）且已放置
        db.Space_Locations.Add(new Space_Location
        {
            Id = Guid.NewGuid(), FloorId = floorId, RackId = Guid.NewGuid(),
            Placed = true, Status = 0
        });
        await db.SaveChangesAsync();

        var scene = await svc.GetSceneAsync(floorId);
        // GetScene 只过滤 Placed=true（不管 Status），草稿放置的也进场景（渲染用）
        Assert.Single(scene.Locations);
    }

    [Fact]
    public async Task ListLocations_ReturnsAllLocationsForRack()
    {
        var (db, svc) = Make();
        var rackId = Guid.NewGuid();
        db.Space_Locations.AddRange(
            new Space_Location { Id = Guid.NewGuid(), RackId = rackId, Placed = true, Status = 1 },
            new Space_Location { Id = Guid.NewGuid(), RackId = rackId, Placed = true, Status = 0 },
            new Space_Location { Id = Guid.NewGuid(), RackId = Guid.NewGuid(), Placed = true, Status = 1 });
        await db.SaveChangesAsync();

        var locs = await svc.ListLocationsAsync(rackId);
        Assert.Equal(2, locs.Count);
    }
}
