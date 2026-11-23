using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using CP6.WebApi.Seed;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task OaConn_real_tenant_seed_registers_manual_fire_without_granting_it()
    {
        const int flowMenuId = 734;
        const int calendarMenuId = 743;
        const int menuRole = 81;
        const int editorRole = 82;
        const string flowMenu = "oa-flow-admin";
        const string fire = "conn.trigger.fire";
        const string legacyEdit = "FlowTrigger.Edit";
        var database = await server.CreateIsolatedDatabaseAsync();
        var tenants = new[] { Guid.NewGuid(), Guid.NewGuid() };
        await using var db = server.ContextForDatabase(database, new TenantContext { CurrentTenantId = tenants[0] });
        foreach (var id in tenants)
            db.Sys_Tenants.Add(new() { Id = id, TenantCode = id.ToString("N"), TenantName = "连接器合同验收" });
        db.Sys_Menus.Add(new() { MenuId = calendarMenuId, MenuKey = "oa-work-calendar", MenuName = "工作日历", Enable = true });
        await db.SaveChangesAsync();

        OawfMenuSeed.EnsureSeeded(db);
        FlowTriggerPermissionSeed.EnsureSeeded(db);

        // 预置已采用的日历与连接器词条及授权，单独观测本次新增词条。
        (int MenuId, string Code)[] administratorActions =
        [
            (calendarMenuId, "Calendar.View"), (calendarMenuId, "Calendar.Edit"),
            (flowMenuId, "Connector.View"), (flowMenuId, "Connector.Edit"),
        ];
        IEnumerable<(int MenuId, string Code)> existingActions = administratorActions
            .Concat(ConnActions.Select(code => (MenuId: flowMenuId, Code: code)))
            .Append((calendarMenuId, "Calendar.Recalculate"));
        int[] roles = [1, menuRole, editorRole];
        foreach (var id in tenants)
        {
            await using var tenantDb = server.ContextForDatabase(database, new TenantContext { CurrentTenantId = id });
            OawfMenuSeed.EnsureSeeded(tenantDb);
            tenantDb.Sys_Roles.AddRange(roles.Select(role => new Sys_Role { RoleId = role, RoleName = $"隔离角色 {role}" }));
            tenantDb.Sys_Users.AddRange(roles.Select(role => new Sys_User
                { UserName = $"seed-role-{role}", Password = "fixture", RoleId = role }));
            tenantDb.Sys_MenuActions.AddRange(existingActions.Select(action => new Sys_MenuAction
                { MenuId = action.MenuId, ActionCode = action.Code, ActionName = action.Code }));
            tenantDb.Sys_RoleActions.AddRange(administratorActions.Select(action => new Sys_RoleAction
                { RoleId = 1, MenuId = action.MenuId, ActionCode = action.Code }));
            tenantDb.Sys_RoleActions.Add(new() { RoleId = editorRole, MenuId = flowMenuId, ActionCode = legacyEdit });
            tenantDb.Sys_RoleMenus.AddRange(
                new Sys_RoleMenu { RoleId = 1, MenuId = calendarMenuId },
                new Sys_RoleMenu { RoleId = menuRole, MenuId = flowMenuId },
                new Sys_RoleMenu { RoleId = editorRole, MenuId = flowMenuId });
            await tenantDb.SaveChangesAsync();
        }

        var menuActions = db.Sys_MenuActions.IgnoreQueryFilters();
        var roleActions = db.Sys_RoleActions.IgnoreQueryFilters().OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.TenantId, a.RoleId, a.MenuId, a.ActionCode });
        var roleMenus = db.Sys_RoleMenus.IgnoreQueryFilters().OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.TenantId, a.RoleId, a.MenuId });
        var menuActionCount = await menuActions.CountAsync();
        var originalRoleActions = await roleActions.ToArrayAsync();
        var originalRoleMenus = await roleMenus.ToArrayAsync();
        var expectedCodes = ConnActions.Append(fire).Order();
        Assert.False(await menuActions.AnyAsync(a => a.ActionCode == fire));
        Assert.All(await TriggerRuntimeRowCounts(db), count => Assert.Equal(0L, count));

        foreach (var phase in new[] { "首次", "重复" })
        {
            WorkCalendarConnectorPermissionSeed.EnsureSeeded(db);

            var fireRows = await menuActions.Where(a => a.ActionCode == fire)
                .Select(a => new { a.TenantId, a.MenuId, a.ActionName }).ToArrayAsync();
            Assert.Equal(tenants.Length, fireRows.Length);
            Assert.Equal(menuActionCount + tenants.Length, await menuActions.CountAsync());
            Assert.Equal(originalRoleActions, await roleActions.ToArrayAsync());
            Assert.Equal(originalRoleMenus, await roleMenus.ToArrayAsync());

            foreach (var id in tenants)
            {
                var row = Assert.Single(fireRows, a => a.TenantId == id);
                Assert.Equal(flowMenuId, row.MenuId);
                Assert.Equal("手动触发流程", row.ActionName);
                var codes = await menuActions
                    .Where(a => a.TenantId == id && a.MenuId == flowMenuId && a.ActionCode.StartsWith("conn."))
                    .Select(a => a.ActionCode).ToArrayAsync();
                Assert.Equal(expectedCodes, codes.Order());

                await using var tenantDb = server.ContextForDatabase(database, new TenantContext { CurrentTenantId = id });
                foreach (var role in roles)
                {
                    var user = await tenantDb.Sys_Users.SingleAsync(u => u.RoleId == role);
                    var principal = CurrentPermissionContextTests.Principal(user.Id, id);
                    var permission = new PermissionService(CurrentPermissionContextTests.Context(tenantDb, principal));
                    Assert.True(await permission.HasMenuAsync(flowMenu));
                    Assert.Equal(role != menuRole, await permission.HasActionAsync(flowMenu, legacyEdit));
                    Assert.False(await permission.HasActionAsync(flowMenu, fire));
                }
            }

            var runtimeRows = await TriggerRuntimeRowCounts(db);
            Assert.All(runtimeRows, count => Assert.Equal(0L, count));
            output.WriteLine($"{phase}播种 两租户 fire 各 1 条，角色动作及菜单增量 0，六个主体无 fire，运行记录 {string.Join(", ", runtimeRows)}");
        }
    }

    private static Task<long[]> TriggerRuntimeRowCounts(CP6Context db) => db.Database.SqlQueryRaw<long>("""
        SELECT row_count AS "Value"
        FROM (VALUES
            (1, (SELECT count(*) FROM public."Wf_FlowInstance")),
            (2, (SELECT count(*) FROM public."Wf_FlowToken")),
            (3, (SELECT count(*) FROM public."Wf_TriggerFire")),
            (4, (SELECT count(*) FROM cp6_oa_conn_v1.runtime_members WHERE kind = 'INVOCATION')),
            (5, (SELECT count(*) FROM cp6_oa_conn_v1.commands)),
            (6, (SELECT count(*) FROM cp6_oa_conn_v1.result_outbox)),
            (7, (SELECT count(*) FROM cp6_approval_intake_v2.start_outbox))
        ) AS observed(ordinal, row_count)
        ORDER BY ordinal
        """).ToArrayAsync();

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
