using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task OaConn_real_tenant_seed_registers_actions_without_granting_review_or_activation()
    {
        var database = await server.CreateIsolatedDatabaseAsync();
        var tenantId = Guid.NewGuid();
        var secondTenantId = Guid.NewGuid();
        await using var db = server.ContextForDatabase(database,
            new CP6.Core.Services.Common.TenantContext { CurrentTenantId = tenantId });
        foreach (var id in new[] { tenantId, secondTenantId })
            db.Sys_Tenants.Add(new() { Id = id, TenantCode = id.ToString("N"), TenantName = "连接器合同验收" });
        await db.SaveChangesAsync();

        CP6.WebApi.Seed.OawfMenuSeed.EnsureSeeded(db);
        CP6.WebApi.Seed.WorkCalendarConnectorPermissionSeed.EnsureSeeded(db);
        CP6.WebApi.Seed.WorkCalendarConnectorPermissionSeed.EnsureSeeded(db);

        foreach (var id in new[] { tenantId, secondTenantId })
        {
            var codes = await db.Sys_MenuActions.IgnoreQueryFilters()
                .Where(a => a.TenantId == id && a.MenuId == 734 && a.ActionCode.StartsWith("conn."))
                .Select(a => a.ActionCode).ToArrayAsync();
            Assert.Equal(ConnActions.Order(), codes.Order());
            Assert.False(await db.Sys_RoleActions.IgnoreQueryFilters()
                .AnyAsync(a => a.TenantId == id && a.MenuId == 734 && a.ActionCode.StartsWith("conn.")));
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task OaConn_options_require_current_read_and_return_only_registered_choices()
    {
        await using var h = await Seed();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,
            new Dictionary<string,string> { ["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D") });
        var token = await Login(host, h);
        using var denied = await host.Send(OperationsRequest(token, HttpMethod.Get, "api/oa/conn/v1/options?kind=CONNECTOR"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await EngineeringAction(h, "oa-flow-admin", "conn.read");
        var menu = await h.Db.Sys_Menus.SingleAsync(m => m.MenuKey == "oa-flow-admin");
        if (!await h.Db.Sys_RoleMenus.AnyAsync(m => m.RoleId == 72 && m.MenuId == menu.MenuId))
        {
            h.Db.Sys_RoleMenus.Add(new() { RoleId = 72, MenuId = menu.MenuId });
            await h.Db.SaveChangesAsync();
        }
        token = await Login(host, h);
        using var allowed = await host.Send(OperationsRequest(token, HttpMethod.Get, "api/oa/conn/v1/options?kind=CONNECTOR"));
        Assert.True(allowed.IsSuccessStatusCode, await OperationsFailure(allowed));
        var json = await allowed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(json.GetProperty("data").GetProperty("profiles").EnumerateArray());
    }
}
