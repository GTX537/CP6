using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Moq;

namespace CP6.Tests;

/// <summary>
/// IWmsNotifier 連動テスト
///
/// 1. StockMovementService.ApplyAsync が IWmsNotifier.NotifyStockChangedAsync を呼ぶ
/// 2. Notifier 例外でも本処理は成功する（best-effort）
/// 3. InboundService.ConfirmReceiptAsync が NotifyInboundReceivedAsync を呼ぶ
/// 4. OutboundService.ShipAsync が NotifyOutboundShippedAsync を呼ぶ
/// </summary>
public class WmsNotifierTests
{
    [Fact]
    public async Task StockMovementService_ShouldInvokeNotifier_OnSuccessfulApply()
    {
        var db = TestHelper.CreateInMemoryContext();
        db.Warehouses.Add(new Warehouse { WarehouseCd = "W01", WarehouseName = "M", AllowNegative = false });
        await db.SaveChangesAsync();

        var notifier = new Mock<IWmsNotifier>();
        var svc = new StockMovementService(db, new WmsSequenceService(db), notifier.Object);

        var no = await svc.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01", LocationCd = "L01",
            ProductCd = "P1", LotNo = "L", Qty = 10,
        });

        Assert.StartsWith("TXN", no);
        notifier.Verify(n => n.NotifyStockChangedAsync(It.Is<StockChangedEvent>(
            e => e.TxnType == WmsTxnType.IN && e.ProductCd == "P1" && e.Qty == 10m
        )), Times.Once);
    }

    [Fact]
    public async Task StockMovementService_NotifierException_ShouldNotBreakApply()
    {
        var db = TestHelper.CreateInMemoryContext();
        db.Warehouses.Add(new Warehouse { WarehouseCd = "W01", WarehouseName = "M", AllowNegative = false });
        await db.SaveChangesAsync();

        var notifier = new Mock<IWmsNotifier>();
        notifier.Setup(n => n.NotifyStockChangedAsync(It.IsAny<StockChangedEvent>()))
                .ThrowsAsync(new Exception("hub down"));

        var svc = new StockMovementService(db, new WmsSequenceService(db), notifier.Object);

        // notifier 例外でも例外伝播せず、TXN は返る
        var no = await svc.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01", LocationCd = "L01",
            ProductCd = "P1", LotNo = "L", Qty = 5,
        });

        Assert.StartsWith("TXN", no);
        var stock = db.Stocks.Single();
        Assert.Equal(5m, stock.PhysicalQty);
    }

    [Fact]
    public async Task InboundService_ShouldInvokeInboundReceivedNotifier()
    {
        var db = TestHelper.CreateInMemoryContext();
        db.Warehouses.Add(new Warehouse { WarehouseCd = "W01", WarehouseName = "M" });
        await db.SaveChangesAsync();

        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        var notifier = new Mock<IWmsNotifier>();
        var inbound = new InboundService(db, seq, stock, notifier.Object);

        var receiptNo = await inbound.ConfirmReceiptAsync(new InboundReceiptDto
        {
            WarehouseCd = "W01",
            Details = new() { new() { ProductCd = "P1", LotNo = "L", ReceivedQty = 10, LocationCd = "L01" } }
        }, "u");

        Assert.StartsWith("RC", receiptNo);
        notifier.Verify(n => n.NotifyInboundReceivedAsync(receiptNo, "W01"), Times.Once);
    }

    [Fact]
    public async Task OutboundService_ShouldInvokeOutboundShippedNotifier()
    {
        var db = TestHelper.CreateInMemoryContext();
        db.Warehouses.Add(new Warehouse { WarehouseCd = "W01", WarehouseName = "M" });
        await db.SaveChangesAsync();

        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        // 在庫を作る
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01", LocationCd = "L01",
            ProductCd = "P1", LotNo = "L1", Qty = 100,
        });

        var notifier = new Mock<IWmsNotifier>();
        var outbound = new OutboundService(db, seq, stock, notifier.Object);

        var no = await outbound.CreateOrderAsync(new OutboundOrderDto
        {
            OutboundType = OutboundType.Shipping, WarehouseCd = "W01",
            Details = new() { new() { ProductCd = "P1", RequiredQty = 30m } }
        }, "u");
        await outbound.ConfirmOrderAsync(no, "u");
        await outbound.AllocateAsync(no, "u");
        var pkg = await outbound.ShipAsync(no, new ShipRequest(), "u");

        Assert.NotNull(pkg);
        notifier.Verify(n => n.NotifyOutboundShippedAsync(no, pkg), Times.Once);
    }
}
