using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Fin;

public class BudgetVsActualTests
{
    [Fact]
    public async Task VsActual_MatchesBudgetBucket_ComputesVariance()
    {
        var db = TestHelper.CreateInMemoryContext();
        var acct = new GlAccount { Code="6602", Name="管理费用", Type=AccountType.Expense, NormalSide=AccountSide.Debit, IsLeaf=true, IsActive=true, StandardScheme="CN-GAAP" };
        db.GlAccounts.Add(acct);
        var p2 = new FiscalPeriod { FiscalYear=2027, Year=2027, Month=2, PeriodNo=2, Status=PeriodStatus.Open, PeriodStart=new(2027,2,1), PeriodEnd=new(2027,2,28) };
        db.FiscalPeriods.Add(p2);
        var b = new Budget { No="BUD-2027-00001", Name="2027", FiscalYear=2027, IsActive=true };
        db.Budgets.Add(b);
        var v = new BudgetVersion { BudgetId=b.Id, VersionNo=1, Status=BudgetVersionStatus.Approved, IsActive=true };
        db.BudgetVersions.Add(v);
        await db.SaveChangesAsync();
        var line = new BudgetLine { VersionId=v.Id, AccountId=acct.Id, AnnualAmount=1200m }; line.NormalizeKeys();
        db.BudgetLines.Add(line); await db.SaveChangesAsync();
        for (int i=1;i<=12;i++) db.BudgetLinePeriods.Add(new BudgetLinePeriod { BudgetLineId=line.Id, PeriodNo=i, Amount=100m });
        var e = new JournalEntry { VoucherDate=new(2027,2,10), PeriodId=p2.Id, Source=VoucherSource.Manual, Status=JournalStatus.Posted,
            Lines=new(){ new JournalLine { AccountId=acct.Id, Debit=80m, Credit=0m } } };
        db.JournalEntries.Add(e); await db.SaveChangesAsync();

        var svc = new BudgetReportService(db);
        var rep = await svc.BuildVsActualAsync(2027, null, 1, 12);
        var row = rep.Rows.First(r => r.AccountCode == "6602" && !r.IsUnbudgeted);
        Assert.Equal(1200m, row.Budget);
        Assert.Equal(80m, row.Actual);
        Assert.Equal(1120m, row.Variance);
    }

    [Fact]
    public async Task VsActual_ActualWithoutBudget_GoesToUnbudgetedGroup()
    {
        var db = TestHelper.CreateInMemoryContext();
        var acct = new GlAccount { Code="6603", Name="差旅费", Type=AccountType.Expense, NormalSide=AccountSide.Debit, IsLeaf=true, IsActive=true, StandardScheme="CN-GAAP" };
        db.GlAccounts.Add(acct);
        var p2 = new FiscalPeriod { FiscalYear=2027, Year=2027, Month=2, PeriodNo=2, Status=PeriodStatus.Open, PeriodStart=new(2027,2,1), PeriodEnd=new(2027,2,28) };
        db.FiscalPeriods.Add(p2);
        var b = new Budget { No="BUD-2027-00001", Name="2027", FiscalYear=2027, IsActive=true };
        db.Budgets.Add(b);
        var v = new BudgetVersion { BudgetId=b.Id, VersionNo=1, Status=BudgetVersionStatus.Approved, IsActive=true };
        db.BudgetVersions.Add(v);
        await db.SaveChangesAsync();
        // 无预算行，但有实际 6603=50
        var e = new JournalEntry { VoucherDate=new(2027,2,10), PeriodId=p2.Id, Source=VoucherSource.Manual, Status=JournalStatus.Posted,
            Lines=new(){ new JournalLine { AccountId=acct.Id, Debit=50m, Credit=0m } } };
        db.JournalEntries.Add(e); await db.SaveChangesAsync();

        var svc = new BudgetReportService(db);
        var rep = await svc.BuildVsActualAsync(2027, null, 1, 12);
        var ub = rep.Rows.First(r => r.IsUnbudgeted && r.AccountCode == "6603");
        Assert.Equal(0m, ub.Budget);
        Assert.Equal(50m, ub.Actual);
        Assert.True(rep.Rows.Any(r => r.IsUnbudgeted));
    }
}
