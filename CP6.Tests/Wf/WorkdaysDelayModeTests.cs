using System;
using System.Threading;
using System.Threading.Tasks;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.Data.Sqlite;
using Xunit;
using static CP6.Tests.WfsInfraTestHarness;

namespace CP6.Tests;

public class WorkdaysDelayModeTests
{
    private static async Task<ServiceTaskNodeHandler> HandlerAsync(SqliteConnection conn, int fireHour = 9)
    {
        // 2026-05-04/05/06 假；此测试用服务器本地 tz 作 app 默认（I-A 口径）
        using (var db = Ctx(conn))
        {
            db.Sys_WorkCalendars.AddRange(
                new Sys_WorkCalendar { Date = new DateTime(2026, 5, 4), IsWorkday = false },
                new Sys_WorkCalendar { Date = new DateTime(2026, 5, 5), IsWorkday = false },
                new Sys_WorkCalendar { Date = new DateTime(2026, 5, 6), IsWorkday = false });
            await db.SaveChangesAsync();
        }
        var calDb = Ctx(conn);
        return new ServiceTaskNodeHandler(Array.Empty<IServiceTaskExecutor>(),
            new WorkdayCalculator(calDb), new WfsInfraOptions { WorkdayFireHour = fireHour });
    }

    [Fact]
    public async Task ComputeWorkdaysDue_LandsOnFireHour_ServerLocalToUtc()
    {
        using var conn = NewSqliteWithSchema();
        var handler = await HandlerAsync(conn, fireHour: 9);
        var node = new FlowNode { Id = "t", Type = "serviceTask", ServiceKind = ServiceKind.Timer,
            ServiceDelayMode = "workdays", ServiceDelayValue = "1" };

        // 从固定本地 now=2026-05-01T14:00(Fri) 顺延 1 工作日 → 05-07(Thu) 09:00 本地 → UTC
        var nowLocal = new DateTime(2026, 5, 1, 14, 0, 0, DateTimeKind.Unspecified);
        var due = await handler.ComputeTimerDueUtcForTestAsync(node, "{}", nowLocal, CancellationToken.None);

        var expectedLocal = new DateTime(2026, 5, 7, 9, 0, 0, DateTimeKind.Unspecified);
        var expectedUtc = TimeZoneInfo.ConvertTimeToUtc(expectedLocal, TimeZoneInfo.Local);
        Assert.Equal(expectedUtc, due);
    }

    [Fact]
    public async Task ComputeWorkdaysDue_NonPositiveValue_DegradesToImmediate()
    {
        using var conn = NewSqliteWithSchema();
        var handler = await HandlerAsync(conn);
        var node = new FlowNode { Id = "t", Type = "serviceTask", ServiceKind = ServiceKind.Timer,
            ServiceDelayMode = "workdays", ServiceDelayValue = "0" };
        var nowLocal = new DateTime(2026, 5, 1, 14, 0, 0, DateTimeKind.Unspecified);

        var due = await handler.ComputeTimerDueUtcForTestAsync(node, "{}", nowLocal, CancellationToken.None);

        // 非正整数 → 降级立即（now 的 UTC，容 2s 抖动）
        Assert.True((DateTime.UtcNow - due).Duration() < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ComputeDueUtc_ExistingThreeModes_ByteEquivalent()
    {
        // 既有三模式静态方法零回归
        var dur = new FlowNode { ServiceDelayMode = "duration", ServiceDelayValue = "2h" };
        var due = ServiceTaskNodeHandler.ComputeDueUtc(dur, "{}");
        Assert.True(due > DateTime.UtcNow.AddMinutes(110) && due < DateTime.UtcNow.AddMinutes(130));

        var none = new FlowNode { ServiceDelayMode = "duration", ServiceDelayValue = null };
        Assert.True((ServiceTaskNodeHandler.ComputeDueUtc(none, "{}") - DateTime.UtcNow).Duration() < TimeSpan.FromSeconds(5));
    }
}
