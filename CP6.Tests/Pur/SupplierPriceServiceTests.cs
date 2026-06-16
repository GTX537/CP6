using CP6.Core.Services.Pur;
using CP6.Entity.DomainModels.Pur;
using Xunit;

namespace CP6.Tests.Pur;

/// <summary>
/// 采购价表带价解析单测（采购 章01 §4）。
/// 规则：满足 MinQty≤数量 ∧ 当时有效，取 MinQty 最大一档；无适用价返回 null。
/// </summary>
public class SupplierPriceServiceTests
{
    private const string S = "SUP001";
    private const string I = "ITEM001";
    private static readonly DateTime D0 = new(2026, 1, 1);
    private static readonly DateTime Now = new(2026, 6, 1);

    [Fact]
    public async Task ResolvePrice_PicksHighestQualifyingTier()
    {
        using var db = TestHelper.CreateInMemoryContext();
        db.SupplierPrices.AddRange(
            new SupplierPrice { SupplierId = S, ItemId = I, Price = 10m, MinQty = 1m, ValidFrom = D0 },
            new SupplierPrice { SupplierId = S, ItemId = I, Price = 8m, MinQty = 1000m, ValidFrom = D0 });
        await db.SaveChangesAsync();
        var svc = new SupplierPriceService(db);

        Assert.Equal(8m, await svc.ResolvePriceAsync(S, I, qty: 1500m, onDate: Now));  // 够 1000 档 → 8
        Assert.Equal(10m, await svc.ResolvePriceAsync(S, I, qty: 500m, onDate: Now));  // 不够 → 10
    }

    [Fact]
    public async Task ResolvePrice_NoQualifyingTier_ReturnsNull()
    {
        using var db = TestHelper.CreateInMemoryContext();
        db.SupplierPrices.Add(new SupplierPrice { SupplierId = S, ItemId = I, Price = 8m, MinQty = 1000m, ValidFrom = D0 });
        await db.SaveChangesAsync();
        var svc = new SupplierPriceService(db);

        Assert.Null(await svc.ResolvePriceAsync(S, I, qty: 500m, onDate: Now));   // 量不足任何档 → null
    }

    [Fact]
    public async Task ResolvePrice_RespectsValidityWindow()
    {
        using var db = TestHelper.CreateInMemoryContext();
        db.SupplierPrices.Add(new SupplierPrice
        {
            SupplierId = S, ItemId = I, Price = 9m, MinQty = 1m,
            ValidFrom = D0, ValidTo = new DateTime(2026, 3, 31)   // 已过期
        });
        await db.SaveChangesAsync();
        var svc = new SupplierPriceService(db);

        Assert.Null(await svc.ResolvePriceAsync(S, I, qty: 100m, onDate: Now));            // onDate 超出有效期 → null
        Assert.Equal(9m, await svc.ResolvePriceAsync(S, I, qty: 100m, onDate: new DateTime(2026, 2, 1)));  // 期内 → 9
    }

    [Fact]
    public async Task ResolvePrice_NewerValidFrom_Wins_AtSameTier()
    {
        using var db = TestHelper.CreateInMemoryContext();
        db.SupplierPrices.AddRange(
            new SupplierPrice { SupplierId = S, ItemId = I, Price = 10m, MinQty = 1m, ValidFrom = D0 },
            new SupplierPrice { SupplierId = S, ItemId = I, Price = 7m, MinQty = 1m, ValidFrom = new DateTime(2026, 5, 1) });
        await db.SaveChangesAsync();
        var svc = new SupplierPriceService(db);

        Assert.Equal(7m, await svc.ResolvePriceAsync(S, I, qty: 100m, onDate: Now));  // 同档取较新生效价
    }

    [Fact]
    public async Task Save_Then_List_RoundTrips()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var svc = new SupplierPriceService(db);
        await svc.SaveAsync(new SupplierPrice { SupplierId = S, ItemId = I, Price = 10m, MinQty = 1m, ValidFrom = D0 }, "u1");

        var list = await svc.ListAsync(S);
        Assert.Single(list);
        Assert.Equal("u1", list[0].Creator);
    }
}
