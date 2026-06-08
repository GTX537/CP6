using CP6.Core.EFDbContext;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace CP6.Tests;

public class Outbound_ShortageBackflowTests
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
    public async Task Allocate_MaterialOutbound_InsufficientStock_WritesShortage_DoesNotThrow()
    {
        using var db = NewDb();
        SeedWarehouse(db);
        SeedOutbound(db, "OUT-MAT-001", OutboundType.Material, "WO-MAT-001");
        db.Stocks.Add(new Stock
        {
            WarehouseCd = "W01",
            LocationCd = "L01",
            ProductCd = "MAT-001",
            LotNo = "LOT-0",
            PhysicalQty = 0m,
            AvailableQty = 0m,
        });
        await db.SaveChangesAsync();

        var notifier = new Mock<IMaterialShortageNotifier>();
        var svc = NewOutboundService(db, notifier);

        await svc.AllocateAsync("OUT-MAT-001", "u");

        var shortage = await db.MaterialShortages.SingleAsync();
        Assert.Equal(MaterialShortageStatus.Open, shortage.Status);
        Assert.Equal("WO-MAT-001", shortage.WorkOrderNo);
        Assert.Equal("OUT-MAT-001", shortage.RelatedOutboundNo);
        Assert.Equal("MAT-001", shortage.ProductCd);
        Assert.Equal(100m, shortage.RequiredQty);
        Assert.Equal(0m, shortage.AvailableQty);
        Assert.Equal(OutboundOrderStatus.PartialAllocated,
            (await db.OutboundOrders.SingleAsync(x => x.OutboundNo == "OUT-MAT-001")).Status);
        notifier.Verify(x => x.NotifyAsync("WO-MAT-001", "MAT-001", 100m), Times.Once);
    }

    [Fact]
    public async Task Allocate_ShippingOutbound_InsufficientStock_StillThrows_NoShortageWritten()
    {
        using var db = NewDb();
        SeedWarehouse(db);
        SeedOutbound(db, "OUT-SHP-001", OutboundType.Shipping, null);
        db.Stocks.Add(new Stock
        {
            WarehouseCd = "W01",
            LocationCd = "L01",
            ProductCd = "MAT-001",
            LotNo = "LOT-0",
            PhysicalQty = 0m,
            AvailableQty = 0m,
        });
        await db.SaveChangesAsync();

        var notifier = new Mock<IMaterialShortageNotifier>();
        var svc = NewOutboundService(db, notifier);

        await Assert.ThrowsAsync<InsufficientStockException>(() => svc.AllocateAsync("OUT-SHP-001", "u"));
        Assert.Equal(0, await db.MaterialShortages.CountAsync());
        notifier.Verify(x => x.NotifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>()), Times.Never);
    }

    private static OutboundService NewOutboundService(CP6Context db, Mock<IMaterialShortageNotifier> notifier)
    {
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        var shortage = new MaterialShortageService(db);
        return new OutboundService(db, seq, stock, shortage: shortage, shortageNotifier: notifier.Object);
    }

    private static void SeedWarehouse(CP6Context db)
    {
        db.Warehouses.Add(new Warehouse
        {
            WarehouseCd = "W01",
            WarehouseName = "Main",
            WarehouseType = WarehouseType.RawMaterial,
        });
    }

    private static void SeedOutbound(CP6Context db, string outboundNo, int outboundType, string? workOrderNo)
    {
        db.OutboundOrders.Add(new OutboundOrder
        {
            OutboundNo = outboundNo,
            OutboundType = outboundType,
            WorkOrderNo = workOrderNo,
            WarehouseCd = "W01",
            Status = OutboundOrderStatus.Confirmed,
        });
        db.OutboundOrderDetails.Add(new OutboundOrderDetail
        {
            OutboundNo = outboundNo,
            LineNo = 1,
            ProductCd = "MAT-001",
            RequiredQty = 100m,
        });
    }
}
