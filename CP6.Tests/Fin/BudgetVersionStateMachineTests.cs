using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Fin;

internal class StubApprovalForTest : IApprovalService
{
    public Task<Guid> SubmitAsync(string bizType, string bizId, Guid starterId,
        object? formSnapshot = null, Guid? instanceId = null)
        => Task.FromResult(Guid.NewGuid());

    public Task<ApprovalStatus> GetStatusAsync(string bizType, string bizId)
        => Task.FromResult(ApprovalStatus.None);
}

public class BudgetVersionStateMachineTests
{
    private static CP6Context Db() => TestHelper.CreateInMemoryContext();

    [Fact]
    public async Task CreateBudget_DuplicateFiscalYear_Rejected()
    {
        using var db = Db();
        var svc = new BudgetService(db, new FinSequenceService(db), new StubApprovalForTest());
        var r1 = await svc.CreateBudgetAsync(new Budget { Name = "2027", FiscalYear = 2027 }, "admin");
        Assert.True(r1.Ok);
        var r2 = await svc.CreateBudgetAsync(new Budget { Name = "2027b", FiscalYear = 2027 }, "admin");
        Assert.False(r2.Ok);
        Assert.Equal("E-A5-BUDGET-001", r2.Code);
    }

    [Fact]
    public async Task CreateVersion_AutoIncrementsVersionNo()
    {
        using var db = Db();
        var svc = new BudgetService(db, new FinSequenceService(db), new StubApprovalForTest());
        var b = (await svc.CreateBudgetAsync(new Budget { Name = "2027", FiscalYear = 2027 }, "admin")).Data!;
        var v1 = (await svc.CreateVersionAsync(b.Id, "初稿", "admin")).Data!;
        var v2 = (await svc.CreateVersionAsync(b.Id, "调整", "admin")).Data!;
        Assert.Equal(1, v1.VersionNo);
        Assert.Equal(2, v2.VersionNo);
        Assert.Equal(BudgetVersionStatus.Draft, v2.Status);
    }

    [Fact]
    public async Task ActivateFromApproval_SetsActive_ArchivesPriorActive()
    {
        using var db = Db();
        var svc = new BudgetService(db, new FinSequenceService(db), new StubApprovalForTest());
        var b = (await svc.CreateBudgetAsync(new Budget { Name = "2027", FiscalYear = 2027 }, "admin")).Data!;
        var v1 = (await svc.CreateVersionAsync(b.Id, "v1", "admin")).Data!;
        v1.Status = BudgetVersionStatus.Approved; await db.SaveChangesAsync();
        await svc.ActivateAsync(v1.Id);
        var v2 = (await svc.CreateVersionAsync(b.Id, "v2", "admin")).Data!;
        v2.Status = BudgetVersionStatus.PendingApproval; await db.SaveChangesAsync();

        await svc.ActivateFromApprovalAsync(v2.Id, "checker");
        await db.SaveChangesAsync();   // 模拟 OA 引擎最终持久化

        var rv1 = await db.BudgetVersions.FindAsync(v1.Id);
        var rv2 = await db.BudgetVersions.FindAsync(v2.Id);
        Assert.False(rv1!.IsActive);
        Assert.Equal(BudgetVersionStatus.Archived, rv1.Status);
        Assert.True(rv2!.IsActive);
        Assert.Equal(BudgetVersionStatus.Approved, rv2.Status);
    }

    [Fact]
    public async Task Activate_NonApproved_Rejected()
    {
        using var db = Db();
        var svc = new BudgetService(db, new FinSequenceService(db), new StubApprovalForTest());
        var b = (await svc.CreateBudgetAsync(new Budget { Name = "2027", FiscalYear = 2027 }, "admin")).Data!;
        var v = (await svc.CreateVersionAsync(b.Id, "v1", "admin")).Data!;   // Draft
        var r = await svc.ActivateAsync(v.Id);
        Assert.False(r.Ok);
        Assert.Equal("E-A5-VERSION-004", r.Code);
    }
}
