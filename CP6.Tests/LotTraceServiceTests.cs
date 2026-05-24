using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

/// <summary>
/// MSBBWM160 ロット追溯 単体テスト
///
/// テスト観点：
/// 1. GetNodes — そのロットの全 StockTransaction を時系列で取得
/// 2. Forward — OUT 系から OutboundOrder → CustomerCd を解決
/// 3. Backward — IN 系から InboundReceipt → InboundOrder → SupplierCd を解決
/// 4. Recall flag — 当該ロットの全 Stock.RecallFlag を一括更新
/// 5. Stock summary — 複数倉庫の合計 + RecallFlag any
/// </summary>
public class LotTraceServiceTests
{
    private static (CP6.Core.EFDbContext.CP6Context db, StockMovementService stock) Create()
    {
        var db = TestHelper.CreateInMemoryContext();
        db.Warehouses.Add(new Warehouse { WarehouseCd = "W01", WarehouseName = "M", AllowNegative = false });
        db.SaveChanges();
        var stock = new StockMovementService(db, new WmsSequenceService(db));
        return (db, stock);
    }

    [Fact]
    public async Task GetStockSummary_ShouldAggregateAcrossLocations()
    {
        var (db, stock) = Create();
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01", LocationCd = "L01",
            ProductCd = "P1", LotNo = "LOT_A", Qty = 100,
            ExpiryDate = DateTime.Today.AddDays(60),
        });
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01", LocationCd = "L02",
            ProductCd = "P1", LotNo = "LOT_A", Qty = 50,
            ExpiryDate = DateTime.Today.AddDays(30),
        });

        var svc = new LotTraceService(db);
        var sum = await svc.GetStockSummaryAsync("P1", "LOT_A");
        Assert.NotNull(sum);
        Assert.Equal(150m, sum!.TotalPhysicalQty);
        Assert.Equal(2, sum.LocationCount);
        Assert.False(sum.RecallFlag);
        Assert.Equal(DateTime.Today.AddDays(30), sum.ExpiryDate); // 最早の期限
    }

    [Fact]
    public async Task SetRecallFlag_ShouldUpdateAllStocksOfLot()
    {
        var (db, stock) = Create();
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01", LocationCd = "L01",
            ProductCd = "P1", LotNo = "BAD_LOT", Qty = 10,
        });
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01", LocationCd = "L02",
            ProductCd = "P1", LotNo = "BAD_LOT", Qty = 5,
        });

        var svc = new LotTraceService(db);
        var n = await svc.SetRecallFlagAsync("P1", "BAD_LOT", true, "tester");

        Assert.Equal(2, n);
        var stocks = await db.Stocks.Where(s => s.LotNo == "BAD_LOT").ToListAsync();
        Assert.All(stocks, s => Assert.True(s.RecallFlag));
    }

    [Fact]
    public async Task TraceForward_ShouldResolveAffectedCustomers()
    {
        var (db, stock) = Create();
        // 在庫を作る
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01", LocationCd = "L01",
            ProductCd = "P1", LotNo = "TRACE_FW", Qty = 100,
        });
        // 出庫指示を作って出荷する
        var seq = new WmsSequenceService(db);
        var outbound = new OutboundService(db, seq, stock);
        var no = await outbound.CreateOrderAsync(new OutboundOrderDto
        {
            OutboundType = OutboundType.Shipping, WarehouseCd = "W01",
            CustomerCd = "CUST_A", CustomerName = "顧客A",
            Details = new() { new() { ProductCd = "P1", RequiredQty = 30m } }
        }, "u");
        await outbound.ConfirmOrderAsync(no, "u");
        await outbound.AllocateAsync(no, "u");
        await outbound.ShipAsync(no, new ShipRequest(), "u");

        var svc = new LotTraceService(db);
        var result = await svc.TraceForwardAsync("P1", "TRACE_FW");

        Assert.Equal("FORWARD", result.Direction);
        Assert.NotEmpty(result.Nodes);
        Assert.Single(result.AffectedCustomers);
        var ac = result.AffectedCustomers[0];
        Assert.Equal(no, ac.OutboundNo);
        Assert.Equal("CUST_A", ac.CustomerCd);
        Assert.Equal(30m, ac.Qty);
    }

    [Fact]
    public async Task TraceBackward_ShouldResolveAffectedSuppliers()
    {
        var (db, stock) = Create();
        var seq = new WmsSequenceService(db);
        var inbound = new InboundService(db, seq, stock);

        var inNo = await inbound.CreateOrderAsync(new InboundOrderDto
        {
            WarehouseCd = "W01", InboundType = 1,
            SupplierCd = "SUP_A", SupplierName = "仕入先A",
            ExpectedArrivalDate = DateTime.Today,
            Details = new() { new() { ProductCd = "P1", ExpectedQty = 200m } }
        }, "u");
        await inbound.ConfirmOrderAsync(inNo, "u");
        await inbound.ConfirmReceiptAsync(new InboundReceiptDto
        {
            InboundNo = inNo, WarehouseCd = "W01",
            Details = new() {
                new() { RefOrderLineNo = 1, ProductCd = "P1", LotNo = "TRACE_BW", ReceivedQty = 200m, LocationCd = "L01" }
            }
        }, "u");

        var svc = new LotTraceService(db);
        var result = await svc.TraceBackwardAsync("P1", "TRACE_BW");

        Assert.Equal("BACKWARD", result.Direction);
        Assert.Single(result.AffectedSuppliers);
        var sup = result.AffectedSuppliers[0];
        Assert.Equal("SUP_A", sup.SupplierCd);
        Assert.Equal("仕入先A", sup.SupplierName);
        Assert.Equal(200m, sup.Qty);
    }

    [Fact]
    public async Task GetStockSummary_NoStock_ShouldReturnNull()
    {
        var (db, _) = Create();
        var svc = new LotTraceService(db);
        var sum = await svc.GetStockSummaryAsync("P_NONE", "L_NONE");
        Assert.Null(sum);
    }
}
