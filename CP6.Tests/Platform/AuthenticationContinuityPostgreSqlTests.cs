using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;
using CP6.WebApi.Services;
using System.Text.Json;

namespace CP6.Tests.Platform;

[Collection("TenantManagementPostgres")]
public sealed class AuthenticationContinuityPostgreSqlTests(TenantManagementPostgresFixture server)
{
    private const string Password = "Continuity1!Password";

    [CurrentPermissionPostgresFact]
    public async Task Browser_known_closure_and_required_audit_commit_together_and_retry_does_not_duplicate_audit()
    {
        var user = await Seed(); await using var db = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        var key = AuthenticationInteractionStoreV1.NewChallenge();
        var login = Post("api/auth/login", new { user.UserName, Password, TenantCode = "continuity_" + user.TenantId.ToString("N") });
        login.Headers.Add(AuthenticationRecoveryBindingV1.Header, key);
        using var logged = await host.Send(login); var cookies = Cookies(logged);
        var fn = "browser_audit_" + Guid.NewGuid().ToString("N");
        await db.Database.ExecuteSqlRawAsync($"CREATE FUNCTION public.{fn}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"UserId\"='{user.Id}'::uuid AND NEW.\"EventType\"={(int)SecurityEventType.Logout} THEN RAISE EXCEPTION 'injected closure audit failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER {fn} BEFORE INSERT ON public.\"Sys_SecurityLogs\" FOR EACH ROW EXECUTE FUNCTION public.{fn}()");
        async Task<HttpResponseMessage> Close()
        {
            var request = Post("api/auth/completion/browser/close", new {}); request.Headers.Add(AuthenticationRecoveryBindingV1.Header, key);
            return await host.Send(request);
        }
        try
        {
            using var failed = await Close(); Assert.False(failed.IsSuccessStatusCode);
            using var stillOpen = await host.Profile(cookies["cp6_at"]); Assert.Equal(HttpStatusCode.OK, stillOpen.StatusCode);
        }
        finally { await db.Database.ExecuteSqlRawAsync($"DROP TRIGGER {fn} ON public.\"Sys_SecurityLogs\"; DROP FUNCTION public.{fn}()"); }
        using (var closed = await Close()) Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        using (var repeated = await Close()) Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        using var denied = await host.Profile(cookies["cp6_at"]); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        var audit = Assert.Single(await db.Sys_SecurityLogs.AsNoTracking().Where(x => x.UserId == user.Id && x.Reason == "browser authentication closure").ToListAsync());
        Assert.DoesNotContain(key, audit.ContextJson!); Assert.DoesNotContain(cookies["cp6_rt"], audit.ContextJson!);
    }

