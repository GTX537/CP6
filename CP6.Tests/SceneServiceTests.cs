using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Space;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CP6.Tests;

/// <summary>
/// G-1 场景差量保存测试：新增货架/库位落库且 AbsX 重算；位姿变更触发重算；删货架按契约§7.1（有已发布→E-403，其余级联删）。
/// </summary>
public class SceneServiceTests
{
    private static (CP6Context db, SceneService svc) Make()
    {
        var db = new CP6Context(new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var geo = new LocationGeometryService(db);
        var publish = new LocationPublishService(db, new TenantContext(), new CodeEngineService(db),
            new SpaceBridgeHook(db, NullLogger<SpaceBridgeHook>.Instance, new NoOpWmsLocationConsumer()),
            new StubWmsStockQuery(), new CP6.Core.Services.Wms.WmsBinDeactivator(db));
        return (db, new SceneService(db, geo, publish));
    }

    [Fact]
    public async Task SaveScene_NewRack_PersistedAndAbsXRecalculated()
    {
        var (db, svc) = Make();
        var floorId = Guid.NewGuid();
        var zoneId  = Guid.NewGuid();
        var rackId  = Guid.NewGuid();
        var locId   = Guid.NewGuid();

        db.Space_Zones.Add(new Space_Zone
        {
            Id = zoneId, FloorId = floorId,
            ZoneCode = "Z1", ZoneName = "Zone1",
            Polygon = "[[0,0],[10000,0],[10000,10000],[0,10000]]"
        });
        await db.SaveChangesAsync();

        var dto = new SceneSaveDto
        {
            Racks = new List<RackDto>
            {
                new RackDto
                {
                    Id = rackId, ZoneId = zoneId,
                    RackCode = "R1", X = 1000, Y = 2000, Z = 0, RotationZ = 0,
                    Cols = 2, Levels = 3, DepthCount = 1,
                    CellW = 1200, CellH = 1500, CellD = 1000
                }
            },
            Locations = new List<SceneLocationSaveDto>
            {
                new SceneLocationSaveDto
                {
                    Id = locId, RackId = rackId,
                    Col = 1, Level = 1, Depth = 1,
                    Placed = true, Status = 0, CodeOrigin = 1
                }
            }
        };

        await svc.SaveSceneAsync(floorId, dto, "u");

        var rack = await db.Space_Racks.FirstOrDefaultAsync(r => r.Id == rackId);
        Assert.NotNull(rack);
        Assert.Equal(1000, rack!.X);

        var loc = await db.Space_Locations.FirstOrDefaultAsync(l => l.Id == locId);
        Assert.NotNull(loc);
        Assert.True(loc!.Placed);
        // AbsX = rack.X + (col - 0.5) * CellW = 1000 + 0.5*1200 = 1600
        Assert.Equal(1600, loc.AbsX);
    }

    [Fact]
    public async Task SaveScene_ChangedRackPosition_RecalculatesAbsX()
    {
        var (db, svc) = Make();
        var floorId = Guid.NewGuid();
        var zoneId  = Guid.NewGuid();
        var rackId  = Guid.NewGuid();

        db.Space_Zones.Add(new Space_Zone
        {
            Id = zoneId, FloorId = floorId, ZoneCode = "Z1", ZoneName = "Z1",
            Polygon = "[[0,0],[10000,0],[10000,10000],[0,10000]]"
        });
        db.Space_Racks.Add(new Space_Rack
        {
            Id = rackId, ZoneId = zoneId, FloorId = floorId,
            RackCode = "R1", X = 0, Y = 0, Z = 0, RotationZ = 0,
            Cols = 1, Levels = 1, DepthCount = 1,
            CellW = 1000, CellH = 1000, CellD = 1000
        });
        await db.SaveChangesAsync();

        var locId = Guid.NewGuid();
        db.Space_Locations.Add(new Space_Location
        {
            Id = locId, RackId = rackId, FloorId = floorId,
            Col = 1, Level = 1, Depth = 1, Placed = true, Status = 0, CodeOrigin = 1
        });
        await db.SaveChangesAsync();

        var rack = await db.Space_Racks.FirstAsync();
        var dto = new SceneSaveDto
        {
            Racks = new List<RackDto>
            {
                new RackDto
                {
                    Id = rackId, ZoneId = zoneId,
                    RackCode = "R1", X = 5000, Y = 0, Z = 0, RotationZ = 0,
                    Cols = 1, Levels = 1, DepthCount = 1,
                    CellW = 1000, CellH = 1000, CellD = 1000,
                    RowVersion = rack.RowVersion
                }
            }
        };

        await svc.SaveSceneAsync(floorId, dto, "u");

        var loc = await db.Space_Locations.FirstOrDefaultAsync(l => l.Id == locId);
        Assert.NotNull(loc);
        // AbsX = 5000 + (1 - 0.5)*1000 = 5500
        Assert.Equal(5500, loc!.AbsX);
    }

    [Fact]
    public async Task SaveScene_DeleteRackWithDraftLocations_Cascades()
    {
        // 语义变更（契约 §7.1 + 2026-07-06 拍板）：旧 E-003「有任何库位全拦」废止——
        // 草稿种子 → 库位连带删+删货架成功（原断言 E-SPACE-003 已按种子实际状态改写）。
        var (db, svc) = Make();
        var floorId = Guid.NewGuid();
        var zoneId  = Guid.NewGuid();
        var rackId  = Guid.NewGuid();

        db.Space_Zones.Add(new Space_Zone
        {
            Id = zoneId, FloorId = floorId, ZoneCode = "Z1", ZoneName = "Z1",
            Polygon = "[[0,0],[10000,0],[10000,10000],[0,10000]]"
        });
        db.Space_Racks.Add(new Space_Rack
        {
            Id = rackId, ZoneId = zoneId, FloorId = floorId,
            RackCode = "R1", X = 0, Y = 0, Z = 0, RotationZ = 0,
            Cols = 1, Levels = 1, DepthCount = 1,
            CellW = 1000, CellH = 1000, CellD = 1000
        });
        db.Space_Locations.Add(new Space_Location
        {
            Id = Guid.NewGuid(), RackId = rackId, Placed = true, Status = 0
        });
        await db.SaveChangesAsync();

        var dto = new SceneSaveDto
        {
            Deletes = new Deletes { Racks = new List<Guid> { rackId } }
        };

        await svc.SaveSceneAsync(floorId, dto, "u");

        Assert.Equal(0, await db.Space_Racks.CountAsync());
        Assert.Equal(0, await db.Space_Locations.CountAsync());
    }

    [Fact]
    public async Task SaveScene_CannotFlipPublishedStatus_OrCodeOrigin()
    {
        // H1：场景保存曾可任意覆盖 Status/CodeOrigin——绕过 publish/deactivate 状态机的后门
        var (db, svc) = Make();
        var floorId = Guid.NewGuid();
        var locId = Guid.NewGuid();
        db.Space_Locations.Add(new Space_Location
        {
            Id = locId, FloorId = floorId, RackId = null,
            Placed = false, Status = 1, CodeOrigin = 2, LocationCode = "EXT-001", Version = 3
        });
        await db.SaveChangesAsync();

        await svc.SaveSceneAsync(floorId, new SceneSaveDto
        {
            Locations = new List<SceneLocationSaveDto>
            {
                new SceneLocationSaveDto { Id = locId, RackId = Guid.Empty, Col = 1, Level = 1, Depth = 1, Placed = false, Status = 0, CodeOrigin = 1 }
            }
        }, "u");

        var loc = await db.Space_Locations.SingleAsync();
        Assert.Equal(1, loc.Status);       // 发布状态不被场景保存改写
        Assert.Equal(2, loc.CodeOrigin);   // 来源标签（对账依据）同理
    }

    [Fact]
    public async Task SaveScene_NewLocation_ForcedDraft()
    {
        var (db, svc) = Make();
        var floorId = Guid.NewGuid();

        await svc.SaveSceneAsync(floorId, new SceneSaveDto
        {
            Locations = new List<SceneLocationSaveDto>
            {
                new SceneLocationSaveDto { Id = Guid.NewGuid(), RackId = Guid.Empty, Col = 1, Level = 1, Depth = 1, Placed = false, Status = 1, CodeOrigin = 2 }
            }
        }, "u");

        var loc = await db.Space_Locations.SingleAsync();
        Assert.Equal(0, loc.Status);       // 编辑器新建恒草稿；发布走 publish、采纳走 adopt
        Assert.Equal(1, loc.CodeOrigin);
    }

    [Fact]
    public async Task SaveScene_RackZoneChanged_RepublishesPublishedLocations()
    {
        // H4（ch04 §7.2 路径B）：层级归属变更 → 已发布库位自动 re-publish 刷新 WMS path
        var (db, svc) = Make();
        var floorId = Guid.NewGuid();
        var site = new Space_Site { Id = Guid.NewGuid(), SiteCode = "WH1", SiteName = "S1" };
        var floor = new Space_Floor { Id = floorId, SiteId = site.Id, Level = 1, FloorCode = "F1", FloorName = "F1" };
        var zoneA = new Space_Zone { Id = Guid.NewGuid(), FloorId = floorId, ZoneCode = "ZA", ZoneName = "A" };
        var zoneB = new Space_Zone { Id = Guid.NewGuid(), FloorId = floorId, ZoneCode = "ZB", ZoneName = "B" };
        var rack = new Space_Rack { Id = Guid.NewGuid(), ZoneId = zoneA.Id, FloorId = floorId, RackCode = "R1", Cols = 1, Levels = 1, DepthCount = 1, CellW = 1000, CellH = 1000, CellD = 1000 };
        var pubLoc = new Space_Location { Id = Guid.NewGuid(), FloorId = floorId, RackId = rack.Id, Placed = true, Status = 1, CodeOrigin = 1, LocationCode = "ZA-01", Col = 1, Level = 1, Depth = 1, Version = 1 };
        var draftLoc = new Space_Location { Id = Guid.NewGuid(), FloorId = floorId, RackId = rack.Id, Placed = true, Status = 0, CodeOrigin = 1, LocationCode = null, Col = 1, Level = 1, Depth = 1 };
        db.AddRange(site, floor, zoneA, zoneB, rack, pubLoc, draftLoc);
        await db.SaveChangesAsync();
        var rowVersion = rack.RowVersion;

        await svc.SaveSceneAsync(floorId, new SceneSaveDto
        {
            Racks = new List<RackDto>
            {
                new RackDto
                {
                    Id = rack.Id, ZoneId = zoneB.Id, AisleId = null, RackCode = "R1",
                    X = 0, Y = 0, Z = 0, RotationZ = 0,
                    Cols = 1, Levels = 1, DepthCount = 1, CellW = 1000, CellH = 1000, CellD = 1000,
                    Enable = true, RowVersion = rowVersion
                }
            }
        }, "u");

        var loc = await db.Space_Locations.SingleAsync(l => l.Id == pubLoc.Id);
        Assert.Equal(2, loc.Version);                       // re-publish 升版
        Assert.Equal(1, loc.Status);                        // 状态不变
        var evt = await db.IntegrationEvents.SingleAsync();
        var payload = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(evt.PayloadJson);
        var item = payload.GetProperty("Items")[0];
        Assert.Equal("UPSERT", item.GetProperty("Op").GetString());
        Assert.Equal("ZB", item.GetProperty("Path").GetProperty("ZoneCode").GetString());   // 新归属
        // 草稿库位不受波及
        Assert.Equal(0, (await db.Space_Locations.SingleAsync(l => l.Id == draftLoc.Id)).Version);
    }

    [Fact]
    public async Task SaveScene_GeometryOnlyChange_NoRepublish()
    {
        // D4：纯几何（挪位/旋转/改尺寸不缩格）不发布——join key 不漂移
        var (db, svc) = Make();
        var floorId = Guid.NewGuid();
        var zone = new Space_Zone { Id = Guid.NewGuid(), FloorId = floorId, ZoneCode = "Z1", ZoneName = "Z" };
        var rack = new Space_Rack { Id = Guid.NewGuid(), ZoneId = zone.Id, FloorId = floorId, RackCode = "R1", Cols = 1, Levels = 1, DepthCount = 1, CellW = 1000, CellH = 1000, CellD = 1000 };
        db.AddRange(zone, rack);
        db.Space_Locations.Add(new Space_Location { Id = Guid.NewGuid(), FloorId = floorId, RackId = rack.Id, Placed = true, Status = 1, CodeOrigin = 1, LocationCode = "Z1-01", Col = 1, Level = 1, Depth = 1, Version = 1 });
        await db.SaveChangesAsync();

        await svc.SaveSceneAsync(floorId, new SceneSaveDto
        {
            Racks = new List<RackDto>
            {
                new RackDto
                {
                    Id = rack.Id, ZoneId = zone.Id, AisleId = null, RackCode = "R1",
                    X = 5000, Y = 3000, Z = 0, RotationZ = 90,   // 只挪位旋转
                    Cols = 1, Levels = 1, DepthCount = 1, CellW = 1000, CellH = 1000, CellD = 1000,
                    Enable = true, RowVersion = rack.RowVersion
                }
            }
        }, "u");

        Assert.Equal(1, (await db.Space_Locations.SingleAsync()).Version);   // 版本不动
        Assert.Equal(0, await db.IntegrationEvents.CountAsync());            // 零事件
    }

    [Fact]
    public async Task SaveScene_ShrinkRack_PublishedOutOfBounds_Throws403_NothingSaved()
    {
        // H2（2026-07-06 拍板：Restrict 阻断，非契约§4字面的自动停用）
        var (db, svc) = Make();
        var floorId = Guid.NewGuid();
        var zone = new Space_Zone { Id = Guid.NewGuid(), FloorId = floorId, ZoneCode = "Z1", ZoneName = "Z" };
        var rack = new Space_Rack { Id = Guid.NewGuid(), ZoneId = zone.Id, FloorId = floorId, RackCode = "R1", Cols = 3, Levels = 1, DepthCount = 1, CellW = 1000, CellH = 1000, CellD = 1000 };
        db.AddRange(zone, rack);
        db.Space_Locations.Add(new Space_Location { Id = Guid.NewGuid(), FloorId = floorId, RackId = rack.Id, Placed = true, Status = 1, CodeOrigin = 1, LocationCode = "Z1-03", Col = 3, Level = 1, Depth = 1, Version = 1 });
        await db.SaveChangesAsync();

        var dto = new SceneSaveDto
        {
            Racks = new List<RackDto>
            {
                new RackDto
                {
                    Id = rack.Id, ZoneId = zone.Id, AisleId = null, RackCode = "R1",
                    X = 0, Y = 0, Z = 0, RotationZ = 0,
                    Cols = 2, Levels = 1, DepthCount = 1, CellW = 1000, CellH = 1000, CellD = 1000,   // 3→2 缩格
                    Enable = true, RowVersion = rack.RowVersion
                }
            }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SaveSceneAsync(floorId, dto, "u"));
        Assert.StartsWith("E-SPACE-403", ex.Message);
        Assert.Equal(3, (await db.Space_Racks.AsNoTracking().SingleAsync()).Cols);   // 缩格未生效
        Assert.Equal(1, await db.Space_Locations.CountAsync());                      // 库位仍在
    }

    [Fact]
    public async Task SaveScene_ShrinkRack_DraftOutOfBounds_CascadeDeleted()
    {
        var (db, svc) = Make();
        var floorId = Guid.NewGuid();
        var zone = new Space_Zone { Id = Guid.NewGuid(), FloorId = floorId, ZoneCode = "Z1", ZoneName = "Z" };
        var rack = new Space_Rack { Id = Guid.NewGuid(), ZoneId = zone.Id, FloorId = floorId, RackCode = "R1", Cols = 3, Levels = 1, DepthCount = 1, CellW = 1000, CellH = 1000, CellD = 1000 };
        db.AddRange(zone, rack);
        var inBounds = new Space_Location { Id = Guid.NewGuid(), FloorId = floorId, RackId = rack.Id, Placed = true, Status = 0, CodeOrigin = 1, Col = 1, Level = 1, Depth = 1 };
        var ghost = new Space_Location { Id = Guid.NewGuid(), FloorId = floorId, RackId = rack.Id, Placed = true, Status = 0, CodeOrigin = 1, Col = 3, Level = 1, Depth = 1 };
        db.AddRange(inBounds, ghost);
        await db.SaveChangesAsync();

        await svc.SaveSceneAsync(floorId, new SceneSaveDto
        {
            Racks = new List<RackDto>
            {
                new RackDto
                {
                    Id = rack.Id, ZoneId = zone.Id, AisleId = null, RackCode = "R1",
                    X = 0, Y = 0, Z = 0, RotationZ = 0,
                    Cols = 2, Levels = 1, DepthCount = 1, CellW = 1000, CellH = 1000, CellD = 1000,
                    Enable = true, RowVersion = rack.RowVersion
                }
            }
        }, "u");

        Assert.Equal(2, (await db.Space_Racks.SingleAsync()).Cols);
        var remaining = await db.Space_Locations.ToListAsync();
        Assert.Single(remaining);                                   // 幽灵位已连带删
        Assert.Equal(inBounds.Id, remaining[0].Id);
    }

    [Fact]
    public async Task SaveScene_DeleteLocation_DraftAndDeactivated_Allowed_PublishedRejected()
    {
        // 库位删除通道（2026-07-06 拍板：0/2 可删，1 拒绝。停用位删除后码仍占 T_WmsBin 锚——已知代价）
        var (db, svc) = Make();
        var floorId = Guid.NewGuid();
        var draft = new Space_Location { Id = Guid.NewGuid(), FloorId = floorId, Status = 0 };
        var deact = new Space_Location { Id = Guid.NewGuid(), FloorId = floorId, Status = 2, LocationCode = "X-01", Version = 2 };
        var pub = new Space_Location { Id = Guid.NewGuid(), FloorId = floorId, Status = 1, LocationCode = "P-01", Version = 1 };
        db.AddRange(draft, deact, pub);
        await db.SaveChangesAsync();

        // 草稿+停用：删除成功
        await svc.SaveSceneAsync(floorId, new SceneSaveDto
        {
            Deletes = new Deletes { Locations = new List<Guid> { draft.Id, deact.Id } }
        }, "u");
        Assert.Equal(1, await db.Space_Locations.CountAsync());

        // 已发布：拒绝
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SaveSceneAsync(floorId, new SceneSaveDto
        {
            Deletes = new Deletes { Locations = new List<Guid> { pub.Id } }
        }, "u"));
        Assert.StartsWith("E-SPACE-408", ex.Message);
        Assert.Equal(1, await db.Space_Locations.CountAsync());
    }

