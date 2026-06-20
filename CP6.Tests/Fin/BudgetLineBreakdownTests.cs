using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Fin;

public class BudgetLineBreakdownTests
{
    private static async Task<(CP6Context db, BudgetVersion v, Guid expenseAcct)> SeedAsync()
    {
        var db = TestHelper.CreateInMemoryContext();
        var acct = new GlAccount { Code = "6602", Name = "管理费用", Type = AccountType.Expense, NormalSide = AccountSide.Debit, IsLeaf = true, IsActive = true };
        db.GlAccounts.Add(acct);
        var b = new Budget { No = "BUD-2027-00001", Name = "2027", FiscalYear = 2027, IsActive = true };
        db.Budgets.Add(b);
        var v = new BudgetVersion { BudgetId = b.Id, VersionNo = 1, Status = BudgetVersionStatus.Draft };
        db.BudgetVersions.Add(v);
        await db.SaveChangesAsync();
        return (db, v, acct.Id);
    }

    [Fact]
    public async Task UpsertLine_EvenSpread_FillsAnnualAndPeriods()
    {
        var (db, v, acct) = await SeedAsync();
        var svc = new BudgetLineService(db);
        var r = await svc.UpsertLineAsync(new BudgetLineDto {
            VersionId = v.Id, AccountId = acct, AnnualAmount = 1200m, SpreadMode = "even"
        });
        Assert.True(r.Ok);
        var line = await db.BudgetLines.FirstAsync(l => l.VersionId == v.Id);
        var periods = await db.BudgetLinePeriods.Where(p => p.BudgetLineId == line.Id).OrderBy(p => p.PeriodNo).ToListAsync();
        Assert.Equal(12, periods.Count);
        Assert.Equal(100m, periods[0].Amount);
        Assert.Equal(1200m, periods.Sum(p => p.Amount));
        Assert.Equal(1200m, line.AnnualAmount);
        Assert.Equal(Guid.Empty, line.CostCenterKey);
        Assert.Equal("", line.CostObjectTypeKey);
    }

    [Fact]
    public async Task UpsertLine_NonLeafOrNonPnL_Rejected()
    {
        var (db, v, _) = await SeedAsync();
        var asset = new GlAccount { Code = "1001", Name = "现金", Type = AccountType.Asset, NormalSide = AccountSide.Debit, IsLeaf = true, IsActive = true };
        db.GlAccounts.Add(asset); await db.SaveChangesAsync();
        var svc = new BudgetLineService(db);
        var r = await svc.UpsertLineAsync(new BudgetLineDto { VersionId = v.Id, AccountId = asset.Id, AnnualAmount = 100m, SpreadMode = "even" });
        Assert.False(r.Ok);
        Assert.Equal("E-A5-LINE-002", r.Code);
    }

    [Fact]
    public async Task UpsertLine_DuplicateBucket_SecondUpdatesNotDuplicates()
    {
        var (db, v, acct) = await SeedAsync();
        var svc = new BudgetLineService(db);
        await svc.UpsertLineAsync(new BudgetLineDto { VersionId = v.Id, AccountId = acct, AnnualAmount = 1200m, SpreadMode = "even" });
        await svc.UpsertLineAsync(new BudgetLineDto { VersionId = v.Id, AccountId = acct, AnnualAmount = 2400m, SpreadMode = "even" });
        var lines = await db.BudgetLines.Where(l => l.VersionId == v.Id).ToListAsync();
        Assert.Single(lines);
        Assert.Equal(2400m, lines[0].AnnualAmount);
    }

    [Fact]
    public async Task UpsertLine_VersionNotDraft_Rejected()
    {
        var (db, v, acct) = await SeedAsync();
        v.Status = BudgetVersionStatus.Approved; await db.SaveChangesAsync();
        var svc = new BudgetLineService(db);
        var r = await svc.UpsertLineAsync(new BudgetLineDto { VersionId = v.Id, AccountId = acct, AnnualAmount = 100m, SpreadMode = "even" });
        Assert.False(r.Ok);
        Assert.Equal("E-A5-VERSION-005", r.Code);
    }

