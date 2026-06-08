using System.Text;
using CP6.Core.EFDbContext;
using CP6.Core.Services;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class UnshippedOrderCsvExportTests
{
    private static readonly string Header = string.Join(",", new[]
    {
        "WebOrderNo",
        "CustomerCd",
        "CustomerName",
        "OrderDate",
        "CustomerDeliveryDate",
        "OrderStatus",
        "OrderedQty",
        "ShippedQty",
        "RemainingQty",
        "IsOverdue",
        "MesStatusSummary",
        "WmsStatusSummary",
    });

    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    private static UnshippedOrderService NewService(CP6Context db) => new(db);

    private static void SeedPartner(CP6Context db, string customerCd, string customerName)
    {
        db.BusinessPartners.Add(new BusinessPartner
        {
            BpCd = customerCd,
            BpName = customerName,
            BaseCd = "B01",
            CustomerFlg = true,
            Creator = "seed",
            CreateDate = DateTime.Now,
        });
    }

    private static void SeedOrder(
        CP6Context db,
        string webOrderNo,
        string customerCd,
        DateTime orderDate,
        DateTime deliveryDate,
        decimal quantity = 10m,
        decimal? shippedQty = 0m)
    {
        db.Orders.Add(new Order
        {
            WebOrderNo = webOrderNo,
            CustomerCd = customerCd,
            OrderType = "01",
            OrderDate = orderDate,
            CustomerDeliveryDate = deliveryDate,
            OrderStatus = OrderLifecycleStatus.Confirmed,
            ShipStatus = 0,
        });
        db.OrderDetails.Add(new OrderDetail
        {
            WebOrderNo = webOrderNo,
            WebOrderDetailNo = 1,
            ProductCd = $"P-{webOrderNo}",
            Quantity = quantity,
            ShippedQty = shippedQty,
        });
    }

    private static string DecodeCsv(byte[] bytes)
    {
        Assert.True(bytes.Length >= 3);
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);
        return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
    }

    [Fact]
    public async Task Export_BasicRows_ProducesCorrectCsvShape()
    {
        await using var db = NewDb();
        SeedPartner(db, "C001", "Customer One");
        SeedPartner(db, "C002", "Customer Two");
        SeedPartner(db, "C003", "Customer Three");
        SeedOrder(db, "WEB-001", "C001", new DateTime(2026, 1, 1), DateTime.Today.AddDays(1), 10m, 0m);
        SeedOrder(db, "WEB-002", "C002", new DateTime(2026, 1, 2), DateTime.Today.AddDays(2), 20m, 5m);
        SeedOrder(db, "WEB-003", "C003", new DateTime(2026, 1, 3), DateTime.Today.AddDays(3), 30m, 10m);
        await db.SaveChangesAsync();

        var bytes = await NewService(db).ExportCsvAsync(new UnshippedOrderQuery { Page = 1, PageSize = 1 });

        var csv = DecodeCsv(bytes);
        Assert.Contains("\r\n", csv);
        var lines = csv.Split("\r\n", StringSplitOptions.None);
        Assert.Equal(Header, lines[0]);
        Assert.Equal(5, lines.Length);
        Assert.Equal(string.Empty, lines[^1]);
        Assert.StartsWith("WEB-001,C001,Customer One,2026-01-01,", lines[1]);
        Assert.StartsWith("WEB-002,C002,Customer Two,2026-01-02,", lines[2]);
        Assert.StartsWith("WEB-003,C003,Customer Three,2026-01-03,", lines[3]);
    }

    [Fact]
    public async Task Export_FieldsWithCommas_AreQuoted()
    {
        await using var db = NewDb();
        SeedPartner(db, "C001", "Acme, Inc.");
        SeedOrder(db, "WEB-001", "C001", new DateTime(2026, 1, 1), DateTime.Today.AddDays(1));
        await db.SaveChangesAsync();

        var csv = DecodeCsv(await NewService(db).ExportCsvAsync(new UnshippedOrderQuery()));

        Assert.Contains("WEB-001,C001,\"Acme, Inc.\",2026-01-01,", csv);
    }

    [Fact]
    public async Task Export_FieldsWithQuotes_AreEscaped()
    {
        await using var db = NewDb();
        SeedPartner(db, "C001", "Jane \"The Buyer\"");
        SeedOrder(db, "WEB-001", "C001", new DateTime(2026, 1, 1), DateTime.Today.AddDays(1));
        await db.SaveChangesAsync();

        var csv = DecodeCsv(await NewService(db).ExportCsvAsync(new UnshippedOrderQuery()));

        Assert.Contains("WEB-001,C001,\"Jane \"\"The Buyer\"\"\",2026-01-01,", csv);
    }

    [Fact]
    public async Task Export_NoRows_StillProducesHeader()
    {
        await using var db = NewDb();

        var csv = DecodeCsv(await NewService(db).ExportCsvAsync(new UnshippedOrderQuery()));

        Assert.Equal(Header + "\r\n", csv);
    }
}
