using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Fin;

public class BudgetGuardTests
{
    // Active 版本含 Block 行(公司级 年=annual, mode/basis 由参数)；期 2 Open
    private static async Task<(CP6Context db, Guid acct, Guid periodId, int periodNo)> SeedAsync(decimal annual, BudgetControlMode mode, BudgetControlBasis basis, Guid? cc = null)
    {
        var db = TestHelper.CreateInMemoryContext();
        var acct = new GlAccount { Code = "6602", Name = "管理费用", Type = AccountType.Expense, NormalSide = AccountSide.Debit, IsLeaf = true, IsActive = true, StandardScheme = "CN-GAAP" };
        db.GlAccounts.Add(acct);
        var p2 = new FiscalPeriod { FiscalYear = 2027, Year = 2027, Month = 2, PeriodNo = 2, Status = PeriodStatus.Open, PeriodStart = new DateTime(2027, 2, 1), PeriodEnd = new DateTime(2027, 2, 28) };
        db.FiscalPeriods.Add(p2);
        var b = new Budget { No = "BUD-2027-00001", Name = "2027", FiscalYear = 2027, IsActive = true };
        db.Budgets.Add(b);
        var v = new BudgetVersion { BudgetId = b.Id, VersionNo = 1, Status = BudgetVersionStatus.Approved, IsActive = true, DefaultControlMode = mode, DefaultControlBasis = basis };
        db.BudgetVersions.Add(v);
        await db.SaveChangesAsync();
        var line = new BudgetLine { VersionId = v.Id, AccountId = acct.Id, CostCenterId = cc, AnnualAmount = annual };
        line.NormalizeKeys();
        db.BudgetLines.Add(line);
        await db.SaveChangesAsync();
        for (int i = 1; i <= 12; i++) db.BudgetLinePeriods.Add(new BudgetLinePeriod { BudgetLineId = line.Id, PeriodNo = i, Amount = annual / 12 });
        await db.SaveChangesAsync();
        return (db, acct.Id, p2.Id, 2);
    }

    private static JournalEntry Entry(Guid acct, Guid periodId, decimal debit, Guid? cc = null) => new JournalEntry
    {
        VoucherDate = new DateTime(2027, 2, 15),
        PeriodId = periodId,
        Source = VoucherSource.Manual,
        Status = JournalStatus.Draft,
        Lines = new() { new JournalLine { AccountId = acct, Debit = debit, Credit = 0m, CostCenterId = cc } }
    };

    [Fact]
    public async Task Block_Ytd_ExceedsCumulativeBudget_Rejected()
    {
        var (db, acct, pid, _) = await SeedAsync(1200m, BudgetControlMode.Block, BudgetControlBasis.Ytd);
        var p1 = new FiscalPeriod { FiscalYear = 2027, Year = 2027, Month = 1, PeriodNo = 1, Status = PeriodStatus.Open, PeriodStart = new(2027, 1, 1), PeriodEnd = new(2027, 1, 31) };
        db.FiscalPeriods.Add(p1); await db.SaveChangesAsync();
        var posted = Entry(acct, p1.Id, 90m); posted.Status = JournalStatus.Posted;
        db.JournalEntries.Add(posted); await db.SaveChangesAsync();

        var r = await BudgetGuard.CheckPostingAsync(db, Entry(acct, pid, 120m));   // YTD 已用90+120=210 > 累计预算(期1+2)=200 → 拒
        Assert.False(r.Ok);
        Assert.Equal("E-A5-BUDGET-EXCEEDED", r.Code);
    }

    [Fact]
    public async Task Block_WithinBudget_Passes()
    {
        var (db, acct, pid, _) = await SeedAsync(1200m, BudgetControlMode.Block, BudgetControlBasis.Period);
        var r = await BudgetGuard.CheckPostingAsync(db, Entry(acct, pid, 80m));   // 期2预算100，过80 → 放行
        Assert.True(r.Ok);
    }

    [Fact]
    public async Task NoActiveVersion_ShortCircuitsPass()
    {
        var (db, acct, pid, _) = await SeedAsync(1200m, BudgetControlMode.Block, BudgetControlBasis.Period);
        foreach (var v in db.BudgetVersions) v.IsActive = false;
        await db.SaveChangesAsync();
        var r = await BudgetGuard.CheckPostingAsync(db, Entry(acct, pid, 99999m));
        Assert.True(r.Ok);
    }

