using Microsoft.EntityFrameworkCore;
using CP6.Entity.DomainModels.Mes;
using CP6.Core.Services.Mes;

namespace CP6.Tests;

public class WorkCenterServiceTests
{
    private static WorkCenterService Create(out CP6.Core.EFDbContext.CP6Context db)
    {
        db = TestHelper.CreateInMemoryContext();
        return new WorkCenterService(db);
    }

    [Fact]
    public async Task Upsert_Insert_ThenUpdate_SameWgCd()
    {
        var svc = Create(out var db);
        await svc.UpsertAsync(new WorkCenter { WgCd = "PRINT", WgName = "印刷", DailyCapacityHours = 16 }, "admin");
        await svc.UpsertAsync(new WorkCenter { WgCd = "PRINT", WgName = "印刷機", DailyCapacityHours = 20 }, "admin");

        var rows = await db.WorkCenters.Where(x => x.WgCd == "PRINT" && !x.IsDeleted).ToListAsync();
        Assert.Single(rows);
        Assert.Equal("印刷機", rows[0].WgName);
        Assert.Equal(20m, rows[0].DailyCapacityHours);
    }

    [Fact]
    public async Task Upsert_NegativeCapacity_Throws()
    {
        var svc = Create(out _);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpsertAsync(new WorkCenter { WgCd = "X", DailyCapacityHours = -1 }, "admin"));
    }

    [Fact]
    public async Task List_FiltersKeyword_ExcludesDeleted()
    {
        var svc = Create(out _);
        await svc.UpsertAsync(new WorkCenter { WgCd = "PRINT" }, "admin");
        await svc.UpsertAsync(new WorkCenter { WgCd = "DIECUT" }, "admin");
        await svc.DeleteAsync("DIECUT", "admin");

        var all = await svc.ListAsync(null);
        Assert.Single(all);
        Assert.Equal("PRINT", all[0].WgCd);
    }
}
