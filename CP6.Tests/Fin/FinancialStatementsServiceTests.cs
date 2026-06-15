using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Fin;

/// <summary>
/// 财务章08 C：三大报表（资产负债表 期末余额必平 + 损益表 本期发生毛利/净利润）。
/// 报表 = 科目余额视图，复用试算表。场景：投资 10万 + 现销 5万(收入) + COGS 3万 + 管理费 0.5万。
/// </summary>
public class FinancialStatementsServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static FiscalPeriodService Periods(CP6Context db) => new(db, 1);
    private static JournalEntryService Jes(CP6Context db) => new(db, Periods(db), new FinSequenceService(db));
    private static readonly DateTime Biz = new(2026, 6, 15);

    private sealed class Kit
    {
        public required CP6Context Db;
        public required GlAccountService Gl;
        public required BalanceSheetService Bs;
        public required IncomeStatementService Is;
        public required Guid PeriodId;
    }

    private static async Task<Kit> SetupAsync(CP6Context db)
    {
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "seed");
        var trial = new TrialBalanceService(db);
        var period = await Periods(db).EnsureOpenAsync(new DateTime(2026, 6, 1));
        return new Kit
        {
            Db = db, Gl = gl,
            Bs = new BalanceSheetService(db, trial),
            Is = new IncomeStatementService(db, trial),
            PeriodId = period.Id,
        };
    }

    /// <summary>过账一张两行平衡凭证（借 debitCode / 贷 creditCode），均为不需往来单位的科目。</summary>
    private static async Task PostAsync(Kit k, string debitCode, string creditCode, decimal amount)
    {
        var dr = (await k.Gl.GetByCodeAsync(debitCode))!.Id;
        var cr = (await k.Gl.GetByCodeAsync(creditCode))!.Id;
        var svc = Jes(k.Db);
        var e = new JournalEntry
        {
            VoucherDate = Biz,
            Source = VoucherSource.Manual,
            Lines =
            {
                new JournalLine { AccountId = dr, Debit = amount },
                new JournalLine { AccountId = cr, Credit = amount },
            },
        };
        var id = await svc.CreateDraftAsync(e, "maker");
        await svc.SubmitForReviewAsync(id);
        var r = await svc.PostAsync(id, "checker");
        Assert.True(r.Ok, r.Code);
    }

    private static async Task SeedScenarioAsync(Kit k)
    {
        await PostAsync(k, "1002", "3001", 100000m);   // 投资：借银行存款/贷实收资本
        await PostAsync(k, "1002", "4001", 50000m);    // 现销：借银行存款/贷主营业务收入
        await PostAsync(k, "5001", "1002", 30000m);    // 结转成本：借主营业务成本(COGS)/贷银行存款
        await PostAsync(k, "6002", "1002", 5000m);     // 管理费用：借管理费用/贷银行存款
    }

    [Fact]
    public async Task BalanceSheet_Balances_AssetsEqualLiabEquityPlusProfit()
    {
        using var db = NewDb();
        var k = await SetupAsync(db);
        await SeedScenarioAsync(k);

        var bs = await k.Bs.BuildAsync(k.PeriodId);

        Assert.Equal(115000m, bs.TotalAssets);              // 银行存款 100000+50000-30000-5000
        Assert.Equal(0m, bs.TotalLiabilities);
        Assert.Equal(15000m, bs.CurrentProfit);             // 收入50000 − 费用(30000+5000)
        Assert.Equal(115000m, bs.TotalEquity);              // 实收资本100000 + 本年利润15000
        Assert.Equal(115000m, bs.TotalLiabEquity);
        Assert.True(bs.IsBalanced);                         // ★资产 = 负债+权益+本年利润
    }

    [Fact]
    public async Task BalanceSheet_RevenueExpenseAccounts_NotListedAsBsLines()
    {
        using var db = NewDb();
        var k = await SetupAsync(db);
        await SeedScenarioAsync(k);

        var bs = await k.Bs.BuildAsync(k.PeriodId);
        var allLines = bs.Assets.Concat(bs.Liabilities).Concat(bs.Equity).Select(l => l.Code).ToList();
        Assert.DoesNotContain("4001", allLines);            // 收入不进资产负债表
        Assert.DoesNotContain("5001", allLines);            // 成本不进资产负债表
        Assert.Contains("1002", allLines);                  // 银行存款是资产行
        Assert.Contains("3001", allLines);                  // 实收资本是权益行
    }

    [Fact]
    public async Task IncomeStatement_GrossAndNetProfit_FromPeriodMovement()
    {
        using var db = NewDb();
        var k = await SetupAsync(db);
        await SeedScenarioAsync(k);

        var pnl = await k.Is.BuildAsync(k.PeriodId);

        Assert.Equal(50000m, pnl.Revenue);
        Assert.Equal(30000m, pnl.Cost);                     // COGS（Role=COGS）
        Assert.Equal(20000m, pnl.GrossProfit);
        Assert.Equal(5000m, pnl.OperatingExpense);          // 管理费用（非 COGS）
        Assert.Equal(15000m, pnl.NetProfit);
    }

    [Fact]
    public async Task IncomeStatement_CogsSeparatedFromOperatingExpense()
    {
        using var db = NewDb();
        var k = await SetupAsync(db);
        await SeedScenarioAsync(k);

        var pnl = await k.Is.BuildAsync(k.PeriodId);
        Assert.Contains(pnl.CostLines, l => l.Code == "5001");      // 主营业务成本入 COGS
        Assert.Contains(pnl.ExpenseLines, l => l.Code == "6002");   // 管理费用入营业费用
        Assert.DoesNotContain(pnl.ExpenseLines, l => l.Code == "5001");
    }

    [Fact]
    public async Task EmptyPeriod_BalanceSheetBalancedAtZero()
    {
        using var db = NewDb();
        var k = await SetupAsync(db);

        var bs = await k.Bs.BuildAsync(k.PeriodId);
        Assert.Equal(0m, bs.TotalAssets);
        Assert.Equal(0m, bs.TotalLiabEquity);
        Assert.True(bs.IsBalanced);
    }
}
