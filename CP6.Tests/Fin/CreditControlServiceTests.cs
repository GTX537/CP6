using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Fin;

/// <summary>
/// 财务章04 C-3：信用控制（AR 独有反向风控）。额度 0/null=不控制；已欠+本单≤额度=放行；超额度=拦截。
/// 已欠口径与子账勾稽一致（未结发票 Gross−Settled，红字反向）。
/// </summary>
public class CreditControlServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static readonly DateTime Biz = new(2026, 6, 15);

    private static async Task SeedCustomerAsync(CP6Context db, decimal? limit)
    {
        db.BusinessPartners.Add(new BusinessPartner { BpCd = "CUST1", BpName = "客户1", BaseCd = "BASE1", CreditLimit = limit });
        await db.SaveChangesAsync();
    }

    private static async Task SeedOpenInvoiceAsync(CP6Context db, decimal gross, decimal settled = 0m, bool creditMemo = false)
    {
        db.ArInvoices.Add(new ArInvoice
        {
            Id = Guid.NewGuid(), No = $"AR-{Guid.NewGuid():N}".Substring(0, 12), CustomerId = "CUST1",
            InvoiceDate = Biz, DueDate = Biz, GrossAmount = gross, NetAmount = gross, SettledAmount = settled,
            FxRate = 1m, IsCreditMemo = creditMemo, Status = ArInvoiceStatus.Posted,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task NoLimit_NotControlled()
    {
        using var db = NewDb();
        await SeedCustomerAsync(db, null);
        var svc = new CreditControlService(db);

        var r = await svc.CheckCreditAsync("CUST1", 99999m);
        Assert.False(r.Controlled);
        Assert.False(r.Exceeded);
    }

    [Fact]
    public async Task WithinLimit_Passes()
    {
        using var db = NewDb();
        await SeedCustomerAsync(db, 10000m);
        await SeedOpenInvoiceAsync(db, 3000m);
        var svc = new CreditControlService(db);

        var r = await svc.CheckCreditAsync("CUST1", 2000m);
        Assert.True(r.Controlled);
        Assert.False(r.Exceeded);
        Assert.Equal(3000m, r.OpenAr);
        Assert.Equal(5000m, r.Available);     // 10000 − 3000 − 2000
    }

    [Fact]
    public async Task ExceedsLimit_Blocked()
    {
        using var db = NewDb();
        await SeedCustomerAsync(db, 10000m);
        await SeedOpenInvoiceAsync(db, 8000m);
        var svc = new CreditControlService(db);

        var r = await svc.CheckCreditAsync("CUST1", 5000m);
        Assert.True(r.Exceeded);              // 8000 + 5000 > 10000
        Assert.Equal(8000m, r.OpenAr);
    }

    [Fact]
    public async Task CreditMemo_ReducesOpenAr()
    {
        using var db = NewDb();
        await SeedCustomerAsync(db, 10000m);
        await SeedOpenInvoiceAsync(db, 8000m);
        await SeedOpenInvoiceAsync(db, 3000m, creditMemo: true);   // 红字反向 −3000
        var svc = new CreditControlService(db);

        var r = await svc.CheckCreditAsync("CUST1", 4000m);
        Assert.Equal(5000m, r.OpenAr);        // 8000 − 3000
        Assert.False(r.Exceeded);             // 5000 + 4000 ≤ 10000
    }
}
