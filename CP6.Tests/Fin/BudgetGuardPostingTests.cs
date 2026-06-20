using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Fin;

/// <summary>
/// Task E-2：验证 BudgetGuard 已钩入 JournalEntryService.PostAsync。
/// Block 超支 → 拒（E-A5-BUDGET-EXCEEDED）；Warn 超支 → 放行；预算内 → 正常过账。
/// spec §6.2 / §7.4
/// </summary>
public class BudgetGuardPostingTests
{
    // ──────────────────────────────────────────────────────────────
    // 服务构造（镜像 JournalEntryServiceTests.Svc）
    // ──────────────────────────────────────────────────────────────
    private static JournalEntryService Svc(CP6Context db) =>
        new(db, new FiscalPeriodService(db, 1), new FinSequenceService(db));

    // ──────────────────────────────────────────────────────────────
    // 共享种子：费用科目 6602 + 现金科目 1002（从 CN-GAAP 模板）
    //           Open 期间 period2（2027-02）
    //           Active Budget 版本，mode 由参数控制
    // ──────────────────────────────────────────────────────────────
    private static async Task<(CP6Context db, Guid expenseAcct, Guid cashAcct, Guid periodId)>
        SeedAsync(BudgetControlMode mode, decimal periodBudget = 100m)
    {
        var db = TestHelper.CreateInMemoryContext();

        // 1. 导入 CN-GAAP 科目表（含 1002 银行存款、4001 收入等末级科目）
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "seed");

        // 2. 添加费用科目（6602 管理费用，末级）
        var expenseAcct = new GlAccount
        {
            Code = "6602",
            Name = "管理费用",
            Type = AccountType.Expense,
            NormalSide = AccountSide.Debit,
            IsLeaf = true,
            IsActive = true,
            StandardScheme = "CN-GAAP"
        };
        db.GlAccounts.Add(expenseAcct);
        await db.SaveChangesAsync();

        // 3. 获取现金科目（1002 银行存款，末级，来自模板）
        var cashAcctEntity = await db.GlAccounts.FirstAsync(a => a.Code == "1002");
        var cashAcctId = cashAcctEntity.Id;

        // 4. 创建 Open 财务期间（2027-02）
        var p2 = new FiscalPeriod
        {
            FiscalYear = 2027,
            Year = 2027,
            Month = 2,
            PeriodNo = 2,
            Status = PeriodStatus.Open,
            PeriodStart = new DateTime(2027, 2, 1),
            PeriodEnd = new DateTime(2027, 2, 28)
        };
        db.FiscalPeriods.Add(p2);
        await db.SaveChangesAsync();

        // 5. 创建 Budget + 激活版本
        var budget = new Budget { No = "BUD-2027-00001", Name = "2027", FiscalYear = 2027, IsActive = true };
        db.Budgets.Add(budget);

        var version = new BudgetVersion
        {
            BudgetId = budget.Id,
            VersionNo = 1,
            Status = BudgetVersionStatus.Approved,
            IsActive = true,
            DefaultControlMode = mode,
            DefaultControlBasis = BudgetControlBasis.Period
        };
        db.BudgetVersions.Add(version);
        await db.SaveChangesAsync();

        // 6. 预算行：公司级费用桶（无成本中心限制），年预算 = periodBudget * 12
        var annual = periodBudget * 12m;
        var line = new BudgetLine
        {
            VersionId = version.Id,
            AccountId = expenseAcct.Id,
            AnnualAmount = annual
        };
        line.NormalizeKeys();
        db.BudgetLines.Add(line);
        await db.SaveChangesAsync();

        // 7. 按月分摊（均等），期2=periodBudget
        for (int i = 1; i <= 12; i++)
            db.BudgetLinePeriods.Add(new BudgetLinePeriod
            {
                BudgetLineId = line.Id,
                PeriodNo = i,
                Amount = annual / 12m
            });
        await db.SaveChangesAsync();

