using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Fin;

/// <summary>
/// 财务波D D.2：资产负债表「本年利润」改期间口径（本财年内发生额，非建账累计）。
/// 损益科目期末余额是建账以来累计——年结后（D.1）已被 Carryover 结转清零，CloseBal 天然只剩本财年；
/// 但跨年未年结时 CloseBal 含往年累计，直接入本年利润会虚增。BuildAsync 按报表期所属财年起点截断。
/// 硬约束：任何口径下资产 = 负债 + 权益（含本年利润）恒平。
/// 往年未年结损益 → 现算入「期初未分配利润」行（code=3104.PY），保持借贷恒等。
/// </summary>
public class BalanceSheetCurrentProfitTests
{
    /// <summary>与实现约定一致的往年未结转损益合成行编码。</summary>
    private const string PriorProfitCode = "3104.PY";

    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static FiscalPeriodService Periods(CP6Context db, int fyStart = 1) => new(db, fyStart);
    private static JournalEntryService Jes(CP6Context db, int fyStart = 1) =>
        new(db, Periods(db, fyStart), new FinSequenceService(db));
    private static PeriodCloseService Close(CP6Context db, int fyStart = 1) =>
        new(db, Periods(db, fyStart), new TrialBalanceService(db), journal: Jes(db, fyStart));

