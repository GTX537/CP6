using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaNativeBrowserPostgresFact]
    public async Task Workspace_browser_actual_metrics_preference_recovery_and_tenant_deeplink()
    {
        await using var a = await Seed();
        await using var b = await Seed();
        var home = await WorkspaceQueryGrant(a, "workspace-dashboard-fixture");
        await WorkspaceQueryGrant(b, "workspace-dashboard-fixture");
        await a.Db.Sys_Menus.Where(x => x.MenuId == home).ExecuteUpdateAsync(set => set
            .SetProperty(x => x.RoutePath, "/dashboard").SetProperty(x => x.MenuName, "工作台"));
        await WorkspaceQueryGrant(a, "order-list");
        var date = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified);
        await a.Db.Orders.ExecuteUpdateAsync(set => set.SetProperty(x => x.CreateDate, date));
        var id = await a.Db.Orders.Select(x => x.Id).SingleAsync();

        using var result = await BrowserScenario(a, a.User, "workspace-browser.mjs", "workspace-browser", new()
        {
            ["WorkspaceNavigation__CrmOrigin"] = "https://crm-destination.workspace-fixture.test"
        }, new()
        {
            ["CP6_WORKSPACE_SECOND_TENANT"] = b.Tenant.ToString("N"),
            ["CP6_WORKSPACE_ORDER"] = id.ToString("D"),
        });
        Assert.Equal("PASS", result.RootElement.GetProperty("result").GetString());
        Assert.Equal(1, await a.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::integer AS "Value" FROM cp6_workspace_v1.preference_commands
            WHERE tenant_id={a.Tenant} AND subject_id={a.User}
            """).SingleAsync());
        Assert.Equal(0, await b.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::integer AS "Value" FROM cp6_workspace_v1.preference_commands
            WHERE tenant_id={b.Tenant} AND subject_id={b.User}
            """).SingleAsync());
    }
}
