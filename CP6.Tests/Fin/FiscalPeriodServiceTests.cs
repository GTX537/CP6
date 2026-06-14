using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Fin;

/// <summary>财务章02 C-1：会计期间归期（财年起始月可配）+ EnsureOpen/IsOpen/Previous。</summary>
public class FiscalPeriodServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    [Fact]
    public void ComputeFiscal_JapanAprilStart()
    {
        var svc = new FiscalPeriodService(NewDb(), fiscalYearStartMonth: 4);
        Assert.Equal((2026, 1), svc.ComputeFiscal(2026, 4));    // 财年第1期
        Assert.Equal((2026, 9), svc.ComputeFiscal(2026, 12));
        Assert.Equal((2026, 12), svc.ComputeFiscal(2027, 3));   // 跨日历年仍属 FY2026 第12期
        Assert.Equal((2027, 1), svc.ComputeFiscal(2027, 4));    // 新财年
    }

    [Fact]
    public void ComputeFiscal_CalendarYearDefault()
    {
        var svc = new FiscalPeriodService(NewDb(), fiscalYearStartMonth: 1);
        Assert.Equal((2026, 6), svc.ComputeFiscal(2026, 6));
        Assert.Equal((2026, 1), svc.ComputeFiscal(2026, 1));
        Assert.Equal((2026, 12), svc.ComputeFiscal(2026, 12));
    }

    [Fact]
    public async Task EnsureOpen_CreatesPeriod_WithCorrectBoundsAndFiscal()
    {
        using var db = NewDb();
        var svc = new FiscalPeriodService(db, fiscalYearStartMonth: 4);

        var p = await svc.EnsureOpenAsync(new DateTime(2026, 6, 15), "u1");

        Assert.Equal(2026, p.Year);
        Assert.Equal(6, p.Month);
        Assert.Equal(2026, p.FiscalYear);
        Assert.Equal(3, p.PeriodNo);                           // 4月起 → 6月是第3期
        Assert.Equal(new DateTime(2026, 6, 1), p.PeriodStart);
        Assert.Equal(new DateTime(2026, 6, 30), p.PeriodEnd);
        Assert.Equal(PeriodStatus.Open, p.Status);
    }

    [Fact]
    public async Task EnsureOpen_Idempotent_SameDateReturnsSameRow()
    {
        using var db = NewDb();
        var svc = new FiscalPeriodService(db, 1);
        var p1 = await svc.EnsureOpenAsync(new DateTime(2026, 6, 10));
        var p2 = await svc.EnsureOpenAsync(new DateTime(2026, 6, 25));   // 同月
        Assert.Equal(p1.Id, p2.Id);
        Assert.Equal(1, await db.FiscalPeriods.CountAsync());
    }

    [Fact]
    public async Task EnsureOpen_MonthRollover_Normalizes()
    {
        using var db = NewDb();
        var svc = new FiscalPeriodService(db, 1);
        var p = await svc.EnsureOpenAsync(2026, 13);            // 13月 → 2027-01
        Assert.Equal(2027, p.Year);
        Assert.Equal(1, p.Month);
    }

    [Fact]
    public async Task IsOpen_And_Previous()
    {
        using var db = NewDb();
        var svc = new FiscalPeriodService(db, 1);
        var may = await svc.EnsureOpenAsync(new DateTime(2026, 5, 1));
        var jun = await svc.EnsureOpenAsync(new DateTime(2026, 6, 1));

        Assert.True(await svc.IsOpenAsync(jun.Id));
        var prev = await svc.PreviousAsync(jun.Id);
        Assert.Equal(may.Id, prev!.Id);

        // 锁期后 IsOpen=false
        jun.Status = PeriodStatus.Closed;
        await db.SaveChangesAsync();
        Assert.False(await svc.IsOpenAsync(jun.Id));
    }
}