    [CurrentPermissionPostgresFact]
    public async Task Browser_recovery_requires_explicit_exact_capability_and_does_not_broaden_CSRF_exemption()
    {
        var user = await Seed(); await using var db = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        var key = AuthenticationInteractionStoreV1.NewChallenge();
        foreach (var sample in new[] { ("api/auth/completion/browser/status", (string?)null),
            ("api/auth/completion/browser/close", "short"), ("api/auth/completion/browser/close/extra", key),
            ("api/auth/completion/browser/status/", key) })
        {
            var request = Post(sample.Item1, new {});
            if (sample.Item2 != null) request.Headers.Add(AuthenticationRecoveryBindingV1.Header, sample.Item2);
            using var denied = await host.Send(request); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        var multiple = Post("api/auth/completion/browser/close", new {});
        multiple.Headers.Add(AuthenticationRecoveryBindingV1.Header, new[] { key, key });
        using (var denied = await host.Send(multiple)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var missing = Post("api/auth/completion/browser/status", new {}); missing.Headers.Add(AuthenticationRecoveryBindingV1.Header, key);
        using var unavailable = await host.Send(missing); Assert.Equal(HttpStatusCode.OK, unavailable.StatusCode);
        Assert.Equal("UNAVAILABLE", (await unavailable.Content.ReadFromJsonAsync<BrowserAuthenticationStatusV1>())!.State);
        Assert.False(unavailable.Headers.Contains("Set-Cookie"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Browser_origin_recovery_follows_actual_impersonation_alias_and_closes_the_original_family()
    {
        var actor = await Seed(); await using var db = server.Context(new TenantContext { CurrentTenantId = actor.TenantId });
        await db.Sys_Users.Where(x => x.Id == actor.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsPlatformAdmin, true));
        var targetTenant = new Sys_Tenant { Id = Guid.NewGuid(), TenantCode = "browser_target_" + Guid.NewGuid().ToString("N"), TenantName = "Target" };
        var target = new Sys_User { TenantId = targetTenant.Id, UserName = "browser-target", Password = "not-a-login-hash", Enable = true };
        db.Sys_Tenants.Add(targetTenant); db.Sys_Users.Add(target); await db.SaveChangesAsync();
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        var key = AuthenticationInteractionStoreV1.NewChallenge();
        var login = Post("api/auth/login", new { actor.UserName, Password, TenantCode = "continuity_" + actor.TenantId.ToString("N") });
        login.Headers.Add(AuthenticationRecoveryBindingV1.Header, key);
        using var logged = await host.Send(login); Assert.Equal(HttpStatusCode.OK, logged.StatusCode); var cookies = Cookies(logged);
        using var started = await host.Send(Post("api/platform/impersonation/start", new { TenantId = targetTenant.Id, UserId = target.Id, Reason = "browser boundary regression" }, cookies));
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        foreach (var pair in Cookies(started)) cookies[pair.Key] = pair.Value;
        async Task<string> Profile()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "api/auth/profile");
            request.Headers.Add("Cookie", "cp6_at=" + cookies["cp6_at"]); request.Headers.Add(AuthenticationRecoveryBindingV1.Header, key);
            using var response = await host.Send(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("userName").GetString()!;
        }
        Assert.Equal(target.UserName, await Profile());
        using var ended = await host.Send(Post("api/platform/impersonation/end", new {}, cookies)); Assert.Equal(HttpStatusCode.OK, ended.StatusCode);
        foreach (var pair in Cookies(ended)) cookies[pair.Key] = pair.Value;
        Assert.Equal(actor.UserName, await Profile());
        var close = Post("api/auth/completion/browser/close", new {}); close.Headers.Add(AuthenticationRecoveryBindingV1.Header, key);
        using var closed = await host.Send(close); Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        using var denied = await host.Profile(cookies["cp6_at"]); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Browser_rotation_capability_matches_exact_output_and_closes_only_its_generation()
    {
        var user = await Seed(); await using var db = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        using var login = await host.Send(Post("api/auth/login", new { user.UserName, Password, TenantCode = "continuity_" + user.TenantId.ToString("N") }));
        var old = Cookies(login);
        using var healthyLogin = await host.Send(Post("api/auth/login", new { user.UserName, Password, TenantCode = "continuity_" + user.TenantId.ToString("N") }));
        var healthy = Cookies(healthyLogin);
        var key = AuthenticationInteractionStoreV1.NewChallenge();
        var refresh = Post("api/auth/refresh", new {}, old); refresh.Headers.Add(AuthenticationRecoveryBindingV1.Header, key);
        using var rotated = await host.Send(refresh); Assert.Equal(HttpStatusCode.OK, rotated.StatusCode); var next = Cookies(rotated);
        foreach (var sample in new[] { (old, HttpStatusCode.Unauthorized), (next, HttpStatusCode.OK), (healthy, HttpStatusCode.Unauthorized) })
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "api/auth/profile");
            request.Headers.Add("Cookie", "cp6_at=" + sample.Item1["cp6_at"]); request.Headers.Add(AuthenticationRecoveryBindingV1.Header, key);
            using var profile = await host.Send(request); Assert.Equal(sample.Item2, profile.StatusCode);
        }
        var close = Post("api/auth/completion/browser/close", new {}); close.Headers.Add(AuthenticationRecoveryBindingV1.Header, key);
        using var closed = await host.Send(close); Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        using var rejected = await host.Profile(next["cp6_at"]); Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        using var untouched = await host.Profile(healthy["cp6_at"]); Assert.Equal(HttpStatusCode.OK, untouched.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Browser_pending_factor_cookie_must_match_its_original_recovery_capability()
    {
        var user = await Seed(); await using var db = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
        await db.Sys_Tenants.Where(x => x.Id == user.TenantId).ExecuteUpdateAsync(s => s.SetProperty(x => x.TwoFactorMode, 2));
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        var key = AuthenticationInteractionStoreV1.NewChallenge();
        var login = Post("api/auth/login", new { user.UserName, Password, TenantCode = "continuity_" + user.TenantId.ToString("N") }); login.Headers.Add(AuthenticationRecoveryBindingV1.Header, key);
        using var pending = await host.Send(login); Assert.Equal(HttpStatusCode.OK, pending.StatusCode); var cookies = Cookies(pending);
        var wrong = Post("api/auth/2fa/setup", new {}, cookies); wrong.Headers.Add(AuthenticationRecoveryBindingV1.Header, AuthenticationInteractionStoreV1.NewChallenge());
        using var denied = await host.Send(wrong); Assert.False(denied.IsSuccessStatusCode);
        var right = Post("api/auth/2fa/setup", new {}, cookies); right.Headers.Add(AuthenticationRecoveryBindingV1.Header, key);
        using var allowed = await host.Send(right); Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        var status = Post("api/auth/completion/browser/status", new {}); status.Headers.Add(AuthenticationRecoveryBindingV1.Header, key);
        using var result = await host.Send(status); var safe = (await result.Content.ReadFromJsonAsync<BrowserAuthenticationStatusV1>())!;
        Assert.Equal("PENDING", safe.State); Assert.Equal("2fa_enroll", safe.Purpose); Assert.True(safe.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [CurrentPermissionPostgresFact]
    public async Task Browser_profile_must_match_the_original_attempt_not_an_old_valid_cookie()
    {
        var user = await Seed(); await using var db = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        var key = AuthenticationInteractionStoreV1.NewChallenge();
        var login = Post("api/auth/login", new { user.UserName, Password, TenantCode = "continuity_" + user.TenantId.ToString("N") });
        login.Headers.Add(AuthenticationRecoveryBindingV1.Header, key);
        using var logged = await host.Send(login); Assert.Equal(HttpStatusCode.OK, logged.StatusCode); var cookies = Cookies(logged);
        var wrong = new HttpRequestMessage(HttpMethod.Get, "api/auth/profile");
        wrong.Headers.Add("Cookie", "cp6_at=" + cookies["cp6_at"]);
        wrong.Headers.Add(AuthenticationRecoveryBindingV1.Header, AuthenticationInteractionStoreV1.NewChallenge());
        using var unrelated = await host.Send(wrong); Assert.Equal(HttpStatusCode.Unauthorized, unrelated.StatusCode);
        var right = new HttpRequestMessage(HttpMethod.Get, "api/auth/profile"); right.Headers.Add("Cookie", "cp6_at=" + cookies["cp6_at"]);
        right.Headers.Add(AuthenticationRecoveryBindingV1.Header, key);
        using var own = await host.Send(right); Assert.Equal(HttpStatusCode.OK, own.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Browser_can_fence_an_unknown_attempt_before_a_late_login_registers_it()
    {
        var user = await Seed(); await using var db = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        var key = AuthenticationInteractionStoreV1.NewChallenge();
        var close = Post("api/auth/completion/browser/close", new {}); close.Headers.Add(AuthenticationRecoveryBindingV1.Header, key);
        using var closed = await host.Send(close); Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        Assert.Equal("CLOSED", (await closed.Content.ReadFromJsonAsync<AuthenticationRecoveryStatusV1>())!.State);
        var late = Post("api/auth/login", new { user.UserName, Password, TenantCode = "continuity_" + user.TenantId.ToString("N") });
        late.Headers.Add(AuthenticationRecoveryBindingV1.Header, key);
        using var denied = await host.Send(late); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var fresh = await host.Send(Post("api/auth/login", new { user.UserName, Password, TenantCode = "continuity_" + user.TenantId.ToString("N") }));
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Actual_Web_completion_status_closes_undelivered_attempt_without_reissuing_secrets()
    {
        var user = await Seed(); await using var db = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        var key = AuthenticationInteractionStoreV1.NewChallenge();
        var loginRequest = Post("api/auth/login", new { user.UserName, Password, TenantCode = "continuity_" + user.TenantId.ToString("N") });
        loginRequest.Headers.Add(AuthenticationRecoveryBindingV1.Header, key);
        using var login = await host.Send(loginRequest); Assert.Equal(HttpStatusCode.OK, login.StatusCode); var cookies = Cookies(login);
        async Task<HttpResponseMessage> Recovery(string path, string attempt)
        {
            var request = Post("api/auth/completion/" + path, new {});
            request.Headers.Add(AuthenticationRecoveryBindingV1.Header, attempt); return await host.Send(request);
        }
        using (var status = await Recovery("status", key))
        {
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            var body = await status.Content.ReadAsStringAsync(); Assert.Equal("COMPLETED", JsonDocument.Parse(body).RootElement.GetProperty("state").GetString());
            Assert.DoesNotContain(cookies["cp6_at"], body); Assert.DoesNotContain(cookies["cp6_rt"], body); Assert.DoesNotContain(user.Id.ToString(), body);
            Assert.False(status.Headers.Contains("Set-Cookie"));
        }
        using (var guessed = await Recovery("close", Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"))) Assert.Equal(HttpStatusCode.ServiceUnavailable, guessed.StatusCode);
        using (var active = await host.Profile(cookies["cp6_at"])) Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        using (var closed = await Recovery("close", key)) Assert.Equal("CLOSED", (await closed.Content.ReadFromJsonAsync<AuthenticationRecoveryStatusV1>())!.State);
        using var denied = await host.Profile(cookies["cp6_at"]); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using (var status = await Recovery("status", key)) Assert.Equal("CLOSED", (await status.Content.ReadFromJsonAsync<AuthenticationRecoveryStatusV1>())!.State);
        var replay = Post("api/auth/login", new { user.UserName, Password, TenantCode = "continuity_" + user.TenantId.ToString("N") });
        replay.Headers.Add(AuthenticationRecoveryBindingV1.Header, key);
        using var replayed = await host.Send(replay); Assert.Equal(HttpStatusCode.Unauthorized, replayed.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Actual_native_refresh_recovery_closes_exact_successor_and_its_descendants()
    {
        var user = await Seed(); await using var db = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        var client = new ClientContextDto { ClientKind = "Windows", DeviceId = "recovery-device", AppVersion = "1.0.0" };
        var original = await NativeLogin(host, user, client); var healthy = await NativeLogin(host, user, client);
        using var first = await host.Send(Post("api/client-auth/refresh", new NativeRefreshRequest { Client = client, RefreshToken = original.RefreshToken }));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode); var next = (await first.Content.ReadFromJsonAsync<TokenSessionDto>())!;
        using var second = await host.Send(Post("api/client-auth/refresh", new NativeRefreshRequest { Client = client, RefreshToken = next.RefreshToken }));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode); var descendant = (await second.Content.ReadFromJsonAsync<TokenSessionDto>())!;
        var other = new ClientContextDto { ClientKind = client.ClientKind, DeviceId = "different-device", AppVersion = client.AppVersion };
        using var wrong = await host.Send(Post("api/client-auth/completion/close", new NativeCompletionRecoveryRequest { Kind = "refresh", Credential = original.RefreshToken, Client = other }));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        using var close = await host.Send(Post("api/client-auth/completion/close", new NativeCompletionRecoveryRequest { Kind = "refresh", Credential = original.RefreshToken, Client = client }));
        Assert.Equal(HttpStatusCode.OK, close.StatusCode); Assert.Equal("CLOSED", (await close.Content.ReadFromJsonAsync<AuthenticationRecoveryStatusV1>())!.State);
        using var blocked = await host.Profile(descendant.AccessToken); Assert.Equal(HttpStatusCode.Unauthorized, blocked.StatusCode);
        using var alive = await host.Profile(healthy.AccessToken); Assert.Equal(HttpStatusCode.OK, alive.StatusCode);
        using var rejected = await host.Send(Post("api/client-auth/refresh", new NativeRefreshRequest { Client = client, RefreshToken = descendant.RefreshToken }));
        Assert.False(rejected.IsSuccessStatusCode);
        using var healthyRefresh = await host.Send(Post("api/client-auth/refresh", new NativeRefreshRequest { Client = client, RefreshToken = healthy.RefreshToken }));
        Assert.Equal(HttpStatusCode.OK, healthyRefresh.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Actual_cookie_recovery_requires_CSRF_and_never_uses_a_completion_id_as_authority()
    {
        var user = await Seed(); await using var db = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        using var login = await host.Send(Post("api/auth/login", new { user.UserName, Password, TenantCode = "continuity_" + user.TenantId.ToString("N") }));
        var cookies = Cookies(login); var noCsrf = Post("api/auth/completion/close", new {}, cookies); noCsrf.Headers.Remove("X-CSRF-Token");
        using var denied = await host.Send(noCsrf); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var status = await host.Send(Post("api/auth/completion/status", new {}, cookies)); Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        using var alive = await host.Profile(cookies["cp6_at"]); Assert.Equal(HttpStatusCode.OK, alive.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Actual_Web_rotations_retain_original_evidence_and_logout_only_their_origin()
    {
        var user = await Seed(); await using var db = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        async Task<Dictionary<string,string>> Login()
        {
            using var response = await host.Send(Post("api/auth/login", new { user.UserName, Password, TenantCode = "continuity_" + user.TenantId.ToString("N") }));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); return Cookies(response);
        }
        var original = await Login(); var healthy = await Login(); var current = original;
        var store = new AuthenticationInteractionStoreV1(db);
        var first = (await store.ReadSessionByRefreshAsync(AuthenticationInteractionStoreV1.Hash(original["cp6_rt"])))!.Parse();
        for (var i = 0; i < 2; i++)
        {
            using var response = await host.Send(Post("api/auth/refresh", new {}, current)); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            current = Cookies(response);
            var evidence = (await store.ReadSessionByRefreshAsync(AuthenticationInteractionStoreV1.Hash(current["cp6_rt"])))!.Parse();
            Assert.Equal(first, evidence);
            using var profile = await host.Profile(current["cp6_at"]); Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        }
        using var logout = await host.Send(Post("api/auth/logout", new {}, current)); Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        using var old = await host.Profile(original["cp6_at"]); Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
        using var alive = await host.Profile(healthy["cp6_at"]); Assert.Equal(HttpStatusCode.OK, alive.StatusCode);
        using var refreshHealthy = await host.Send(Post("api/auth/refresh", new {}, healthy)); Assert.Equal(HttpStatusCode.OK, refreshHealthy.StatusCode);
    }

    [TenantManagementPostgresTheory]
    [InlineData("disable-restore")]
    [InlineData("registration-delete-restore")]
    [InlineData("feature-enable-restore")]
    public async Task Actual_native_sessions_do_not_revive_after_device_or_admission_policy_ABA(string change)
    {
        var user = await Seed(); await using var db = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
        var client = new ClientContextDto { ClientKind = "Windows", DeviceId = "continuity-native", AppVersion = "1.0.0" };
        db.ClientDevices.Add(new ClientDevice { TenantId = user.TenantId, DeviceId = client.DeviceId, Platform = client.ClientKind,
            DeviceMode = ClientDeviceMode.Shared, Status = ClientDeviceStatus.Active, PublicKey = "fixture", ActivatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(); await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        var session = await NativeLogin(host, user, client);
        using (var profile = await host.Profile(session.AccessToken)) Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        if (change == "disable-restore")
        {
            await db.ClientDevices.Where(x => x.DeviceId == client.DeviceId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ClientDeviceStatus.Disabled));
            await db.ClientDevices.Where(x => x.DeviceId == client.DeviceId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ClientDeviceStatus.Active));
        }
        else if (change == "registration-delete-restore")
        {
            await db.ClientDevices.Where(x => x.DeviceId == client.DeviceId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true));
            await db.ClientDevices.Where(x => x.DeviceId == client.DeviceId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, false));
        }
        else
        {
            db.WmsFeatureFlags.Add(new WmsFeatureFlag { TenantId = user.TenantId, WarehouseCd = "TEST", ProductionMoveEnabled = true });
            await db.SaveChangesAsync();
            await db.WmsFeatureFlags.ExecuteUpdateAsync(s => s.SetProperty(x => x.ProductionMoveEnabled, false));
        }
        using var denied = await host.Profile(session.AccessToken); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var refresh = await host.Send(Post("api/client-auth/refresh", new NativeRefreshRequest { Client = client, RefreshToken = session.RefreshToken }));
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        var fresh = await NativeLogin(host, user, client);
        using var valid = await host.Profile(fresh.AccessToken); Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_quick_switch_requires_original_window_and_never_extends_it(bool tamperWindow)
    {
        var user = await Seed(); await using var db = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
        await db.Sys_Tenants.Where(x => x.Id == user.TenantId).ExecuteUpdateAsync(s => s.SetProperty(x => x.QuickSwitchAllowed, true));
        var target = new Sys_User { TenantId = user.TenantId, UserName = "quick-target", Password = new BCryptPasswordHasher(4).Hash(Password),
            Enable = true, BadgeNo = "quick-badge", QuickPinHash = new BCryptPasswordHasher(4).Hash("123456") };
        db.Sys_Users.Add(target);
        var client = new ClientContextDto { ClientKind = "Windows", DeviceId = "quick-native", AppVersion = "1.0.0" };
        db.ClientDevices.Add(new ClientDevice { TenantId = user.TenantId, DeviceId = client.DeviceId, Platform = client.ClientKind,
            DeviceMode = ClientDeviceMode.Shared, Status = ClientDeviceStatus.Active, PublicKey = "fixture", ActivatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(); await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        var session = await NativeLogin(host, user, client);
        var deadline = await db.ClientDevices.AsNoTracking().Select(x => x.FullAuthExpiresAt).SingleAsync(); Assert.NotNull(deadline);
        if (tamperWindow)
        {
            await db.ClientDevices.ExecuteUpdateAsync(s => s.SetProperty(x => x.FullAuthExpiresAt, deadline.Value.AddHours(1)));
            await db.ClientDevices.ExecuteUpdateAsync(s => s.SetProperty(x => x.FullAuthExpiresAt, deadline));
        }
        var request = Post("api/client-auth/quick-switch", new QuickSwitchRequest { TenantCode = "continuity_" + user.TenantId.ToString("N"), BadgeNo = target.BadgeNo!, Pin = "123456", Client = client });
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.AccessToken);
        using var response = await host.Send(request);
        if (tamperWindow) { Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); return; }
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var quick = (await response.Content.ReadFromJsonAsync<NativeAuthResult>())!.Session!;
        using var refreshed = await host.Send(Post("api/client-auth/refresh", new NativeRefreshRequest { Client = client, RefreshToken = quick.RefreshToken }));
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var next = (await refreshed.Content.ReadFromJsonAsync<TokenSessionDto>())!;
        using var profile = await host.Profile(next.AccessToken); Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        Assert.Equal(deadline, await db.ClientDevices.AsNoTracking().Select(x => x.FullAuthExpiresAt).SingleAsync());
        var evidence = (await new AuthenticationInteractionStoreV1(db).ReadSessionByRefreshAsync(AuthenticationInteractionStoreV1.Hash(next.RefreshToken)))!.Parse();
        Assert.Equal(AuthenticationPrimaryMethod.QuickSwitchPin, evidence.Primary.PrimaryMethod); Assert.Equal(target.Id, evidence.Primary.UserId);
        Assert.NotNull(evidence.Device!.SharedWindow); Assert.Null(evidence.Factor);
    }

    private static async Task<TokenSessionDto> NativeLogin(AuthenticationApiProcess host, Sys_User user, ClientContextDto client)
    {
        using var login = await host.Send(Post("api/client-auth/login", new NativeLoginRequest { Client = client, UserName = user.UserName,
            Password = Password, TenantCode = "continuity_" + user.TenantId.ToString("N") }));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode); return (await login.Content.ReadFromJsonAsync<NativeAuthResult>())!.Session!;
    }

    [CurrentPermissionPostgresFact]
    public async Task Actual_five_PIN_failures_require_fresh_full_authentication_even_if_device_fields_are_restored()
    {
        var user = await Seed(); await using var db = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
        await db.Sys_Tenants.Where(x => x.Id == user.TenantId).ExecuteUpdateAsync(s => s.SetProperty(x => x.QuickSwitchAllowed, true));
        await db.Sys_Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.BadgeNo, "failure-badge")
            .SetProperty(x => x.QuickPinHash, new BCryptPasswordHasher(4).Hash("123456")));
        var client = new ClientContextDto { ClientKind = "Windows", DeviceId = "pin-failure-device", AppVersion = "1.0.0" };
        db.ClientDevices.Add(new ClientDevice { TenantId = user.TenantId, DeviceId = client.DeviceId, Platform = client.ClientKind,
            DeviceMode = ClientDeviceMode.Shared, Status = ClientDeviceStatus.Active, PublicKey = "fixture", ActivatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(); await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        var session = await NativeLogin(host, user, client);
        var deadline = await db.ClientDevices.AsNoTracking().Select(x => x.FullAuthExpiresAt).SingleAsync();
        async Task<HttpResponseMessage> Switch(string pin)
        {
            var request = Post("api/client-auth/quick-switch", new QuickSwitchRequest { Client = client, TenantCode = "continuity_" + user.TenantId.ToString("N"), BadgeNo = "failure-badge", Pin = pin });
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.AccessToken);
            return await host.Send(request);
        }
        for (var i = 0; i < 5; i++) { using var failed = await Switch("999999"); Assert.False(failed.IsSuccessStatusCode); }
        var device = await db.ClientDevices.AsNoTracking().SingleAsync(); Assert.Equal(5, device.QuickSwitchFailureCount); Assert.Null(device.FullAuthExpiresAt);
        await db.ClientDevices.ExecuteUpdateAsync(s => s.SetProperty(x => x.QuickSwitchFailureCount, 0).SetProperty(x => x.FullAuthExpiresAt, deadline));
        using (var denied = await Switch("123456")) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        session = await NativeLogin(host, user, client);
        using var allowed = await Switch("123456"); Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [TenantManagementPostgresTheory]
    [InlineData("access")]
    [InlineData("refresh")]
    public async Task Actual_policy_change_cannot_preserve_original_non_MFA_session(string endpoint)
    {
        var user = await Seed(); await using var db = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        using var login = await host.Send(Post("api/auth/login", new { user.UserName, Password, TenantCode = "continuity_" + user.TenantId.ToString("N") }));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode); var cookies = Cookies(login);
        await db.Sys_Tenants.Where(x => x.Id == user.TenantId).ExecuteUpdateAsync(s => s.SetProperty(x => x.TwoFactorMode, 2));
        using var response = endpoint == "access" ? await host.Profile(cookies["cp6_at"]) : await host.Send(Post("api/auth/refresh", new { }, cookies));
        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, response.StatusCode.ToString());
    }

    [CurrentPermissionPostgresFact]
    public async Task Refresh_without_original_completion_receipt_requires_new_authentication()
    {
        var user = await Seed(); var tenant = new TenantContext { CurrentTenantId = user.TenantId }; await using var db = server.Context(tenant);
        var service = new RefreshTokenService(db, Options.Create(new SecurityOptions()), tenant);
        var raw = await service.IssueAsync(user, null, null);
        var denied = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RotateAsync(raw, null, null));
        Assert.Equal("REAUTH_REQUIRED", denied.Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Actual_quick_switch_cannot_skip_targets_required_local_factor()
    {
        var user = await Seed(); await using var db = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
        await db.Sys_Tenants.Where(x => x.Id == user.TenantId).ExecuteUpdateAsync(s => s.SetProperty(x => x.QuickSwitchAllowed, true));
        await db.Sys_Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.BadgeNo, "target-badge")
            .SetProperty(x => x.QuickPinHash, new BCryptPasswordHasher(4).Hash("123456")).SetProperty(x => x.TwoFactorEnabled, true)
            .SetProperty(x => x.TwoFactorSecret, "JBSWY3DPEHPK3PXP"));
        db.ClientDevices.Add(new ClientDevice { TenantId = user.TenantId, DeviceId = "shared-device", Platform = "Windows",
            DeviceMode = ClientDeviceMode.Shared, Status = ClientDeviceStatus.Active, PublicKey = "fixture", ActivatedAt = DateTime.UtcNow,
            FullAuthExpiresAt = DateTime.UtcNow.AddHours(1) }); await db.SaveChangesAsync();
        db.Sys_Users.Add(new Sys_User { TenantId = user.TenantId, UserName = "window-opener", Password = new BCryptPasswordHasher(4).Hash(Password), Enable = true });
        await db.SaveChangesAsync();
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        var client = new ClientContextDto { ClientKind = "Windows", DeviceId = "shared-device", AppVersion = "1.0.0" };
        using var login = await host.Send(Post("api/client-auth/login", new { Client = client, UserName = "window-opener", Password, TenantCode = "continuity_" + user.TenantId.ToString("N") }));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode); var session = (await login.Content.ReadFromJsonAsync<NativeAuthResult>())!.Session!;
        var request = Post("api/client-auth/quick-switch", new QuickSwitchRequest { TenantCode = "continuity_" + user.TenantId.ToString("N"),
            BadgeNo = "target-badge", Pin = "123456", Client = client });
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.AccessToken);
        using var response = await host.Send(request);
        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, response.StatusCode.ToString());
    }

    private async Task<Sys_User> Seed()
    {
        var tenant = new TenantContext { CurrentTenantId = Guid.NewGuid() }; await using var db = server.Context(tenant);
        db.Sys_Tenants.Add(new Sys_Tenant { Id = tenant.CurrentTenantId, TenantCode = "continuity_" + tenant.CurrentTenantId.ToString("N"), TenantName = "Continuity", TwoFactorMode = 0 });
        var user = new Sys_User { TenantId = tenant.CurrentTenantId, UserName = "continuity-user", Password = new BCryptPasswordHasher(4).Hash(Password), Enable = true };
        db.Sys_Users.Add(user); await db.SaveChangesAsync(); return user;
    }

    private static HttpRequestMessage Post(string path, object body, Dictionary<string,string>? cookies = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (cookies != null)
        {
            request.Headers.Add("Cookie", string.Join("; ", cookies.Select(x => x.Key + "=" + x.Value)));
            request.Headers.Add("X-CSRF-Token", cookies["cp6_csrf"]);
        }
        return request;
    }
    private static Dictionary<string,string> Cookies(HttpResponseMessage response)
    {
        var result = new Dictionary<string,string>(StringComparer.Ordinal);
        if (response.Headers.TryGetValues("Set-Cookie", out var headers))
            foreach (var header in headers)
            {
                var pair = header.Split(';', 2)[0].Split('=', 2);
                if (pair.Length != 2) continue;
                if (pair[1].Length == 0) result.Remove(pair[0]); else result[pair[0]] = pair[1];
            }
        return result;
    }
}