    [Fact]
    public async Task SameEntry_MultipleLinesSameBucket_Merged()
    {
        var (db, acct, pid, _) = await SeedAsync(1200m, BudgetControlMode.Block, BudgetControlBasis.Period);
        var e = Entry(acct, pid, 60m);
        e.Lines.Add(new JournalLine { AccountId = acct, Debit = 60m, Credit = 0m });   // 同桶第二行
        var r = await BudgetGuard.CheckPostingAsync(db, e);   // 合并120 > 期预算100 → 拒
        Assert.False(r.Ok);
        Assert.Equal("E-A5-BUDGET-EXCEEDED", r.Code);
    }

    [Fact]
    public async Task Warn_ExceedsBudget_NotBlockedByGuard()
    {
        // Warn 模式：守卫 blockOnly=true 只拦 Block，Warn 超支透传放行（由报表/预检提示）
        var (db, acct, pid, _) = await SeedAsync(1200m, BudgetControlMode.Warn, BudgetControlBasis.Period);
        var r = await BudgetGuard.CheckPostingAsync(db, Entry(acct, pid, 9999m));   // 远超期预算100
        Assert.True(r.Ok);
    }

    [Fact]
    public async Task Revenue_OverTarget_NotBlocked()
    {
        // 收入科目不参与硬控制（仅 Expense 预算行被加载）；收入超目标不拦过账
        var (db, _, pid, _) = await SeedAsync(1200m, BudgetControlMode.Block, BudgetControlBasis.Period);
        var rev = new GlAccount { Code = "6001", Name = "主营业务收入", Type = AccountType.Revenue, NormalSide = AccountSide.Credit, IsLeaf = true, IsActive = true, StandardScheme = "CN-GAAP" };
        db.GlAccounts.Add(rev);
        var v = await db.BudgetVersions.FirstAsync(x => x.IsActive);
        var revLine = new BudgetLine { VersionId = v.Id, AccountId = rev.Id, AnnualAmount = 1200m, ControlMode = BudgetControlMode.Block };
        revLine.NormalizeKeys();
        db.BudgetLines.Add(revLine);
        await db.SaveChangesAsync();
        for (int i = 1; i <= 12; i++) db.BudgetLinePeriods.Add(new BudgetLinePeriod { BudgetLineId = revLine.Id, PeriodNo = i, Amount = 100m });
        await db.SaveChangesAsync();
        var e = new JournalEntry
        {
            VoucherDate = new DateTime(2027, 2, 15), PeriodId = pid, Source = VoucherSource.Manual, Status = JournalStatus.Draft,
            Lines = new() { new JournalLine { AccountId = rev.Id, Debit = 0m, Credit = 9999m } }
        };
        var r = await BudgetGuard.CheckPostingAsync(db, e);
        Assert.True(r.Ok);
    }

    [Fact]
    public async Task MostSpecific_CostCenterBucket_ControlsOverCompanyBucket()
    {
        // 公司级桶期预算=10000；新增 cc 专属 Block 桶期预算=300。向该 cc 过账 500 → 命中更具体的 cc 桶 → 拒（虽远低于公司预算）
        var ccId = Guid.NewGuid();
        var (db, acct, pid, _) = await SeedAsync(120000m, BudgetControlMode.Block, BudgetControlBasis.Period);
        var v = await db.BudgetVersions.FirstAsync(x => x.IsActive);
        var ccLine = new BudgetLine { VersionId = v.Id, AccountId = acct, CostCenterId = ccId, AnnualAmount = 3600m, ControlMode = BudgetControlMode.Block };
        ccLine.NormalizeKeys();
        db.BudgetLines.Add(ccLine);
        await db.SaveChangesAsync();
        for (int i = 1; i <= 12; i++) db.BudgetLinePeriods.Add(new BudgetLinePeriod { BudgetLineId = ccLine.Id, PeriodNo = i, Amount = 300m });
        await db.SaveChangesAsync();
        var r = await BudgetGuard.CheckPostingAsync(db, Entry(acct, pid, 500m, cc: ccId));
        Assert.False(r.Ok);
        Assert.Equal("E-A5-BUDGET-EXCEEDED", r.Code);
    }

