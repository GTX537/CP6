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
}
