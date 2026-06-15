using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Fin;

/// <summary>
/// 财务章10 §5 D-4：每日对账。AP/AR 子账↔GL + 试算平衡勾稽，不一致→issue。
/// </summary>
public class FinReconciliationServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static FiscalPeriodService Periods(CP6Context db) => new(db, 1);
    private static JournalEntryService Jes(CP6Context db) => new(db, Periods(db), new FinSequenceService(db));

    private static FinReconciliationService Recon(CP6Context db) =>
        new(db, new TrialBalanceService(db), new ApReconcileService(db), new ArReconcileService(db));

    private static async Task<GlAccountService> SeedAsync(CP6Context db)
    {
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "seed");
        await Periods(db).EnsureOpenAsync(new DateTime(2026, 6, 1));   // 一个开启期间（试算平衡勾稽对象）
        return gl;
    }

    [Fact]
    public async Task Reconcile_CleanSystem_AllClear()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var r = await Recon(db).ReconcileAsync();
        Assert.True(r.AllClear);
        Assert.Empty(r.Issues);
    }

    [Fact]
    public async Task Reconcile_GlApWithoutSubLedger_RaisesApIssue()
    {
        using var db = NewDb();
        var gl = await SeedAsync(db);
        var exp = (await gl.GetByCodeAsync("6002"))!.Id;   // 管理费用
        var ap = (await gl.GetByCodeAsync("2202"))!.Id;    // 应付账款 AP_CONTROL（需往来单位）

        // 手工贷应付控制 1000、但无应付发票子账 → GL≠子账
        var svc = Jes(db);
        var e = new JournalEntry
        {
            VoucherDate = new DateTime(2026, 6, 10),
            Source = VoucherSource.Manual,
            Lines =
            {
                new JournalLine { AccountId = exp, Debit = 1000m },
                new JournalLine { AccountId = ap, Credit = 1000m, PartnerId = "SUP1" },
            },
        };
        var id = await svc.CreateDraftAsync(e, "maker");
        await svc.SubmitForReviewAsync(id);
        await svc.PostAsync(id, "checker");

        var r = await Recon(db).ReconcileAsync();
        Assert.False(r.AllClear);
        Assert.Contains(r.Issues, i => i.Category == "AP");
    }
}
