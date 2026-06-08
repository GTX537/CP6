using CP6.Core.EFDbContext;
using CP6.Core.Services;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CP6.Tests;

public class Rma_ErpCreditNoteE2ETests
{
    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    [Fact]
    public async Task RmaConfirm_GeneratesCreditNote_UpdatesOrderDetailReturnedQty()
    {
        await using var db = NewDb();
        SeedOrderAndRma(db, productCd: "PROD-RMA", rmaQty: 10m);

        var hook = new ErpBridgeHook(db, NullLogger<ErpBridgeHook>.Instance);
        var svc = new RmaService(db, new WmsSequenceService(db), new StockMovementService(db, new WmsSequenceService(db)), hook);

        await svc.CloseAsync("RMA-001", "tester");

        var note = await db.CreditNotes.AsNoTracking().SingleAsync();
        Assert.Equal("RMA-001", note.RmaNo);
        Assert.Equal("WEB-RMA-001", note.WebOrderNo);
        Assert.Equal(CreditNoteType.Refund, note.Type);
        Assert.Equal("C-RMA", note.CustomerCd);
        Assert.Equal("PROD-RMA", note.ProductCd);
        Assert.Equal("LOT-1", note.LotNo);
        Assert.Equal(10m, note.Qty);

        var detail = await db.OrderDetails.AsNoTracking()
            .SingleAsync(x => x.WebOrderNo == "WEB-RMA-001" && x.ProductCd == "PROD-RMA");
        Assert.Equal(10m, detail.ReturnedQty);

        var evt = await db.IntegrationEvents.AsNoTracking().SingleAsync();
        Assert.Equal(IntegrationEventStatus.Success, evt.Status);
        Assert.Equal(nameof(ErpBridgeHook.OnReturnConfirmedAsync), evt.HookName);
        Assert.Equal("RMA-001", evt.SourceNo);
    }

    [Fact]
    public async Task RmaConfirm_NoMatchingOrderDetail_StillCreatesCreditNote_LogsWarning()
    {
        await using var db = NewDb();
        SeedOrderAndRma(db, productCd: "PROD-NO-MATCH", rmaQty: 4m);
        db.OrderDetails.RemoveRange(db.OrderDetails);
        db.OrderDetails.Add(new OrderDetail
        {
            WebOrderNo = "WEB-RMA-001",
            WebOrderDetailNo = 1,
            ProductCd = "OTHER-PROD",
            Quantity = 100m,
        });
        await db.SaveChangesAsync();

        var logger = new Mock<ILogger<ErpBridgeHook>>();
        var hook = new ErpBridgeHook(db, logger.Object);

        var result = await hook.OnReturnConfirmedAsync("RMA-001", "tester");

        Assert.True(result.Success);
        var note = await db.CreditNotes.AsNoTracking().SingleAsync();
        Assert.Equal("C-RMA", note.CustomerCd);
        Assert.Equal("PROD-NO-MATCH", note.ProductCd);

        var detail = await db.OrderDetails.AsNoTracking().SingleAsync();
        Assert.Null(detail.ReturnedQty);
        VerifyLogged(logger, LogLevel.Warning, "OrderDetail");
    }

    [Fact]
    public async Task RmaConfirm_BridgeFailure_DoesNotRollbackRmaConfirm()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ThrowOnCreditNoteSaveContext(options);
        SeedOrderAndRma(db, productCd: "PROD-RMA", rmaQty: 2m);

        var hook = new ErpBridgeHook(db, NullLogger<ErpBridgeHook>.Instance);
        var svc = new RmaService(db, new WmsSequenceService(db), new StockMovementService(db, new WmsSequenceService(db)), hook);

        await svc.CloseAsync("RMA-001", "tester");

        var header = await db.RmaHeaders.AsNoTracking().SingleAsync(x => x.RmaNo == "RMA-001");
        Assert.Equal(RmaStatus.Closed, header.Status);
        Assert.False(await db.CreditNotes.AsNoTracking().AnyAsync());

        var evt = await db.IntegrationEvents.AsNoTracking().SingleAsync();
        Assert.Equal(IntegrationEventStatus.Failed, evt.Status);
        Assert.Equal(nameof(ErpBridgeHook.OnReturnConfirmedAsync), evt.HookName);
        Assert.Contains("CreditNote save failed", evt.LastError ?? "");
    }

    [Fact]
    public async Task OnReturnConfirmedAsync_RmaNotFound_ReturnsSkipped_PersistsIntegrationEvent()
    {
        await using var db = NewDb();
        var hook = new ErpBridgeHook(db, NullLogger<ErpBridgeHook>.Instance);

        var result = await hook.OnReturnConfirmedAsync("RMA-MISSING", "tester");

        Assert.False(result.Success);
        Assert.Contains("SKIPPED", result.Message);

        var evt = await db.IntegrationEvents.AsNoTracking().SingleAsync();
        Assert.Equal(IntegrationEventStatus.Skipped, evt.Status);
        Assert.Equal("WM-MSG-RMA-404", evt.LastError);
        Assert.Equal("RMA-MISSING", evt.SourceNo);
    }

    private static void SeedOrderAndRma(CP6Context db, string productCd, decimal rmaQty)
    {
        db.Orders.Add(new Order
        {
            WebOrderNo = "WEB-RMA-001",
            CustomerCd = "C-RMA",
            OrderType = "01",
            OrderDate = DateTime.Today,
            Status = 1,
        });
        db.OrderDetails.Add(new OrderDetail
        {
            WebOrderNo = "WEB-RMA-001",
            WebOrderDetailNo = 1,
            ProductCd = productCd,
            Quantity = 100m,
        });
        db.OutboundOrders.Add(new OutboundOrder
        {
            OutboundNo = "OUT-RMA-001",
            OutboundType = OutboundType.Shipping,
            WebOrderNo = "WEB-RMA-001",
            WarehouseCd = "W01",
            Status = OutboundOrderStatus.Completed,
        });
        db.RmaHeaders.Add(new RmaHeader
        {
            RmaNo = "RMA-001",
            CustomerCd = "C-RMA",
            OriginalShippingNo = "OUT-RMA-001",
            WarehouseCd = "W01",
            Status = RmaStatus.Judged,
        });
        db.RmaDetails.Add(new RmaDetail
        {
            RmaNo = "RMA-001",
            LineNo = 1,
            ProductCd = productCd,
            LotNo = "LOT-1",
            Qty = rmaQty,
        });
        db.SaveChanges();
    }

    private static void VerifyLogged(Mock<ILogger<ErpBridgeHook>> logger, LogLevel level, string contains)
    {
        logger.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(contains)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    private sealed class ThrowOnCreditNoteSaveContext : CP6Context
    {
        private bool _hasThrown;

        public ThrowOnCreditNoteSaveContext(DbContextOptions<CP6Context> options)
            : base(options)
        {
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (!_hasThrown && ChangeTracker.Entries().Any(e =>
                    e.Entity.GetType().Name == "CreditNote" && e.State == EntityState.Added))
            {
                _hasThrown = true;
                foreach (var entry in ChangeTracker.Entries().Where(e => e.Entity.GetType().Name == "CreditNote").ToList())
                {
                    entry.State = EntityState.Detached;
                }
                throw new TimeoutException("CreditNote save failed");
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