        return (db, expenseAcct.Id, cashAcctId, p2.Id);
    }

    // ──────────────────────────────────────────────────────────────
    // 辅助：创建草稿 → 提交复核，返回 entryId
    // ──────────────────────────────────────────────────────────────
    private static async Task<Guid> CreatePendingReviewEntry(
        CP6Context db, Guid debitAcct, Guid creditAcct, Guid periodId,
        decimal amount, string maker = "maker")
    {
        var svc = Svc(db);

        // 用已知 periodId 推算凭证日期（2027-02-15 落在 period2 内）
        var e = new JournalEntry
        {
            VoucherDate = new DateTime(2027, 2, 15),
            Source = VoucherSource.Manual,
            Description = "预算守卫测试",
            Lines =
            {
                new JournalLine { AccountId = debitAcct,  Debit  = amount },
                new JournalLine { AccountId = creditAcct, Credit = amount },
            }
        };

        var id = await svc.CreateDraftAsync(e, maker);
        var submitR = await svc.SubmitForReviewAsync(id);
        if (!submitR.Ok) throw new InvalidOperationException($"SubmitForReview failed: {submitR.Code}");
        return id;
    }

    // ══════════════════════════════════════════════════════════════
    // 测试 1：Block 超支 → PostAsync 拒绝
    // ══════════════════════════════════════════════════════════════
    [Fact]
    public async Task PostAsync_BlockExceeded_Rejected()
    {
        var (db, expenseAcct, cashAcct, periodId) = await SeedAsync(BudgetControlMode.Block, periodBudget: 100m);

        // 借 费用 500 / 贷 现金 500（超 period 预算 100）
        var entryId = await CreatePendingReviewEntry(db, expenseAcct, cashAcct, periodId, 500m);

        var svc = Svc(db);
        var r = await svc.PostAsync(entryId, "checker");

        Assert.False(r.Ok);
        Assert.Equal("E-A5-BUDGET-EXCEEDED", r.Code);

        // 凭证状态仍为 PendingReview，未被过账
        var entry = await svc.GetAsync(entryId);
        Assert.NotEqual(JournalStatus.Posted, entry!.Status);
    }

    // ══════════════════════════════════════════════════════════════
    // 测试 2：Block 模式但金额在预算内 → PostAsync 正常过账
    // ══════════════════════════════════════════════════════════════
    [Fact]
    public async Task PostAsync_WithinBudget_Posts()
    {
        var (db, expenseAcct, cashAcct, periodId) = await SeedAsync(BudgetControlMode.Block, periodBudget: 100m);

        // 借 费用 80 / 贷 现金 80（≤ period 预算 100）
        var entryId = await CreatePendingReviewEntry(db, expenseAcct, cashAcct, periodId, 80m);

        var svc = Svc(db);
        var r = await svc.PostAsync(entryId, "checker");

        Assert.True(r.Ok);
        var entry = await svc.GetAsync(entryId);
        Assert.Equal(JournalStatus.Posted, entry!.Status);
    }

    // ══════════════════════════════════════════════════════════════
    // 测试 3：Warn 模式超支 → PostAsync 放行（不硬拦）
    // ══════════════════════════════════════════════════════════════
    [Fact]
    public async Task PostAsync_WarnExceeded_Posts()
    {
        var (db, expenseAcct, cashAcct, periodId) = await SeedAsync(BudgetControlMode.Warn, periodBudget: 100m);

        // 借 费用 500 / 贷 现金 500（超预算，但 Warn 不硬拦）
        var entryId = await CreatePendingReviewEntry(db, expenseAcct, cashAcct, periodId, 500m);

        var svc = Svc(db);
        var r = await svc.PostAsync(entryId, "checker");

        Assert.True(r.Ok);
        var entry = await svc.GetAsync(entryId);
        Assert.Equal(JournalStatus.Posted, entry!.Status);
    }

    // ══════════════════════════════════════════════════════════════
    // AC-011：AutoPostAsync 不经过 BudgetGuard（spec §8-2）
    // Block 超支的自动凭证（Source=AP）仍应直接过账成功。
    // ══════════════════════════════════════════════════════════════
    [Fact]
    public async Task AutoPostAsync_BlockExceeded_NotGated_Posts()
    {
        // 种一个 Block 预算，期预算=100
        var (db, expenseAcct, cashAcct, _) = await SeedAsync(BudgetControlMode.Block, periodBudget: 100m);

        // 构造自动凭证：Source=AP（非Manual），金额=500，大幅超预算
        // AutoPostAsync 内部会调用 EnsureOpenAsync 推算 PeriodId，传 VoucherDate 即可
        var entry = new JournalEntry
        {
            Source = VoucherSource.AP,      // 自动凭证，非 Manual
            VoucherDate = new DateTime(2027, 2, 15),
            Description = "AP自动凭证（超预算，不拦）",
            Lines =
            {
                new JournalLine { AccountId = expenseAcct, Debit  = 500m },
                new JournalLine { AccountId = cashAcct,    Credit = 500m },
            }
        };

        var svc = Svc(db);
        var r = await svc.AutoPostAsync(entry);

        // 断言：过账成功（BudgetGuard 不挂 AutoPostAsync）
        Assert.True(r.Ok, $"AutoPostAsync should not be gated by BudgetGuard; got: {r.Code}");
        var saved = await svc.GetAsync(entry.Id);
        Assert.Equal(JournalStatus.Posted, saved!.Status);
        Assert.True(saved.AutoPosted);
    }

    // ══════════════════════════════════════════════════════════════
    // AC-019：ReverseAsync 不被预算守卫拦截（spec §7 红冲释放预算）
    // 先在预算内过账，再红冲，红冲应成功（autoPost=true 走直过路径）。
    // ══════════════════════════════════════════════════════════════
    [Fact]
    public async Task ReverseAsync_NotBudgetGated_Succeeds()
    {
        // Block 预算，期预算=100；先在预算内过账 80
        var (db, expenseAcct, cashAcct, periodId) = await SeedAsync(BudgetControlMode.Block, periodBudget: 100m);

        var entryId = await CreatePendingReviewEntry(db, expenseAcct, cashAcct, periodId, 80m);
        var svc = Svc(db);

        // 过账（80 < 100，放行）
        var postR = await svc.PostAsync(entryId, "checker");
        Assert.True(postR.Ok, $"PostAsync within budget should succeed; got: {postR.Code}");

        // 红冲（autoPost=true：系统直过，免复核流程；预算守卫不挂 ReverseAsync）
        var revR = await svc.ReverseAsync(entryId, "maker", "测试红冲", autoPost: true);

        // 断言：红冲成功，未被预算守卫拦截
        Assert.True(revR.Ok, $"ReverseAsync should not be gated by BudgetGuard; got: {revR.Code}");

        // 验证原凭证已变为 Reversed
        var origin = await svc.GetAsync(entryId);
        Assert.Equal(JournalStatus.Reversed, origin!.Status);
    }
}
