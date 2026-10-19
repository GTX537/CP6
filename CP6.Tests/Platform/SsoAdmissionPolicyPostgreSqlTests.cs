using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Platform;

[Collection("TenantManagementPostgres")]
public sealed class SsoAdmissionPolicyPostgreSqlTests(TenantManagementPostgresFixture server)
{
    private const string Password = "Admission1!Password";

    [CurrentPermissionPostgresFact]
    public async Task Actual_SSO_configuration_write_rejects_a_stale_expected_version()
    {
        var user = await Seed(); await using var db = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        var cookies = await Login(host, user);
        using var response = await host.Send(Put(cookies, Envelope("stale-config-version")));
        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Equal("old-client", await db.Sys_TenantSsoConfigs.AsNoTracking().Select(x => x.ClientId).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Actual_SSO_configuration_audit_failure_cannot_commit_the_configuration()
    {
        var user = await Seed(); await using var db = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        var cookies = await Login(host, user);
        using var read = await host.Send(Get("api/sys/sso-config", cookies)); Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var projection = JsonDocument.Parse(await read.Content.ReadAsStringAsync()).RootElement;
        var version = projection.TryGetProperty("configVersion", out var v) ? v.GetString()! : "legacy-no-version";
        var fn = "sso_config_audit_" + Guid.NewGuid().ToString("N");
        await db.Database.ExecuteSqlRawAsync($"CREATE FUNCTION public.{fn}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"EventType\"={(int)SecurityEventType.SsoConfigChanged} THEN RAISE EXCEPTION 'injected SSO config audit failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER {fn} BEFORE INSERT ON public.\"Sys_SecurityLogs\" FOR EACH ROW EXECUTE FUNCTION public.{fn}()");
        try
        {
            var input = Envelope(version) with { Admission = new("approve", "https://idp.example", true, "verified-email", true) };
            using var response = await host.Send(Put(cookies, input));
            Assert.False(response.IsSuccessStatusCode);
            Assert.Equal("old-client", await db.Sys_TenantSsoConfigs.AsNoTracking().Select(x => x.ClientId).SingleAsync());
            Assert.Null(await SsoConfigurationAdminV1.ReadAdmissionAsync(db, user.TenantId));
            Assert.False(await db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM cp6_authentication_v1.sso_config_operations WHERE tenant_id={user.TenantId} AND operation_id={input.OperationId}) AS \"Value\"").SingleAsync());
        }
        finally { await db.Database.ExecuteSqlRawAsync($"DROP TRIGGER {fn} ON public.\"Sys_SecurityLogs\"; DROP FUNCTION public.{fn}()"); }
    }

