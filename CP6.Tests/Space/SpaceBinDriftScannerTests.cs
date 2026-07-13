using CP6.Core.EFDbContext;
using CP6.Core.Services.Space;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Space;

/// <summary>
/// Space 波5 对账漂移扫描（只读）：已发布库位（Status=1, IsDeleted=0）对应 WMS bin 存在且 IsActive=false
/// → 发布/停用链路乱序漂移，命中告警。两表以主键等值 join（跨系统同一 GUID）。
/// InMemory context 单参构造：实体 SaveChanges 盖章为 DefaultTenant，查询过滤同租户 → 命中。
/// </summary>
public class SpaceBinDriftScannerTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    [Fact]
    public async Task Scan_PublishedLocationWithInactiveBin_Reported()
    {
        using var db = NewDb();
        var id = Guid.NewGuid();
        db.Space_Locations.Add(new Space_Location { Id = id, Status = 1, LocationCode = "A-01-01" });
        db.WmsBins.Add(new WmsBin { Id = id, LocationCode = "A-01-01", WarehouseCd = "W1", IsActive = false, Version = 7 });
        await db.SaveChangesAsync();

        var drifts = await SpaceBinDriftScanner.ScanAsync(db, default);

        Assert.Single(drifts);
        Assert.Equal(id, drifts[0].LocationId);
        Assert.Equal("A-01-01", drifts[0].LocationCode);
        Assert.Equal(7, drifts[0].BinVersion);
    }

    [Fact]
    public async Task Scan_PublishedLocationWithActiveBin_NotReported()
    {
        using var db = NewDb();
        var id = Guid.NewGuid();
        db.Space_Locations.Add(new Space_Location { Id = id, Status = 1, LocationCode = "A-01-02" });
        db.WmsBins.Add(new WmsBin { Id = id, LocationCode = "A-01-02", WarehouseCd = "W1", IsActive = true });
        await db.SaveChangesAsync();

        var drifts = await SpaceBinDriftScanner.ScanAsync(db, default);

        Assert.Empty(drifts);
    }

    [Fact]
    public async Task Scan_UnpublishedLocationWithInactiveBin_NotReported()
    {
        using var db = NewDb();
        var id = Guid.NewGuid();
        db.Space_Locations.Add(new Space_Location { Id = id, Status = 2, LocationCode = "A-01-03" });
        db.WmsBins.Add(new WmsBin { Id = id, LocationCode = "A-01-03", WarehouseCd = "W1", IsActive = false });
        await db.SaveChangesAsync();

        var drifts = await SpaceBinDriftScanner.ScanAsync(db, default);

        Assert.Empty(drifts);
    }
}