    [Fact]
    public async Task SaveScene_DeleteRack_DraftLocationsOnly_Cascades()
    {
        // ch04 §7.1：全草稿 → 连带删（替代旧 E-003 全拦）
        var (db, svc) = Make();
        var floorId = Guid.NewGuid();
        var zone = new Space_Zone { Id = Guid.NewGuid(), FloorId = floorId, ZoneCode = "Z1", ZoneName = "Z" };
        var rack = new Space_Rack { Id = Guid.NewGuid(), ZoneId = zone.Id, FloorId = floorId, RackCode = "R1", Cols = 1, Levels = 1, DepthCount = 1, CellW = 1000, CellH = 1000, CellD = 1000 };
        db.AddRange(zone, rack);
        db.Space_Locations.Add(new Space_Location { Id = Guid.NewGuid(), FloorId = floorId, RackId = rack.Id, Status = 0, Col = 1, Level = 1, Depth = 1 });
        await db.SaveChangesAsync();

        await svc.SaveSceneAsync(floorId, new SceneSaveDto
        {
            Deletes = new Deletes { Racks = new List<Guid> { rack.Id } }
        }, "u");

        Assert.Equal(0, await db.Space_Racks.CountAsync());
        Assert.Equal(0, await db.Space_Locations.CountAsync());
    }

    [Fact]
    public async Task SaveScene_DeleteRack_WithPublishedLocation_Throws403()
    {
        var (db, svc) = Make();
        var floorId = Guid.NewGuid();
        var zone = new Space_Zone { Id = Guid.NewGuid(), FloorId = floorId, ZoneCode = "Z1", ZoneName = "Z" };
        var rack = new Space_Rack { Id = Guid.NewGuid(), ZoneId = zone.Id, FloorId = floorId, RackCode = "R1", Cols = 1, Levels = 1, DepthCount = 1, CellW = 1000, CellH = 1000, CellD = 1000 };
        db.AddRange(zone, rack);
        db.Space_Locations.Add(new Space_Location { Id = Guid.NewGuid(), FloorId = floorId, RackId = rack.Id, Status = 1, LocationCode = "Z1-01", Col = 1, Level = 1, Depth = 1, Version = 1 });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SaveSceneAsync(floorId, new SceneSaveDto
        {
            Deletes = new Deletes { Racks = new List<Guid> { rack.Id } }
        }, "u"));
        Assert.StartsWith("E-SPACE-403", ex.Message);
        Assert.Equal(1, await db.Space_Racks.CountAsync());
    }
}
