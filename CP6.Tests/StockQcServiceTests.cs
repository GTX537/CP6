using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CP6.Tests;

public class StockQcServiceTests
{
    private static StockQcService CreateService(out CP6.Core.EFDbContext.CP6Context db)
    {
        db = TestHelper.CreateInMemoryContext();
        return new StockQcService(db, NullLogger<StockQcService>.Instance);
    }

    private static Stock NewStock(
        string product = "P001",
        string lot = "LOT01",
        string location = "L01",
        decimal qty = 100m)
        => new()
        {
            WarehouseCd = "W01",
            LocationCd = location,
            ProductCd = product,
            LotNo = lot,
            PhysicalQty = qty,
            AllocatedQty = 0m,
            AvailableQty = qty,
            RecallFlag = false,
            OwnerType = StockOwnerType.Self,
            QcStatus = StockQcStatus.Pending,
        };

    [Fact]
    public async Task SetStockQcStatusAsync_ValidTransition_UpdatesAndReturns()
    {
        var svc = CreateService(out var db);
        var stock = NewStock();
        db.Stocks.Add(stock);
        await db.SaveChangesAsync();

        var result = await svc.SetStockQcStatusAsync(stock.Id, StockQcStatus.Failed, "NG", "tester");

        Assert.Equal(stock.Id, result.Id);
        Assert.Equal(StockQcStatus.Failed, result.QcStatus);
        Assert.Equal("tester", result.Modifier);
        Assert.NotNull(result.ModifyDate);
        Assert.Equal(StockQcStatus.Failed, (await db.Stocks.SingleAsync()).QcStatus);
    }

    [Fact]
    public async Task SetStockQcStatusAsync_InvalidStatus_ThrowsArgumentException()
    {
        var svc = CreateService(out _);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.SetStockQcStatusAsync(Guid.NewGuid(), "BOGUS", null, "tester"));

        Assert.StartsWith("WM-MSG-QC-001", ex.Message);
    }

    [Fact]
    public async Task SetStockQcStatusAsync_StockNotFound_ThrowsInvalidOperation()
    {
        var svc = CreateService(out _);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SetStockQcStatusAsync(Guid.NewGuid(), StockQcStatus.Hold, null, "tester"));

        Assert.StartsWith("WM-MSG-QC-404", ex.Message);
    }

    [Fact]
    public async Task MarkLinkedStockByWorkOrderAsync_UpdatesAllMatchingStocks()
    {
        var svc = CreateService(out var db);
        db.InboundReceipts.Add(new InboundReceipt
        {
            ReceiptNo = "RC001",
            WorkOrderNo = "WO001",
            WarehouseCd = "W01",
        });
        db.InboundReceiptDetails.AddRange(
            new InboundReceiptDetail { ReceiptNo = "RC001", LineNo = 1, ProductCd = "P001", LotNo = "L1", LocationCd = "LOC" },
            new InboundReceiptDetail { ReceiptNo = "RC001", LineNo = 2, ProductCd = "P002", LotNo = "L2", LocationCd = "LOC" });
        db.Stocks.AddRange(
            NewStock("P001", "L1", "A01"),
            NewStock("P001", "L1", "A02"),
            NewStock("P002", "L2", "A03"),
            NewStock("P003", "L3", "A04"));
        await db.SaveChangesAsync();

        var affected = await svc.MarkLinkedStockByWorkOrderAsync("WO001", StockQcStatus.Hold, "manual hold", "tester");

        Assert.Equal(3, affected);
        Assert.All(await db.Stocks.Where(s => s.ProductCd != "P003").ToListAsync(),
            s => Assert.Equal(StockQcStatus.Hold, s.QcStatus));
        Assert.Equal(StockQcStatus.Pending, (await db.Stocks.SingleAsync(s => s.ProductCd == "P003")).QcStatus);
    }

    [Fact]
    public async Task MarkLinkedStockByWorkOrderAsync_NoReceipts_ReturnsZero()
    {
        var svc = CreateService(out var db);
        db.Stocks.Add(NewStock());
        await db.SaveChangesAsync();

        var affected = await svc.MarkLinkedStockByWorkOrderAsync("WO404", StockQcStatus.Passed, null, "tester");

        Assert.Equal(0, affected);
        Assert.Equal(StockQcStatus.Pending, (await db.Stocks.SingleAsync()).QcStatus);
    }

    [Fact]
    public async Task MarkLinkedStockByWorkOrderAsync_PreservesUnrelatedFields()
    {
        var svc = CreateService(out var db);
        db.InboundReceipts.Add(new InboundReceipt
        {
            ReceiptNo = "RC002",
            WorkOrderNo = "WO002",
            WarehouseCd = "W01",
        });
        db.InboundReceiptDetails.Add(new InboundReceiptDetail
        {
            ReceiptNo = "RC002",
            LineNo = 1,
            ProductCd = "P010",
            LotNo = "LOT10",
            LocationCd = "LOC",
        });
        db.Stocks.Add(new Stock
        {
            WarehouseCd = "W01",
            LocationCd = "A01",
            ProductCd = "P010",
            LotNo = "LOT10",
            PhysicalQty = 88m,
            AllocatedQty = 12m,
            AvailableQty = 76m,
            RecallFlag = true,
            OwnerType = StockOwnerType.Self,
            QcStatus = StockQcStatus.Pending,
        });
        await db.SaveChangesAsync();

        await svc.MarkLinkedStockByWorkOrderAsync("WO002", StockQcStatus.Failed, "failed qc", "tester");

        var stock = await db.Stocks.SingleAsync();
        Assert.Equal(88m, stock.PhysicalQty);
        Assert.Equal(12m, stock.AllocatedQty);
        Assert.True(stock.RecallFlag);
        Assert.Equal(StockQcStatus.Failed, stock.QcStatus);
    }
}
