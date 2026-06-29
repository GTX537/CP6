using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

public class WmsAdvancedQueryTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task SeedOrderAsync(CP6Context db, string ob, int status,
        params (int line, string code, decimal qty, string product)[] lines)
    {
        db.OutboundOrders.Add(new OutboundOrder { OutboundNo = ob, WarehouseCd = "W1", Status = status });
        foreach (var (line, code, qty, product) in lines)
            db.OutboundOrderDetails.Add(new OutboundOrderDetail
            {
                OutboundNo = ob, LineNo = line, ProductCd = product, LocationCd = code,
                RequiredQty = qty, AllocatedQty = qty, ShippedQty = 0m,
            });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetPickPath_OrdersByLineNo()
    {
        using var db = NewDb();
        await SeedOrderAsync(db, "OB-1", 3,
            (3, "A-03", 1m, "P3"), (1, "A-01", 5m, "P1"), (2, "A-02", 2m, "P2"));

        var path = await new WmsPickTaskQuery(db).GetPickPathAsync("OB-1");

        Assert.Equal("OB-1", path.TaskNo);
        Assert.Equal(3, path.Items.Count);
        Assert.Equal(new[] { 1, 2, 3 }, path.Items.Select(i => i.Seq).ToArray());
        Assert.Equal("A-01", path.Items[0].LocationCode);
        Assert.Equal(5m, path.Items[0].Qty);
        Assert.Equal("P1", path.Items[0].MaterialNo);
    }

    [Fact]
    public async Task GetPickPath_SkipsNullLocationLines()
    {
        using var db = NewDb();
        db.OutboundOrders.Add(new OutboundOrder { OutboundNo = "OB-2", WarehouseCd = "W1", Status = 3 });
        db.OutboundOrderDetails.Add(new OutboundOrderDetail { OutboundNo = "OB-2", LineNo = 1, ProductCd = "P1", LocationCd = "A-01", RequiredQty = 1m });
        db.OutboundOrderDetails.Add(new OutboundOrderDetail { OutboundNo = "OB-2", LineNo = 2, ProductCd = "P2", LocationCd = null, RequiredQty = 1m });
        await db.SaveChangesAsync();

        var path = await new WmsPickTaskQuery(db).GetPickPathAsync("OB-2");
        Assert.Equal("A-01", Assert.Single(path.Items).LocationCode);
    }

    [Fact]
    public async Task GetPickPath_UnknownOrder_EmptyItems()
    {
        using var db = NewDb();
        var path = await new WmsPickTaskQuery(db).GetPickPathAsync("NOPE");
        Assert.Equal("NOPE", path.TaskNo);
        Assert.Empty(path.Items);
    }

    // ── Task 2: WmsWorkloadQuery helpers ────────────────────────────────────

    private static async Task SeedPlacedLocAsync(CP6Context db, Guid floorId, string code)
    {
        db.Space_Locations.Add(new Space_Location
        {
            FloorId = floorId, LocationCode = code, Placed = true, Status = 1,
            AbsX = 100, AbsY = 200, AbsZ = 300,
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedTxnAsync(CP6Context db, string code, DateTime when, string type = "OUT")
    {
        db.StockTransactions.Add(new StockTransaction
        {
            TxnNo = "TXN-" + Guid.NewGuid().ToString("N")[..8], TxnType = type, TxnDateTime = when,
            WarehouseCd = "W1", LocationCd = code, ProductCd = "P1", LotNo = "", Qty = 1m,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetWorkload_CountsInWindow_ByLocation()
    {
        using var db = NewDb();
        var floor = Guid.NewGuid();
        var t0 = new DateTime(2026, 6, 28, 9, 0, 0);
        await SeedPlacedLocAsync(db, floor, "A-01");
        await SeedPlacedLocAsync(db, floor, "A-02");
        await SeedTxnAsync(db, "A-01", t0);
        await SeedTxnAsync(db, "A-01", t0.AddMinutes(10));
        await SeedTxnAsync(db, "A-01", t0.AddMinutes(20));
        await SeedTxnAsync(db, "A-02", t0.AddMinutes(5));

        var rows = await new WmsWorkloadQuery(db).GetWorkloadAsync(
            floor, t0.Date, t0.Date.AddDays(1));

        Assert.Equal(2, rows.Count);
        Assert.Equal(3, rows.Single(r => r.LocationCode == "A-01").OpCount);
        Assert.Equal(1, rows.Single(r => r.LocationCode == "A-02").OpCount);
    }

    [Fact]
    public async Task GetWorkload_ExcludesOutsideWindow()
    {
        using var db = NewDb();
        var floor = Guid.NewGuid();
        var t0 = new DateTime(2026, 6, 28, 9, 0, 0);
        await SeedPlacedLocAsync(db, floor, "A-01");
        await SeedTxnAsync(db, "A-01", t0.AddDays(-2));   // 窗外
        await SeedTxnAsync(db, "A-01", t0);               // 窗内

        var rows = await new WmsWorkloadQuery(db).GetWorkloadAsync(floor, t0.Date, t0.Date.AddDays(1));
        Assert.Equal(1, Assert.Single(rows).OpCount);
    }

    [Fact]
    public async Task GetWorkload_OnlyFloorPlacedCodes()
    {
        using var db = NewDb();
        var floor = Guid.NewGuid();
        var t0 = new DateTime(2026, 6, 28, 9, 0, 0);
        await SeedPlacedLocAsync(db, floor, "A-01");      // 本层 placed
        // "B-99" 流水但不属本层任何 placed 库位 → 不计
        await SeedTxnAsync(db, "A-01", t0);
        await SeedTxnAsync(db, "B-99", t0);

        var rows = await new WmsWorkloadQuery(db).GetWorkloadAsync(floor, t0.Date, t0.Date.AddDays(1));
        Assert.Equal("A-01", Assert.Single(rows).LocationCode);
    }

    [Fact]
    public async Task GetWorkload_NoPlacedLocations_Empty()
    {
        using var db = NewDb();
        var rows = await new WmsWorkloadQuery(db).GetWorkloadAsync(
            Guid.NewGuid(), DateTime.Today, DateTime.Today.AddDays(1));
        Assert.Empty(rows);
    }

    [Fact]
    public async Task GetWorkload_TenantIsolated()
    {
        var dbName = Guid.NewGuid().ToString();
        var opts = new DbContextOptionsBuilder<CP6Context>().UseInMemoryDatabase(dbName).Options;
        var floor = Guid.NewGuid();
        var t0 = new DateTime(2026, 6, 28, 9, 0, 0);
        var t2 = new CP6.Core.Services.Common.TenantContext { CurrentTenantId = Guid.NewGuid() };

        using (var db2 = new CP6Context(opts, t2))
        {
            db2.Space_Locations.Add(new Space_Location { FloorId = floor, LocationCode = "A-01", Placed = true, Status = 1 });
            db2.StockTransactions.Add(new StockTransaction { TxnNo = "TXN-X", TxnType = "OUT", TxnDateTime = t0, WarehouseCd = "W1", LocationCd = "A-01", ProductCd = "P1", LotNo = "", Qty = 1m });
            await db2.SaveChangesAsync();
        }
        using var dbDefault = new CP6Context(opts);
        var rows = await new WmsWorkloadQuery(dbDefault).GetWorkloadAsync(floor, t0.Date, t0.Date.AddDays(1));
        Assert.Empty(rows);   // 默认租户看不到租户2 的层/流水
    }
}
