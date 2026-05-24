using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

/// <summary>
/// OutboundService 単体テスト（MSBBWM Phase 3）
///
/// テスト観点：
/// 1. CRUD + 状態機（Draft→Confirmed→Allocated→Completed / Cancelled）
/// 2. Allocate：FIFO + 期限優先（先入れ・古い期限優先）
/// 3. Allocate：在庫不足で InsufficientStockException
/// 4. Ship：OUT トランザクション発行、引当連動消費
/// 5. Cancel（引当済）：UNRSV で在庫を戻す
/// 6. 出荷区分（OutboundType=2）：Ship 時に ShippingPackage 自動採番
/// 7. CreateFromWorkOrder：MES 指図 → 材料出庫指示 展開
/// 8. CreateFromOrder：PA 受注 → 出荷指示 展開
/// 9. 二重展開拒否
/// </summary>
public class OutboundServiceTests
{
    private static OutboundService CreateService(out CP6.Core.EFDbContext.CP6Context db, bool allowNegative = false)
    {
        db = TestHelper.CreateInMemoryContext();
        db.Warehouses.Add(new Warehouse
        {
            WarehouseCd = "W01",
            WarehouseName = "メイン倉庫",
            WarehouseType = WarehouseType.RawMaterial,
            AllowNegative = allowNegative,
        });
        db.SaveChanges();

        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        return new OutboundService(db, seq, stock);
    }