    private static async Task<GlAccountService> SeedCoa(CP6Context db)
    {
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "seed");
        return gl;
    }

    /// <summary>过账一张两行平衡凭证（借 drAcct / 贷 crAcct）到 VoucherDate 所属期间。</summary>
    private static async Task PostManual(CP6Context db, DateTime date, Guid drAcct, Guid crAcct, decimal amount,
        int fyStart = 1)
    {
        var svc = Jes(db, fyStart);
        var e = new JournalEntry
        {
            VoucherDate = date,
            Source = VoucherSource.Manual,
            Lines =
            {
                new JournalLine { AccountId = drAcct, Debit = amount },
                new JournalLine { AccountId = crAcct, Credit = amount },
            },
        };
        var id = await svc.CreateDraftAsync(e, "u1");
        await svc.SubmitForReviewAsync(id);
        var r = await svc.PostAsync(id, "u2");
        Assert.True(r.Ok, r.Code);
    }

    private static async Task CloseAll12(CP6Context db, int year, int fyStart = 1)
    {
        for (var m = 1; m <= 12; m++)
            await Periods(db, fyStart).EnsureOpenAsync(new DateTime(year, m, 1));
        for (var m = 1; m <= 12; m++)
        {
            var p = (await Periods(db, fyStart).ResolveAsync(new DateTime(year, m, 1)))!;
            var r = await Close(db, fyStart).CloseAsync(p.Id, "boss");
            Assert.True(r.Ok, $"close {year}-{m}: {r.Code}");
        }
    }

    // ───────── 场景1：年结后跨年——本年利润=本财年损益（往年已进 3104），资产负债表平 ─────────

    [Fact]
    public async Task CrossYear_AfterYearClose_CurrentProfitIsCurrentFyOnly()
    {
        using var db = NewDb();
        var gl = await SeedCoa(db);
        var bank = (await gl.GetByCodeAsync("1002"))!.Id;
        var rev = (await gl.GetByCodeAsync("4001"))!.Id;
        var cogs = (await gl.GetByCodeAsync("5001"))!.Id;

        // FY2025：收入 1000（1月）/ 费用 400（2月）→ 净利 600，随后年结 2025
        await PostManual(db, new DateTime(2025, 1, 15), bank, rev, 1000m);
        await PostManual(db, new DateTime(2025, 2, 15), cogs, bank, 400m);
        await CloseAll12(db, 2025);
        Assert.True((await Close(db).YearCloseAsync(2025, "boss")).Ok);

        // FY2026：收入 500（3月）/ 费用 200（4月）→ 净利 300
        await PostManual(db, new DateTime(2026, 3, 10), bank, rev, 500m);
        await PostManual(db, new DateTime(2026, 4, 10), cogs, bank, 200m);
        var jun26 = await Periods(db).EnsureOpenAsync(new DateTime(2026, 6, 1));

        var bs = await new BalanceSheetService(db, new TrialBalanceService(db)).BuildAsync(jun26.Id);

        // ★本年利润仅 FY2026 = 300（非累计 900）
        Assert.Equal(300m, bs.CurrentProfit);
        // 往年净利 600 已由年结进 3104（真实科目行），不再走合成行
        Assert.Equal(600m, bs.Equity.Single(l => l.Code == "3104").Amount);
        Assert.DoesNotContain(bs.Equity, l => l.Code == PriorProfitCode);
        // 资产 = 银行 900（600+300），权益 = 3104(600)+本年利润(300)=900
        Assert.Equal(900m, bs.TotalAssets);
        Assert.Equal(900m, bs.TotalLiabEquity);
        Assert.True(bs.IsBalanced);
    }

    // ───────── 场景2：未年结跨年——本年利润按财年截断，往年未结转损益落期初未分配利润行 ─────────

    [Fact]
    public async Task CrossYear_BeforeYearClose_CurrentProfitTruncated_PriorGoesToOpeningRetained()
    {
        using var db = NewDb();
        var gl = await SeedCoa(db);
        var bank = (await gl.GetByCodeAsync("1002"))!.Id;
        var rev = (await gl.GetByCodeAsync("4001"))!.Id;
        var cogs = (await gl.GetByCodeAsync("5001"))!.Id;

        // FY2025：收入 1000 / 费用 400 → 净利 600（★不年结）
        await PostManual(db, new DateTime(2025, 1, 15), bank, rev, 1000m);
        await PostManual(db, new DateTime(2025, 2, 15), cogs, bank, 400m);
        // FY2026：收入 500 / 费用 200 → 净利 300
        await PostManual(db, new DateTime(2026, 3, 10), bank, rev, 500m);
        await PostManual(db, new DateTime(2026, 4, 10), cogs, bank, 200m);
        var jun26 = await Periods(db).EnsureOpenAsync(new DateTime(2026, 6, 1));

        var bs = await new BalanceSheetService(db, new TrialBalanceService(db)).BuildAsync(jun26.Id);

        // ★本年利润仅 FY2026 = 300（旧口径会给累计 900——虚增）
        Assert.Equal(300m, bs.CurrentProfit);
        // 往年未年结净利 600 → 期初未分配利润现算行
        Assert.Equal(600m, bs.Equity.Single(l => l.Code == PriorProfitCode).Amount);
        // 未年结 → 3104 真实科目无余额
        Assert.DoesNotContain(bs.Equity, l => l.Code == "3104");
        // 资产 = 银行 900；权益 = 期初未分配(600)+本年利润(300)=900——仍平
        Assert.Equal(900m, bs.TotalAssets);
        Assert.Equal(900m, bs.TotalLiabEquity);
        Assert.True(bs.IsBalanced);
    }

    // ───────── 场景3：单财年内不受影响（回归旧口径：无往年→本年利润=全部损益）─────────

    [Fact]
    public async Task SingleFiscalYear_CurrentProfitUnchanged_NoPriorLine()
    {
        using var db = NewDb();
        var gl = await SeedCoa(db);
        var bank = (await gl.GetByCodeAsync("1002"))!.Id;
        var rev = (await gl.GetByCodeAsync("4001"))!.Id;
        var cogs = (await gl.GetByCodeAsync("5001"))!.Id;

        await PostManual(db, new DateTime(2026, 3, 10), bank, rev, 500m);
        await PostManual(db, new DateTime(2026, 4, 10), cogs, bank, 200m);
        var jun26 = await Periods(db).EnsureOpenAsync(new DateTime(2026, 6, 1));

        var bs = await new BalanceSheetService(db, new TrialBalanceService(db)).BuildAsync(jun26.Id);

        Assert.Equal(300m, bs.CurrentProfit);
        Assert.DoesNotContain(bs.Equity, l => l.Code == PriorProfitCode);
        Assert.True(bs.IsBalanced);
    }

    // ───────── 场景4：非日历财年（4月起）——按财年起点而非日历年截断 ─────────

    [Fact]
    public async Task NonCalendarFiscalYear_TruncatesAtFiscalYearStart()
    {
        using var db = NewDb();
        var gl = await SeedCoa(db);
        var bank = (await gl.GetByCodeAsync("1002"))!.Id;
        var rev = (await gl.GetByCodeAsync("4001"))!.Id;
        const int fy = 4;   // 财年 4 月起（日本）

        // FY2025（2025-04 ~ 2026-03）：收入 700（2025年5月）——属往年
        await PostManual(db, new DateTime(2025, 5, 10), bank, rev, 700m, fy);
        // FY2026（2026-04 ~ 2027-03）：收入 500（2026年5月）——本财年
        await PostManual(db, new DateTime(2026, 5, 10), bank, rev, 500m, fy);
        var jun26 = await Periods(db, fy).EnsureOpenAsync(new DateTime(2026, 6, 1));   // FiscalYear=2026, PeriodNo=3

        var bs = await new BalanceSheetService(db, new TrialBalanceService(db)).BuildAsync(jun26.Id);

        // 财年起点=2026-04-01：2026-05 计本年利润，2025-05 计期初留存
        Assert.Equal(500m, bs.CurrentProfit);
        Assert.Equal(700m, bs.Equity.Single(l => l.Code == PriorProfitCode).Amount);
        Assert.Equal(1200m, bs.TotalAssets);       // 银行 700+500
        Assert.True(bs.IsBalanced);
    }
}
