using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CP6.Core.Services.Wf;
using Xunit;
using static CP6.Tests.WfsInfraTestHarness;

namespace CP6.Tests;

public class WorkCalendarServiceTests
{
    [Fact]
    public async Task ImportJapaneseHolidays_Idempotent_35Rows()
    {
        using var conn = NewSqliteWithSchema();
        using var db = Ctx(conn);
        var svc = new WorkCalendarService(db);

        var n1 = await svc.ImportJapaneseHolidaysAsync(CancellationToken.None);
        var n2 = await svc.ImportJapaneseHolidaysAsync(CancellationToken.None);   // 第二次全命中不重复

        Assert.Equal(35, n1);
        Assert.Equal(0, n2);
        Assert.Equal(35, db.Sys_WorkCalendars.Count());
    }

    [Fact]
    public async Task IsEmpty_TrueBeforeImport_FalseAfter()
    {
        using var conn = NewSqliteWithSchema();
        using var db = Ctx(conn);
        var svc = new WorkCalendarService(db);
        Assert.True(await svc.IsEmptyAsync(CancellationToken.None));
        await svc.ImportJapaneseHolidaysAsync(CancellationToken.None);
        Assert.False(await svc.IsEmptyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ToggleDay_InsertsThenReverses_ThenRemovesOnBackToDefault()
    {
        using var conn = NewSqliteWithSchema();
        using var db = Ctx(conn);
        var svc = new WorkCalendarService(db);
        var sat = new DateTime(2026, 5, 16);   // 周六，默认非工作日

        await svc.SetDayAsync(sat, isWorkday: true, note: "補班", CancellationToken.None);   // 反转为补班
        Assert.Single(db.Sys_WorkCalendars.Where(c => c.Date == sat));
        Assert.True(db.Sys_WorkCalendars.Single(c => c.Date == sat).IsWorkday);

        await svc.ClearDayAsync(sat, CancellationToken.None);   // 回归默认 → 删除例外行
        Assert.Empty(db.Sys_WorkCalendars.Where(c => c.Date == sat));
    }

    [Fact]
    public async Task ListYear_ReturnsOnlyThatYear()
    {
        using var conn = NewSqliteWithSchema();
        using var db = Ctx(conn);
        var svc = new WorkCalendarService(db);
        await svc.ImportJapaneseHolidaysAsync(CancellationToken.None);
        var y2026 = await svc.ListYearAsync(2026, CancellationToken.None);
        Assert.Equal(18, y2026.Count);
        Assert.All(y2026, r => Assert.Equal(2026, r.Date.Year));
    }
}
