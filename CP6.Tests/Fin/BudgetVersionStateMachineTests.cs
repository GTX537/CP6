using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Fin;

public class BudgetVersionStateMachineTests
{
    private static CP6Context Db() => TestHelper.CreateInMemoryContext();

    [Fact]
    public async Task CreateBudget_DuplicateFiscalYear_Rejected()
    {
        using var db = Db();
        var svc = new BudgetService(db, new FinSequenceService(db));
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
        var svc = new BudgetService(db, new FinSequenceService(db));
        var b = (await svc.CreateBudgetAsync(new Budget { Name = "2027", FiscalYear = 2027 }, "admin")).Data!;
        var v1 = (await svc.CreateVersionAsync(b.Id, "初稿", "admin")).Data!;
        var v2 = (await svc.CreateVersionAsync(b.Id, "调整", "admin")).Data!;
        Assert.Equal(1, v1.VersionNo);
        Assert.Equal(2, v2.VersionNo);
        Assert.Equal(BudgetVersionStatus.Draft, v2.Status);
    }
}
