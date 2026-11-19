using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Workspace_menu_definition_http_checks_route_namespace_version_and_original_result()
    {
        await using var workspace = await TreeSeed();
        await using var other = await Seed();
        var ids = await TreeNodes(workspace);
        other.Db.Sys_RoleMenus.Add(new() { RoleId = 72, MenuId = ids[1] });
        await other.Db.SaveChangesAsync();
        var version = (await TreeRead(workspace)).Revision;
        await using var host = await AuthenticationApiProcess.Start(workspace.Db.Database.GetConnectionString()!);
        var admin = await Login(host, workspace);
        var outsider = await Login(host, other);

        object Command(string operation, string? route, string? menuKey) => new
        {
            operationRef = operation, expectedRevision = version.ToString(), kind = "update", menuId = ids[1],
            value = new { menuName = "已登记工作台", routePath = route, menuKey, icon = (string?)null,
                parentId = ids[0], orderNo = 1, enable = true },
            reason = "工作台菜单验收"
        };
        var operation = Guid.NewGuid().ToString("N");
        var valid = Command(operation, "/dashboard", null);
        const string path = "api/menu/tree-v1/definition/commands";

        using (var invalid = await host.Send(OperationsRequest(admin, HttpMethod.Post, path,
            Command(Guid.NewGuid().ToString("N"), "https://workspace-fixture.invalid/view", null))))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Contains("MENU_DEFINITION_ROUTE_UNKNOWN", await invalid.Content.ReadAsStringAsync());
        }
        using (var renamed = await host.Send(OperationsRequest(admin, HttpMethod.Post, path,
            Command(Guid.NewGuid().ToString("N"), "/dashboard", "workspace-fixture"))))
        {
            Assert.Equal(HttpStatusCode.Conflict, renamed.StatusCode);
            Assert.Contains("MENU_DEFINITION_KEY_REFERENCED", await renamed.Content.ReadAsStringAsync());
        }
        Assert.Equal(version, (await TreeRead(workspace)).Revision);
        using (var denied = await host.Send(OperationsRequest(outsider, HttpMethod.Post, path, valid)))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        MenuTreeReceiptV1 saved;
        using (var response = await host.Send(OperationsRequest(admin, HttpMethod.Post, path, valid)))
            saved = await NumberHttpData<MenuTreeReceiptV1>(response);
        Assert.True(saved.AppliedRevision > version);
        using (var recovered = await host.Send(OperationsRequest(admin, HttpMethod.Post,
            "api/menu/tree-v1/commands/read", new { operationRef = operation })))
            SameTreeReceipt(saved, await NumberHttpData<MenuTreeReceiptV1>(recovered));
        using (var stale = await host.Send(OperationsRequest(admin, HttpMethod.Post, path,
            Command(Guid.NewGuid().ToString("N"), "/order-list", null))))
            Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        var current = await TreeRead(workspace);
        Assert.Equal("/dashboard", current.Menus.Single(m => m.MenuId == ids[1]).RoutePath);
    }

    [CurrentPermissionPostgresFact]
    public async Task Workspace_menu_delete_checks_all_tenant_references_before_removing_nodes()
    {
        await using var workspace = await TreeSeed();
        await using var other = await Seed();
        var ids = await TreeNodes(workspace);
        var editor = await workspace.Db.Sys_Menus.SingleAsync(m => m.MenuKey == "menu");
        workspace.Db.Sys_MenuActions.Add(new() { MenuId = editor.MenuId, ActionCode = "delete", ActionName = "删除" });
        workspace.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = editor.MenuId, ActionCode = "delete" });
        other.Db.Sys_RoleMenus.Add(new() { RoleId = 72, MenuId = ids[1] });
        await workspace.Db.SaveChangesAsync();
        await other.Db.SaveChangesAsync();
        workspace.Db.ChangeTracker.Clear();
        other.Db.ChangeTracker.Clear();
        await workspace.Db.Sys_Menus.Where(x => x.MenuId == ids[1]).ExecuteUpdateAsync(set => set.SetProperty(x => x.RoutePath, "/dashboard"));
        var snapshot = await TreeRead(workspace);
        await using var host = await AuthenticationApiProcess.Start(workspace.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, workspace);

        object Delete(int id) => new { operationRef = Guid.NewGuid().ToString("N"), expectedRevision = snapshot.Revision.ToString(),
            kind = "delete", menuId = id, value = (object?)null, reason = "共享菜单引用验收" };
        const string commands = "api/menu/tree-v1/definition/commands";
        foreach (var id in ids.Take(2))
        {
            using var blocked = await host.Send(OperationsRequest(cookies, HttpMethod.Post, commands, Delete(id)));
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Contains("MENU_DEFINITION_REFERENCED", await blocked.Content.ReadAsStringAsync());
        }
        Assert.Equal(snapshot.Revision, (await TreeRead(workspace)).Revision);
        Assert.True(await other.Db.Sys_RoleMenus.AsNoTracking().AnyAsync(m => m.RoleId == 72 && m.MenuId == ids[1]));

        var sibling = ids[2];
        using (var stillEnabled = await host.Send(OperationsRequest(cookies, HttpMethod.Post, commands, Delete(sibling))))
        {
            Assert.Equal(HttpStatusCode.Conflict, stillEnabled.StatusCode);
            Assert.Contains("MENU_DEFINITION_DISABLE_FIRST", await stillEnabled.Content.ReadAsStringAsync());
        }
        var disable = new { operationRef = Guid.NewGuid().ToString("N"), expectedRevision = snapshot.Revision.ToString(),
            kind = "update", menuId = ids[1], value = new { menuName = "Child", routePath = "/dashboard", menuKey = (string?)null,
                icon = (string?)null, parentId = ids[0], orderNo = 1, enable = false },
            reason = "停用保留历史引用" };
        var otherUser = await Login(host, other);
        const string navigation = "api/dashboard/scoped-v1/navigation";
        WorkspaceNavigationSnapshotV1 prior;
        using (var read = await host.Send(OperationsRequest(otherUser, HttpMethod.Get, navigation)))
        {
            Assert.True(read.StatusCode == HttpStatusCode.OK, await OperationsFailure(read));
            prior = (await read.Content.ReadFromJsonAsync<WorkspaceNavigationSnapshotV1>())!;
            Assert.Contains(prior.Menus, menu => menu.Id == ids[1] && menu.RoutePath == "/dashboard");
        }
        using (var saved = await host.Send(OperationsRequest(cookies, HttpMethod.Post, commands, disable)))
            Assert.True(saved.StatusCode == HttpStatusCode.OK, await OperationsFailure(saved));
        Assert.False((await TreeRead(workspace)).Menus.Single(x => x.MenuId == ids[1]).Enable);
        Assert.True(await other.Db.Sys_RoleMenus.AsNoTracking().AnyAsync(m => m.RoleId == 72 && m.MenuId == ids[1]));
        using (var read = await host.Send(OperationsRequest(otherUser, HttpMethod.Get, navigation)))
        {
            Assert.True(read.StatusCode == HttpStatusCode.OK, await OperationsFailure(read));
            var nav = await read.Content.ReadFromJsonAsync<WorkspaceNavigationSnapshotV1>();
            Assert.DoesNotContain(nav!.Menus, menu => menu.Id == ids[1]);
            Assert.NotEqual(prior.MenuVersion, nav.MenuVersion);
        }
        Assert.True(await workspace.Db.Sys_Menus.AsNoTracking().AnyAsync(m => m.MenuId == ids[1]));
    }
}
