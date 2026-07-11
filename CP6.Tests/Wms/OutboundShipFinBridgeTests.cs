using CP6.Core.Services.Fin;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Wms;

/// <summary>
/// 出庫確定（Ship）→ FIN 自動開票桥（IFinBridgeHook.OnShipmentConfirmedAsync）点火の回帰テスト（F1 財務油路 波B T-B.1）。
///
/// 検証観点：
///  ① 出荷区分の出庫確定後、FIN 桥が呼ばれ、request.ShipmentId=出庫号 / Lines 数=出庫明細数。
///  ② Lines.UnitPrice は受注明細 IndividualUnitPrice 由来（引当で在庫成本価に汚染された出庫明細 UnitPrice ではない）。
///  ③ 材料出庫（非 Shipping）では FIN 桥は呼ばれない。
///  ④ FIN 桥が例外を投げても出荷確定は成功する（best-effort、出荷をブロックしない）。
/// </summary>
public class OutboundShipFinBridgeTests
{
    /// <summary>呼び出しを記録する偽 IFinBridgeHook（mock フレームワーク不使用、仓内範式）。</summary>
    private sealed class CapturingFinBridge : IFinBridgeHook
    {
        public int ConfirmCalls;
        public FinShipmentInvoiceRequest? LastRequest;
        public bool ThrowOnConfirm;

        public Task<FinBridgeResult> OnShipmentConfirmedAsync(FinShipmentInvoiceRequest request, string? userName)
        {
            ConfirmCalls++;
            LastRequest = request;
            if (ThrowOnConfirm) throw new InvalidOperationException("FIN 桥爆炸测试");
            return Task.FromResult(FinBridgeResult.Ok("INV-TEST"));
        }

        public Task<FinBridgeResult> OnShipmentCancelledAsync(string shipmentId, string? userName)
            => Task.FromResult(FinBridgeResult.Skipped("n/a"));

        public Task<FinBridgeResult> OnWorkOrderCompletedAsync(string workOrderNo, string? userName)
            => Task.FromResult(FinBridgeResult.Skipped("n/a"));
    }

    private static OutboundService CreateService(out CP6.Core.EFDbContext.CP6Context db, IFinBridgeHook finBridge)
    {
        db = TestHelper.CreateInMemoryContext();
        db.Warehouses.Add(new Warehouse
        {
            WarehouseCd = "W01", WarehouseName = "メイン倉庫",
            WarehouseType = WarehouseType.RawMaterial, AllowNegative = false,
        });
        db.SaveChanges();
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        return new OutboundService(db, seq, stock, finBridge: finBridge);
    }

