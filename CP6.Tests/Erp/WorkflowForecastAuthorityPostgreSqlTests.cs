using System.Net;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string CronExplanationRoute = "/api/oa/work-calendar/versions/cron-explanations";
    private const string LegacyCronExplanationRoute = "/api/oa/flow-triggers/cron-preview";

    private static async Task<int> ForecastActionFixture(Harness h, bool? grant = true)
    {
        // 仅为本次隔离 PostgreSQL 的合成租户布置权限词条和授权。
        const string menuKey = ConnectorVersionControlService.Menu;
        const string action = WorkflowForecastAuthorityV1.Action;
        await EngineeringAction(h, menuKey, "FlowTrigger.View");
        var menuId = await h.Db.Sys_Menus.Where(m => m.MenuKey == menuKey).Select(m => m.MenuId).SingleAsync();
        if (!await h.Db.Sys_RoleMenus.AnyAsync(m => m.MenuId == menuId && m.RoleId == 72))
        {
            h.Db.Sys_RoleMenus.Add(new() { MenuId = menuId, RoleId = 72 });
            await h.Db.SaveChangesAsync();
            h.Db.ChangeTracker.Clear();
        }
        if (grant == true)
            await EngineeringAction(h, menuKey, action);
        else if (grant == false)
        {
            h.Db.Sys_MenuActions.Add(new() { MenuId = menuId, ActionCode = action, ActionName = "隔离预演夹具" });
            await h.Db.SaveChangesAsync();
            h.Db.ChangeTracker.Clear();
        }
        return menuId;
    }

    private static CronExplanationInputV1 CronAuthorityInput(JsonElement year) =>
        new("30 1 * * *", year.GetProperty("calendarId").GetGuid(), year.GetProperty("revision").GetInt64(),
            year.GetProperty("timeZoneId").GetString()!, year.GetProperty("timeZoneRulesVersion").GetString()!,
            new DateTimeOffset(2027, 11, 7, 4, 0, 0, TimeSpan.Zero));

    [CurrentPermissionPostgresFact]
    public async Task Forecast_authority_requires_exact_owner_registration_then_current_action_and_resource_reads()
    {
        await using var h = await Seed();
        var owner = await PrepareAuthoring(h);
        await CalendarPermissions(h);
        await TimeoutZone(h);
        // 在同一租户授予另一菜单下的同名动作，核对它不等价于连接器 Owner 的登记。
        await EngineeringAction(h, "oa-work-calendar", WorkflowForecastAuthorityV1.Action);
        await ForecastActionFixture(h, grant: null);
        var draft = (await owner.SaveAsync("forecast-authority", AuthoringCommand())).Result!;
        var before = await ForecastReadState(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, CalendarHostSettings());
        var cookies = await Login(host, h);
        var api = new DeadlineApi(host, cookies);
        var cron = CronAuthorityInput(await api.Data(VersionCalendarApi + "?year=2027"));
        const string definition = ForecastRoot + "forecast-authority";
        const string privateInput = "isolated-private-forecast-marker";
        var request = DraftForecast(draft, $$"""{"memo":"{{privateInput}}"}""");
        var reads = new (string Route, object Input)[]
        {
            (definition + "/forecast", request), (definition + "/forecast/export", request),
            (definition + "/simulate", new { draftVersionId = draft.DraftId, expectedVersion = draft.Version, varsJson = "{}" }),
            (CronExplanationRoute, cron), (LegacyCronExplanationRoute, cron),
        };
        async Task RequireRejection(HttpStatusCode status, string code)
        {
            foreach (var (route, body) in reads)
            {
                using var refused = await host.Send(BoundRequest(cookies, route, body));
                var response = await refused.Content.ReadAsStringAsync();
                Assert.True(refused.StatusCode == status, route + " " + refused.StatusCode + " " + response);
                Assert.Contains(code, response);
                Assert.DoesNotContain(privateInput, response);
                Assert.DoesNotContain(cron.TimeZoneRulesVersion, response);
                Assert.DoesNotContain(draft.DraftId.ToString("D"), response);
                Assert.True(refused.Headers.CacheControl?.NoStore);
            }
        }

        await RequireRejection(HttpStatusCode.ServiceUnavailable, "FORECAST_AUTHORITY_UNPROVEN");
        await ForecastActionFixture(h, grant: false);
        await RequireRejection(HttpStatusCode.Forbidden, "FORECAST_ACTION_FORBIDDEN");
        await ForecastActionFixture(h);
        Assert.Equal("DRAFT_SIMULATION", (await api.Data(definition + "/forecast", request)).GetProperty("source").GetString());
        Assert.Equal(5, (await api.Data(CronExplanationRoute, cron)).GetProperty("points").GetArrayLength());

        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = FlowAuthoringOwnerV2.Resource, FieldName = "FormKey", Access = 3 });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        await api.Reject(definition + "/forecast", request, HttpStatusCode.Forbidden, "FIELD_READ_FORBIDDEN");
        await api.Reject(definition + "/forecast/export", request, HttpStatusCode.Forbidden, "FIELD_READ_FORBIDDEN");
        await api.Reject(definition + "/simulate", reads[2].Input, HttpStatusCode.Forbidden, "FIELD_READ_FORBIDDEN");

        var calendarMenu = await h.Db.Sys_Menus.Where(m => m.MenuKey == "oa-work-calendar").Select(m => m.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == calendarMenu && x.RoleId == 72 && x.ActionCode == "Calendar.View").ExecuteDeleteAsync();
        await api.Reject(CronExplanationRoute, cron, HttpStatusCode.Forbidden, "ACTION_FORBIDDEN");
        Assert.Equal(before, await ForecastReadState(h));
        output.WriteLine("实际 Main 两个预演入口、导出与两个 Cron 入口：仅设计器/日历/旧查看或别菜单同名均不放行。登记而未授予返回 403；确切动作不替代资源、字段读权。");
    }

    [CurrentPermissionPostgresFact]
    public async Task Forecast_action_revocation_waits_and_rejects_queued_export_and_cron()
    {
        await using var h = await Seed();
        var owner = await PrepareAuthoring(h);
        await CalendarPermissions(h);
        await TimeoutZone(h);
        var menuId = await ForecastActionFixture(h);
        var draft = (await owner.SaveAsync("forecast-current-action", AuthoringCommand())).Result!;
        var before = await ForecastReadState(h);
        var app = "forecast-action-r2-" + Guid.NewGuid().ToString("N");
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()! + ";Application Name=" + app,
            CalendarHostSettings());
        var cookies = await Login(host, h);
        var api = new DeadlineApi(host, cookies);
        var cron = CronAuthorityInput(await api.Data(VersionCalendarApi + "?year=2027"));
        var input = DraftForecast(draft, """{"memo":"isolated-in-flight-wp1215"}""");
        var export = ForecastRoot + "forecast-current-action/forecast/export";
        Assert.Equal("EXPLANATION_ONLY", (await api.Data(export, input)).GetProperty("mode").GetString());
        Assert.Equal(5, (await api.Data(CronExplanationRoute, cron)).GetProperty("points").GetArrayLength());

        await using var writer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        await using var tx = await writer.Database.BeginTransactionAsync();
        await AuthenticationCompletionGateV1.AcquireAsync(writer, default);
        Assert.Equal(1, await writer.Sys_RoleActions.Where(a => a.RoleId == 72 && a.MenuId == menuId &&
            a.ActionCode == WorkflowForecastAuthorityV1.Action).ExecuteDeleteAsync());
        var pendingExport = host.Send(BoundRequest(cookies, export, input));
        var pendingCron = host.Send(BoundRequest(cookies, CronExplanationRoute, cron));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM pg_stat_activity WHERE application_name={app}
                AND wait_event_type='Lock' AND state='active'
            """).SingleAsync(timeout.Token) < 2)
        {
            Assert.False(pendingExport.IsCompleted);
            Assert.False(pendingCron.IsCompleted);
            await Task.Delay(50, timeout.Token);
        }
        await tx.CommitAsync();
        using var deniedExport = await pendingExport;
        using var deniedCron = await pendingCron;
        foreach (var response in new[] { deniedExport, deniedCron })
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("FORECAST_ACTION_FORBIDDEN", body);
            Assert.DoesNotContain("isolated-in-flight-wp1215", body);
            Assert.DoesNotContain(cron.TimeZoneRulesVersion, body);
            Assert.DoesNotContain(draft.DraftId.ToString("D"), body);
            Assert.True(response.Headers.CacheControl?.NoStore);
        }
        Assert.Equal(before, await ForecastReadState(h));
        Assert.Equal(h.Tenant, (await api.Data(VersionCalendarApi + "?year=2027")).GetProperty("calendarId").GetGuid());
        output.WriteLine("同一真实 Main HTTP 进程的导出、Cron 请求同时等待当前 IAM 根锁。合成角色撤 conn.forecast 提交后均 403/no-store，Calendar.View 原年历仍可读。");
    }
}
