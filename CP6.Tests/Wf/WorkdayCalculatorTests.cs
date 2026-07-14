using System;
using System.Threading;
using System.Threading.Tasks;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using Xunit;
using static CP6.Tests.WfsInfraTestHarness;

namespace CP6.Tests;

public class WorkdayCalculatorTests
{
    // 2026-05-04(Mon,みどりの日 假日) / 05-05(Tue,こどもの日 假日) / 05-06(Wed,振替休日) / 05-07(Thu,普通工作日)
    // 2026-05-09(Sat 普通周末) / 05-10(Sun 普通周末) / 2026-05-16(Sat 补班演示)
    private static async Task SeedCalendarAsync(Microsoft.Data.Sqlite.SqliteConnection conn)
    {
        using var db = Ctx(conn);
        db.Sys_WorkCalendars.AddRange(
            new Sys_WorkCalendar { Date = new DateTime(2026, 5, 4), IsWorkday = false, Note = "みどりの日" },
            new Sys_WorkCalendar { Date = new DateTime(2026, 5, 5), IsWorkday = false, Note = "こどもの日" },
            new Sys_WorkCalendar { Date = new DateTime(2026, 5, 6), IsWorkday = false, Note = "振替休日" },
            new Sys_WorkCalendar { Date = new DateTime(2026, 5, 16), IsWorkday = true, Note = "臨時出勤" });   // 周六补班
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task IsWorkday_ExceptionReversalMatrix()
    {
        using var conn = NewSqliteWithSchema();
        await SeedCalendarAsync(conn);
        using var db = Ctx(conn);
        var cal = new WorkdayCalculator(db);

        Assert.False(await cal.IsWorkdayAsync(new DateTime(2026, 5, 4), CancellationToken.None));  // 假日（工作日却休）
        Assert.True(await cal.IsWorkdayAsync(new DateTime(2026, 5, 16), CancellationToken.None));   // 补班（周末却上班）
        Assert.False(await cal.IsWorkdayAsync(new DateTime(2026, 5, 9), CancellationToken.None));   // 普通周六
        Assert.True(await cal.IsWorkdayAsync(new DateTime(2026, 5, 7), CancellationToken.None));    // 普通周四
    }

    [Fact]
    public async Task AddWorkdays_SkipsWeekendsHolidaysAndSubstitute()
    {
        using var conn = NewSqliteWithSchema();
        await SeedCalendarAsync(conn);
        using var db = Ctx(conn);
        var cal = new WorkdayCalculator(db);

        // 起点 2026-05-01(Fri 普通工作日)，顺延 1 工作日：跳 05-02(Sat)/03(Sun,系普通周末)/04(假)/05(假)/06(振替) → 05-07(Thu)
        var r1 = await cal.AddWorkdaysAsync(new DateTime(2026, 5, 1), 1, CancellationToken.None);
        Assert.Equal(new DateTime(2026, 5, 7), r1.Date);

        // 起点 2026-05-15(Fri)，顺延 1：05-16 是补班工作日 → 命中
        var r2 = await cal.AddWorkdaysAsync(new DateTime(2026, 5, 15), 1, CancellationToken.None);
        Assert.Equal(new DateTime(2026, 5, 16), r2.Date);
    }

    [Fact]
    public async Task AddWorkdays_TimeComponentStripped_ReturnsDateMidnight()
    {
        using var conn = NewSqliteWithSchema();
        await SeedCalendarAsync(conn);
        using var db = Ctx(conn);
        var cal = new WorkdayCalculator(db);

        var r = await cal.AddWorkdaysAsync(new DateTime(2026, 5, 1, 14, 30, 0), 1, CancellationToken.None);
        Assert.Equal(new DateTime(2026, 5, 7), r);   // 当天不算、返回日期午夜
    }

    [Fact]
    public async Task AddWorkdays_NonPositive_Throws()
    {
        using var conn = NewSqliteWithSchema();
        using var db = Ctx(conn);
        var cal = new WorkdayCalculator(db);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => cal.AddWorkdaysAsync(new DateTime(2026, 1, 1), 0, CancellationToken.None));
    }

    [Fact]
    public async Task AddWorkdays_366ConsecutiveNonWorkdays_FailsFast_NoInfiniteLoop()
    {
        using var conn = NewSqliteWithSchema();
        using (var db = Ctx(conn))
        {
            // 灌满 2026-01-02 起 400 天全设假日 → 无工作日
            var start = new DateTime(2026, 1, 2);
            for (int i = 0; i < 400; i++)
                db.Sys_WorkCalendars.Add(new Sys_WorkCalendar { Date = start.AddDays(i), IsWorkday = false, Note = "灌满" });
            await db.SaveChangesAsync();
        }
        using var db2 = Ctx(conn);
        var cal = new WorkdayCalculator(db2);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => cal.AddWorkdaysAsync(new DateTime(2026, 1, 1), 1, CancellationToken.None));
        Assert.Contains("E-WF-016", ex.Message);
    }
}
