using CP6.Core.EFDbContext;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class StockQc_AllocateE2ETests
{
    private const string Wh = "W01";
    private const string ProductCd = "P-QC-E2E";
    private const string User = "tester";

    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var db = new CP6Context(options);
        db.Warehouses.Add(new Warehouse
        {
            WarehouseCd = Wh,
            WarehouseName = "Main warehouse",
            WarehouseType = WarehouseType.RawMaterial,
            AllowNegative = false,
        });
        db.SaveChanges();
        return db;
    }

    private static OutboundService NewWms(CP6Context db)
    {
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        return new OutboundService(db, seq, stock);
    }

    private static async Task SeedStockAsync(
        CP6Context db,
        string lotNo,
        string qcStatus,
        DateTime expiryDate)
    {
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN,
            WarehouseCd = Wh,
            LocationCd = $"LOC-{lotNo}",
            ProductCd = ProductCd,
            LotNo = lotNo,
            Qty = 100m,
            UnitCd = "EA",
            ExpiryDate = expiryDate,
            OperatorCd = "seed",
        });

        var row = await db.Stocks.SingleAsync(s => s.ProductCd == ProductCd && s.LotNo == lotNo);
        row.QcStatus = qcStatus;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Allocate_QcBlockedStock_SkipsFailed_ReservesPassed_AndWritesTransactions()
    {
        await using var db = NewDb();
        var outbound = NewWms(db);

        await SeedStockAsync(db, "FAILED-LOT", StockQcStatus.Failed, DateTime.Today.AddDays(1));
        await SeedStockAsync(db, "PASSED-LOT", StockQcStatus.Passed, DateTime.Today.AddDays(2));
        await SeedStockAsync(db, "PENDING-LOT", StockQcStatus.Pending, DateTime.Today.AddDays(3));

        var outboundNo = await outbound.CreateOrderAsync(new OutboundOrderDto
        {
            OutboundType = OutboundType.Material,
            WarehouseCd = Wh,
            PlannedDate = DateTime.Today,
            Details =
            {
                new OutboundOrderDetailDto
                {
                    ProductCd = ProductCd,
                    RequiredQty = 50m,
                    UnitCd = "EA",
                },
            },
        }, User);
        await outbound.ConfirmOrderAsync(outboundNo, User);

        await outbound.AllocateAsync(outboundNo, User);

        var detail = await db.OutboundOrderDetails.AsNoTracking()
            .SingleAsync(d => d.OutboundNo == outboundNo);
        Assert.Equal("PASSED-LOT", detail.LotNo);
        Assert.Equal(50m, detail.AllocatedQty);
        Assert.Equal("LOC-PASSED-LOT", detail.LocationCd);
        Assert.NotNull(detail.AllocateTxnNo);

        var stocks = await db.Stocks.AsNoTracking()
            .Where(s => s.ProductCd == ProductCd)
            .ToDictionaryAsync(s => s.LotNo);
        Assert.Equal(0m, stocks["FAILED-LOT"].AllocatedQty);
        Assert.Equal(100m, stocks["FAILED-LOT"].AvailableQty);
        Assert.Equal(50m, stocks["PASSED-LOT"].AllocatedQty);
        Assert.Equal(50m, stocks["PASSED-LOT"].AvailableQty);
        Assert.Equal(0m, stocks["PENDING-LOT"].AllocatedQty);
        Assert.Equal(100m, stocks["PENDING-LOT"].AvailableQty);

        var rsv = await db.StockTransactions.AsNoTracking()
            .SingleAsync(t => t.TxnType == WmsTxnType.RSV && t.RelatedNo == outboundNo);
        Assert.Equal(detail.AllocateTxnNo, rsv.TxnNo);
        Assert.Equal(ProductCd, rsv.ProductCd);
        Assert.Equal("PASSED-LOT", rsv.LotNo);
        Assert.Equal("LOC-PASSED-LOT", rsv.LocationCd);
        Assert.Equal(50m, rsv.Qty);
        Assert.Equal("OUTBOUND", rsv.RelatedType);
        Assert.Equal(User, rsv.OperatorCd);

        var inboundTxns = await db.StockTransactions.AsNoTracking()
            .Where(t => t.TxnType == WmsTxnType.IN && t.ProductCd == ProductCd)
            .OrderBy(t => t.LotNo)
            .ToListAsync();
        Assert.Equal(3, inboundTxns.Count);
        Assert.All(inboundTxns, t => Assert.Equal(100m, t.Qty));

        Assert.Equal(OutboundOrderStatus.Allocated,
            (await db.OutboundOrders.AsNoTracking().SingleAsync(o => o.OutboundNo == outboundNo)).Status);
    }
}
