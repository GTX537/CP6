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
}
