using CP6.Core.EFDbContext;
using CP6.Core.Services.Space;
using CP6.Entity.DomainModels.Space;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests;

/// <summary>
/// 定位查询测试（ch06 §8）：locate 命中/未命中、search 前缀、detail 变长路径。
/// v1.1：构造只注 CP6Context（单参→默认租户，全局过滤自洽）。
/// </summary>
public class SpaceLocateServiceTests
{
    private static (CP6Context, SpaceLocateService) Make()
    {
        var db = new CP6Context(new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return (db, new SpaceLocateService(db));
    }

    [Fact]
    public async Task Locate_Found_ReturnsFloorAndCoords()
    {
        var (db, svc) = Make();
        var fid = Guid.NewGuid();
        db.Space_Locations.Add(new Space_Location
        {
            Id = Guid.NewGuid(), FloorId = fid, LocationCode = "A-03-02-05",
            Placed = true, Status = 1, AbsX = 100, AbsY = 200, AbsZ = 300
        });
        await db.SaveChangesAsync();

        var r = await svc.LocateAsync("A-03-02-05");
        Assert.NotNull(r);
        Assert.Equal(fid, r!.FloorId);
        Assert.Equal(100, r.AbsX);
        Assert.True(r.Placed);
    }

    [Fact]
    public async Task Locate_NotFound_ReturnsNull()
    {
        var (_, svc) = Make();
        Assert.Null(await svc.LocateAsync("NOPE-999"));
    }

    [Fact]
    public async Task Search_Prefix_ReturnsCandidatesSorted()
    {
        var (db, svc) = Make();
        db.Space_Locations.AddRange(
            new Space_Location { Id = Guid.NewGuid(), LocationCode = "A-03-02-05", Placed = true, Status = 1 },
            new Space_Location { Id = Guid.NewGuid(), LocationCode = "A-03-01-01", Placed = true, Status = 1 },
            new Space_Location { Id = Guid.NewGuid(), LocationCode = "B-01-01-01", Placed = true, Status = 1 });
        await db.SaveChangesAsync();

        var r = await svc.SearchAsync("A-03", null);
        Assert.Equal(2, r.Count);
        Assert.Equal("A-03-01-01", r[0].LocationCode);   // OrderBy code
        Assert.Equal("A-03-02-05", r[1].LocationCode);
    }

    [Fact]
    public async Task Detail_AssemblesVariableLengthPath()
    {
        var (db, svc) = Make();
        var siteId = Guid.NewGuid(); var floorId = Guid.NewGuid();
        var zoneId = Guid.NewGuid(); var rackId = Guid.NewGuid();
        db.Space_Sites.Add(new Space_Site { Id = siteId, SiteCode = "WH1", SiteName = "s" });
        db.Space_Floors.Add(new Space_Floor { Id = floorId, SiteId = siteId, Level = 3, FloorCode = "F3", FloorName = "3F" });
        db.Space_Zones.Add(new Space_Zone { Id = zoneId, FloorId = floorId, ZoneCode = "A", ZoneName = "a" });
        db.Space_Racks.Add(new Space_Rack { Id = rackId, ZoneId = zoneId, FloorId = floorId, RackCode = "R03", AisleId = null });
        var locId = Guid.NewGuid();
        db.Space_Locations.Add(new Space_Location
        {
            Id = locId, RackId = rackId, FloorId = floorId, LocationCode = "WH1-A-R03-02-05",
            Col = 5, Level = 2, Depth = 1, Placed = true, Status = 1, CodeOrigin = 1
        });
        await db.SaveChangesAsync();

        var d = await svc.DetailAsync(locId);
        Assert.NotNull(d);
        Assert.Equal("WH1", d!.Path.SiteCode);
        Assert.Equal(3, d.Path.FloorLevel);
        Assert.Equal("A", d.Path.ZoneCode);
        Assert.Null(d.Path.AisleCode);     // 无巷道（变长路径跳过）
        Assert.Equal("R03", d.Path.RackCode);
        Assert.Equal(5, d.Path.Col);
    }
}
