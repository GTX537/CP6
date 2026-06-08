using CP6.Core.EFDbContext;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace CP6.Tests;

public class MaterialShortage_E2ETests
{
    private const string User = "tester";
    private const string WarehouseCd = "W01";

    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    [Fact]
    public async Task MaterialOutbound_InsufficientStock_WritesShortage_NotifiesNoOpNotifier_AndOutboundIsPartialAllocated()
    {
        await using var db = NewDb();
        SeedMaterialOutbound(db);
        var notifier = new Mock<IMaterialShortageNotifier>();
        notifier
            .Setup(x => x.NotifyAsync("WO-SHORT-001", "MAT-SHORT-001", 100m))
            .Returns(Task.CompletedTask);

        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        var shortage = new MaterialShortageService(db);
        var outbound = new OutboundService(
            db,
            seq,
            stock,
            shortage: shortage,
            shortageNotifier: notifier.Object);

        await outbound.AllocateAsync("OUT-SHORT-001", User);

        var savedShortage = await db.MaterialShortages.AsNoTracking().SingleAsync();
        Assert.Equal(MaterialShortageStatus.Open, savedShortage.Status);
        Assert.Equal("WO-SHORT-001", savedShortage.WorkOrderNo);
        Assert.Equal("OUT-SHORT-001", savedShortage.RelatedOutboundNo);
        Assert.Equal("MAT-SHORT-001", savedShortage.ProductCd);
        Assert.Equal(100m, savedShortage.RequiredQty);
        Assert.Equal(0m, savedShortage.AvailableQty);

        var header = await db.OutboundOrders.AsNoTracking().SingleAsync(x => x.OutboundNo == "OUT-SHORT-001");
        Assert.Equal(OutboundOrderStatus.PartialAllocated, header.Status);

        var detail = await db.OutboundOrderDetails.AsNoTracking().SingleAsync(x => x.OutboundNo == "OUT-SHORT-001");
        Assert.Equal(0m, detail.AllocatedQty);

        notifier.Verify(
            x => x.NotifyAsync("WO-SHORT-001", "MAT-SHORT-001", 100m),
            Times.Once);
    }

    private static void SeedMaterialOutbound(CP6Context db)
    {
        db.Warehouses.Add(new Warehouse
        {
            WarehouseCd = WarehouseCd,
            WarehouseName = "Raw material warehouse",
            WarehouseType = WarehouseType.RawMaterial,
            AllowNegative = false,
        });
        db.WorkOrders.Add(new WorkOrder
        {
            WorkOrderNo = "WO-SHORT-001",
            ProductCd = "FG-SHORT-001",
            ProductionQty = 10m,
            Status = WorkOrderStatus.Issued,
        });
        db.OutboundOrders.Add(new OutboundOrder
        {
            OutboundNo = "OUT-SHORT-001",
            OutboundType = OutboundType.Material,
            WorkOrderNo = "WO-SHORT-001",
            WarehouseCd = WarehouseCd,
            Status = OutboundOrderStatus.Confirmed,
            PlannedDate = DateTime.Today,
        });
        db.OutboundOrderDetails.Add(new OutboundOrderDetail
        {
            OutboundNo = "OUT-SHORT-001",
            LineNo = 1,
            ProductCd = "MAT-SHORT-001",
            RequiredQty = 100m,
            UnitCd = "EA",
        });
        db.Stocks.Add(new Stock
        {
            WarehouseCd = WarehouseCd,
            LocationCd = "RM01",
            ProductCd = "MAT-SHORT-001",
            LotNo = "LOT-ZERO",
            PhysicalQty = 0m,
            AllocatedQty = 0m,
            AvailableQty = 0m,
            UnitCd = "EA",
            QcStatus = StockQcStatus.Passed,
        });
        db.SaveChanges();
    }
}
