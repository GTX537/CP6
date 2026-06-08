using CP6.Core.EFDbContext;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Mes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace CP6.Tests;

public class QualityInspection_AutoQcLinkTests
{
    private const string WorkOrderNo = "WO-QC-LINK-001";
    private const string ProductCd = "FG-QC-LINK";
    private const string LotNo = "LOT-QC-LINK";
    private const string Wh = "W01";
    private const string User = "tester";

    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    private static QualityInspectionService NewService(CP6Context db, IStockQcService? stockQc = null) =>
        new(
            db,
            new MesSequenceService(db),
            stockQc ?? new StockQcService(db, NullLogger<StockQcService>.Instance),
            NullLogger<QualityInspectionService>.Instance);

    private static QualityInspectionDto NewInspection(int overallResult, string? workOrderNo = WorkOrderNo) => new()
    {
        WorkOrderNo = workOrderNo ?? string.Empty,
        InspectorCd = "INSPECTOR-01",
        InspectionDate = DateTime.Today,
        InspectionType = 3,
        InspectionQty = 10m,
        OverallResult = overallResult,
        DispositionAction = overallResult == 2 ? 1 : null,
    };

    private static async Task SeedLinkedStockAsync(CP6Context db)
    {
        db.InboundReceipts.Add(new InboundReceipt
        {
            ReceiptNo = "RC-QC-LINK-001",
            SourceType = InboundSourceType.Production,
            WorkOrderNo = WorkOrderNo,
            WarehouseCd = Wh,
            Status = InboundReceiptStatus.Confirmed,
            Creator = "seed",
            CreateDate = DateTime.Now,
        });
        db.InboundReceiptDetails.Add(new InboundReceiptDetail
        {
            ReceiptNo = "RC-QC-LINK-001",
            LineNo = 1,
            ProductCd = ProductCd,
            LotNo = LotNo,
            ReceivedQty = 10m,
            UnitCd = "EA",
            LocationCd = "FG-A",
            Creator = "seed",
            CreateDate = DateTime.Now,
        });
        db.Stocks.Add(new Stock
        {
            WarehouseCd = Wh,
            LocationCd = "FG-A",
            ProductCd = ProductCd,
            LotNo = LotNo,
            PhysicalQty = 10m,
            AvailableQty = 10m,
            QcStatus = StockQcStatus.Pending,
            Creator = "seed",
            CreateDate = DateTime.Now,
        });
        db.Stocks.Add(new Stock
        {
            WarehouseCd = Wh,
            LocationCd = "FG-B",
            ProductCd = "UNRELATED",
            LotNo = "LOT-OTHER",
            PhysicalQty = 10m,
            AvailableQty = 10m,
            QcStatus = StockQcStatus.Pending,
            Creator = "seed",
            CreateDate = DateTime.Now,
        });

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Create_NgInspection_AutoMarksLinkedStockFailed()
    {
        await using var db = NewDb();
        await SeedLinkedStockAsync(db);

        var inspectionNo = await NewService(db).CreateAsync(NewInspection(2), User);

        Assert.NotEmpty(inspectionNo);
        var linked = await db.Stocks.AsNoTracking()
            .SingleAsync(s => s.ProductCd == ProductCd && s.LotNo == LotNo);
        Assert.Equal(StockQcStatus.Failed, linked.QcStatus);
        Assert.Equal(User, linked.Modifier);

        var unrelated = await db.Stocks.AsNoTracking().SingleAsync(s => s.ProductCd == "UNRELATED");
        Assert.Equal(StockQcStatus.Pending, unrelated.QcStatus);
        Assert.Equal(1, await db.QualityInspections.CountAsync(q => q.WorkOrderNo == WorkOrderNo));
    }

    [Fact]
    public async Task Create_PassInspection_DoesNotChangeLinkedStock()
    {
        await using var db = NewDb();
        var recorder = new RecordingStockQcService();
        await SeedLinkedStockAsync(db);

        var inspectionNo = await NewService(db, recorder).CreateAsync(NewInspection(1), User);

        Assert.NotEmpty(inspectionNo);
        Assert.Equal(0, recorder.CallCount);
        var linked = await db.Stocks.AsNoTracking()
            .SingleAsync(s => s.ProductCd == ProductCd && s.LotNo == LotNo);
        Assert.Equal(StockQcStatus.Pending, linked.QcStatus);
    }

    [Fact]
    public async Task Create_NgInspectionWithNoLinkedStock_StillSavesQualityInspection()
    {
        await using var db = NewDb();

        var inspectionNo = await NewService(db).CreateAsync(NewInspection(2, "WO-QC-NO-STOCK"), User);

        Assert.NotEmpty(inspectionNo);
        var inspection = await db.QualityInspections.AsNoTracking().SingleAsync();
        Assert.Equal("WO-QC-NO-STOCK", inspection.WorkOrderNo);
        Assert.Equal(2, inspection.OverallResult);
        Assert.Empty(await db.Stocks.AsNoTracking().ToListAsync());
    }

    private sealed class RecordingStockQcService : IStockQcService
    {
        public int CallCount { get; private set; }

        public Task<Stock> SetStockQcStatusAsync(Guid stockId, string newStatus, string? reason, string? userName) =>
            throw new NotSupportedException();

        public Task<int> MarkLinkedStockByWorkOrderAsync(string workOrderNo, string newStatus, string? reason, string? userName)
        {
            CallCount++;
            return Task.FromResult(0);
        }
    }
}
