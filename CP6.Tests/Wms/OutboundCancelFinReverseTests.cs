using CP6.Core.Services.Fin;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Wms;

/// <summary>
/// 出庫取消（CancelOrderAsync）→ FIN 红冲桥（IFinBridgeHook.OnShipmentCancelledAsync）点火の回帰テスト（F1 財務油路 波B T-B.2）。
///
/// 主控裁決1：消費端（FinBridgeHook.OnShipmentCancelledAsync）は発票が無ければ Skipped で優雅に no-op する
/// ため、WMS 側から Fin テーブルを跨模块照会せず、Shipping 区分の取消では**無条件点火**（Fin 側で判定）。
///
/// 検証観点：
///  ① 出荷区分の取消後、FIN 桥が呼ばれ、shipmentId=出庫号。
///  ② 材料出庫（非 Shipping）の取消では FIN 桥は呼ばれない（門槛=OutboundType.Shipping、B.1 と対称）。
///  ③ FIN 桥が例外を投げても取消は成功する（best-effort、取消をブロックしない）。
/// </summary>
public class OutboundCancelFinReverseTests
{
    /// <summary>呼び出しを記録する偽 IFinBridgeHook（mock フレームワーク不使用、仓内範式）。</summary>
    private sealed class CapturingFinBridge : IFinBridgeHook
    {
        public int CancelCalls;
        public string? LastShipmentId;
        public bool ThrowOnCancel;

        public Task<FinBridgeResult> OnShipmentConfirmedAsync(FinShipmentInvoiceRequest request, string? userName)
            => Task.FromResult(FinBridgeResult.Skipped("n/a"));

        public Task<FinBridgeResult> OnShipmentCancelledAsync(string shipmentId, string? userName)
        {
            CancelCalls++;
            LastShipmentId = shipmentId;
            if (ThrowOnCancel) throw new InvalidOperationException("FIN 红冲桥爆炸测试");
            return Task.FromResult(FinBridgeResult.Ok("CM-TEST"));
        }

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
            UnitPrice = 999m,
        });
    }

    // ═════════ ① Shipping：取消 → FIN 红冲桥点火（shipmentId=出庫号） ═════════

    [Fact]
    public async Task Cancel_ShippingType_FiresFinReverse_WithShipmentNo()
    {
        var bridge = new CapturingFinBridge();
        var svc = CreateService(out var db, bridge);
        await SeedStockAsync(db, "PROD-X", "L1", "L01", 100m);

        db.Orders.Add(new Order
        {
            WebOrderNo = "WO_CAN1", CustomerCd = "C001", OrderDate = DateTime.Today,
            OrderType = "01", Status = 1,
        });
        db.OrderDetails.Add(new OrderDetail
        {
            WebOrderNo = "WO_CAN1", WebOrderDetailNo = 1, ProductCd = "PROD-X",
            Quantity = 20m, UnitPriceUnit = "EA", IndividualUnitPrice = 12.5m,
        });
        await db.SaveChangesAsync();

        var no = await svc.CreateFromOrderAsync("WO_CAN1", "u");
        await svc.ConfirmOrderAsync(no, "u");
        await svc.AllocateAsync(no, "u");   // → Allocated（Ship 前なので取消可）

        await svc.CancelOrderAsync(no, "u");

        Assert.Equal(1, bridge.CancelCalls);
        Assert.Equal(no, bridge.LastShipmentId);   // 幂等键=出庫号

        var h = await db.OutboundOrders.SingleAsync(x => x.OutboundNo == no);
        Assert.Equal(OutboundOrderStatus.Cancelled, h.Status);
    }

    // ═════════ ② 材料出庫の取消では発火しない ═════════

    [Fact]
    public async Task Cancel_MaterialType_DoesNotFireFinReverse()
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

        await svc.CancelOrderAsync(no, "u");

        Assert.Equal(0, bridge.CancelCalls);
        Assert.Null(bridge.LastShipmentId);
    }

    // ═════════ ③ FIN 桥例外は取消をブロックしない（best-effort） ═════════

    [Fact]
    public async Task Cancel_FinBridgeThrows_CancelStillSucceeds()
    {
        var bridge = new CapturingFinBridge { ThrowOnCancel = true };
        var svc = CreateService(out var db, bridge);
        await SeedStockAsync(db, "PROD-X", "L1", "L01", 100m);

        db.Orders.Add(new Order
        {
            WebOrderNo = "WO_CAN3", CustomerCd = "C003", OrderDate = DateTime.Today,
            OrderType = "01", Status = 1,
        });
        db.OrderDetails.Add(new OrderDetail
        {
            WebOrderNo = "WO_CAN3", WebOrderDetailNo = 1, ProductCd = "PROD-X",
            Quantity = 10m, UnitPriceUnit = "EA", IndividualUnitPrice = 8m,
        });
        await db.SaveChangesAsync();

        var no = await svc.CreateFromOrderAsync("WO_CAN3", "u");
        await svc.ConfirmOrderAsync(no, "u");
        await svc.AllocateAsync(no, "u");

        // 例外を投げても取消は成功して状態は Cancelled
        await svc.CancelOrderAsync(no, "u");

        Assert.Equal(1, bridge.CancelCalls);   // 呼ばれた（が投げた）
        var h = await db.OutboundOrders.SingleAsync(x => x.OutboundNo == no);
        Assert.Equal(OutboundOrderStatus.Cancelled, h.Status);
    }
}
