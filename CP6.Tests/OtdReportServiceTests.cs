using System.Text;
using CP6.Core.EFDbContext;
using CP6.Core.Services;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class OtdReportServiceTests
{
    private static readonly string Header = string.Join(",", new[]
    {
        "GroupKey",
        "GroupLabel",
        "TotalShippedOrders",
        "OnTimeCount",
        "LateCount",
        "OnTimeRate",
        "AvgLateDays",
    });

    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    private static OtdReportService NewService(CP6Context db) => new(db);

    [Fact]
    public async Task OtdSummary_GroupByCustomer_AggregatesCorrectly()
    {
        await using var db = NewDb();
        SeedCustomer(db, "C001", "Customer One");
        SeedCustomer(db, "C002", "Customer Two");
        SeedOrder(db, "WEB-C1-ON", "C001", new DateTime(2026, 1, 3), new DateTime(2026, 1, 10), new DateTime(2026, 1, 10));
        SeedOrder(db, "WEB-C1-LATE", "C001", new DateTime(2026, 1, 4), new DateTime(2026, 1, 10), new DateTime(2026, 1, 12));
        SeedOrder(db, "WEB-C2-ON", "C002", new DateTime(2026, 1, 5), new DateTime(2026, 1, 15), new DateTime(2026, 1, 14));
        SeedOrder(db, "WEB-C2-OPEN", "C002", new DateTime(2026, 1, 6), new DateTime(2026, 1, 20), null, shipStatus: 0);
        await db.SaveChangesAsync();

        var result = await NewService(db).GetSummaryAsync(new OtdReportQuery
        {
            GroupBy = OtdReportGroupBy.Customer,
            DateFrom = new DateTime(2026, 1, 1),
            DateTo = new DateTime(2026, 1, 31),
        });

        Assert.Equal(2, result.Rows.Count);
        var c1 = result.Rows.Single(x => x.GroupKey == "C001");
        var c2 = result.Rows.Single(x => x.GroupKey == "C002");
        Assert.Equal("Customer One", c1.GroupLabel);
        Assert.Equal(2, c1.TotalShippedOrders);
        Assert.Equal(1, c1.OnTimeCount);
        Assert.Equal(1, c1.LateCount);
        Assert.Equal(0.5m, c1.OnTimeRate);
        Assert.Equal(1, c2.TotalShippedOrders);
        Assert.Equal(1m, c2.OnTimeRate);
    }

    [Fact]
    public async Task OtdSummary_GroupByMonth_AggregatesCorrectly()
    {
        await using var db = NewDb();
        SeedCustomer(db, "C001", "Customer One");
        SeedOrder(db, "WEB-JAN-ON", "C001", new DateTime(2026, 1, 2), new DateTime(2026, 1, 8), new DateTime(2026, 1, 8));
        SeedOrder(db, "WEB-JAN-LATE", "C001", new DateTime(2026, 1, 12), new DateTime(2026, 1, 18), new DateTime(2026, 1, 21));
        SeedOrder(db, "WEB-FEB-ON", "C001", new DateTime(2026, 2, 3), new DateTime(2026, 2, 9), new DateTime(2026, 2, 7));
        await db.SaveChangesAsync();

        var result = await NewService(db).GetSummaryAsync(new OtdReportQuery
        {
            GroupBy = OtdReportGroupBy.Month,
            DateFrom = new DateTime(2026, 1, 1),
            DateTo = new DateTime(2026, 2, 28),
        });

        Assert.Equal(2, result.Rows.Count);
        var jan = result.Rows.Single(x => x.GroupKey == "202601");
        var feb = result.Rows.Single(x => x.GroupKey == "202602");
        Assert.Equal("2026-01", jan.GroupLabel);
        Assert.Equal(2, jan.TotalShippedOrders);
        Assert.Equal(1, jan.OnTimeCount);
        Assert.Equal(1, jan.LateCount);
        Assert.Equal(1, feb.TotalShippedOrders);
        Assert.Equal(1m, feb.OnTimeRate);
    }

    [Fact]
    public async Task OtdSummary_OnTimeRate_MathematicallyCorrect()
    {
        await using var db = NewDb();
        SeedCustomer(db, "C001", "Customer One");
        SeedOrder(db, "WEB-ON-1", "C001", new DateTime(2026, 3, 1), new DateTime(2026, 3, 10), new DateTime(2026, 3, 8));
        SeedOrder(db, "WEB-ON-2", "C001", new DateTime(2026, 3, 2), new DateTime(2026, 3, 10), new DateTime(2026, 3, 10));
        SeedOrder(db, "WEB-ON-3", "C001", new DateTime(2026, 3, 3), new DateTime(2026, 3, 12), new DateTime(2026, 3, 11));
        SeedOrder(db, "WEB-LATE-1", "C001", new DateTime(2026, 3, 4), new DateTime(2026, 3, 13), new DateTime(2026, 3, 14));
        SeedOrder(db, "WEB-LATE-2", "C001", new DateTime(2026, 3, 5), new DateTime(2026, 3, 15), new DateTime(2026, 3, 18));
        await db.SaveChangesAsync();

        var result = await NewService(db).GetSummaryAsync(new OtdReportQuery { GroupBy = OtdReportGroupBy.Customer });

        var row = Assert.Single(result.Rows);
        Assert.Equal(5, row.TotalShippedOrders);
        Assert.Equal(3, row.OnTimeCount);
        Assert.Equal(2, row.LateCount);
        Assert.Equal(0.6m, row.OnTimeRate);
    }

    [Fact]
    public async Task OtdSummary_AvgLateDays_OnlyConsidersLateOrders()
    {
        await using var db = NewDb();
        SeedCustomer(db, "C001", "Customer One");
        SeedOrder(db, "WEB-EARLY", "C001", new DateTime(2026, 4, 1), new DateTime(2026, 4, 20), new DateTime(2026, 4, 10));
        SeedOrder(db, "WEB-LATE-2", "C001", new DateTime(2026, 4, 2), new DateTime(2026, 4, 20), new DateTime(2026, 4, 22));
        SeedOrder(db, "WEB-LATE-6", "C001", new DateTime(2026, 4, 3), new DateTime(2026, 4, 20), new DateTime(2026, 4, 26));
        await db.SaveChangesAsync();

        var result = await NewService(db).GetSummaryAsync(new OtdReportQuery { GroupBy = OtdReportGroupBy.Customer });

        var row = Assert.Single(result.Rows);
        Assert.Equal(2, row.LateCount);
        Assert.Equal(4m, row.AvgLateDays);
    }

    [Fact]
    public async Task OtdExport_GeneratesCsv_WithBomAndHeader()
    {
        await using var db = NewDb();
        SeedCustomer(db, "C001", "Acme, Inc.");
        SeedOrder(db, "WEB-CSV", "C001", new DateTime(2026, 5, 1), new DateTime(2026, 5, 10), new DateTime(2026, 5, 11));
        await db.SaveChangesAsync();

        var bytes = await NewService(db).ExportCsvAsync(new OtdReportQuery { GroupBy = OtdReportGroupBy.Customer });

        Assert.True(bytes.Length >= 3);
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);
        var csv = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.StartsWith(Header + "\r\n", csv);
        Assert.Contains("C001,\"Acme, Inc.\",1,0,1,0,1", csv);
    }

    private static void SeedCustomer(CP6Context db, string customerCd, string customerName)
    {
        db.BusinessPartners.Add(new BusinessPartner
        {
            BpCd = customerCd,
            BpName = customerName,
            BaseCd = "B01",
            CustomerFlg = true,
        });
    }

    private static void SeedOrder(
        CP6Context db,
        string webOrderNo,
        string customerCd,
        DateTime orderDate,
        DateTime promiseDate,
        DateTime? actualDate,
        int shipStatus = 9)
    {
        db.Orders.Add(new Order
        {
            WebOrderNo = webOrderNo,
            CustomerCd = customerCd,
            OrderType = "01",
            OrderDate = orderDate,
            CustomerDeliveryDate = promiseDate,
            Quantity = 10m,
            ShipStatus = shipStatus,
            ActualShipDate = actualDate,
            OrderStatus = shipStatus >= 9 ? OrderLifecycleStatus.Shipped : OrderLifecycleStatus.Confirmed,
        });
        db.OrderDetails.Add(new OrderDetail
        {
            WebOrderNo = webOrderNo,
            WebOrderDetailNo = 1,
            ProductCd = $"P-{webOrderNo}",
            Quantity = 10m,
            ShippedQty = shipStatus >= 5 ? 10m : 0m,
            ShipStatus = shipStatus,
            LastShipDate = actualDate,
        });
    }
}
