using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Fin;

/// <summary>财务章02 C-2：★三栏试算平衡表（期初含历史 + 两层平衡）。</summary>
public class TrialBalanceServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static JournalEntryService Jes(CP6Context db) =>
        new(db, new FiscalPeriodService(db, 1), new FinSequenceService(db));

    /// <summary>过账一张 应收账款 借 / 主营业务收入 贷 的凭证。</summary>
    private static async Task PostArRevenue(CP6Context db, DateTime date, Guid ar, Guid rev, decimal amount)
    {
        var svc = Jes(db);
        var e = new JournalEntry
        {
            VoucherDate = date,
            Source = VoucherSource.Manual,
            Lines =
            {
                new JournalLine { AccountId = ar, Debit = amount, PartnerId = "C1" },
                new JournalLine { AccountId = rev, Credit = amount },
            },
        };
        var id = await svc.CreateDraftAsync(e, "u1");
        await svc.SubmitForReviewAsync(id);
        await svc.PostAsync(id, "u2");
    }

    [Fact]
    public async Task TrialBalance_ThreeColumns_OpeningIncludesHistory()
    {
        using var db = NewDb();
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "seed");
        var ar = (await gl.GetByCodeAsync("1122"))!.Id;
        var rev = (await gl.GetByCodeAsync("4001"))!.Id;

        await PostArRevenue(db, new DateTime(2026, 5, 10), ar, rev, 20000m);   // 5月
        await PostArRevenue(db, new DateTime(2026, 6, 10), ar, rev, 5000m);    // 6月

        var june = (await new FiscalPeriodService(db, 1).ResolveAsync(new DateTime(2026, 6, 1)))!;
        var tb = await new TrialBalanceService(db).BuildAsync(june.Id);

        var arRow = tb.Rows.Single(r => r.Code == "1122");
        Assert.Equal(20000m, arRow.OpenBal);       // ★期初含 5 月历史
        Assert.Equal(5000m, arRow.PeriodDebit);    // 仅本期
        Assert.Equal(25000m, arRow.CloseBal);      // 期初 + 本期

        Assert.True(tb.MovementBalanced);          // Σ本期借 == Σ本期贷
        Assert.True(tb.ClosingBalanced);           // Σ借余额 == Σ贷余额
    }

    [Fact]
    public async Task TrialBalance_RevenueRow_NormalCreditSideShownPositive()
    {
        using var db = NewDb();
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "seed");
        var ar = (await gl.GetByCodeAsync("1122"))!.Id;
        var rev = (await gl.GetByCodeAsync("4001"))!.Id;

        await PostArRevenue(db, new DateTime(2026, 6, 10), ar, rev, 800m);

        var june = (await new FiscalPeriodService(db, 1).ResolveAsync(new DateTime(2026, 6, 1)))!;
        var tb = await new TrialBalanceService(db).BuildAsync(june.Id);

        var revRow = tb.Rows.Single(r => r.Code == "4001");
        Assert.Equal(0m, revRow.OpenBal);
        Assert.Equal(800m, revRow.PeriodCredit);
        Assert.Equal(800m, revRow.CloseBal);       // 贷方科目正常方向余额显示为正
        Assert.True(tb.IsBalanced);
    }

    [Fact]
    public async Task TrialBalance_EmptyPeriod_IsBalanced()
    {
        using var db = NewDb();
        var june = await new FiscalPeriodService(db, 1).EnsureOpenAsync(new DateTime(2026, 6, 1));
        var tb = await new TrialBalanceService(db).BuildAsync(june.Id);
        Assert.Empty(tb.Rows);
        Assert.True(tb.IsBalanced);                // 0 == 0
    }

    [Fact]
    public async Task TrialBalance_UnknownPeriod_Throws()
    {
        using var db = NewDb();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new TrialBalanceService(db).BuildAsync(Guid.NewGuid()));
        Assert.Equal("E-FIN-140", ex.Message);
    }
}
