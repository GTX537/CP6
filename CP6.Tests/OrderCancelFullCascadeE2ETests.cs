using CP6.Core.EFDbContext;
using CP6.Core.Services;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs;
using CP6.Entity.DTOs.Mes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CP6.Tests;

/// <summary>
/// Phase 6 order cancel full-cascade E2E tests.
/// </summary>
public class OrderCancelFullCascadeE2ETests
{
    private const string Wh = "W01";
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
            BpCd = "C001",
            BpName = "Customer 001",
            BaseCd = "B01",
            CustomerFlg = true,
            Creator = "seed",
            CreateDate = DateTime.Now,
        });

        SeedProduct(db, "FG-CANCEL-A", "MAT-CANCEL-A");
        SeedProduct(db, "FG-CANCEL-B", "MAT-CANCEL-B");
        await db.SaveChangesAsync();

        var wmsSeq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, wmsSeq);
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN,
            WarehouseCd = Wh,
            LocationCd = "RM-A",
            ProductCd = "MAT-CANCEL-A",
            LotNo = "LOT-A",
            Qty = 100m,
            UnitCd = "EA",
            OperatorCd = "seed",
        });
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN,
            WarehouseCd = Wh,
            LocationCd = "RM-B",
            ProductCd = "MAT-CANCEL-B",
            LotNo = "LOT-B",
            Qty = 100m,
            UnitCd = "EA",
            OperatorCd = "seed",
        });

        var inbound = new InboundService(db, wmsSeq, stock);
        var outbound = new OutboundService(db, wmsSeq, stock);
        var wmsBridge = new WmsBridgeHook(db, outbound, inbound, NullLogger<WmsBridgeHook>.Instance);

        var mesSeq = new MesSequenceService(db);
        var woService = new WorkOrderService(db, mesSeq, wmsBridge);
        var mesBridge = new MesBridgeHook(db, woService, NullLogger<MesBridgeHook>.Instance);
        var cancelBridge = new OrderCancelBridgeHook(db, woService, outbound, NullLogger<OrderCancelBridgeHook>.Instance);

        return new OrderService(
            db,
            new Mock<IPowerEggWorkflowService>().Object,
            wmsBridge,
            mesBridge,
            cancelBridge);
    }

    private static void SeedProduct(CP6Context db, string productCd, string materialCd)
    {
        db.ProductMasters.Add(new ProductMaster
        {
            ProductCd = productCd,
            ItemCd = productCd,
            CustomerCd = "C001",
            SetProductCd = productCd,
            SetProductName = productCd,
            CustomerItemName1 = productCd,
            CpItemName1 = productCd,
            QtyUnit = "EA",
            UnitPriceUnit = "EA",
            Creator = "seed",
            CreateDate = DateTime.Now,
        });
        db.ProductProcesses.Add(new ProductProcess
        {
            ProductCd = productCd,
            TaskCd = "TASK01",
            ProcessCd = "P01",
            Spec01 = "Cut",
            SortOrder = 1,
            Creator = "seed",
            CreateDate = DateTime.Now,
        });
        db.ProductMaterials.Add(new ProductMaterial
        {
            ProductCd = productCd,
            ProcessCd = "P01",
            MaterialCd = materialCd,
            MaterialTypeDiv = "3",
            ItemCd = materialCd,
            SortOrder = 1,
            Creator = "seed",
            CreateDate = DateTime.Now,
        });
    }

    private static OrderDto NewOrder()
    {
        return new OrderDto
        {
            CustomerCd = "C001",
            OrderType = "20",
            OrderDate = DateTime.Today,
            CustomerDeliveryDate = DateTime.Today.AddDays(14),
            Quantity = 20m,
            Details =
            {
                NewDetail("FG-CANCEL-A", 10m),
                NewDetail("FG-CANCEL-B", 10m),
            },
        };
    }

    private static OrderDetailDto NewDetail(string productCd, decimal qty)
    {
        return new OrderDetailDto
        {
            ProductCd = productCd,
            ItemCd = productCd,
            CustomerItemName1 = productCd,
            CpItemName1 = productCd,
            Quantity = qty,
            QtyUnit = "EA",
            UnitPriceUnit = "EA",
            IndividualUnitPrice = 1m,
            CustomerDeliveryDate = DateTime.Today.AddDays(14),
        };
    }

    private static async Task<IReadOnlyList<string>> CreateAndIssueWorkOrdersAsync(CP6Context db, OrderService orderService)
    {
        var webOrderNo = await orderService.CreateAsync(NewOrder(), User);

        var woNos = await db.WorkOrders.AsNoTracking()
            .Where(w => w.WebOrderNo == webOrderNo)
            .OrderBy(w => w.WorkOrderNo)
            .Select(w => w.WorkOrderNo)
            .ToListAsync();

        Assert.Equal(2, woNos.Count);

        var wmsSeq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, wmsSeq);
        var outbound = new OutboundService(db, wmsSeq, stock);
        var wmsBridge = new WmsBridgeHook(
            db,
            outbound,
            new InboundService(db, wmsSeq, stock),
            NullLogger<WmsBridgeHook>.Instance);
        var woService = new WorkOrderService(db, new MesSequenceService(db), wmsBridge);

        foreach (var woNo in woNos)
        {
            await woService.IssueAsync(woNo, User);
        }

        return woNos;
    }

    [Fact]
    public async Task OrderCancel_FullCascade_Cancelled_AllStatusesUpdated()
    {
        await using var db = NewDb();
        var orderService = await NewServiceGraphAsync(db);
        var woNos = await CreateAndIssueWorkOrdersAsync(db, orderService);
        var webOrderNo = await db.WorkOrders.AsNoTracking()
            .Where(w => woNos.Contains(w.WorkOrderNo))
            .Select(w => w.WebOrderNo!)
            .FirstAsync();

        var wmsSeq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, wmsSeq);
        var outbound = new OutboundService(db, wmsSeq, stock);
        var materialOutbounds = await db.OutboundOrders.AsNoTracking()
            .Where(o => woNos.Contains(o.WorkOrderNo!) && o.OutboundType == OutboundType.Material)
            .OrderBy(o => o.OutboundNo)
            .Select(o => o.OutboundNo)
            .ToListAsync();

        foreach (var outboundNo in materialOutbounds)
        {
            await outbound.ConfirmOrderAsync(outboundNo, User);
            await outbound.AllocateAsync(outboundNo, User);
        }

        Assert.All(await db.Stocks.AsNoTracking().ToListAsync(), s =>
            Assert.Equal(s.ProductCd.StartsWith("MAT-CANCEL-") ? 10m : 0m, s.AllocatedQty));

        var before = DateTime.Now.AddSeconds(-10);
        var result = await orderService.CancelAsync(webOrderNo, "Customer changed mind", force: true, User);
        var after = DateTime.Now.AddSeconds(10);

        Assert.Equal(CancelOutcome.Cancelled, result.Outcome);

        var order = await db.Orders.AsNoTracking().SingleAsync(o => o.WebOrderNo == webOrderNo);
        Assert.Equal(OrderLifecycleStatus.Cancelled, order.OrderStatus);
        Assert.Equal("Customer changed mind", order.CancelReason);
        Assert.NotNull(order.CancelledAt);
        Assert.InRange(order.CancelledAt!.Value, before, after);

        var workOrders = await db.WorkOrders.AsNoTracking()
            .Where(w => w.WebOrderNo == webOrderNo)
            .ToListAsync();
        Assert.All(workOrders, w => Assert.Equal(WorkOrderStatus.Cancelled, w.Status));

        var outbounds = await db.OutboundOrders.AsNoTracking()
            .Where(o => o.WebOrderNo == webOrderNo || woNos.Contains(o.WorkOrderNo!))
            .ToListAsync();
        Assert.All(outbounds, o => Assert.Equal(OutboundOrderStatus.Cancelled, o.Status));

        var stocks = await db.Stocks.AsNoTracking()
            .Where(s => s.ProductCd.StartsWith("MAT-CANCEL-"))
            .ToListAsync();
        Assert.All(stocks, s => Assert.Equal(0m, s.AllocatedQty));

        Assert.True(await db.IntegrationEvents.CountAsync() >= 4);
    }

    [Fact]
    public async Task OrderCancel_FullCascade_ForceFalse_AutoCancelsWhenAllRelatedItemsAreCancellable()
    {
        await using var db = NewDb();
        var orderService = await NewServiceGraphAsync(db);
        var woNos = await CreateAndIssueWorkOrdersAsync(db, orderService);
        var webOrderNo = await db.WorkOrders.AsNoTracking()
            .Where(w => woNos.Contains(w.WorkOrderNo))
            .Select(w => w.WebOrderNo!)
            .FirstAsync();

        var result = await orderService.CancelAsync(webOrderNo, "Customer changed mind", force: false, User);

        Assert.Equal(CancelOutcome.Cancelled, result.Outcome);
        Assert.Equal(OrderLifecycleStatus.Cancelled,
            (await db.Orders.AsNoTracking().SingleAsync(o => o.WebOrderNo == webOrderNo)).OrderStatus);
        Assert.All(await db.WorkOrders.AsNoTracking().Where(w => w.WebOrderNo == webOrderNo).ToListAsync(),
            w => Assert.Equal(WorkOrderStatus.Cancelled, w.Status));
        Assert.All(await db.OutboundOrders.AsNoTracking()
                .Where(o => o.WebOrderNo == webOrderNo || woNos.Contains(o.WorkOrderNo!)).ToListAsync(),
            o => Assert.Equal(OutboundOrderStatus.Cancelled, o.Status));
        Assert.True(await db.IntegrationEvents.CountAsync() >= 4);
    }
}
