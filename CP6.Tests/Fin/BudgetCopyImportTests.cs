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

    [Fact]
    public async Task CopyFromVersion_PreservesAllDimensionsAndControl()
    {
        var db = TestHelper.CreateInMemoryContext();
        var acct = new GlAccount { Code="6602", Name="管理费用", Type=AccountType.Expense, NormalSide=AccountSide.Debit, IsLeaf=true, IsActive=true };
        db.GlAccounts.Add(acct);
        var cc = new CostCenter { Code="SALES", Name="销售部", Type=CostCenterType.Department, IsActive=true };
        db.CostCenters.Add(cc);
        var b = new Budget { No="BUD-2027-00001", Name="2027", FiscalYear=2027, IsActive=true };
        db.Budgets.Add(b);
        var src = new BudgetVersion { BudgetId=b.Id, VersionNo=1, Status=BudgetVersionStatus.Draft };
        db.BudgetVersions.Add(src);
        await db.SaveChangesAsync();
        var lineSvc = new BudgetLineService(db);
        // 含全维度 + 控制配置的桶
        await lineSvc.UpsertLineAsync(new BudgetLineDto {
            VersionId=src.Id, AccountId=acct.Id, CostCenterId=cc.Id,
            CostObjectType="WorkOrder", CostObjectId="WO-1", AnnualAmount=1200m, SpreadMode="even",
            ControlMode=BudgetControlMode.Block, ControlBasis=BudgetControlBasis.Ytd, Memo="差旅"
        });
        // 第二个公司级桶（验证多行复制）
        await lineSvc.UpsertLineAsync(new BudgetLineDto { VersionId=src.Id, AccountId=acct.Id, AnnualAmount=600m, SpreadMode="even" });
        src.Status = BudgetVersionStatus.Approved;
        await db.SaveChangesAsync();

        var svc = new BudgetService(db, new FinSequenceService(db), new StubApprovalForTest());
        var v2 = (await svc.CreateVersionAsync(b.Id, "copy", "admin", copyFromVersionId: src.Id)).Data!;

        var lines = await db.BudgetLines.Where(l => l.VersionId == v2.Id).ToListAsync();
        Assert.Equal(2, lines.Count);   // 多行复制
        var dim = lines.Single(l => l.CostCenterId == cc.Id);
        Assert.Equal("WorkOrder", dim.CostObjectType);
        Assert.Equal("WO-1", dim.CostObjectId);
        Assert.Equal(BudgetControlMode.Block, dim.ControlMode);
        Assert.Equal(BudgetControlBasis.Ytd, dim.ControlBasis);
        Assert.Equal("差旅", dim.Memo);
        // 规范化键由复制后重新派生，与真实维度一致
        Assert.Equal(cc.Id, dim.CostCenterKey);
        Assert.Equal("WorkOrder", dim.CostObjectTypeKey);
        Assert.Equal("WO-1", dim.CostObjectIdKey);
    }

    // ── C-3: Excel 导入 ──────────────────────────────────────────────────────

    [Fact]
    public async Task Import_FatalRow_RejectsWholeBatch()
    {
        var db = TestHelper.CreateInMemoryContext();
        var acct = new GlAccount { Code="6602", Name="管理费用", Type=AccountType.Expense, NormalSide=AccountSide.Debit, IsLeaf=true, IsActive=true };
        db.GlAccounts.Add(acct);
        var b = new Budget { No="BUD-2027-00001", Name="2027", FiscalYear=2027, IsActive=true };
        db.Budgets.Add(b);
        var v = new BudgetVersion { BudgetId=b.Id, VersionNo=1, Status=BudgetVersionStatus.Draft };
        db.BudgetVersions.Add(v);
        await db.SaveChangesAsync();
        var svc = new BudgetLineService(db);

        using var xls = BudgetExcelFixture.Build(new[] {
            ("6602", "", "", new decimal[12]),
            ("9999", "", "", new decimal[12]),   // 科目不存在 → 致命
        });
        var r = await svc.ConfirmImportAsync(v.Id, xls);
        Assert.False(r.Ok);
        Assert.Equal("E-A5-IMPORT-001", r.Code);
        Assert.Equal(0, await db.BudgetLines.CountAsync());   // 零持久化
    }

    [Fact]
    public async Task Import_AllValid_ConfirmPersists()
    {
        var db = TestHelper.CreateInMemoryContext();
        var acct = new GlAccount { Code="6602", Name="管理费用", Type=AccountType.Expense, NormalSide=AccountSide.Debit, IsLeaf=true, IsActive=true };
        db.GlAccounts.Add(acct);
        var b = new Budget { No="BUD-2027-00001", Name="2027", FiscalYear=2027, IsActive=true };
        db.Budgets.Add(b);
        var v = new BudgetVersion { BudgetId=b.Id, VersionNo=1, Status=BudgetVersionStatus.Draft };
        db.BudgetVersions.Add(v);
        await db.SaveChangesAsync();
        var svc = new BudgetLineService(db);
        var months = new decimal[12]; months[0] = 100m; months[1] = 200m;
        using var xls = BudgetExcelFixture.Build(new[] { ("6602", "", "", months) });
        var r = await svc.ConfirmImportAsync(v.Id, xls);
        Assert.True(r.Ok);
        var line = await db.BudgetLines.SingleAsync(l => l.VersionId == v.Id);
        Assert.Equal(300m, line.AnnualAmount);
    }

    [Fact]
    public async Task Import_GarbageMonthCell_RejectsGracefully()
    {
        var db = TestHelper.CreateInMemoryContext();
        var acct = new GlAccount { Code="6602", Name="管理费用", Type=AccountType.Expense, NormalSide=AccountSide.Debit, IsLeaf=true, IsActive=true };
        db.GlAccounts.Add(acct);
        var b = new Budget { No="BUD-2027-00001", Name="2027", FiscalYear=2027, IsActive=true };
        db.Budgets.Add(b);
        var v = new BudgetVersion { BudgetId=b.Id, VersionNo=1, Status=BudgetVersionStatus.Draft };
        db.BudgetVersions.Add(v);
        await db.SaveChangesAsync();
        var svc = new BudgetLineService(db);
        using var xls = BudgetExcelFixture.BuildWithTextMonthCell("6602");
        var r = await svc.ConfirmImportAsync(v.Id, xls);   // must NOT throw
        Assert.False(r.Ok);
        Assert.Equal("E-A5-IMPORT-001", r.Code);
        Assert.Equal(0, await db.BudgetLines.CountAsync());
    }

    [Fact]
    public async Task Preview_ReturnsRowErrors_WithoutPersisting()
    {
        var db = TestHelper.CreateInMemoryContext();
        var acct = new GlAccount { Code="6602", Name="管理费用", Type=AccountType.Expense, NormalSide=AccountSide.Debit, IsLeaf=true, IsActive=true };
        db.GlAccounts.Add(acct);
        var b = new Budget { No="BUD-2027-00001", Name="2027", FiscalYear=2027, IsActive=true };
        db.Budgets.Add(b);
        var v = new BudgetVersion { BudgetId=b.Id, VersionNo=1, Status=BudgetVersionStatus.Draft };
        db.BudgetVersions.Add(v);
        await db.SaveChangesAsync();
        var svc = new BudgetLineService(db);
        using var xls = BudgetExcelFixture.Build(new[] { ("6602", "", "", new decimal[12]), ("9999", "", "", new decimal[12]) });
        var preview = await svc.PreviewImportAsync(v.Id, xls);
        Assert.True(preview.HasFatal);
        Assert.Contains(preview.Rows, r => r.AccountCode == "9999" && !r.Ok);
        Assert.Contains(preview.Rows, r => r.AccountCode == "6602" && r.Ok);
        Assert.Equal(0, await db.BudgetLines.CountAsync());   // dry-run persists nothing
    }

    [Fact]
    public async Task ConfirmImport_NonDraftVersion_Rejected()
    {
        var db = TestHelper.CreateInMemoryContext();
        var acct = new GlAccount { Code="6602", Name="管理费用", Type=AccountType.Expense, NormalSide=AccountSide.Debit, IsLeaf=true, IsActive=true };
        db.GlAccounts.Add(acct);
        var b = new Budget { No="BUD-2027-00001", Name="2027", FiscalYear=2027, IsActive=true };
        db.Budgets.Add(b);
        var v = new BudgetVersion { BudgetId=b.Id, VersionNo=1, Status=BudgetVersionStatus.Approved };
        db.BudgetVersions.Add(v);
        await db.SaveChangesAsync();
        var svc = new BudgetLineService(db);
        using var xls = BudgetExcelFixture.Build(new[] { ("6602", "", "", new decimal[12]) });
        var r = await svc.ConfirmImportAsync(v.Id, xls);
        Assert.False(r.Ok);
        Assert.Equal("E-A5-VERSION-005", r.Code);
    }
}
