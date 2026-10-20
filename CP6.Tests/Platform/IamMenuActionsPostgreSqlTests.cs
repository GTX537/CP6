using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Menu_action_lost_ack_recovers_once_and_closed_operation_cannot_write()
    {
        var fault = new CommitFault(); await using var h = await Seed(fault);
        var session = PolicySession(h); var domain = await PolicyDomain(h);
        var editor = await session.Service.ReadMenuActionsAsync(domain.Menu, h.Actor, default);
        var command = new IamMenuActionCommandV1
        {
            OperationId = Guid.NewGuid(), MenuId = domain.Menu, ExpectedVersion = editor.Version,
            ExpectedReferencesVersion = editor.ReferencesVersion, ExpectedCatalogVersion = editor.CatalogVersion,
            Reason = "Owned action definition test", Actions = [new() { ActionCode = "read", ActionName = "Updated", Sort = int.MinValue }]
        };
        fault.Armed = true;
        Assert.Equal("IAM_OPERATION_UNKNOWN", (await Assert.ThrowsAsync<IamAdministrationExceptionV1>(
            () => session.Service.ChangeMenuActionsAsync(command, h.Actor, default))).Message);
        var original = await session.Authority.Resolve(command.OperationId, "menu-actions", true, h.Actor, default);
        Assert.Equal("COMMITTED", original.State);
        Assert.Equal(original, await session.Service.ChangeMenuActionsAsync(command, h.Actor, default));
        Assert.Equal(1, await h.Db.Sys_SecurityLogs.CountAsync(x => x.Reason == command.Reason));
        Assert.Equal(int.MinValue, await h.Db.Sys_MenuActions.Where(x => x.MenuId == domain.Menu).Select(x => x.Sort).SingleAsync());
        var closed = command with { OperationId = Guid.NewGuid(), Actions = [] };
        await session.Authority.Resolve(closed.OperationId, "menu-actions", true, h.Actor, default);
        Assert.Equal("IAM_OPERATION_CLOSED", (await Assert.ThrowsAsync<IamAdministrationExceptionV1>(
            () => session.Service.ChangeMenuActionsAsync(closed, h.Actor, default))).Message);
        Assert.True(await h.Db.Sys_MenuActions.AnyAsync(x => x.MenuId == domain.Menu));
    }

    [CurrentPermissionPostgresFact]
    public async Task Actual_menu_action_HTTP_requires_CSRF_current_versions_and_unreferenced_removal()
    {
        await using var h = await Seed(); await GrantPolicyHttpPermissions(h); var domain = await PolicyDomain(h);
        h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = domain.Menu, ActionCode = "read" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, IdentityEnvironment(h.Tenant));
        var cookies = await Login(host, h); var path = $"api/iam/role-policies/menu-actions/{domain.Menu}";
        using var read = await host.Send(Request(HttpMethod.Get, path + "/editor", cookies));
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var editor = (await read.Content.ReadFromJsonAsync<IamMenuActionEditorV1>())!;
        var command = new IamMenuActionCommandV1
        {
            OperationId = Guid.NewGuid(), MenuId = domain.Menu, ExpectedVersion = editor.Version,
            ExpectedReferencesVersion = editor.ReferencesVersion, ExpectedCatalogVersion = editor.CatalogVersion,
            Reason = "Owned HTTP action definition", Actions = []
        };
        using (var csrf = await host.Send(Request(HttpMethod.Put, path, cookies, command, false)))
            Assert.Equal(HttpStatusCode.Forbidden, csrf.StatusCode);
        using (var mismatch = await host.Send(Request(HttpMethod.Put, path, cookies, command with { MenuId = domain.Menu + 1 })))
            Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        using (var referenced = await host.Send(Request(HttpMethod.Put, path, cookies, command)))
        {
            Assert.Equal(HttpStatusCode.Conflict, referenced.StatusCode);
            Assert.Contains("IAM_MENU_ACTION_REFERENCED", await referenced.Content.ReadAsStringAsync());
        }
        var closedPath = $"api/iam/role-policies/operations/{command.OperationId}/close?kind=menu-actions";
        using (var close = await host.Send(Request(HttpMethod.Post, closedPath, cookies)))
            Assert.Equal(HttpStatusCode.OK, close.StatusCode);
        await h.Db.Sys_RoleActions.Where(x => x.RoleId == 72 && x.MenuId == domain.Menu).ExecuteDeleteAsync();
        using (var stale = await host.Send(Request(HttpMethod.Put, path, cookies, command with { OperationId = Guid.NewGuid() })))
            Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        using var fresh = await host.Send(Request(HttpMethod.Get, path + "/editor", cookies));
        editor = (await fresh.Content.ReadFromJsonAsync<IamMenuActionEditorV1>())!;
        command = command with { OperationId = Guid.NewGuid(), ExpectedVersion = editor.Version,
            ExpectedReferencesVersion = editor.ReferencesVersion, ExpectedCatalogVersion = editor.CatalogVersion };
        using (var saved = await host.Send(Request(HttpMethod.Put, path, cookies, command)))
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.False(await h.Db.Sys_MenuActions.AnyAsync(x => x.MenuId == domain.Menu));
    }
}
