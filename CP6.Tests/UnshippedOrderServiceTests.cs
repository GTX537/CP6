using CP6.Core.EFDbContext;
using CP6.Core.Services;
using CP6.Entity.DomainModels;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class UnshippedOrderServiceTests
{
    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    private static UnshippedOrderService NewService(CP6Context db) => new(db);

    private static Order SeedOrder(
        CP6Context db,
        string webOrderNo,
        string customerCd = "C001",
        string orderStatus = OrderLifecycleStatus.Confirmed,
        int shipStatus = 0,
        DateTime? deliveryDate = null,
        DateTime? orderDate = null,
        bool isDeleted = false)
    {
        var order = new Order
        {
            WebOrderNo = webOrderNo,
            CustomerCd = customerCd,
            OrderType = "01",
            OrderDate = orderDate ?? DateTime.Today,
            CustomerDeliveryDate = deliveryDate ?? DateTime.Today.AddDays(1),
            OrderStatus = orderStatus,
            ShipStatus = shipStatus,
            IsDeleted = isDeleted,
        };
        db.Orders.Add(order);
        return order;
    }

    private static void SeedDetail(
        CP6Context db,
        string webOrderNo,
        int detailNo = 1,
        decimal quantity = 10m,
        decimal? shippedQty = 0m)
    {
        db.OrderDetails.Add(new OrderDetail
        {
            WebOrderNo = webOrderNo,
            WebOrderDetailNo = detailNo,
            ProductCd = $"P{detailNo:D3}",
            Quantity = quantity,
            ShippedQty = shippedQty,
        });
    }

    [Fact]
    public async Task Search_FullyShipped_IsExcluded()
    {
        using var db = NewDb();
        SeedOrder(db, "WEB-001", orderStatus: OrderLifecycleStatus.Shipped, shipStatus: 9);
        await db.SaveChangesAsync();

        var result = await NewService(db).SearchAsync(new UnshippedOrderQuery());

        Assert.Empty(result.Items);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task Search_Cancelled_IsExcluded()
    {
        using var db = NewDb();
        SeedOrder(db, "WEB-001", orderStatus: OrderLifecycleStatus.Cancelled, shipStatus: 0);
        await db.SaveChangesAsync();

        var result = await NewService(db).SearchAsync(new UnshippedOrderQuery());

        Assert.Empty(result.Items);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task Search_PartiallyCancelled_IsIncluded()
    {
        using var db = NewDb();
        SeedOrder(db, "WEB-001", orderStatus: OrderLifecycleStatus.PartiallyCancelled, shipStatus: 0);
        await db.SaveChangesAsync();

        var result = await NewService(db).SearchAsync(new UnshippedOrderQuery());

        var item = Assert.Single(result.Items);
        Assert.Equal("WEB-001", item.WebOrderNo);
        Assert.Equal(OrderLifecycleStatus.PartiallyCancelled, item.OrderStatus);
    }

    [Fact]
    public async Task Search_FilterByCustomer_OnlyReturnsThatCustomer()
    {
        using var db = NewDb();
        SeedOrder(db, "WEB-001", customerCd: "C001");
        SeedOrder(db, "WEB-002", customerCd: "C002");
        SeedOrder(db, "WEB-003", customerCd: "C003");
        await db.SaveChangesAsync();

        var result = await NewService(db).SearchAsync(new UnshippedOrderQuery { CustomerCd = "C002" });

        var item = Assert.Single(result.Items);
        Assert.Equal("WEB-002", item.WebOrderNo);
        Assert.Equal("C002", item.CustomerCd);
    }

    [Fact]
    public async Task Search_OnlyOverdue_FiltersByCustomerDeliveryDate()
    {
        using var db = NewDb();
        SeedOrder(db, "WEB-OLD", deliveryDate: DateTime.Today.AddDays(-1));
        SeedOrder(db, "WEB-NEW", deliveryDate: DateTime.Today.AddDays(1));
        await db.SaveChangesAsync();

        var result = await NewService(db).SearchAsync(new UnshippedOrderQuery { OnlyOverdue = true });

        var item = Assert.Single(result.Items);
        Assert.Equal("WEB-OLD", item.WebOrderNo);
        Assert.True(item.IsOverdue);
        Assert.Equal(-1, item.DaysUntilDue);
    }

    [Fact]
    public async Task Search_AggregatesMesAndWmsStatus()
    {
        using var db = NewDb();
        SeedOrder(db, "WEB-001");
        db.WorkOrders.AddRange(
            new WorkOrder
            {
                WorkOrderNo = "MO-001",
                WebOrderNo = "WEB-001",
                ProductCd = "P001",
                Status = WorkOrderStatus.Issued,
            },
            new WorkOrder
            {
                WorkOrderNo = "MO-002",
                WebOrderNo = "WEB-001",
                ProductCd = "P002",
                Status = WorkOrderStatus.InProgress,
            });
        db.OutboundOrders.Add(new OutboundOrder
        {
            OutboundNo = "OUT-001",
            WebOrderNo = "WEB-001",
            WarehouseCd = "W01",
            OutboundType = OutboundType.Shipping,
            Status = OutboundOrderStatus.Allocated,
        });
        await db.SaveChangesAsync();

        var result = await NewService(db).SearchAsync(new UnshippedOrderQuery());

        var item = Assert.Single(result.Items);
        Assert.Contains("Issued", item.MesStatusSummary);
        Assert.Contains("InProgress", item.MesStatusSummary);
        Assert.Contains("Allocated", item.WmsStatusSummary);
    }

    [Fact]
    public async Task Search_RemainingQty_IsCalculated()
    {
        using var db = NewDb();
        SeedOrder(db, "WEB-001");
        SeedDetail(db, "WEB-001", quantity: 100m, shippedQty: 30m);
        await db.SaveChangesAsync();

        var result = await NewService(db).SearchAsync(new UnshippedOrderQuery());

        var item = Assert.Single(result.Items);
        Assert.Equal(100m, item.OrderedQty);
        Assert.Equal(30m, item.ShippedQty);
        Assert.Equal(70m, item.RemainingQty);
    }

    [Fact]
    public async Task Search_NoBusinessPartner_FallsBackToCustomerCd()
    {
        using var db = NewDb();
        SeedOrder(db, "WEB-001", customerCd: "C001");
        await db.SaveChangesAsync();

        var result = await NewService(db).SearchAsync(new UnshippedOrderQuery());

        var item = Assert.Single(result.Items);
        Assert.Equal("C001", item.CustomerName);
    }

    [Fact]
    public async Task Search_Pagination_RespectsPageAndPageSize()
    {
        using var db = NewDb();
        for (var i = 1; i <= 25; i++)
        {
            SeedOrder(db, $"WEB-{i:D3}", deliveryDate: DateTime.Today.AddDays(i));
        }
        await db.SaveChangesAsync();

        var result = await NewService(db).SearchAsync(new UnshippedOrderQuery
        {
            Page = 2,
            PageSize = 10,
        });

        Assert.Equal(25, result.Total);
        Assert.Equal(2, result.PageIndex);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(10, result.Items.Count);
        Assert.Equal("WEB-011", result.Items[0].WebOrderNo);
    }
}