    /// <summary>テスト用に在庫を直接 IN で積む</summary>
    private static async Task SeedStockAsync(StockMovementService stock,
        string product, string lot, string location, decimal qty,
        DateTime? receiveDate = null, DateTime? expiryDate = null)
    {
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN,
            WarehouseCd = "W01",
            LocationCd = location,
            ProductCd = product,
            LotNo = lot,
            Qty = qty,
            ReceiveDate = receiveDate,
            ExpiryDate = expiryDate,
        });
    }

    private static OutboundOrderDto NewMaterialOrderDto(string productCd = "P001", decimal qty = 30m)
    {
        return new OutboundOrderDto
        {
            OutboundType = OutboundType.Material,
            WarehouseCd = "W01",
            PlannedDate = DateTime.Today,
            Details = new List<OutboundOrderDetailDto>
            {
                new() { ProductCd = productCd, RequiredQty = qty }
            }
        };
    }

    // ═════════ 1. CRUD + 状態機 ═════════

    [Fact]
    public async Task CreateOrder_ShouldAssignSequentialOutboundNo()
    {
        var svc = CreateService(out var db);
        var no1 = await svc.CreateOrderAsync(NewMaterialOrderDto(), "u");
        var no2 = await svc.CreateOrderAsync(NewMaterialOrderDto(), "u");
        Assert.StartsWith("OUT", no1);
        Assert.NotEqual(no1, no2);
        Assert.Equal(2, db.OutboundOrders.Count());
    }

    [Fact]
    public async Task ConfirmThenCancel_ShouldMoveToCancelled()
    {
        var svc = CreateService(out var db);
        var no = await svc.CreateOrderAsync(NewMaterialOrderDto(), "u");
        await svc.ConfirmOrderAsync(no, "u");
        await svc.CancelOrderAsync(no, "u");
        Assert.Equal(OutboundOrderStatus.Cancelled, (await db.OutboundOrders.SingleAsync()).Status);
    }

    [Fact]
    public async Task DeleteConfirmed_ShouldThrow()
    {
        var svc = CreateService(out _);
        var no = await svc.CreateOrderAsync(NewMaterialOrderDto(), "u");
        await svc.ConfirmOrderAsync(no, "u");
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DeleteOrderAsync(no, "u"));
    }

    // ═════════ 2. Allocate — FIFO + 期限優先 ═════════

    [Fact]
    public async Task Allocate_ShouldChooseEarliestExpiry()
    {
        var svc = CreateService(out var db);
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);

        // 同じ製品 2 ロット：LOT_A は期限近い、LOT_B は遠い
        await SeedStockAsync(stock, "P001", "LOT_B", "L01", 100m,
            receiveDate: DateTime.Today, expiryDate: DateTime.Today.AddDays(60));
        await SeedStockAsync(stock, "P001", "LOT_A", "L02", 100m,
            receiveDate: DateTime.Today.AddDays(-5), expiryDate: DateTime.Today.AddDays(10));

        var no = await svc.CreateOrderAsync(NewMaterialOrderDto("P001", 30m), "u");
        await svc.ConfirmOrderAsync(no, "u");
        await svc.AllocateAsync(no, "u");

        var d = await db.OutboundOrderDetails.SingleAsync(x => x.OutboundNo == no);
        Assert.Equal("LOT_A", d.LotNo);          // 期限優先で LOT_A が選ばれる
        Assert.Equal("L02", d.LocationCd);
        Assert.Equal(30m, d.AllocatedQty);
        // LOT_A の利用可能在庫が 70 に減少
        var stockA = await db.Stocks.SingleAsync(s => s.LotNo == "LOT_A");
        Assert.Equal(70m, stockA.AvailableQty);
        Assert.Equal(30m, stockA.AllocatedQty);
    }

    [Fact]
    public async Task Allocate_FIFOWhenNoExpiry()
    {
        var svc = CreateService(out var db);
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);

        // 期限なし、受入日のみ違い
        await SeedStockAsync(stock, "P001", "NEW", "L01", 100m, receiveDate: DateTime.Today);
        await SeedStockAsync(stock, "P001", "OLD", "L02", 100m, receiveDate: DateTime.Today.AddDays(-30));

        var no = await svc.CreateOrderAsync(NewMaterialOrderDto("P001", 50m), "u");
        await svc.ConfirmOrderAsync(no, "u");
        await svc.AllocateAsync(no, "u");

        var d = await db.OutboundOrderDetails.SingleAsync();
        Assert.Equal("OLD", d.LotNo); // FIFO = 古いものから
    }

    [Fact]
    public async Task Allocate_InsufficientStock_ShouldThrow()
    {
        var svc = CreateService(out var db);
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);

        await SeedStockAsync(stock, "P001", "L1", "L01", 10m); // 在庫 10

        var no = await svc.CreateOrderAsync(NewMaterialOrderDto("P001", 50m), "u");
        await svc.ConfirmOrderAsync(no, "u");

        await Assert.ThrowsAsync<InsufficientStockException>(() => svc.AllocateAsync(no, "u"));
    }

    [Fact]
    public async Task Allocate_RecallFlagStockExcluded()
    {
        var svc = CreateService(out var db);
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);

        await SeedStockAsync(stock, "P001", "BAD", "L01", 100m);
        // 在庫を回収対象にマーク
        var s = await db.Stocks.SingleAsync();
        s.RecallFlag = true;
        await db.SaveChangesAsync();

        var no = await svc.CreateOrderAsync(NewMaterialOrderDto("P001", 50m), "u");
        await svc.ConfirmOrderAsync(no, "u");
        await Assert.ThrowsAsync<InsufficientStockException>(() => svc.AllocateAsync(no, "u"));
    }

    // ═════════ 3. Ship — OUT 発行 ═════════

    [Fact]
    public async Task Ship_ShouldIssueOutAndConsumeAllocation()
    {
        var svc = CreateService(out var db);
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        await SeedStockAsync(stock, "P001", "L1", "L01", 100m);

        var no = await svc.CreateOrderAsync(NewMaterialOrderDto("P001", 30m), "u");
        await svc.ConfirmOrderAsync(no, "u");
        await svc.AllocateAsync(no, "u");
        var pkg = await svc.ShipAsync(no, new ShipRequest(), "u");

        Assert.Null(pkg); // 材料出庫は梱包なし
        var s = await db.Stocks.SingleAsync();
        Assert.Equal(70m, s.PhysicalQty);    // 100 - 30
        Assert.Equal(0m, s.AllocatedQty);    // OUT が引当も同時に消費
        Assert.Equal(70m, s.AvailableQty);

        var d = await db.OutboundOrderDetails.SingleAsync();
        Assert.Equal(30m, d.ShippedQty);
        Assert.NotNull(d.ShipTxnNo);

        var h = await db.OutboundOrders.SingleAsync();
        Assert.Equal(OutboundOrderStatus.Completed, h.Status);
    }

    [Fact]
    public async Task Ship_ShippingType_ShouldCreatePackage()
    {
        var svc = CreateService(out var db);
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        await SeedStockAsync(stock, "P001", "L1", "L01", 100m);

        // 出荷区分の指示を作成
        var no = await svc.CreateOrderAsync(new OutboundOrderDto
        {
            OutboundType = OutboundType.Shipping,
            WarehouseCd = "W01",
            CustomerCd = "C001",
            CarrierCd = "YAMATO",
            PlannedDate = DateTime.Today,
            Details = new() { new() { ProductCd = "P001", RequiredQty = 20m } }
        }, "u");

        await svc.ConfirmOrderAsync(no, "u");
        await svc.AllocateAsync(no, "u");
        var pkg = await svc.ShipAsync(no, new ShipRequest
        {
            CaseQty = 3, TotalWeightKg = 15.5m, TrackingNo = "TRK12345"
        }, "u");

        Assert.NotNull(pkg);
        Assert.StartsWith("PKG", pkg);
        var p = await db.ShippingPackages.SingleAsync();
        Assert.Equal(3, p.CaseQty);
        Assert.Equal(15.5m, p.TotalWeightKg);
        Assert.Equal("TRK12345", p.TrackingNo);
        Assert.Equal("YAMATO", p.CarrierCd); // header からのフォールバック
    }

    // ═════════ 4. Cancel — 引当解除 ═════════

    [Fact]
    public async Task CancelAllocated_ShouldReleaseStock()
    {
        var svc = CreateService(out var db);
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        await SeedStockAsync(stock, "P001", "L1", "L01", 100m);

        var no = await svc.CreateOrderAsync(NewMaterialOrderDto("P001", 30m), "u");
        await svc.ConfirmOrderAsync(no, "u");
        await svc.AllocateAsync(no, "u");
        // 引当済を取消
        await svc.CancelOrderAsync(no, "u");

        var s = await db.Stocks.SingleAsync();
        Assert.Equal(100m, s.PhysicalQty);
        Assert.Equal(0m, s.AllocatedQty);  // UNRSV で戻る
        Assert.Equal(100m, s.AvailableQty);
        Assert.Equal(OutboundOrderStatus.Cancelled, (await db.OutboundOrders.SingleAsync()).Status);
    }

    // ═════════ 5. CreateFromWorkOrder ═════════

    [Fact]
    public async Task CreateFromWorkOrder_ShouldExpandMaterialOrder()
    {
        var svc = CreateService(out var db);

        // テスト用の WorkOrder + WorkOrderMaterial を直接登録
        db.WorkOrders.Add(new WorkOrder
        {
            WorkOrderNo = "WO001", Status = 1, ProductCd = "PROD-A",
            ProductionQty = 100m, PlanStartDate = DateTime.Today.AddDays(1), Priority = 2,
        });
        db.WorkOrderMaterials.Add(new WorkOrderMaterial
        {
            WorkOrderNo = "WO001", ProcessCd = "P01", MaterialCd = "M001",
            MaterialName = "原紙", PlanQty = 500m, Unit = "kg", SortOrder = 1,
        });
        db.WorkOrderMaterials.Add(new WorkOrderMaterial
        {
            WorkOrderNo = "WO001", ProcessCd = "P02", MaterialCd = "M002",
            MaterialName = "インキ", PlanQty = 10m, Unit = "L", SortOrder = 2,
        });
        await db.SaveChangesAsync();

        var no = await svc.CreateFromWorkOrderAsync("WO001", "u");

        var header = await db.OutboundOrders.SingleAsync();
        Assert.Equal(OutboundType.Material, header.OutboundType);
        Assert.Equal("WO001", header.WorkOrderNo);
        Assert.Equal(2, header.Priority);

        var details = await db.OutboundOrderDetails.OrderBy(d => d.LineNo).ToListAsync();
        Assert.Equal(2, details.Count);
        Assert.Equal("M001", details[0].ProductCd);
        Assert.Equal(500m, details[0].RequiredQty);
        Assert.Equal("M002", details[1].ProductCd);
        Assert.Equal(10m, details[1].RequiredQty);
    }

    [Fact]
    public async Task CreateFromWorkOrder_DuplicateShouldThrow()
    {
        var svc = CreateService(out var db);
        db.WorkOrders.Add(new WorkOrder
        {
            WorkOrderNo = "WO001", Status = 1, ProductCd = "PROD-A",
            ProductionQty = 100m,
        });
        db.WorkOrderMaterials.Add(new WorkOrderMaterial
        {
            WorkOrderNo = "WO001", ProcessCd = "P01", MaterialCd = "M001", PlanQty = 5m,
        });
        await db.SaveChangesAsync();

        await svc.CreateFromWorkOrderAsync("WO001", "u");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateFromWorkOrderAsync("WO001", "u"));
    }

    // ═════════ 6. CreateFromOrder ═════════

    [Fact]
    public async Task CreateFromOrder_ShouldExpandShippingOrder()
    {
        var svc = CreateService(out var db);

        db.Orders.Add(new Order
        {
            WebOrderNo = "WO_PA001", CustomerCd = "C001", OrderDate = DateTime.Today,
            OrderType = "01", Status = 1,
        });
        db.OrderDetails.Add(new OrderDetail
        {
            WebOrderNo = "WO_PA001", WebOrderDetailNo = 1, ProductCd = "PROD-X",
            Quantity = 1000m, UnitPriceUnit = "EA", IndividualUnitPrice = 12.5m,
        });
        await db.SaveChangesAsync();

        var no = await svc.CreateFromOrderAsync("WO_PA001", "u");

        var header = await db.OutboundOrders.SingleAsync();
        Assert.Equal(OutboundType.Shipping, header.OutboundType);
        Assert.Equal("WO_PA001", header.WebOrderNo);
        Assert.Equal("C001", header.CustomerCd);

        var d = await db.OutboundOrderDetails.SingleAsync();
        Assert.Equal("PROD-X", d.ProductCd);
        Assert.Equal(1000m, d.RequiredQty);
        Assert.Equal(12.5m, d.UnitPrice);
    }
}
