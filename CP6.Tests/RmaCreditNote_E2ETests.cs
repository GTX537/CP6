using CP6.Core.EFDbContext;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace CP6.Tests;

public class RmaCreditNote_E2ETests
{
    private const string User = "tester";

    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    [Fact]
    public async Task RmaConfirm_GeneratesCreditNote_UpdatesReturnedQty_PersistsIntegrationEvent()
    {
        await using var db = NewDb();
        SeedOrderOutboundAndRma(db);

        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        var hook = new ErpBridgeHook(db, NullLogger<ErpBridgeHook>.Instance);
        var svc = new RmaService(db, seq, stock, hook);

        await svc.CloseAsync("RMA-E2E-001", User);

        var note = await db.CreditNotes.AsNoTracking().SingleAsync();
        Assert.Equal("WEB-E2E-RMA-001", note.WebOrderNo);
        Assert.Equal("RMA-E2E-001", note.RmaNo);
        Assert.Equal(CreditNoteType.Refund, note.Type);
        Assert.Equal("C-E2E", note.CustomerCd);
        Assert.Equal("PROD-E2E", note.ProductCd);
        Assert.Equal("LOT-E2E", note.LotNo);
        Assert.Equal(10m, note.Qty);

        var detail = await db.OrderDetails.AsNoTracking()
            .SingleAsync(x => x.WebOrderNo == "WEB-E2E-RMA-001" && x.ProductCd == "PROD-E2E");
        Assert.Equal(100m, detail.ShippedQty);
        Assert.Equal(10m, detail.ReturnedQty);

        var evt = await db.IntegrationEvents.AsNoTracking().SingleAsync();
        Assert.Equal(IntegrationEventStatus.Success, evt.Status);
        Assert.Equal(nameof(ErpBridgeHook.OnReturnConfirmedAsync), evt.HookName);
        Assert.Equal("WMS", evt.SourceModule);
        Assert.Equal("ERP", evt.TargetModule);
        Assert.Equal("RMA-E2E-001", evt.SourceNo);
    }

    private static void SeedOrderOutboundAndRma(CP6Context db)
    {
        db.Orders.Add(new Order
        {
            WebOrderNo = "WEB-E2E-RMA-001",
            CustomerCd = "C-E2E",
            OrderType = "01",
            OrderDate = DateTime.Today,
            Status = 1,
        });
        db.OrderDetails.Add(new OrderDetail
        {
            WebOrderNo = "WEB-E2E-RMA-001",
            WebOrderDetailNo = 1,
            ProductCd = "PROD-E2E",
            Quantity = 100m,
            ShippedQty = 100m,
        });
        db.OutboundOrders.Add(new OutboundOrder
        {
            OutboundNo = "OUT-E2E-RMA-001",
            OutboundType = OutboundType.Shipping,
            WebOrderNo = "WEB-E2E-RMA-001",
            CustomerCd = "C-E2E",
            WarehouseCd = "W01",
            Status = OutboundOrderStatus.Completed,
        });
        db.RmaHeaders.Add(new RmaHeader
        {
            RmaNo = "RMA-E2E-001",
            CustomerCd = "C-E2E",
            OriginalShippingNo = "OUT-E2E-RMA-001",
            WarehouseCd = "W01",
            Status = RmaStatus.Judged,
        });
        db.RmaDetails.Add(new RmaDetail
        {
            RmaNo = "RMA-E2E-001",
            LineNo = 1,
            ProductCd = "PROD-E2E",
            LotNo = "LOT-E2E",
            Qty = 10m,
        });
        db.SaveChanges();
    }
}
