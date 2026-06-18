using Microsoft.EntityFrameworkCore;
using CP6.Entity.DomainModels.Mes;
using CP6.Core.Services.Mes;

namespace CP6.Tests;

public class ProcessCostRateServiceTests
{
    private static ProcessCostRateService Create(out CP6.Core.EFDbContext.CP6Context db)
    {
        db = TestHelper.CreateInMemoryContext();
        db.WorkCenters.Add(new WorkCenter { WgCd = "PRINT", Enable = true });
        db.SaveChanges();
        return new ProcessCostRateService(db);
    }

    private static ProcessCostRate Rate(string wg, decimal l, decimal o, DateTime from, DateTime? to = null)
        => new() { WgCd = wg, LaborRate = l, OverheadRate = o, ValidFrom = from, ValidTo = to };

    [Fact]
    public async Task Resolve_TakesLatestEffective()
    {
        var svc = Create(out var db);
        await svc.UpsertAsync(Rate("PRINT", 80, 120, new(2026, 1, 1), new(2026, 5, 31)), "admin");
        await svc.UpsertAsync(Rate("PRINT", 90, 130, new(2026, 6, 1)), "admin");

        var r = await svc.ResolveAsync("PRINT", new(2026, 7, 1));
        Assert.NotNull(r);
        Assert.Equal(90m, r!.LaborRate);
        Assert.Equal(130m, r.OverheadRate);
    }

    [Fact]
    public async Task Resolve_Expired_NotTaken()
    {
        var svc = Create(out _);
        await svc.UpsertAsync(Rate("PRINT", 80, 120, new(2026, 1, 1), new(2026, 5, 31)), "admin");
        Assert.Null(await svc.ResolveAsync("PRINT", new(2026, 7, 1)));
    }

    [Fact]
    public async Task Upsert_OverlappingPeriod_Throws()
    {
        var svc = Create(out _);
        await svc.UpsertAsync(Rate("PRINT", 80, 120, new(2026, 1, 1), new(2026, 6, 30)), "admin");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpsertAsync(Rate("PRINT", 90, 130, new(2026, 6, 1)), "admin"));   // 与上条 [1/1,6/30] 重叠
    }

    [Fact]
    public async Task Upsert_NegativeRate_Throws()
    {
        var svc = Create(out _);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpsertAsync(Rate("PRINT", -1, 120, new(2026, 1, 1)), "admin"));
    }

    [Fact]
    public async Task Upsert_UnknownWorkCenter_Throws()
    {
        var svc = Create(out _);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpsertAsync(Rate("NOPE", 80, 120, new(2026, 1, 1)), "admin"));
    }
}