    private static async Task SeedStockAsync(CP6.Core.EFDbContext.CP6Context db,
        string product, string lot, string location, decimal qty)
    {
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01",
            LocationCd = location, ProductCd = product, LotNo = lot, Qty = qty,
            // 在庫成本価（受注售価とは別値）— 汚染判別のため意図的に異なる値
            UnitPrice = 999m,
        });
    }

    // ═════════ ①② Shipping：FIN 桥点火 + 受注售価由来 ═════════

    [Fact]
    public async Task Ship_ShippingWithOrder_FiresFinBridge_WithOrderSalePrice()
    {
        var bridge = new CapturingFinBridge();
        var svc = CreateService(out var db, bridge);
        await SeedStockAsync(db, "PROD-X", "L1", "L01", 100m);

        db.Orders.Add(new Order
        {
            WebOrderNo = "WO_SALE1", CustomerCd = "C001", OrderDate = DateTime.Today,
            OrderType = "01", Status = 1,
        });
        db.OrderDetails.Add(new OrderDetail
        {
            WebOrderNo = "WO_SALE1", WebOrderDetailNo = 1, ProductCd = "PROD-X",
            Quantity = 20m, UnitPriceUnit = "EA", IndividualUnitPrice = 12.5m,
        });
        await db.SaveChangesAsync();

        var no = await svc.CreateFromOrderAsync("WO_SALE1", "u");
        await svc.ConfirmOrderAsync(no, "u");
        await svc.AllocateAsync(no, "u");

        // 引当後に出庫明細 UnitPrice を成本価で汚染 → FIN 桥は受注售価 12.5 を優先すべき
        var det = await db.OutboundOrderDetails.SingleAsync(d => d.OutboundNo == no);
        det.UnitPrice = 999m;
        await db.SaveChangesAsync();

        var pkg = await svc.ShipAsync(no, new ShipRequest(), "u");

        Assert.NotNull(pkg);
        Assert.Equal(1, bridge.ConfirmCalls);
        var req = Assert.IsType<FinShipmentInvoiceRequest>(bridge.LastRequest);
        Assert.Equal(no, req.ShipmentId);                 // 幂等键=出庫号
        Assert.Equal("WO_SALE1", req.OrderId);
        Assert.Equal("C001", req.CustomerId);
        var line = Assert.Single(req.Lines);              // Lines 数=出庫明細数(1)
        Assert.Equal("PROD-X", line.ItemId);
        Assert.Equal(20m, line.Qty);
        Assert.Equal(12.5m, line.UnitPrice);              // 受注售価，非汚染的 999
    }

    // ═════════ ③ 材料出庫では発火しない ═════════

    [Fact]
    public async Task Ship_MaterialType_DoesNotFireFinBridge()
    {
        var bridge = new CapturingFinBridge();
        var svc = CreateService(out var db, bridge);
        await SeedStockAsync(db, "P001", "L1", "L01", 100m);

        var no = await svc.CreateOrderAsync(new OutboundOrderDto
        {
            OutboundType = OutboundType.Material,
            WarehouseCd = "W01",
            PlannedDate = DateTime.Today,
            Details = new List<OutboundOrderDetailDto> { new() { ProductCd = "P001", RequiredQty = 30m } }
        }, "u");
        await svc.ConfirmOrderAsync(no, "u");
        await svc.AllocateAsync(no, "u");
        await svc.ShipAsync(no, new ShipRequest(), "u");

        Assert.Equal(0, bridge.ConfirmCalls);
        Assert.Null(bridge.LastRequest);
    }

    // ═════════ ④ FIN 桥例外は出荷をブロックしない（best-effort） ═════════

    [Fact]
    public async Task Ship_FinBridgeThrows_ShipmentStillSucceeds()
    {
        var bridge = new CapturingFinBridge { ThrowOnConfirm = true };
        var svc = CreateService(out var db, bridge);
        await SeedStockAsync(db, "PROD-X", "L1", "L01", 100m);

        db.Orders.Add(new Order
        {
            WebOrderNo = "WO_SALE2", CustomerCd = "C002", OrderDate = DateTime.Today,
            OrderType = "01", Status = 1,
        });
        db.OrderDetails.Add(new OrderDetail
        {
            WebOrderNo = "WO_SALE2", WebOrderDetailNo = 1, ProductCd = "PROD-X",
            Quantity = 10m, UnitPriceUnit = "EA", IndividualUnitPrice = 8m,
        });
        await db.SaveChangesAsync();

        var no = await svc.CreateFromOrderAsync("WO_SALE2", "u");
        await svc.ConfirmOrderAsync(no, "u");
        await svc.AllocateAsync(no, "u");

        // 例外を投げても出荷確定は成功して packageNo を返す
        var pkg = await svc.ShipAsync(no, new ShipRequest(), "u");

        Assert.NotNull(pkg);
        Assert.Equal(1, bridge.ConfirmCalls);             // 呼ばれた（が投げた）
        var h = await db.OutboundOrders.SingleAsync(x => x.OutboundNo == no);
        Assert.Equal(OutboundOrderStatus.Completed, h.Status);   // 出荷は確定済み
    }
}
