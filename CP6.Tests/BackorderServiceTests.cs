using CP6.Core.EFDbContext;
using CP6.Core.Services;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class BackorderServiceTests
{
    private const string User = "tester";

    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    [Fact]
    public async Task Queue_FiltersOnlyOpenOrdersWithRemaining()
    {
        await using var db = NewDb();
        SeedCustomer(db);
        SeedOrderWithDetail(db, "WEB-BO-001", OrderLifecycleStatus.Confirmed, 100m, 40m, null);
        SeedOrderWithDetail(db, "WEB-BO-002", OrderLifecycleStatus.Confirmed, 100m, 100m, null);
        SeedOrderWithDetail(db, "WEB-BO-003", OrderLifecycleStatus.Cancelled, 100m, 20m, null);
        SeedOrderWithDetail(db, "WEB-BO-004", OrderLifecycleStatus.InProduction, 100m, 40m, 60m);
        SeedOrderWithDetail(db, "WEB-BO-005", OrderLifecycleStatus.PartiallyCancelled, 80m, 20m, 10m);
        await db.SaveChangesAsync();

        var rows = await new BackorderService(db).GetQueueAsync(new BackorderQueueQuery());

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, x => x.WebOrderNo == "WEB-BO-001" && x.RemainingQty == 60m);
        Assert.Contains(rows, x => x.WebOrderNo == "WEB-BO-005" && x.RemainingQty == 50m);
        Assert.DoesNotContain(rows, x => x.WebOrderNo == "WEB-BO-002");
        Assert.DoesNotContain(rows, x => x.WebOrderNo == "WEB-BO-003");
        Assert.DoesNotContain(rows, x => x.WebOrderNo == "WEB-BO-004");
    }

    [Fact]
    public async Task CloseRemaining_SetsBackorderQty_AndShipStatus9()
    {
        await using var db = NewDb();
        SeedCustomer(db);
        SeedOrderWithDetail(db, "WEB-BO-CLOSE", OrderLifecycleStatus.Confirmed, 100m, 40m, null);
        await db.SaveChangesAsync();

        var result = await new BackorderService(db).CloseRemainingAsync(
            "WEB-BO-CLOSE",
            1,
            new BackorderActionRequest { Reason = "customer forfeits remainder" },
            User);

        Assert.Equal("WEB-BO-CLOSE", result.WebOrderNo);
        Assert.Equal(60m, result.BackorderQty);
        Assert.Equal(0m, result.RemainingQty);

        var detail = await db.OrderDetails.AsNoTracking().SingleAsync(x => x.WebOrderNo == "WEB-BO-CLOSE");
        Assert.Equal(60m, detail.BackorderQty);
        Assert.Equal(100m, detail.ShippedQty);
        Assert.Equal(9, detail.ShipStatus);
        Assert.Contains("customer forfeits remainder", detail.SlipNote);
    }

    [Fact]
    public async Task SplitToNewOrder_CreatesNewOrderWithRemainingQty()
    {
        await using var db = NewDb();
        SeedCustomer(db);
        SeedOrderWithDetail(db, "WEB-BO-SPLIT", OrderLifecycleStatus.Confirmed, 100m, 40m, null);
        await db.SaveChangesAsync();

        var result = await new BackorderService(db).SplitToNewOrderAsync(
            "WEB-BO-SPLIT",
            1,
            new BackorderActionRequest { Reason = "create backorder" },
            User);

        Assert.NotEqual("WEB-BO-SPLIT", result.NewWebOrderNo);
        Assert.StartsWith($"ORD{DateTime.Today:yyyyMM}", result.NewWebOrderNo);

        var newDetail = await db.OrderDetails.AsNoTracking().SingleAsync(x => x.WebOrderNo == result.NewWebOrderNo);
        Assert.Equal(1, newDetail.WebOrderDetailNo);
        Assert.Equal(60m, newDetail.Quantity);
        Assert.Equal("PROD-BO", newDetail.ProductCd);

        var parentDetail = await db.OrderDetails.AsNoTracking().SingleAsync(x => x.WebOrderNo == "WEB-BO-SPLIT");
        Assert.Equal(60m, parentDetail.BackorderQty);
        Assert.Equal(100m, parentDetail.ShippedQty);
        Assert.Equal(9, parentDetail.ShipStatus);
    }

    [Fact]
    public async Task SplitToNewOrder_CopiesHeaderFields()
    {
        await using var db = NewDb();
        SeedCustomer(db);
        SeedOrderWithDetail(db, "WEB-BO-COPY", OrderLifecycleStatus.InProduction, 120m, 30m, null);
        await db.SaveChangesAsync();

        var result = await new BackorderService(db).SplitToNewOrderAsync(
            "WEB-BO-COPY",
            1,
            new BackorderActionRequest { Reason = "new delivery" },
            User);

        var newOrder = await db.Orders.AsNoTracking().SingleAsync(x => x.WebOrderNo == result.NewWebOrderNo);
        Assert.Equal("C-BO", newOrder.CustomerCd);
        Assert.Equal("01", newOrder.OrderType);
        Assert.Equal("D01", newOrder.OrderDepartment);
        Assert.Equal(DateTime.Today, newOrder.OrderDate);
        Assert.Equal(OrderLifecycleStatus.Confirmed, newOrder.OrderStatus);
        Assert.Equal("BO from WEB-BO-COPY", newOrder.Memo1);
    }

    [Fact]
    public async Task OperationOnAlreadyClosedDetail_Throws()
    {
        await using var db = NewDb();
        SeedCustomer(db);
        SeedOrderWithDetail(db, "WEB-BO-CLOSED", OrderLifecycleStatus.Confirmed, 100m, 40m, 60m);
        await db.SaveChangesAsync();

        var service = new BackorderService(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CloseRemainingAsync(
            "WEB-BO-CLOSED",
            1,
            new BackorderActionRequest { Reason = "again" },
            User));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SplitToNewOrderAsync(
            "WEB-BO-CLOSED",
            1,
            new BackorderActionRequest { Reason = "again" },
            User));
    }

    private static void SeedCustomer(CP6Context db)
    {
        db.BusinessPartners.Add(new BusinessPartner
        {
            BpCd = "C-BO",
            BpName = "Backorder Customer",
            BaseCd = "B01",
            CustomerFlg = true,
        });
    }

    private static void SeedOrderWithDetail(
        CP6Context db,
        string webOrderNo,
        string orderStatus,
        decimal quantity,
        decimal shippedQty,
        decimal? backorderQty)
    {
        db.Orders.Add(new Order
        {
            WebOrderNo = webOrderNo,
            CustomerCd = "C-BO",
            OrderType = "01",
            OrderDepartment = "D01",
            OrderDate = new DateTime(2026, 6, 6),
            CustomerDeliveryDate = new DateTime(2026, 6, 20),
            OrderSheetNo = "SHEET-BO",
            Carrier = "CAR-BO",
            ShipCondition = "STD",
            SalesPriceDiv = "1",
            Quantity = quantity,
            OrderStatus = orderStatus,
            Status = 1,
            Memo1 = "parent memo",
        });
        db.OrderDetails.Add(new OrderDetail
        {
            WebOrderNo = webOrderNo,
            WebOrderDetailNo = 1,
            HaibaiNo1 = $"{webOrderNo}-001",
            HaibaiNo2 = "C-BO",
            ProductCd = "PROD-BO",
            ProductCatBig = "A",
            ProductCatMid = "B",
            ProductCatSml = "C",
            CustomerItemName1 = "Backorder product",
            QtyUnit = "EA",
            Quantity = quantity,
            ShippedQty = shippedQty,
            BackorderQty = backorderQty,
            ShipStatus = shippedQty > 0 ? 5 : 0,
            LastShipDate = new DateTime(2026, 6, 7),
            LastOutboundNo = "OUT-BO",
            OrderType = "01",
            Amount = quantity * 12m,
            IndividualUnitPrice = 12m,
            SlipNote = "original slip",
        });
    }
}
