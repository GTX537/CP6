using System;
using System.Threading;
using System.Threading.Tasks;
using CP6.Core.Services.Common;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.Data.Sqlite;
using Xunit;
using static CP6.Tests.WfsInfraTestHarness;

namespace CP6.Tests;

/// <summary>E-T2 时区消费点接线：workdays/untilDate 落点从「服务器本地 tz」换「租户时区」（ITenantClock），
/// 含 DST 口径（春跳缺口取下一有效瞬间 / 秋拨歧义取标准时）。日本无 DST 但字段不限日本。</summary>
public class WorkdaysTokyoTimeZoneTests
{
    // 建带指定 tz 的租户 + 时钟 + 工作日历，返回接线了 ITenantClock 的 handler。
    private static async Task<(ServiceTaskNodeHandler handler, TimeZoneInfo tz)> HandlerWithTenantTzAsync(
        SqliteConnection conn, string tzId, int fireHour = 9)
    {
        Guid tid;
        using (var db = Ctx(conn))
        {
            var t = new Sys_Tenant { TenantCode = "t", TenantName = "T", TimeZoneId = tzId };
            db.Sys_Tenants.Add(t);
            await db.SaveChangesAsync();
            tid = t.Id;
        }
        var tzCtx = new StubTenantContext { CurrentTenantId = tid };
        var clock = new TenantClock(Ctx(conn), tzCtx, new WfsInfraOptions());
        var handler = new ServiceTaskNodeHandler(Array.Empty<IServiceTaskExecutor>(),
            new WorkdayCalculator(Ctx(conn)), new WfsInfraOptions { WorkdayFireHour = fireHour }, clock);
        return (handler, clock.GetTenantTimeZone());
    }

    [Fact]
    public async Task Workdays_LandsOnFireHour_InTenantTokyoTimeZone_NotServerLocal()
    {
        using var conn = NewSqliteWithSchema();
        var (handler, tz) = await HandlerWithTenantTzAsync(conn, "Asia/Tokyo", fireHour: 9);
        var node = new FlowNode { Id = "t", Type = "serviceTask", ServiceKind = ServiceKind.Timer,
            ServiceDelayMode = "workdays", ServiceDelayValue = "1" };

        // 2026-05-01(Fri) +1 工作日 → 跳周末 → 05-04(Mon) 09:00 东京 → UTC（+9 → 前一日 00:00Z）
        var nowLocal = new DateTime(2026, 5, 1, 14, 0, 0, DateTimeKind.Unspecified);
        var due = await handler.ComputeTimerDueUtcForTestAsync(node, "{}", nowLocal, CancellationToken.None);

        var expected = TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(2026, 5, 4, 9, 0, 0, DateTimeKind.Unspecified), tz);
        Assert.Equal(expected, due);
        Assert.Equal(new DateTime(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc), due);   // JST 09:00 == 00:00Z
    }

    [Fact]
    public async Task UntilDate_DstSpringForwardGap_TakesNextValidInstant()
    {
        using var conn = NewSqliteWithSchema();
        var (handler, _) = await HandlerWithTenantTzAsync(conn, "America/New_York");
        // 2026-03-08 02:30 落在春跳缺口（02:00 EST→03:00 EDT 之间，本地时刻不存在）→ 取下一有效（+DST 偏移 1h → 03:30 EDT）
        var node = new FlowNode { Id = "t", Type = "serviceTask", ServiceKind = ServiceKind.Timer,
            ServiceDelayMode = "untilDate", ServiceDelayValue = "2026-03-08 02:30" };

        var due = await handler.ComputeTimerDueUtcForTestAsync(node, "{}", DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(new DateTime(2026, 3, 8, 7, 30, 0, DateTimeKind.Utc), due);   // 03:30 EDT(-4) == 07:30Z
    }

    [Fact]
    public async Task UntilDate_DstFallBackAmbiguous_TakesStandardTime()
    {
        using var conn = NewSqliteWithSchema();
        var (handler, _) = await HandlerWithTenantTzAsync(conn, "America/New_York");
        // 2026-11-01 01:30 落在秋拨歧义区（01:00-02:00 出现两次）→ 取标准时（EST -5）
        var node = new FlowNode { Id = "t", Type = "serviceTask", ServiceKind = ServiceKind.Timer,
            ServiceDelayMode = "untilDate", ServiceDelayValue = "2026-11-01 01:30" };

        var due = await handler.ComputeTimerDueUtcForTestAsync(node, "{}", DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(new DateTime(2026, 11, 1, 6, 30, 0, DateTimeKind.Utc), due);   // 01:30 EST(-5) == 06:30Z
    }
}
