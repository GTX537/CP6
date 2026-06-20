using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Fin;

public class BudgetPreCheckTests
{
    [Fact]
    public async Task PreCheck_WarnExceeded_ReturnsWarning()
    {
        // Warn 模式超支：守卫不拦，但 PreCheck 返回预警（供 UI 提交前提示）
        var db = TestHelper.CreateInMemoryContext();
        var acct = new GlAccount { Code="6602", Name="管理费用", Type=AccountType.Expense, NormalSide=AccountSide.Debit, IsLeaf=true, IsActive=true, StandardScheme="CN-GAAP" };
        db.GlAccounts.Add(acct);
        var p2 = new FiscalPeriod { FiscalYear=2027, Year=2027, Month=2, PeriodNo=2, Status=PeriodStatus.Open, PeriodStart=new(2027,2,1), PeriodEnd=new(2027,2,28) };
        db.FiscalPeriods.Add(p2);
        var b = new Budget { No="BUD-2027-00001", Name="2027", FiscalYear=2027, IsActive=true };
        db.Budgets.Add(b);
        var v = new BudgetVersion { BudgetId=b.Id, VersionNo=1, Status=BudgetVersionStatus.Approved, IsActive=true, DefaultControlMode=BudgetControlMode.Warn, DefaultControlBasis=BudgetControlBasis.Period };
        db.BudgetVersions.Add(v);
        await db.SaveChangesAsync();
        var line = new BudgetLine { VersionId=v.Id, AccountId=acct.Id, AnnualAmount=1200m }; line.NormalizeKeys();
        db.BudgetLines.Add(line); await db.SaveChangesAsync();
        for (int i=1;i<=12;i++) db.BudgetLinePeriods.Add(new BudgetLinePeriod { BudgetLineId=line.Id, PeriodNo=i, Amount=100m });
        await db.SaveChangesAsync();

        var svc = new BudgetReportService(db);
        var entry = new JournalEntry { VoucherDate=new(2027,2,15), PeriodId=p2.Id, Source=VoucherSource.Manual, Status=JournalStatus.Draft,
            Lines=new(){ new JournalLine { AccountId=acct.Id, Debit=500m, Credit=0m } } };
        var warnings = await svc.PreCheckAsync(entry);
        Assert.Contains(warnings, w => w.AccountCode == "6602" && !w.IsBlock);
    }

    [Fact]
    public async Task PreCheck_WithinBudget_NoWarnings()
    {
        var db = TestHelper.CreateInMemoryContext();
        var acct = new GlAccount { Code="6602", Name="管理费用", Type=AccountType.Expense, NormalSide=AccountSide.Debit, IsLeaf=true, IsActive=true, StandardScheme="CN-GAAP" };
        db.GlAccounts.Add(acct);
        var p2 = new FiscalPeriod { FiscalYear=2027, Year=2027, Month=2, PeriodNo=2, Status=PeriodStatus.Open, PeriodStart=new(2027,2,1), PeriodEnd=new(2027,2,28) };
        db.FiscalPeriods.Add(p2);
        var b = new Budget { No="BUD-2027-00001", Name="2027", FiscalYear=2027, IsActive=true };
        db.Budgets.Add(b);
        var v = new BudgetVersion { BudgetId=b.Id, VersionNo=1, Status=BudgetVersionStatus.Approved, IsActive=true, DefaultControlMode=BudgetControlMode.Block, DefaultControlBasis=BudgetControlBasis.Period };
        db.BudgetVersions.Add(v);
        await db.SaveChangesAsync();
        var line = new BudgetLine { VersionId=v.Id, AccountId=acct.Id, AnnualAmount=1200m }; line.NormalizeKeys();
        db.BudgetLines.Add(line); await db.SaveChangesAsync();
        for (int i=1;i<=12;i++) db.BudgetLinePeriods.Add(new BudgetLinePeriod { BudgetLineId=line.Id, PeriodNo=i, Amount=100m });
        await db.SaveChangesAsync();

        var svc = new BudgetReportService(db);
        var entry = new JournalEntry { VoucherDate=new(2027,2,15), PeriodId=p2.Id, Source=VoucherSource.Manual, Status=JournalStatus.Draft,
            Lines=new(){ new JournalLine { AccountId=acct.Id, Debit=50m, Credit=0m } } };
        var warnings = await svc.PreCheckAsync(entry);
        Assert.Empty(warnings);
    }
}
