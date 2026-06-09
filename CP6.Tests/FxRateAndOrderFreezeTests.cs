using CP6.Core.EFDbContext;
using CP6.Core.Services;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace CP6.Tests;

/// <summary>
/// Gap 4.3 多通貨／為替レート凍結テスト。
///
/// 観点：
/// 1. ResolveForCustomer：基軸/未設定通貨 → (JPY, 1.0)
/// 2. ResolveForCustomer：外貨 → 基準日以前の最新レートを凍結
/// 3. ResolveForCustomer：外貨だがレート未登録 → 例外
/// 4. CreateAsync：外貨得意先 → Order に通貨・凍結レートが記録される
/// 5. CreateAsync：JPY 得意先 → 既定 (JPY, 1.0)
/// </summary>
public class FxRateAndOrderFreezeTests
{
    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    private static void SeedCustomer(CP6Context db, string bpCd, string? currencyCd)
    {
        db.BusinessPartners.Add(new BusinessPartner
        {
            BpCd = bpCd, BpName = bpCd + " 商事", BaseCd = "B01",
            CurrencyCd = currencyCd, Creator = "seed", CreateDate = DateTime.Now,
        });
        db.SaveChanges();
    }

    private static void SeedRate(CP6Context db, string cur, DateTime date, decimal rate)
    {
        db.FxRates.Add(new FxRate
        {
            Id = Guid.NewGuid(), CurrencyCd = cur, RateDate = date, Rate = rate,
            Creator = "seed", CreateDate = DateTime.Now,
        });
        db.SaveChanges();
    }

    private static OrderService NewOrderSvc(CP6Context db)
    {
        var pe = new Mock<IPowerEggWorkflowService>().Object;
        var wms = new Mock<IWmsBridgeHook>().Object;
        return new OrderService(db, pe, wms, null, null, new FxRateService(db));
    }

    private static OrderDto NewOrderDto(string customerCd) => new()
    {
        CustomerCd = customerCd,
        OrderType = "20", // シート受注 = isEditable → 納期 LT チェックをスキップ
        OrderDate = new DateTime(2026, 3, 10),
        Details = new() { new OrderDetailDto { ProductCd = "P001", Quantity = 100m } },
    };

    // ───────── 1-3. ResolveForCustomer ─────────

    [Fact]
    public async Task Resolve_BaseCurrency_ReturnsJpyRate1()
    {
        using var db = NewDb();
        SeedCustomer(db, "C-JPY", currencyCd: null);
        SeedCustomer(db, "C-JPY2", currencyCd: "JPY");

        var svc = new FxRateService(db);
        var a = await svc.ResolveForCustomerAsync("C-JPY", new DateTime(2026, 3, 10));
        var b = await svc.ResolveForCustomerAsync("C-JPY2", new DateTime(2026, 3, 10));

        Assert.Equal(("JPY", 1m), a);
        Assert.Equal(("JPY", 1m), b);
    }

    [Fact]
    public async Task Resolve_Foreign_FreezesLatestRateOnOrBeforeDate()
    {
        using var db = NewDb();
        SeedCustomer(db, "C-USD", "USD");
        SeedRate(db, "USD", new DateTime(2026, 3, 1), 148m);
        SeedRate(db, "USD", new DateTime(2026, 3, 8), 150m);   // 基準日以前で最新
        SeedRate(db, "USD", new DateTime(2026, 3, 15), 152m);  // 基準日より後 → 不採用

        var (cur, rate) = await new FxRateService(db).ResolveForCustomerAsync("C-USD", new DateTime(2026, 3, 10));

        Assert.Equal("USD", cur);
        Assert.Equal(150m, rate);
    }

    [Fact]
    public async Task Resolve_ForeignNoRate_Throws()
    {
        using var db = NewDb();
        SeedCustomer(db, "C-EUR", "EUR");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new FxRateService(db).ResolveForCustomerAsync("C-EUR", new DateTime(2026, 3, 10)));
    }

    // ───────── 4-5. CreateAsync 凍結 ─────────

    [Fact]
    public async Task CreateAsync_ForeignCustomer_FreezesFxRate()
    {
        using var db = NewDb();
        SeedCustomer(db, "C-USD", "USD");
        SeedRate(db, "USD", new DateTime(2026, 3, 1), 150m);

        var no = await NewOrderSvc(db).CreateAsync(NewOrderDto("C-USD"), "u");

        var order = await db.Orders.SingleAsync(o => o.WebOrderNo == no);
        Assert.Equal("USD", order.CurrencyCd);
        Assert.Equal(150m, order.FxRate);
    }

    [Fact]
    public async Task CreateAsync_JpyCustomer_DefaultsBaseRate()
    {
        using var db = NewDb();
        SeedCustomer(db, "C-JPY", currencyCd: null);

        var no = await NewOrderSvc(db).CreateAsync(NewOrderDto("C-JPY"), "u");

        var order = await db.Orders.SingleAsync(o => o.WebOrderNo == no);
        Assert.Equal("JPY", order.CurrencyCd);
        Assert.Equal(1m, order.FxRate);
    }
}