    [CurrentPermissionPostgresFact]
    public async Task Explicit_approval_commits_with_receipt_and_secret_is_not_projected_or_logged()
    {
        var user = await Seed(); await using var db = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        var cookies = await Login(host, user); var projection = await Read(host, cookies);
        Assert.Equal("none", projection.GetProperty("policyVersion").GetString());
        Assert.Equal(JsonValueKind.Null, projection.GetProperty("admissionPolicy").ValueKind);
        var input = Envelope(projection.GetProperty("configVersion").GetString()!) with
        {
            AutoProvision = true, DefaultRoleId = 17, ClientSecret = "owned-do-not-project-or-log",
            Admission = new("approve", "https://idp.example", true, "verified-email", true, true, [17])
        };
        using var saved = await host.Send(Put(cookies, input)); Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var receipt = JsonDocument.Parse(await saved.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("COMMITTED", receipt.GetProperty("state").GetString());
        Assert.NotEqual("none", receipt.GetProperty("policyVersion").GetString());
        using var expired = await host.Send(Get("api/sys/sso-config", cookies)); Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        cookies = await Login(host, user); projection = await Read(host, cookies);
        Assert.DoesNotContain(input.ClientSecret!, projection.ToString()); Assert.True(projection.GetProperty("hasClientSecret").GetBoolean());
        Assert.True(projection.GetProperty("admissionPolicy").GetProperty("allowInitialEmailLink").GetBoolean());
        using var replay = await host.Send(Put(cookies, input)); Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var conflict = await host.Send(Put(cookies, input with { Reason = "different intent" })); Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var status = await host.Send(Get($"api/sys/sso-config/operations/{input.OperationId}", cookies)); Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Contains("COMMITTED", await status.Content.ReadAsStringAsync());
        Assert.Equal(1, await db.Sys_SecurityLogs.CountAsync(x => x.EventType == (int)SecurityEventType.SsoConfigChanged));
        Assert.False(await db.Sys_OperLogs.AnyAsync(x => x.RequestBody != null && x.RequestBody.Contains(input.ClientSecret!)));
        var protectedSecret = await db.Sys_TenantSsoConfigs.Select(x => x.ClientSecretProtected).SingleAsync();
        var revoke = Envelope(projection.GetProperty("configVersion").GetString()!) with
        { ExpectedPolicyVersion = projection.GetProperty("policyVersion").GetString(), ExpectedPolicyRef = projection.GetProperty("admissionPolicy").GetProperty("policyRef").GetGuid(), Admission = new("revoke") };
        using var revoked = await host.Send(Put(cookies, revoke)); Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        Assert.Null(await SsoConfigurationAdminV1.ReadAdmissionAsync(db, user.TenantId));
        Assert.Equal(protectedSecret, await db.Sys_TenantSsoConfigs.Select(x => x.ClientSecretProtected).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Policy_only_approval_invalidates_old_sessions_even_when_config_values_are_unchanged()
    {
        var user = await Seed(); await using var db = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        var cookies = await Login(host, user); var projection = await Read(host, cookies);
        var original = await db.Sys_TenantSsoConfigs.AsNoTracking().SingleAsync();
        var input = Envelope(projection.GetProperty("configVersion").GetString()!) with { ClientId = original.ClientId,
            Admission = new("approve", "https://idp.example", true, "verified-email", true) };
        using var saved = await host.Send(Put(cookies, input)); Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(original.ClientId, await db.Sys_TenantSsoConfigs.Select(x => x.ClientId).SingleAsync());
        using var stale = await host.Send(Get("api/sys/sso-config", cookies)); Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        var current = await Read(host, await Login(host, user));
        Assert.NotEqual(projection.GetProperty("configVersion").GetString(), current.GetProperty("configVersion").GetString());
    }

    [CurrentPermissionPostgresFact]
    public async Task Missing_approval_unconfirmed_issuer_and_disabled_roles_cannot_enable_first_JIT()
    {
        var user = await Seed(); await using var db = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
        db.Sys_Roles.Add(new Sys_Role { TenantId = user.TenantId, RoleId = 18, RoleName = "disabled", Enable = false }); await db.SaveChangesAsync();
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        var cookies = await Login(host, user); var projection = await Read(host, cookies);
        var input = Envelope(projection.GetProperty("configVersion").GetString()!) with { AutoProvision = true, DefaultRoleId = 17 };
        using var missing = await host.Send(Put(cookies, input)); Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);
        using var unconfirmed = await host.Send(Put(cookies, input with { Admission = new("approve", "https://idp.example", false, "verified-email", true, true, [17]) }));
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        using var role = await host.Send(Put(cookies, input with { Admission = new("approve", "https://idp.example", true, "verified-email", true, true, [18]) }));
        Assert.Equal(HttpStatusCode.Conflict, role.StatusCode);
        Assert.Null(await SsoConfigurationAdminV1.ReadAdmissionAsync(db, user.TenantId));
        Assert.False(await db.Sys_TenantSsoConfigs.Select(x => x.AutoProvision).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Closing_missing_original_operation_blocks_late_write_and_receipt_is_actor_bound()
    {
        var user = await Seed(); await using var db = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
        var other = new Sys_User { TenantId = user.TenantId, UserName = "other-admin", Password = new BCryptPasswordHasher(4).Hash(Password), Enable = true, RoleId = 17 };
        db.Sys_Users.Add(other); await db.SaveChangesAsync();
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        var cookies = await Login(host, user); var projection = await Read(host, cookies); var input = Envelope(projection.GetProperty("configVersion").GetString()!);
        using var missing = await host.Send(Get($"api/sys/sso-config/operations/{input.OperationId}", cookies)); Assert.Contains("UNKNOWN", await missing.Content.ReadAsStringAsync());
        var close = new HttpRequestMessage(HttpMethod.Post, $"api/sys/sso-config/operations/{input.OperationId}/close"); AddCookies(close, cookies);
        using var closed = await host.Send(close); Assert.Equal(HttpStatusCode.OK, closed.StatusCode); Assert.Contains("CLOSED", await closed.Content.ReadAsStringAsync());
        using var late = await host.Send(Put(cookies, input)); Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        using var forbidden = await host.Send(Get($"api/sys/sso-config/operations/{input.OperationId}", await Login(host, other))); Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("old-client", await db.Sys_TenantSsoConfigs.Select(x => x.ClientId).SingleAsync());
        Assert.Equal(1, await db.Sys_SecurityLogs.CountAsync(x => x.EventType == (int)SecurityEventType.SsoConfigChanged));
    }

    [CurrentPermissionPostgresFact]
    public async Task Permission_revoked_while_request_waits_for_owner_root_cannot_commit()
    {
        var user = await Seed(); await using var db = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        var cookies = await Login(host, user); var projection = await Read(host, cookies);
        await using var tx = await db.Database.BeginTransactionAsync(); await AuthenticationCompletionGateV1.AcquireAsync(db);
        var attempt = host.Send(Put(cookies, Envelope(projection.GetProperty("configVersion").GetString()!)));
        await using var observer = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!await observer.Database.SqlQueryRaw<bool>("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock' AND query LIKE '%cp6_authentication_v1.acquire()%') AS \"Value\"").SingleAsync(deadline.Token))
            await Task.Delay(20, deadline.Token);
        await db.Sys_RoleActions.Where(x => x.RoleId == 17 && x.ActionCode == "edit").ExecuteDeleteAsync(); await tx.CommitAsync();
        using var denied = await attempt; Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("old-client", await observer.Sys_TenantSsoConfigs.Select(x => x.ClientId).SingleAsync());
    }

    [TenantManagementPostgresTheory]
    [InlineData("role")]
    [InlineData("membership")]
    [InlineData("action")]
    [InlineData("menu")]
    public async Task Native_permission_writers_join_the_admission_root(string kind)
    {
        var user = await Seed(); await using var db = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
        await using var writer = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
        await using var tx = await db.Database.BeginTransactionAsync(); await AuthenticationCompletionGateV1.AcquireAsync(db);
        await writer.Database.OpenConnectionAsync(); var pid = await writer.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync();
        Task change = kind switch
        {
            "role" => writer.Sys_Roles.Where(x => x.RoleId == 17).ExecuteUpdateAsync(s => s.SetProperty(x => x.Enable, false)),
            "action" => writer.Sys_RoleActions.Where(x => x.RoleId == 17 && x.ActionCode == "edit").ExecuteDeleteAsync(),
            "menu" => writer.Sys_Menus.Where(x => x.MenuKey == "sys-sso-config").ExecuteUpdateAsync(s => s.SetProperty(x => x.MenuName, "SSO admission")),
            _ => writer.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO public.\"Sys_UserRole\" (\"Id\",\"TenantId\",\"UserId\",\"RoleId\",\"CreateDate\") VALUES (gen_random_uuid(),{user.TenantId},{user.Id},17,LOCALTIMESTAMP)")
        };
        try
        {
            await using var observer = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!await observer.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE pid={pid} AND wait_event_type='Lock') AS \"Value\"").SingleAsync(deadline.Token)) await Task.Delay(20, deadline.Token);
            Assert.False(change.IsCompleted);
        }
        finally { await tx.CommitAsync(); }
        await change;
    }

    private static async Task<JsonElement> Read(AuthenticationApiProcess host, Dictionary<string,string> cookies)
    {
        using var response = await host.Send(Get("api/sys/sso-config", cookies)); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }
    private static SsoConfigInput Envelope(string version) => new("https://idp.example", "new-client", null,
        "openid email profile", "email", false, false, false, null)
    {
        ExpectedConfigVersion = version, ExpectedPolicyVersion = "none", OperationId = Guid.NewGuid(), Reason = "owned configuration regression",
        Admission = new("preserve")
    };

    private async Task<Sys_User> Seed()
    {
        var tenant = new TenantContext { CurrentTenantId = Guid.NewGuid() }; await using var db = server.Context(tenant);
        db.Sys_Tenants.Add(new Sys_Tenant { Id = tenant.CurrentTenantId, TenantCode = "admission_" + tenant.CurrentTenantId.ToString("N"), TenantName = "Admission", TwoFactorMode = 0 });
        db.Sys_Roles.Add(new Sys_Role { TenantId = tenant.CurrentTenantId, RoleId = 17, RoleName = "SSO administrator", Enable = true });
        var menu = await db.Sys_Menus.SingleOrDefaultAsync(x => x.MenuKey == "sys-sso-config");
        if (menu == null) { menu = new Sys_Menu { MenuId = 9888007, MenuKey = "sys-sso-config", MenuName = "SSO", Enable = true }; db.Sys_Menus.Add(menu); }
        foreach (var action in new[] { "query", "edit" }) db.Sys_RoleActions.Add(new Sys_RoleAction { TenantId = tenant.CurrentTenantId, RoleId = 17, MenuId = menu.MenuId, ActionCode = action });
        var user = new Sys_User { TenantId = tenant.CurrentTenantId, UserName = "admission-admin", Password = new BCryptPasswordHasher(4).Hash(Password), Enable = true, RoleId = 17 };
        db.Sys_Users.Add(user);
        db.Sys_TenantSsoConfigs.Add(new Sys_TenantSsoConfig { TenantId = tenant.CurrentTenantId, Authority = "https://idp.example", ClientId = "old-client", AutoProvision = false, Enabled = false });
        await db.SaveChangesAsync(); return user;
    }
    private static async Task<Dictionary<string,string>> Login(AuthenticationApiProcess host, Sys_User user)
    {
        using var response = await host.Send(new HttpRequestMessage(HttpMethod.Post, "api/auth/login") { Content = JsonContent.Create(new { user.UserName, Password, TenantCode = "admission_" + user.TenantId.ToString("N") }) });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response.Headers.GetValues("Set-Cookie").Select(x => x.Split(';')[0].Split('=', 2)).Where(x => x[1].Length > 0).ToDictionary(x => x[0], x => x[1]);
    }
    private static HttpRequestMessage Get(string path, Dictionary<string,string> cookies)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path); AddCookies(request, cookies); return request;
    }
    private static HttpRequestMessage Put(Dictionary<string,string> cookies, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "api/sys/sso-config") { Content = JsonContent.Create(body) }; AddCookies(request, cookies); return request;
    }
    private static void AddCookies(HttpRequestMessage request, Dictionary<string,string> cookies)
    {
        request.Headers.Add("Cookie", string.Join("; ", cookies.Select(x => x.Key + "=" + x.Value)));
        request.Headers.Add("X-CSRF-Token", cookies["cp6_csrf"]);
    }
}