    // ══════════════════════════════════════════════════════════════
    // AC-022：非自然财年（FiscalYear ≠ calendar Year）通过 PeriodId 落期（spec §7.1）
    // 场景：财年 2027 从 2026-12 开始（PeriodNo=1 = Dec-2026）。
    //       预算 FiscalYear=2027，期1=100；凭证 VoucherDate=2026-12-15，PeriodId→期1。
    //       过账 150 → 超 → 守卫拒绝（E-A5-BUDGET-EXCEEDED）。
    // 若守卫错误地用 VoucherDate.Year(=2026) 查 FiscalYear=2026 的预算，则找不到版本，
    // 会短路放行（Ok=true）—— 该测试证明必须走 PeriodId.FiscalYear 路径。
    // ══════════════════════════════════════════════════════════════
    [Fact]
    public async Task NonNaturalFiscalYear_GuardResolvesViaFiscalPeriod_Rejected()
    {
        var db = TestHelper.CreateInMemoryContext();

        // 费用科目
        var acct = new GlAccount
        {
            Code = "6602", Name = "管理费用",
            Type = AccountType.Expense, NormalSide = AccountSide.Debit,
            IsLeaf = true, IsActive = true, StandardScheme = "CN-GAAP"
        };
        db.GlAccounts.Add(acct);

        // 非自然财年：FiscalYear=2027，但该财年第1期落在日历 2026-12（PeriodNo=1）
        var periodDec2026 = new FiscalPeriod
        {
            FiscalYear = 2027,   // 财年 2027
            Year = 2026,         // 日历年 2026
            Month = 12,          // 日历月 12
            PeriodNo = 1,        // 财年第1期
            Status = PeriodStatus.Open,
            PeriodStart = new DateTime(2026, 12, 1),
            PeriodEnd = new DateTime(2026, 12, 31)
        };
        db.FiscalPeriods.Add(periodDec2026);

        // 预算：FiscalYear=2027，激活版本，Block 模式，年额 = 1200，期1=100
        var budget = new Budget { No = "BUD-2027-00001", Name = "2027", FiscalYear = 2027, IsActive = true };
        db.Budgets.Add(budget);
        var version = new BudgetVersion
        {
            BudgetId = budget.Id, VersionNo = 1,
            Status = BudgetVersionStatus.Approved, IsActive = true,
            DefaultControlMode = BudgetControlMode.Block,
            DefaultControlBasis = BudgetControlBasis.Period
        };
        db.BudgetVersions.Add(version);
        await db.SaveChangesAsync();

        var line = new BudgetLine { VersionId = version.Id, AccountId = acct.Id, AnnualAmount = 1200m };
        line.NormalizeKeys();
        db.BudgetLines.Add(line);
        await db.SaveChangesAsync();

        for (int i = 1; i <= 12; i++)
            db.BudgetLinePeriods.Add(new BudgetLinePeriod { BudgetLineId = line.Id, PeriodNo = i, Amount = 100m });
        await db.SaveChangesAsync();

        // 凭证：VoucherDate=2026-12-15（日历2026年），PeriodId → 财年2027期1
        // 过账金额 150 > 期1预算 100 → 应被拒
        var entry = new JournalEntry
        {
            VoucherDate = new DateTime(2026, 12, 15),
            PeriodId = periodDec2026.Id,             // ← 关键：显式 PeriodId 落财年2027
            Source = VoucherSource.Manual,
            Status = JournalStatus.Draft,
            Lines = new() { new JournalLine { AccountId = acct.Id, Debit = 150m, Credit = 0m } }
        };

        var r = await BudgetGuard.CheckPostingAsync(db, entry);

        // 若守卫用 VoucherDate.Year=2026 查版本，找不到，会短路 Ok=true（测试失败）
        // 正确路径：守卫用 PeriodId→FiscalYear=2027，命中版本，150>100 → 拒
        Assert.False(r.Ok, "守卫应通过 PeriodId 落期到财年2027，超支应被拒绝");
        Assert.Equal("E-A5-BUDGET-EXCEEDED", r.Code);
    }
}
