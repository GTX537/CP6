using CP6.Core.EFDbContext;
using CP6.Core.Services.Pur.Contracts;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Pur;

/// <summary>
/// 采购 WMS 入库适配器单测（P-D1 接桩→真实）。
/// 收货委托 WMS 完整入库流程（InboundService 预定→确定→实绩），库存真增加、返回真实入库号；PoNo 钩子挂上。
/// </summary>
public class WmsReceiveServiceAdapterTests
{
    private static (CP6Context db, WmsReceiveServiceAdapter adapter) NewAdapter()
    {
        var db = TestHelper.CreateInMemoryContext();
        db.Warehouses.Add(new Warehouse { WarehouseCd = "W01", WarehouseName = "原料仓", WarehouseType = WarehouseType.RawMaterial });
        db.SaveChanges();
        var seq = new WmsSequenceService(db);
        var inbound = new InboundService(db, seq, new StockMovementService(db, seq));
        return (db, new WmsReceiveServiceAdapter(inbound));
    }

    private static WmsReceiveRequest Req() => new()
    {
        PoNo = "PO1", SupplierId = "SUP", WarehouseCd = "W01",
        Lines = { new WmsReceiveLine { PoLineNo = 1, ItemId = "ITEM", Qty = 100m } },
    };

    [Fact]
    public async Task Receive_AddsStock_ReturnsInboundNo()
    {
        var (db, adapter) = NewAdapter();

        var r = await adapter.ReceiveAsync(Req(), "u1");

        Assert.StartsWith("IN", r.WmsInboundNo);     // 真实入库预定号（非桩 PURIN）
        var stock = await db.Stocks.SingleAsync(s => s.ProductCd == "ITEM");
        Assert.Equal(100m, stock.PhysicalQty);       // 库存真增加
        Assert.Equal("RECV", stock.LocationCd);      // 落收货暂存位
    }

    [Fact]
    public async Task Receive_HooksPoNo_OnInboundOrder()
    {
        var (db, adapter) = NewAdapter();

        var r = await adapter.ReceiveAsync(Req(), "u1");

        var order = await db.InboundOrders.SingleAsync(o => o.InboundNo == r.WmsInboundNo);
        Assert.Equal("PO1", order.PoNo);             // ★PoNo 钩子挂上
        Assert.Equal(1, order.InboundType);          // 购买入库
        Assert.Equal("SUP", order.SupplierCd);
    }

    [Fact]
    public async Task Receive_ReturnsLineRefs_PerPoLine()
    {
        var (_, adapter) = NewAdapter();

        var r = await adapter.ReceiveAsync(Req(), "u1");

        var lineRef = Assert.Single(r.LineRefs);
        Assert.Equal(1, lineRef.PoLineNo);
        Assert.StartsWith("RC", lineRef.WmsReceiptDetailRef);   // 真实入库实绩号
    }
}
