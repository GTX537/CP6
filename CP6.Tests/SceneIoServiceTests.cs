using CP6.Core.EFDbContext;
using CP6.Core.Services.Space;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests;

/// <summary>
/// G-3 导入导出测试：导入后 rack/location 全新 Id、码空 status0、AbsX 按参数重建。
/// </summary>
public class SceneIoServiceTests
{
    private static (CP6Context db, SceneIoService svc) Make()
    {
        var db = new CP6Context(new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return (db, new SceneIoService(db));
    }

    [Fact]
    public async Task Import_RacksHaveNewIds_LocationsCreated_AbsXRecalculated()
    {
        var (db, svc) = Make();

        var siteId = Guid.NewGuid();
        db.Space_Sites.Add(new Space_Site { Id = siteId, SiteCode = "S1", SiteName = "Site1" });
        await db.SaveChangesAsync();

        var oldZoneId = Guid.NewGuid();
        var oldRackId = Guid.NewGuid();

        var dto = new SceneExportDto
        {
            Meta = new SceneExportMeta
            {
                FloorCode = "F1", FloorName = "Floor1", Level = 1, Height = 6000
            },
            Zones = new List<ZoneExportDto>
            {
                new ZoneExportDto
                {
                    Id = oldZoneId, ZoneCode = "Z1", ZoneName = "Zone1", ZoneType = 1,
                    Polygon = "[[0,0],[10000,0],[10000,10000],[0,10000]]"
                }
            },
            Aisles = new List<AisleExportDto>(),
            Racks = new List<RackExportDto>
            {
                new RackExportDto
                {
                    Id = oldRackId, ZoneId = oldZoneId,
                    RackCode = "R1", X = 1000, Y = 2000, Z = 0, RotationZ = 0,
                    Cols = 2, Levels = 2, DepthCount = 1,
                    CellW = 1200, CellH = 1500, CellD = 1000
                }
            }
        };

        var newFloorId = await svc.ImportAsync(siteId, dto, "u");

        Assert.NotEqual(Guid.Empty, newFloorId);

        var racks = await db.Space_Racks.ToListAsync();
        Assert.Single(racks);
        Assert.NotEqual(oldRackId, racks[0].Id);  // 新 GUID

        // 2 cols × 2 levels × 1 depth = 4 库位
        var locs = await db.Space_Locations.ToListAsync();
        Assert.Equal(4, locs.Count);

        Assert.All(locs, l => Assert.Equal(0, l.Status));
        Assert.All(locs, l => Assert.Equal(1, l.CodeOrigin));
        Assert.All(locs, l => Assert.True(l.Placed));
        Assert.All(locs, l => Assert.Null(l.LocationCode));

        // col=1, level=1, depth=1: AbsX = 1000 + (1-0.5)*1200 = 1600
        var col1 = locs.First(l => l.Col == 1 && l.Level == 1 && l.Depth == 1);
        Assert.Equal(1600, col1.AbsX);
    }
}
