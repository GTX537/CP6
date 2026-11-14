using System.Net;
using System.Net.Http.Json;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static int legacyMenuTarget = 33000;

    [TenantManagementPostgresTheory]
    [InlineData("menu-add")]
    [InlineData("menu-edit")]
    [InlineData("menu-tree")]
    [InlineData("menu-delete")]
    [InlineData("reset")]
    [InlineData("policy")]
    public async Task PGT017_authority_writes_cannot_commit_using_revoked_operator_grant(string route)
    {
        await using var h = await Seed();
        await SeedFenceAdministrator(h);
        var db = h.Db;
        var tenant = h.Tenant;
        var targetMenu = Interlocked.Add(ref legacyMenuTarget, 2);
        var newMenu = targetMenu + 1;
        var target = new Sys_User
        {
            Id = Guid.NewGuid(), UserName = "two-factor-target", Password = "fixture", RoleId = 72,
            TwoFactorEnabled = true, TwoFactorSecret = "JBSWY3DPEHPK3PXP", TwoFactorEnrolledAt = DateTime.Now
        };
        db.Sys_Users.Add(target);
        db.Sys_Menus.Add(new() { MenuId = targetMenu, MenuName = "fence target" });
        var resource = route.StartsWith("menu-", StringComparison.Ordinal) ? "menu" : "user";
        var action = route switch { "menu-add" => "add", "menu-delete" => "delete", _ => "edit" };
        await GrantLegacyAuthorityAction(h, resource, action);
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(tenant));
        var writer = await Login(host, h);
        var manager = await Login(host, "fence-manager", "Fence1!Password", tenant.ToString("N"));
        var revoke = await RevokeActions(host, manager, action);

        (HttpMethod method, string path, object payload) request = route switch
        {
            "menu-add" => (HttpMethod.Post, "api/menu", new { MenuId = newMenu, MenuName = "new" }),
            "menu-edit" => (HttpMethod.Put, "api/menu", new { MenuId = targetMenu, MenuName = "modified", Enable = true }),
            "menu-tree" => (HttpMethod.Put, "api/menu/tree", new[] { new { MenuId = targetMenu, OrderNo = 1 } }),
            "menu-delete" => (HttpMethod.Delete, "api/menu", new[] { targetMenu }),
            "reset" => (HttpMethod.Post, "api/user/reset-2fa", target.Id),
            "policy" => (HttpMethod.Put, "api/sys/two-factor-policy", new { Mode = 1 }),
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        };

        var before = await ReadAuthorityEffects();
        var connection = db.Database.GetConnectionString()!;
        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> change;
        Task<HttpResponseMessage> write;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            await using var lockActions = new NpgsqlCommand(
                """LOCK TABLE public."Sys_RoleAction" IN SHARE MODE""", barrier, hold);
            await lockActions.ExecuteNonQueryAsync();
            change = host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke));
            var first = await WaitForFence(observer, barrier.ProcessID, "%DELETE%Sys_RoleAction%", null, change);
            write = host.Send(OperationsRequest(writer, request.method, request.path, request.payload));
            var second = await WaitForFence(observer, first.Backend, "%cp6_authentication_v1.acquire()%", null, write);
            Assert.Equal(before, await ReadAuthorityEffects());
            await hold.RollbackAsync();
            output.WriteLine($"{route}: IAM backend {first.Backend}; action backend {second.Backend}; wait {second.WaitEvent}");
        }

        using var changed = await change;
        Assert.True(changed.StatusCode == HttpStatusCode.OK, await OperationsFailure(changed));
        using var response = await write;
        Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{route}: {response.StatusCode}: {await OperationsFailure(response)}");
        Assert.Contains("IAM_FORBIDDEN", await OperationsFailure(response));
        Assert.Equal(before, await ReadAuthorityEffects());
        using var stale = await host.Send(OperationsRequest(writer, request.method, request.path, request.payload));
        Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
        Assert.Equal(before, await ReadAuthorityEffects());
        if (resource == "menu")
        {
            await GrantLegacyAuthorityAction(h, resource, action);
            using var allowed = await host.Send(OperationsRequest(writer, request.method, request.path, request.payload));
            Assert.True(allowed.StatusCode == HttpStatusCode.OK, await OperationsFailure(allowed));
            Assert.NotEqual(before, await ReadAuthorityEffects());
        }
        Task<string> ReadAuthorityEffects() => db.Database.SqlQuery<string>($"""
            SELECT jsonb_build_array(
                (SELECT jsonb_agg(to_jsonb(m) ORDER BY m."MenuId") FROM public."Sys_Menus" m
                 WHERE m."MenuId" IN ({targetMenu},{newMenu})),
                (SELECT jsonb_build_array(u."TwoFactorEnabled",u."CredentialRevision",u."AuthenticationEpoch")
                 FROM public."Sys_Users" u WHERE u."Id"={target.Id}),
                (SELECT jsonb_build_array(t."TwoFactorMode",t."AuthenticationPolicyRevision")
                 FROM public."Sys_Tenants" t WHERE t."Id"={tenant}),
                (SELECT count(*) FROM public."Sys_SecurityLogs" WHERE "TenantId"={tenant}
                   AND ("EventType"={(int)SecurityEventType.TwoFactorReset}
                     OR "ContextJson"::jsonb->>'kind'='two-factor-policy'))
            )::text AS "Value"
            """).SingleAsync();
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PGT017_admitted_credential_write_commits_even_when_it_invalidates_its_own_session(bool policy)
    {
        await using var h = await Seed();
        await GrantLegacyAuthorityAction(h, "user", "edit");
        var db = h.Db;
        var before = await db.Sys_Users.AsNoTracking().SingleAsync(u => u.Id == h.User);
        var beforePolicy = await db.Sys_Tenants.AsNoTracking().Where(t => t.Id == h.Tenant)
            .Select(t => t.AuthenticationPolicyRevision).SingleAsync();
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        var writer = await Login(host, h);
        var method = policy ? HttpMethod.Put : HttpMethod.Post;
        var path = policy ? "api/sys/two-factor-policy" : "api/user/reset-2fa";
        object payload = policy ? new { Mode = 1 } : h.User;

        using var changed = await host.Send(OperationsRequest(writer, method, path, payload));
        Assert.True(changed.StatusCode == HttpStatusCode.OK, await OperationsFailure(changed));
        using var stale = await host.Send(OperationsRequest(writer, method, path, payload));
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        if (policy)
        {
            var tenant = await db.Sys_Tenants.AsNoTracking().SingleAsync(t => t.Id == h.Tenant);
            Assert.Equal(1, tenant.TwoFactorMode);
            Assert.NotEqual(beforePolicy, tenant.AuthenticationPolicyRevision);
            Assert.Single(await db.Sys_SecurityLogs.AsNoTracking().Where(l => l.UserId == h.User && l.Reason == "two-factor-policy").ToListAsync());
        }
        else
        {
            var user = await db.Sys_Users.AsNoTracking().SingleAsync(u => u.Id == h.User);
            Assert.NotEqual(before.AuthenticationEpoch, user.AuthenticationEpoch);
            Assert.NotEqual(before.CredentialRevision, user.CredentialRevision);
            var audit = await db.Sys_SecurityLogs.AsNoTracking()
                .SingleAsync(l => l.UserId == h.User && l.EventType == (int)SecurityEventType.TwoFactorReset);
            Assert.Equal("admin-reset", audit.Reason);
            Assert.Contains(h.User.ToString("D"), audit.ContextJson);
        }
    }

    private static async Task GrantLegacyAuthorityAction(Harness h, string resource, string action)
    {
        var db = h.Db;
        var menu = await db.Sys_Menus.SingleOrDefaultAsync(m => m.MenuKey == resource);
        if (menu == null)
        {
            // The menu catalog is shared by every tenant in this test database.
            var nextMenuId = await db.Sys_Menus.MaxAsync(m => (int?)m.MenuId) ?? 0;
            menu = new() { MenuId = Math.Max(34000, nextMenuId + 1), MenuName = resource, MenuKey = resource };
            db.Sys_Menus.Add(menu);
        }
        if (!await db.Sys_MenuActions.AnyAsync(a => a.MenuId == menu.MenuId && a.ActionCode == action))
            db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = action, ActionName = action });
        db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = menu.MenuId, ActionCode = action });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }
}
