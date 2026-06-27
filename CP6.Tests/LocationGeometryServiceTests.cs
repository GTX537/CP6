using CP6.Core.EFDbContext;
using CP6.Core.Services.Space;
using CP6.Entity.DomainModels.Space;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests;

/// <summary>
/// 库位绝对坐标重算（ch00 §6）。纯几何公式 + "几何可动、码不漂移"约束：
/// 重算只改 AbsX/Y/Z，不动 LocationCode/Status/Version。
/// </summary>
public class LocationGeometryServiceTests
{
    private static CP6Context Db() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public void ComputeAbs_NoRotation_AnchorIsMinCorner()
    {
        // 锚点(1000,2000)，单格 1200×1500×1000，Col2/Level2/Depth1，RotationZ=0
        var rack = new Space_Rack { X = 1000, Y = 2000, Z = 0, RotationZ = 0,
            CellW = 1200, CellH = 1500, CellD = 1000 };
        var (x, y, z) = LocationGeometryService.ComputeAbs(rack, col: 2, level: 2, depth: 1);
        // localX=(2-0.5)*1200=1800; localY=(1-0.5)*1000=500; localZ=(2-0.5)*1500=2250
        Assert.Equal(1000 + 1800, x);
        Assert.Equal(2000 + 500,  y);
        Assert.Equal(0 + 2250,    z);
    }

    [Fact]
    public void ComputeAbs_Rotate90_RotatesAroundAnchor()
    {
        var rack = new Space_Rack { X = 0, Y = 0, Z = 0, RotationZ = 90,
            CellW = 1000, CellH = 1000, CellD = 1000 };
        var (x, y, _) = LocationGeometryService.ComputeAbs(rack, col: 1, level: 1, depth: 1);
        // localX=500, localY=500; θ=90°: x=500cos90-500sin90=-500; y=500sin90+500cos90=500
        Assert.Equal(-500, x);
        Assert.Equal(500,  y);
    }

    [Fact]
    public async Task Recalc_OnlyUpdatesCoords_NotCodeNorStatus()
    {
        using var db = Db();
        var rackId = Guid.NewGuid();
        // 不显式设 TenantId —— SaveChanges 自动盖默认租户；查询全局过滤自动按默认租户
        db.Space_Racks.Add(new Space_Rack { Id = rackId, X = 0, Y = 0,
            CellW = 1000, CellH = 1000, CellD = 1000, Cols = 1, Levels = 1, DepthCount = 1 });
        db.Space_Locations.Add(new Space_Location { Id = Guid.NewGuid(), RackId = rackId,
            Placed = true, Col = 1, Level = 1, Depth = 1, LocationCode = "A-01-01-01", Status = 1, Version = 3 });
        await db.SaveChangesAsync();

        var svc = new LocationGeometryService(db);
        await svc.RecalcRackLocationsAsync(rackId);

        var loc = await db.Space_Locations.SingleAsync();
        Assert.Equal(500, loc.AbsX);                   // (1-0.5)*1000=500，无旋转
        Assert.Equal("A-01-01-01", loc.LocationCode);  // 码不变
        Assert.Equal(1, loc.Status);                   // 状态不变
        Assert.Equal(3, loc.Version);                  // 版本不变（纯几何不发布、不升版）
    }
}
