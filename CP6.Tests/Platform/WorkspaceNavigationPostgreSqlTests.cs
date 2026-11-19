using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Auth;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Workspace_navigation_legacy_role_routes_share_cas_and_never_cross_tenants()
    {
        await using var a = await Seed();
        await using var b = await Seed();
        await GrantPolicyHttpPermissions(a);
        var domain = await PolicyDomain(a);
        var actionKey = (await a.Db.Sys_Menus.Where(x => x.MenuId == domain.Menu).Select(x => x.MenuKey).SingleAsync()) + ":read";
        a.Db.Sys_Users.Add(Reader());
        b.Db.Sys_Users.Add(Reader());
        await a.Db.SaveChangesAsync();
        await b.Db.SaveChangesAsync();
        a.Db.ChangeTracker.Clear();
        b.Db.ChangeTracker.Clear();

        var environment = IdentityEnvironment(a.Tenant);
        environment["WorkspaceNavigation__CrmOrigin"] = "https://crm-destination.workspace-fixture.test";
        await using var host = await AuthenticationApiProcess.Start(a.Db.Database.GetConnectionString()!, environment);
        var admin = await Login(host, a);
        var alice = await Login(host, a, userName: "workspace-reader");
        var bob = await Login(host, b, userName: "workspace-reader");
        const string nav = "api/dashboard/scoped-v1/navigation";

        async Task<T> Get<T>(Dictionary<string, string> cookies, string path)
        {
            using var response = await host.Send(Request(HttpMethod.Get, path, cookies));
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<T>())!;
        }
        async Task<IamRolePolicyEditorV1> Editor() =>
            await Get<IamRolePolicyEditorV1>(admin, "api/iam/role-policies/actions/editor?roleId=72");
        Task<HttpResponseMessage> Change(HttpMethod method, string path, IamRolePolicyCommandV1 command)
        {
            var request = Request(method, path, admin);
            request.Content = JsonContent.Create(command, MediaTypeHeaderValue.Parse("application/vnd.cp6.iam-role-policy+json"));
            return host.Send(request);
        }
        IamRolePolicyCommandV1 Command(IamRolePolicyEditorV1 editor, bool grant) => new()
        {
            OperationId = Guid.NewGuid(), ExpectedVersion = editor.Version,
            ExpectedReferencesVersion = editor.ReferencesVersion, ExpectedCatalogVersion = editor.Catalog.Version,
            Reason = "工作台同号角色验收", MenuIds = grant ? [domain.Menu] : [],
            Actions = grant ? [new() { MenuId = domain.Menu, ActionCode = "read" }] : []
        };

        var start = await Get<WorkspaceNavigationSnapshotV1>(alice, nav);
        var foreignStart = await Get<WorkspaceNavigationSnapshotV1>(bob, nav);
        Assert.Empty(start.Menus);
        Assert.Empty(start.ActionKeys);
        Assert.Empty(foreignStart.Menus);
        var editor = await Editor();

        const string roleMenus = "api/role/72/menus";
        const string rolePerm = "api/pub/role-perm/72";
        var grant = Command(editor, true);
        IamOperationV1 grantReceipt;
        using (var reply = await Change(HttpMethod.Post, roleMenus, grant))
        {
            Assert.True(reply.StatusCode == HttpStatusCode.OK, await reply.Content.ReadAsStringAsync());
            grantReceipt = (await reply.Content.ReadFromJsonAsync<IamOperationV1>())!;
        }
        Assert.Equal("COMMITTED", grantReceipt.State);
        var granted = await Get<WorkspaceNavigationSnapshotV1>(alice, nav);
        Assert.Contains(granted.Menus, x => x.Id == domain.Menu);
        Assert.Contains(actionKey, granted.ActionKeys);
        Assert.NotEqual(start.PolicyVersion, granted.PolicyVersion);
        var foreignStill = await Get<WorkspaceNavigationSnapshotV1>(bob, nav);
        Assert.Equal(foreignStart.PolicyVersion, foreignStill.PolicyVersion);
        Assert.Empty(foreignStill.Menus);
        Assert.Empty(foreignStill.ActionKeys);

        using (var stale = await Change(HttpMethod.Put, rolePerm, Command(editor, false)))
            Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        var revoke = Command(await Editor(), false);
        IamOperationV1 revokeReceipt;
        using (var reply = await Change(HttpMethod.Put, rolePerm, revoke))
        {
            Assert.True(reply.StatusCode == HttpStatusCode.OK, await reply.Content.ReadAsStringAsync());
            revokeReceipt = (await reply.Content.ReadFromJsonAsync<IamOperationV1>())!;
        }
        Assert.NotEqual(grantReceipt.Version, revokeReceipt.Version);
        var revoked = await Get<WorkspaceNavigationSnapshotV1>(alice, nav);
        Assert.Empty(revoked.Menus);
        Assert.Empty(revoked.ActionKeys);
        Assert.NotEqual(granted.PolicyVersion, revoked.PolicyVersion);
        var original = await Get<IamOperationV1>(admin, $"api/iam/role-policies/operations/{revoke.OperationId}?kind=role-policy-actions");
        Assert.Equal(revokeReceipt, original);

        var external = await Get<JsonElement>(alice, nav + "/crm?next=https%3A%2F%2Funknown.workspace-fixture.test");
        Assert.Equal("CRM.Workspace", external.GetProperty("featureRef").GetString());
        Assert.Equal("https://crm-destination.workspace-fixture.test/crm", external.GetProperty("target").GetString());
        Assert.Equal("crm-destination.workspace-fixture.test", external.GetProperty("host").GetString());
        using (var unknown = await host.Send(Request(HttpMethod.Get, nav + "/unknown", alice)))
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        await a.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_menu_tree_v1.root RENAME TO workspace_owned_fixture");
        try
        {
            using var unavailable = await host.Send(Request(HttpMethod.Get, nav, alice));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            Assert.Contains("WORKSPACE_AUTHORITY_UNAVAILABLE", await unavailable.Content.ReadAsStringAsync());
        }
        finally { await a.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_menu_tree_v1.workspace_owned_fixture RENAME TO root"); }
    }

    private static Sys_User Reader() => new()
    {
        UserName = "workspace-reader", Password = new BCryptPasswordHasher(4).Hash("Iam1!Password"), RoleId = 72, Enable = true
    };
}
