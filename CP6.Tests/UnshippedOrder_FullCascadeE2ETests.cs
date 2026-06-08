using CP6.Core.EFDbContext;
using CP6.Core.Services;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CP6.Tests;

public class UnshippedOrder_FullCascadeE2ETests
{
    private const string Wh = "W01";
    private const string CustomerCd = "C001";
    private const string CustomerName = "Customer 001";
    private const string ProductCd = "FG-UNSHIP-E2E";
    private const string MaterialCd = "MAT-UNSHIP-E2E";
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

    private static async Task<OrderService> NewServiceGraphAsync(CP6Context db)
    {
        db.BusinessPartners.Add(new BusinessPartner
        {
            BpCd = CustomerCd,
            BpName = CustomerName,
            BaseCd = "B01",
            CustomerFlg = true,
            Creator = "seed",
            CreateDate = DateTime.Now,
        });
        SeedProduct(db);
        await db.SaveChangesAsync();

        var wmsSeq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, wmsSeq);
        var inbound = new InboundService(db, wmsSeq, stock);
        var outbound = new OutboundService(db, wmsSeq, stock);
        var wmsBridge = new WmsBridgeHook(db, outbound, inbound, NullLogger<WmsBridgeHook>.Instance);

        var mesSeq = new MesSequenceService(db);
        var workOrders = new WorkOrderService(db, mesSeq, wmsBridge);
        var mesBridge = new MesBridgeHook(db, workOrders, NullLogger<MesBridgeHook>.Instance);

        return new OrderService(
            db,
            new Mock<IPowerEggWorkflowService>().Object,
            wmsBridge,
            mesBridge);
    }

    private static void SeedProduct(CP6Context db)
    {
        db.ProductMasters.Add(new ProductMaster
        {
            ProductCd = ProductCd,
            ItemCd = ProductCd,
            CustomerCd = CustomerCd,
            SetProductCd = ProductCd,
            SetProductName = ProductCd,
            CustomerItemName1 = ProductCd,
            CpItemName1 = ProductCd,
            QtyUnit = "EA",
            UnitPriceUnit = "EA",
            Creator = "seed",
            CreateDate = DateTime.Now,
        });
        db.ProductProcesses.Add(new ProductProcess
        {
            ProductCd = ProductCd,
            TaskCd = "TASK01",
            ProcessCd = "P01",
            Spec01 = "Cut",
            SortOrder = 1,
            Creator = "seed",
            CreateDate = DateTime.Now,
        });
        db.ProductMaterials.Add(new ProductMaterial
        {
            ProductCd = ProductCd,
            ProcessCd = "P01",
            MaterialCd = MaterialCd,
            MaterialTypeDiv = "3",
            ItemCd = MaterialCd,
            SortOrder = 1,
            Creator = "seed",
            CreateDate = DateTime.Now,
        });
    }

    private static OrderDto NewOrder() => new()
    {
        CustomerCd = CustomerCd,
        OrderType = "20",
        OrderDate = DateTime.Today,
        CustomerDeliveryDate = DateTime.Today.AddDays(14),
        Quantity = 50m,
        Details =
        {
            new OrderDetailDto
            {
                ProductCd = ProductCd,
                ItemCd = ProductCd,
                CustomerItemName1 = ProductCd,
                CpItemName1 = ProductCd,
                Quantity = 50m,
                QtyUnit = "EA",
                UnitPriceUnit = "EA",
                IndividualUnitPrice = 1m,
                CustomerDeliveryDate = DateTime.Today.AddDays(14),
            },
        },
    };

    [Fact]
    public async Task Search_AfterOrderCreateAndWorkOrderIssue_ReturnsUnshippedOrderWithMesAndWmsStatus()
    {
        await using var db = NewDb();
        var orderService = await NewServiceGraphAsync(db);

        var webOrderNo = await orderService.CreateAsync(NewOrder(), User);

        var workOrderNo = await db.WorkOrders.AsNoTracking()
            .Where(w => w.WebOrderNo == webOrderNo)
            .Select(w => w.WorkOrderNo)
            .SingleAsync();

        var wmsSeq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, wmsSeq);
        var wmsBridge = new WmsBridgeHook(
            db,
            new OutboundService(db, wmsSeq, stock),
            new InboundService(db, wmsSeq, stock),
            NullLogger<WmsBridgeHook>.Instance);
        var workOrderService = new WorkOrderService(db, new MesSequenceService(db), wmsBridge);

        await workOrderService.IssueAsync(workOrderNo, User);

        var result = await new UnshippedOrderService(db).SearchAsync(new UnshippedOrderQuery());

        var item = Assert.Single(result.Items);
        Assert.Equal(1, result.Total);
        Assert.Equal(webOrderNo, item.WebOrderNo);
        Assert.Equal(CustomerCd, item.CustomerCd);
        Assert.Equal(CustomerName, item.CustomerName);
        Assert.Equal(OrderLifecycleStatus.Confirmed, item.OrderStatus);
        Assert.Equal(50m, item.OrderedQty);
        Assert.Equal(0m, item.ShippedQty);
        Assert.Equal(50m, item.RemainingQty);
        Assert.False(item.IsOverdue);
        Assert.Contains("Issued", item.MesStatusSummary);
        Assert.Contains("Draft", item.WmsStatusSummary);

        Assert.Equal(WorkOrderStatus.Issued,
            (await db.WorkOrders.AsNoTracking().SingleAsync(w => w.WorkOrderNo == workOrderNo)).Status);
        Assert.True(await db.OutboundOrders.AsNoTracking()
            .AnyAsync(o => o.WebOrderNo == webOrderNo
                && o.OutboundType == OutboundType.Shipping
                && o.Status == OutboundOrderStatus.Draft));
        Assert.True(await db.OutboundOrders.AsNoTracking()
            .AnyAsync(o => o.WorkOrderNo == workOrderNo
                && o.OutboundType == OutboundType.Material
                && o.Status == OutboundOrderStatus.Draft));
    }
}