    [Fact]
    public async Task UpsertLine_SeasonalSpread_WeightedPeriodsSumToAnnual()
    {
        var (db, v, acct) = await SeedAsync();
        var svc = new BudgetLineService(db);
        // 权重 [2,1×11]，sum=13，年度 1300 → 期1=200、其余=100，合计=1300
        var weights = new[] { 2m, 1m, 1m, 1m, 1m, 1m, 1m, 1m, 1m, 1m, 1m, 1m };
        var r = await svc.UpsertLineAsync(new BudgetLineDto {
            VersionId = v.Id, AccountId = acct, AnnualAmount = 1300m, SpreadMode = "seasonal", Periods = weights
        });
        Assert.True(r.Ok);
        var line = await db.BudgetLines.FirstAsync(l => l.VersionId == v.Id);
        var periods = await db.BudgetLinePeriods.Where(p => p.BudgetLineId == line.Id).OrderBy(p => p.PeriodNo).ToListAsync();
        Assert.Equal(200m, periods[0].Amount);
        Assert.Equal(100m, periods[1].Amount);
        Assert.Equal(1300m, periods.Sum(p => p.Amount));
        Assert.Equal(1300m, line.AnnualAmount);
    }

    [Fact]
    public async Task UpsertLine_ManualSpread_UsesProvidedPeriodsAndDerivesAnnual()
    {
        var (db, v, acct) = await SeedAsync();
        var svc = new BudgetLineService(db);
        // 手工逐月，年度额由 Σ 期 自动回填（忽略传入 AnnualAmount）
        var manual = new[] { 100m, 200m, 300m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m };
        var r = await svc.UpsertLineAsync(new BudgetLineDto {
            VersionId = v.Id, AccountId = acct, AnnualAmount = 999m, SpreadMode = "manual", Periods = manual
        });
        Assert.True(r.Ok);
        var line = await db.BudgetLines.FirstAsync(l => l.VersionId == v.Id);
        var periods = await db.BudgetLinePeriods.Where(p => p.BudgetLineId == line.Id).OrderBy(p => p.PeriodNo).ToListAsync();
        Assert.Equal(100m, periods[0].Amount);
        Assert.Equal(300m, periods[2].Amount);
        Assert.Equal(600m, periods.Sum(p => p.Amount));
        Assert.Equal(600m, line.AnnualAmount);   // 自动回填，非 999
    }

    [Fact]
    public async Task UpsertLine_SeasonalAllZeroWeights_FallsBackToEven()
    {
        var (db, v, acct) = await SeedAsync();
        var svc = new BudgetLineService(db);
        var zero = new decimal[12];   // 全 0 权重 → 退化均摊
        var r = await svc.UpsertLineAsync(new BudgetLineDto {
            VersionId = v.Id, AccountId = acct, AnnualAmount = 1200m, SpreadMode = "seasonal", Periods = zero
        });
        Assert.True(r.Ok);
        var line = await db.BudgetLines.FirstAsync(l => l.VersionId == v.Id);
        var periods = await db.BudgetLinePeriods.Where(p => p.BudgetLineId == line.Id).OrderBy(p => p.PeriodNo).ToListAsync();
        Assert.Equal(100m, periods[0].Amount);
        Assert.Equal(1200m, periods.Sum(p => p.Amount));
    }

    [Fact]
    public async Task UpsertLine_WithMatchingRowVersion_UpdatesSuccessfully()
    {
        var (db, v, acct) = await SeedAsync();
        var svc = new BudgetLineService(db);
        await svc.UpsertLineAsync(new BudgetLineDto { VersionId = v.Id, AccountId = acct, AnnualAmount = 1200m, SpreadMode = "even" });
        var grid = (await svc.ListLinesAsync(v.Id)).First();
        // 回传读到的 RowVersion 再次更新 — happy path
        // 注意：EF Core InMemory provider 不强制并发令牌（忽略 RowVersion），故此测试仅验证
        // 正常更新路径不报错；真实并发冲突检测仅在 SQL Server 上生效（E-A5-CONCURRENCY-001）。
        var r = await svc.UpsertLineAsync(new BudgetLineDto { VersionId = v.Id, AccountId = acct, AnnualAmount = 2400m, SpreadMode = "even", RowVersion = grid.RowVersion });
        Assert.True(r.Ok);
        Assert.Equal(2400m, (await db.BudgetLines.FirstAsync(l => l.VersionId == v.Id)).AnnualAmount);
    }
}
