using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

public class OutboundService_QcFilter_Tests
{
    private static OutboundService CreateService(out CP6.Core.EFDbContext.CP6Context db)
    {
        db = TestHelper.CreateInMemoryContext();
        db.Warehouses.Add(new Warehouse
        {
            WarehouseCd = "W01",
            WarehouseName = "Main",
            WarehouseType = WarehouseType.RawMaterial,
            AllowNegative = false,
        });
        db.SaveChanges();

        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        return new OutboundService(db, seq, stock);
    }

    private static async Task SeedStockAsync(
        CP6.Core.EFDbContext.CP6Context db,
        string lot,
        string qcStatus,
        DateTime expiryDate)
    {
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN,
            WarehouseCd = "W01",
            LocationCd = $"LOC-{lot}",
            ProductCd = "P001",
            LotNo = lot,
            Qty = 100m,
            ExpiryDate = expiryDate,
            OperatorCd = "seed",
        });

        var row = await db.Stocks.SingleAsync(s => s.LotNo == lot);
        row.QcStatus = qcStatus;
        await db.SaveChangesAsync();
    }

    private static async Task<string> CreateConfirmedOrderAsync(OutboundService svc, decimal qty = 50m)
    {
        var outboundNo = await svc.CreateOrderAsync(new OutboundOrderDto
        {
            OutboundType = OutboundType.Material,
            WarehouseCd = "W01",
            PlannedDate = DateTime.Today,
            Details = new List<OutboundOrderDetailDto>
            {
                new() { ProductCd = "P001", RequiredQty = qty }
            },
        }, "tester");
        await svc.ConfirmOrderAsync(outboundNo, "tester");
        return outboundNo;
    }

    [Fact]
    public async Task Allocate_OnlyPickPassedAndPending_NotFailed()
    {
        var svc = CreateService(out var db);
        await SeedStockAsync(db, "FAILED", StockQcStatus.Failed, DateTime.Today.AddDays(1));
        await SeedStockAsync(db, "PASSED", StockQcStatus.Passed, DateTime.Today.AddDays(2));
        await SeedStockAsync(db, "PENDING", StockQcStatus.Pending, DateTime.Today.AddDays(3));
        var outboundNo = await CreateConfirmedOrderAsync(svc);

        await svc.AllocateAsync(outboundNo, "tester");

        var detail = await db.OutboundOrderDetails.SingleAsync(d => d.OutboundNo == outboundNo);
        Assert.NotEqual("FAILED", detail.LotNo);
        Assert.Contains(detail.LotNo, new[] { "PASSED", "PENDING" });
    }

    [Fact]
    public async Task Allocate_NoEligibleStock_ThrowsInsufficient()
    {
        var svc = CreateService(out var db);
        await SeedStockAsync(db, "FAILED", StockQcStatus.Failed, DateTime.Today.AddDays(1));
        await SeedStockAsync(db, "HOLD", StockQcStatus.Hold, DateTime.Today.AddDays(2));
        var outboundNo = await CreateConfirmedOrderAsync(svc);

        await Assert.ThrowsAsync<InsufficientStockException>(() => svc.AllocateAsync(outboundNo, "tester"));
    }

    [Fact]
    public async Task Allocate_PendingIsAllocatable_BackwardCompatible()
    {
        var svc = CreateService(out var db);
        await SeedStockAsync(db, "PENDING", StockQcStatus.Pending, DateTime.Today.AddDays(1));
        var outboundNo = await CreateConfirmedOrderAsync(svc);

        await svc.AllocateAsync(outboundNo, "tester");

        var detail = await db.OutboundOrderDetails.SingleAsync(d => d.OutboundNo == outboundNo);
        Assert.Equal("PENDING", detail.LotNo);
        Assert.Equal(50m, detail.AllocatedQty);
    }

    [Fact]
    public async Task Allocate_HoldStockIsSkipped()
    {
        var svc = CreateService(out var db);
        await SeedStockAsync(db, "HOLD", StockQcStatus.Hold, DateTime.Today.AddDays(1));
        await SeedStockAsync(db, "PASSED", StockQcStatus.Passed, DateTime.Today.AddDays(2));
        var outboundNo = await CreateConfirmedOrderAsync(svc);

        await svc.AllocateAsync(outboundNo, "tester");

        var detail = await db.OutboundOrderDetails.SingleAsync(d => d.OutboundNo == outboundNo);
        Assert.NotEqual("HOLD", detail.LotNo);
        Assert.Equal("PASSED", detail.LotNo);
    }
}
