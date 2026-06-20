using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Fin;

public class BudgetCopyImportTests
{
    [Fact]
    public async Task CopyFromVersion_ClonesBucketsAndPeriods()
    {
        var db = TestHelper.CreateInMemoryContext();
        var acct = new GlAccount { Code="6602", Name="管理费用", Type=AccountType.Expense, NormalSide=AccountSide.Debit, IsLeaf=true, IsActive=true };
        db.GlAccounts.Add(acct);
        var b = new Budget { No="BUD-2027-00001", Name="2027", FiscalYear=2027, IsActive=true };
        db.Budgets.Add(b);
        var src = new BudgetVersion { BudgetId=b.Id, VersionNo=1, Status=BudgetVersionStatus.Draft };
        db.BudgetVersions.Add(src);
        await db.SaveChangesAsync();
        var lineSvc = new BudgetLineService(db);
        await lineSvc.UpsertLineAsync(new BudgetLineDto { VersionId=src.Id, AccountId=acct.Id, AnnualAmount=1200m, SpreadMode="even" });
        // 升为 Approved 再执行复制（CopyInto 不校验源版本状态，只读数据）
        src.Status = BudgetVersionStatus.Approved;
        await db.SaveChangesAsync();

        var svc = new BudgetService(db, new FinSequenceService(db), new StubApprovalForTest());
        var v2 = (await svc.CreateVersionAsync(b.Id, "copy", "admin", copyFromVersionId: src.Id)).Data!;

        var lines = await db.BudgetLines.Where(l => l.VersionId == v2.Id).ToListAsync();
        Assert.Single(lines);
        Assert.Equal(1200m, lines[0].AnnualAmount);
        var periods = await db.BudgetLinePeriods.Where(p => p.BudgetLineId == lines[0].Id).ToListAsync();
        Assert.Equal(12, periods.Count);
    }
}
