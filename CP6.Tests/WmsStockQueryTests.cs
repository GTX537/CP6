using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

public class WmsStockQueryTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static IWmsStockQuery Stock(CP6Context db) => new WmsStockQuery(db);

    [Fact]
    public async Task GetStockQtyAsync_SumsPhysicalQty_Decimal()
    {
        using var db = NewDb();
        db.Stocks.Add(new Stock { WarehouseCd = "W1", LocationCd = "A-01", ProductCd = "P1", LotNo = "",
            PhysicalQty = 2.5m, QcStatus = StockQcStatus.Pending });
        db.Stocks.Add(new Stock { WarehouseCd = "W1", LocationCd = "A-01", ProductCd = "P2", LotNo = "",
            PhysicalQty = 1.5m, QcStatus = StockQcStatus.Pending });
        await db.SaveChangesAsync();

        Assert.Equal(4.0m, await Stock(db).GetStockQtyAsync("A-01"));
        Assert.Equal(0m, await Stock(db).GetStockQtyAsync("NOPE"));
    }

    // 播一个库位的 Stock + Location + 可选出库拣货
    private static async Task SeedLocAsync(CP6Context db, string code, decimal qty,
        decimal cap = 0m, bool blocked = false, string product = "P1")
    {
        if (qty > 0)
            db.Stocks.Add(new Stock { WarehouseCd = "W1", LocationCd = code, ProductCd = product, LotNo = "",
                PhysicalQty = qty, AllocatedQty = 0m, QcStatus = StockQcStatus.Pending });
        db.Locations.Add(new Location { LocationCd = code, WarehouseCd = "W1", CapacityQty = cap, IsBlocked = blocked });
        await db.SaveChangesAsync();
    }

    private static async Task SeedPickingAsync(CP6Context db, string code, int status)
    {
        db.OutboundOrders.Add(new OutboundOrder { OutboundNo = "OB-" + code, WarehouseCd = "W1", Status = status });
        db.OutboundOrderDetails.Add(new OutboundOrderDetail { OutboundNo = "OB-" + code, LineNo = 1,
            ProductCd = "P1", LocationCd = code, RequiredQty = 5m, AllocatedQty = 5m, ShippedQty = 0m });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetStockByLocations_Empty_Status0()
    {
        using var db = NewDb();
        await SeedLocAsync(db, "A-01", qty: 0m, cap: 10m);
        var dto = Assert.Single(await Stock(db).GetStockByLocationsAsync(new[] { "A-01" }));
        Assert.Equal(0, dto.BinStatus);
        Assert.Equal(0m, dto.Qty);
        Assert.Equal(10m, dto.Capacity);
    }

    [Fact]
    public async Task GetStockByLocations_HasStock_Status1()
    {
        using var db = NewDb();
        await SeedLocAsync(db, "A-01", qty: 3m, cap: 10m);
        var dto = Assert.Single(await Stock(db).GetStockByLocationsAsync(new[] { "A-01" }));
        Assert.Equal(1, dto.BinStatus);
        Assert.Equal(3m, dto.Qty);
    }

    [Fact]
    public async Task GetStockByLocations_Full_Status2()
    {
        using var db = NewDb();
        await SeedLocAsync(db, "A-01", qty: 10m, cap: 10m);
        Assert.Equal(2, (await Stock(db).GetStockByLocationsAsync(new[] { "A-01" }))[0].BinStatus);
    }

    [Fact]
    public async Task GetStockByLocations_Blocked_Status3_OverridesFull()
    {
        using var db = NewDb();
        await SeedLocAsync(db, "A-01", qty: 10m, cap: 10m, blocked: true);
        Assert.Equal(3, (await Stock(db).GetStockByLocationsAsync(new[] { "A-01" }))[0].BinStatus);
    }

    [Fact]
    public async Task GetStockByLocations_Picking_Status4_OverridesFull()
    {
        using var db = NewDb();
        await SeedLocAsync(db, "A-01", qty: 10m, cap: 10m);
        await SeedPickingAsync(db, "A-01", OutboundOrderStatus.Picking);  // 3
        Assert.Equal(4, (await Stock(db).GetStockByLocationsAsync(new[] { "A-01" }))[0].BinStatus);
    }

    [Fact]
    public async Task GetStockByLocations_AllocatedNotPicking_NotStatus4()
    {
        using var db = NewDb();
        await SeedLocAsync(db, "A-01", qty: 3m, cap: 10m);
        await SeedPickingAsync(db, "A-01", OutboundOrderStatus.Allocated);  // 2，非 Picking → 不算在拣
        Assert.Equal(1, (await Stock(db).GetStockByLocationsAsync(new[] { "A-01" }))[0].BinStatus);
    }

    [Fact]
    public async Task GetStockByLocations_Aggregates_TopMaterial_Kinds()
    {
        using var db = NewDb();
        db.Stocks.Add(new Stock { WarehouseCd = "W1", LocationCd = "A-01", ProductCd = "PA", LotNo = "",
            PhysicalQty = 2m, AllocatedQty = 1m, QcStatus = StockQcStatus.Pending });
        db.Stocks.Add(new Stock { WarehouseCd = "W1", LocationCd = "A-01", ProductCd = "PB", LotNo = "",
            PhysicalQty = 5m, AllocatedQty = 0m, QcStatus = StockQcStatus.Pending });
        db.Locations.Add(new Location { LocationCd = "A-01", WarehouseCd = "W1", CapacityQty = 0m });
        await db.SaveChangesAsync();

        var dto = Assert.Single(await Stock(db).GetStockByLocationsAsync(new[] { "A-01" }));
        Assert.Equal(7m, dto.Qty);
        Assert.Equal(1m, dto.AllocatedQty);
        Assert.Equal("PB", dto.TopMaterial);   // 占量最大
        Assert.Equal(2, dto.ProductKinds);
        Assert.Equal(new[] { "PA", "PB" }, dto.ProductCodes.OrderBy(x => x));
        Assert.Null(dto.Capacity);             // CapacityQty=0 → null
    }

    [Fact]
    public async Task GetStockByLocations_PublishedBinOnly_ReturnsCapacityAndEmptyStatus()
    {
        using var db = NewDb();
        var id = Guid.NewGuid();
        db.Space_Locations.Add(new CP6.Entity.DomainModels.Space.Space_Location
        {
            Id = id, LocationCode = "A-01", Status = 1,
        });
        db.WmsBins.Add(new WmsBin
        {
            Id = id, LocationCode = "A-01", WarehouseCd = "W1", IsActive = true,
            Version = 2, AttrsJson = "{\"capacity\":12,\"capacityUom\":1}",
        });
        await db.SaveChangesAsync();

        var dto = Assert.Single(await Stock(db).GetStockByLocationsAsync(new[] { "A-01" }));
        Assert.Equal(0, dto.BinStatus);
        Assert.Equal(12m, dto.Capacity);
        Assert.Equal(1, dto.CapacityUom);
        Assert.Equal("wms-bin", dto.CapacitySource);
    }

    [Fact]
    public async Task GetStockByLocations_NoData_NotReturned()
    {
        using var db = NewDb();
        var r = await Stock(db).GetStockByLocationsAsync(new[] { "GHOST" });
        Assert.Empty(r);
    }

    [Fact]
    public async Task FindLocations_ByMaterial_ReturnsHitsWithQty()
    {
        using var db = NewDb();
        db.Stocks.Add(new Stock { WarehouseCd = "W1", LocationCd = "A-01", ProductCd = "PX", LotNo = "L1",
            PhysicalQty = 3m, QcStatus = StockQcStatus.Pending });
        db.Stocks.Add(new Stock { WarehouseCd = "W1", LocationCd = "A-02", ProductCd = "PX", LotNo = "L2",
            PhysicalQty = 5m, QcStatus = StockQcStatus.Pending });
        db.Stocks.Add(new Stock { WarehouseCd = "W1", LocationCd = "A-03", ProductCd = "PY", LotNo = "",
            PhysicalQty = 9m, QcStatus = StockQcStatus.Pending });
        await db.SaveChangesAsync();

        var hits = await Stock(db).FindLocationsAsync(new StockLocateQuery { MaterialNo = "PX" });
        Assert.Equal(2, hits.Count);
        Assert.Contains(hits, h => h.LocationCode == "A-01" && h.Qty == 3m);
        Assert.Contains(hits, h => h.LocationCode == "A-02" && h.Qty == 5m);
    }

    [Fact]
    public async Task FindLocations_ByMaterialAndLot_Filters()
    {
        using var db = NewDb();
        db.Stocks.Add(new Stock { WarehouseCd = "W1", LocationCd = "A-01", ProductCd = "PX", LotNo = "L1",
            PhysicalQty = 3m, QcStatus = StockQcStatus.Pending });
        db.Stocks.Add(new Stock { WarehouseCd = "W1", LocationCd = "A-02", ProductCd = "PX", LotNo = "L2",
            PhysicalQty = 5m, QcStatus = StockQcStatus.Pending });
        await db.SaveChangesAsync();

        var hits = await Stock(db).FindLocationsAsync(new StockLocateQuery { MaterialNo = "PX", Lot = "L2" });
        var h = Assert.Single(hits);
        Assert.Equal("A-02", h.LocationCode);
    }

    [Fact]
    public async Task FindLocations_ByContainer_UsesPallet()
    {
        using var db = NewDb();
        db.Pallets.Add(new Pallet { PalletNo = "PLT-1", LocationCd = "A-09", ProductCd = "PZ", LotNo = "L1" });
        await db.SaveChangesAsync();

        var hits = await Stock(db).FindLocationsAsync(new StockLocateQuery { Container = "PLT-1" });
        Assert.Equal("A-09", Assert.Single(hits).LocationCode);
    }

    [Fact]
    public async Task FindLocations_AllEmpty_ReturnsEmpty()
    {
        using var db = NewDb();
        Assert.Empty(await Stock(db).FindLocationsAsync(new StockLocateQuery()));
    }

    [Fact]
    public async Task GetStockQty_WarehouseScoped_ExcludesOtherWarehouses()
    {
        // H7：多仓同码时不带仓维度会跨仓求和 → 误拦他仓同码库位的停用
        using var db = new CP6Context(new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Stocks.Add(new Stock { Id = Guid.NewGuid(), WarehouseCd = "W1", LocationCd = "A-01", ProductCd = "P", LotNo = "", PhysicalQty = 3m });
        db.Stocks.Add(new Stock { Id = Guid.NewGuid(), WarehouseCd = "W2", LocationCd = "A-01", ProductCd = "P", LotNo = "", PhysicalQty = 7m });
        await db.SaveChangesAsync();

        var q = new WmsStockQuery(db);
        Assert.Equal(10m, await q.GetStockQtyAsync("A-01"));        // 兼容：不带仓维度=跨仓求和
        Assert.Equal(3m, await q.GetStockQtyAsync("A-01", "W1"));   // 带仓维度只算本仓
    }

    [Fact]
    public async Task GetStockByLocations_TenantIsolated()
    {
        var dbName = Guid.NewGuid().ToString();
        var optsA = new DbContextOptionsBuilder<CP6Context>().UseInMemoryDatabase(dbName).Options;
        var t2 = new CP6.Core.Services.Common.TenantContext { CurrentTenantId = Guid.NewGuid() };

        // 租户2 播一条
        using (var db2 = new CP6Context(optsA, t2))
        {
            db2.Stocks.Add(new Stock { WarehouseCd = "W1", LocationCd = "A-01", ProductCd = "P1", LotNo = "",
                PhysicalQty = 5m, QcStatus = StockQcStatus.Pending });
            db2.Locations.Add(new Location { LocationCd = "A-01", WarehouseCd = "W1" });
            await db2.SaveChangesAsync();
        }
        // 默认租户查 → 看不到租户2 的数据
        using var dbDefault = new CP6Context(optsA);
        Assert.Empty(await new WmsStockQuery(dbDefault).GetStockByLocationsAsync(new[] { "A-01" }));
    }
}
