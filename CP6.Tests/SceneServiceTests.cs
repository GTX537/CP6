using CP6.Core.EFDbContext;
using CP6.Core.Services.Space;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests;

/// <summary>
/// G-1 场景差量保存测试：新增货架/库位落库且 AbsX 重算；位姿变更触发重算；删有库位货架→E-003。
/// </summary>
public class SceneServiceTests
{
    private static (CP6Context db, SceneService svc) Make()
    {
        var db = new CP6Context(new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var geo = new LocationGeometryService(db);
        return (db, new SceneService(db, geo));
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
    public async Task SaveScene_DeleteRackWithLocations_ThrowsE003()
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
        db.Space_Locations.Add(new Space_Location
        {
            Id = Guid.NewGuid(), RackId = rackId, Placed = true, Status = 0
        });
        await db.SaveChangesAsync();

        var dto = new SceneSaveDto
        {
            Deletes = new Deletes { Racks = new List<Guid> { rackId } }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SaveSceneAsync(floorId, dto, "u"));
        Assert.Equal("E-SPACE-003", ex.Message);
    }
}
