using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DTOs.Client;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CP6.Tests.Platform;

[Collection("TenantManagementPostgres")]
public sealed class AuthenticationEntryPostgreSqlTests(TenantManagementPostgresFixture server)
{
    private const string Password = "Original1!Password";

    [TenantManagementPostgresTheory]
    [InlineData("password")]
    [InlineData("policy")]
    [InlineData("fallback")]
    [InlineData("preparation-restored")]
    public async Task Actual_Web_pending_cannot_replace_original_primary_evidence_with_new_state(string change)
    {
        var user = await Seed(); var tenant = new TenantContext { CurrentTenantId = user.TenantId };
        await using var db = server.Context(tenant); await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        using var login = await host.Send(Post("api/auth/login", new { user.UserName, Password, TenantCode = "entry_" + user.TenantId.ToString("N") }));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode); var cookies = Cookies(login); Assert.True(cookies.ContainsKey("cp6_2fa"));
        using var setup = await host.Send(Post("api/auth/2fa/setup", new { }, cookies)); Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var secret = (await setup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("secret").GetString()!;
        if (change == "password") await db.Sys_Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Password, "changed-after-primary"));
        if (change == "policy") await db.Sys_Tenants.Where(x => x.Id == user.TenantId).ExecuteUpdateAsync(s => s.SetProperty(x => x.TwoFactorMode, 1));
        if (change == "fallback") await db.Sys_Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.AllowPasswordFallback, true));
        if (change == "preparation-restored")
        {
            await db.Sys_Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.TwoFactorSecret, "replaced-preparation"));
            await db.Sys_Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.TwoFactorSecret, secret));
        }
        using var enrolled = await host.Send(Post("api/auth/2fa/enroll", new { code = new OtpNet.Totp(OtpNet.Base32Encoding.ToBytes(secret)).ComputeTotp() }, cookies));
        Assert.False(enrolled.IsSuccessStatusCode);
        Assert.False(Cookies(enrolled).ContainsKey("cp6_at")); Assert.False(Cookies(enrolled).ContainsKey("cp6_rt"));
        Assert.False((await db.Sys_Users.AsNoTracking().SingleAsync(x => x.Id == user.Id)).TwoFactorEnabled);
    }

    [CurrentPermissionPostgresFact]
    public async Task Actual_native_challenge_is_bound_to_original_client_device()
    {
        var user = await Seed(enabledFactor: true); var tenant = new TenantContext { CurrentTenantId = user.TenantId };
        await using var db = server.Context(tenant); await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        var client = new ClientContextDto { ClientKind = "Windows", DeviceId = "original-device", AppVersion = "1.0.0" };
        using var login = await host.Send(Post("api/client-auth/login", new NativeLoginRequest { UserName = user.UserName, Password = Password, TenantCode = "entry_" + user.TenantId.ToString("N"), Client = client }));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode); var pending = (await login.Content.ReadFromJsonAsync<NativeAuthResult>())!;
        Assert.Equal("twoFactorRequired", pending.State); Assert.Null(pending.Session);
        var otherClient = new ClientContextDto { ClientKind = "Windows", DeviceId = "other-device", AppVersion = "1.0.0" };
        using var verified = await host.Send(Post("api/client-auth/2fa/verify", new { Client = otherClient, pending.ChallengeToken, Method = "totp", Code = new OtpNet.Totp(OtpNet.Base32Encoding.ToBytes(user.TwoFactorSecret!)).ComputeTotp() }));
        Assert.False(verified.IsSuccessStatusCode); Assert.DoesNotContain("accessToken", await verified.Content.ReadAsStringAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Actual_Web_enrollment_commits_one_session_and_original_proof_transition()
    {
        var user = await Seed(); await using var db = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        using var login = await host.Send(Post("api/auth/login", new { user.UserName, Password, TenantCode = "entry_" + user.TenantId.ToString("N") }));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode); var pending = Cookies(login);
        using var setup = await host.Send(Post("api/auth/2fa/setup", new { }, pending)); Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var secret = (await setup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("secret").GetString()!;
        var code = new OtpNet.Totp(OtpNet.Base32Encoding.ToBytes(secret)).ComputeTotp();
        var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => host.Send(Post("api/auth/2fa/enroll", new { code }, pending))));
        try
        {
            Assert.Single(attempts.Where(x => x.IsSuccessStatusCode));
            Assert.Single(attempts.Where(x => !x.IsSuccessStatusCode));
            var cookies = Cookies(attempts.Single(x => x.IsSuccessStatusCode)); Assert.True(cookies.ContainsKey("cp6_at")); Assert.False(cookies.ContainsKey("cp6_2fa"));
            var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(cookies["cp6_at"]);
            var receipt = await new AuthenticationInteractionStoreV1(db).ReadSessionAsync(jwt.Id); Assert.NotNull(receipt); Assert.False(receipt.Rejected);
            var evidence = receipt.Parse(); Assert.Equal(AuthSessionVersion.For(user), evidence.Primary.AuthenticationVersion);
            Assert.NotEqual(evidence.Primary.AuthenticationVersion, evidence.AuthenticationVersion); Assert.NotNull(evidence.EnrollmentTransition);
            Assert.Equal(evidence.AuthenticationVersion, evidence.Factor!.AuthenticationVersion);
            Assert.Equal(1, await db.Sys_RefreshTokens.CountAsync(x => x.UserId == user.Id));
            var row = await db.Sys_RefreshTokens.AsNoTracking().SingleAsync(x => x.UserId == user.Id);
            Assert.Equal(new DateTimeOffset(row.ExpiresAt).ToUniversalTime(), evidence.OriginalExpiresAtUtc);
            using var refreshed = await host.Send(Post("api/auth/refresh", new { }, cookies));
            Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
            var renewed = Cookies(refreshed);
            var carried = (await new AuthenticationInteractionStoreV1(db).ReadSessionByRefreshAsync(AuthenticationInteractionStoreV1.Hash(renewed["cp6_rt"])))!.Parse();
            Assert.Equal(evidence, carried);
            using var profile = await host.Profile(renewed["cp6_at"]); Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
            using var replay = await host.Send(Post("api/auth/2fa/enroll", new { code }, pending)); Assert.False(replay.IsSuccessStatusCode);
        }
        finally { foreach (var response in attempts) response.Dispose(); }
    }

    [CurrentPermissionPostgresFact]
    public async Task Actual_native_wrong_factor_keeps_failure_and_valid_retry_cannot_cross_devices()
    {
        var user = await Seed(true); await using var db = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        var client = new ClientContextDto { ClientKind = "Windows", DeviceId = "factor-device", AppVersion = "1.0.0" };
        using var login = await host.Send(Post("api/client-auth/login", new { user.UserName, Password, TenantCode = "entry_" + user.TenantId.ToString("N"), client }));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var pending = (await login.Content.ReadFromJsonAsync<NativeAuthResult>())!;
        using var bad = await host.Send(Post("api/client-auth/2fa/verify", new { client, pending.ChallengeToken, method = "totp", code = "invalid" }));
        Assert.False(bad.IsSuccessStatusCode);
        var fresh = await db.Sys_Users.AsNoTracking().SingleAsync(x => x.Id == user.Id);
        Assert.True(fresh.FailedLoginCount > 0);
        var code = new OtpNet.Totp(OtpNet.Base32Encoding.ToBytes(user.TwoFactorSecret!)).ComputeTotp();
        using var good = await host.Send(Post("api/client-auth/2fa/verify", new { client, pending.ChallengeToken, method = "totp", code }));
        Assert.Equal(HttpStatusCode.OK, good.StatusCode); var result = (await good.Content.ReadFromJsonAsync<NativeAuthResult>())!;
        Assert.Equal("authenticated", result.State); Assert.NotNull(result.Session); Assert.False(Cookies(good).ContainsKey("cp6_at"));
        using var replay = await host.Send(Post("api/client-auth/2fa/verify", new { client, pending.ChallengeToken, method = "totp", code })); Assert.False(replay.IsSuccessStatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Actual_enrollment_audit_failure_rolls_back_epoch_and_session_but_does_not_reopen_claim()
    {
        var user = await Seed(); await using var db = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        using var login = await host.Send(Post("api/auth/login", new { user.UserName, Password, TenantCode = "entry_" + user.TenantId.ToString("N") }));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode); var cookies = Cookies(login);
        using var setup = await host.Send(Post("api/auth/2fa/setup", new { }, cookies)); Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var secret = (await setup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("secret").GetString()!;
        var code = new OtpNet.Totp(OtpNet.Base32Encoding.ToBytes(secret)).ComputeTotp();
        var fn = "entry_audit_" + user.Id.ToString("N");
        await db.Database.ExecuteSqlRawAsync($"CREATE FUNCTION public.{fn}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"UserId\"='{user.Id}'::uuid AND NEW.\"EventType\"={(int)SecurityEventType.LoginSuccess} THEN RAISE EXCEPTION 'injected completion audit failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER {fn} BEFORE INSERT ON public.\"Sys_SecurityLogs\" FOR EACH ROW EXECUTE FUNCTION public.{fn}()");
        try
        {
            using var failed = await host.Send(Post("api/auth/2fa/enroll", new { code }, cookies)); Assert.False(failed.IsSuccessStatusCode); Assert.False(Cookies(failed).ContainsKey("cp6_at"));
            Assert.False((await db.Sys_Users.AsNoTracking().SingleAsync(x => x.Id == user.Id)).TwoFactorEnabled);
            Assert.False(await db.Sys_RefreshTokens.AnyAsync(x => x.UserId == user.Id));
            Assert.Equal(1L, await db.Database.SqlQuery<long>($"SELECT count(*) AS \"Value\" FROM cp6_authentication_v1.consumed_totp WHERE user_id={user.Id}").SingleAsync());
        }
        finally { await db.Database.ExecuteSqlRawAsync($"DROP TRIGGER {fn} ON public.\"Sys_SecurityLogs\"; DROP FUNCTION public.{fn}()"); }
        using var replay = await host.Send(Post("api/auth/2fa/enroll", new { code }, cookies)); Assert.False(replay.IsSuccessStatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Successful_totp_counter_cannot_authenticate_a_second_challenge_but_next_counter_can()
    {
        var user = await Seed(true); await using var db = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        var client = new ClientContextDto { ClientKind = "Windows", DeviceId = "counter-device", AppVersion = "1.0" };
        async Task<string> Challenge()
        {
            using var response = await host.Send(Post("api/client-auth/login", new { client, user.UserName, Password, TenantCode = "entry_" + user.TenantId.ToString("N") }));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await response.Content.ReadFromJsonAsync<NativeAuthResult>())!.ChallengeToken!;
        }
        var first = await Challenge(); var second = await Challenge();
        var totp = new OtpNet.Totp(OtpNet.Base32Encoding.ToBytes(user.TwoFactorSecret!)); var at = DateTime.UtcNow; var code = totp.ComputeTotp(at);
        using var success = await host.Send(Post("api/client-auth/2fa/verify", new { client, challengeToken = first, method = "totp", code }));
        Assert.Equal(HttpStatusCode.OK, success.StatusCode); var session = (await success.Content.ReadFromJsonAsync<NativeAuthResult>())!.Session!;
        using var duplicate = await host.Send(Post("api/client-auth/2fa/verify", new { client, challengeToken = second, method = "totp", code }));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode); Assert.False(await db.Sys_RefreshTokens.CountAsync(x => x.UserId == user.Id) > 1);
        using var healthy = await host.Profile(session.AccessToken); Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
        using var next = await host.Send(Post("api/client-auth/2fa/verify", new { client, challengeToken = second, method = "totp", code = totp.ComputeTotp(at.AddSeconds(30)) }));
        Assert.Equal(HttpStatusCode.OK, next.StatusCode); Assert.Equal(2, await db.Sys_RefreshTokens.CountAsync(x => x.UserId == user.Id));
    }

    [CurrentPermissionPostgresFact]
    public async Task Actual_native_client_without_restricted_flow_receives_no_ordinary_session()
    {
        var user = await Seed(); await using var db = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
        await db.Sys_Tenants.Where(x => x.Id == user.TenantId).ExecuteUpdateAsync(s => s.SetProperty(x => x.TwoFactorMode, 0));
        await db.Sys_Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.MustChangePassword, true));
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        var client = new ClientContextDto { ClientKind = "Windows", DeviceId = "restricted-device", AppVersion = "1.0" };
        using var response = await host.Send(Post("api/client-auth/login", new { client, user.UserName, Password, TenantCode = "entry_" + user.TenantId.ToString("N") }));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("AUTH_RESTRICTED_SESSION_UNAVAILABLE", await response.Content.ReadAsStringAsync());
        Assert.False(await db.Sys_RefreshTokens.AnyAsync(x => x.UserId == user.Id));
    }

    private async Task<Sys_User> Seed(bool enabledFactor = false)
    {
        var id = Guid.NewGuid(); await using var db = server.Context(new TenantContext { CurrentTenantId = id });
        db.Sys_Tenants.Add(new Sys_Tenant { Id = id, TenantCode = "entry_" + id.ToString("N"), TenantName = "Entry", TwoFactorMode = 2 });
        var user = new Sys_User { Id = Guid.NewGuid(), UserName = "entry-user", Password = new BCryptPasswordHasher(4).Hash(Password),
            TenantId = id, Enable = true, TwoFactorEnabled = enabledFactor, TwoFactorSecret = enabledFactor ? OtpNet.Base32Encoding.ToString(OtpNet.KeyGeneration.GenerateRandomKey(20)) : null };
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
        var cookies = new Dictionary<string,string>(StringComparer.Ordinal);
        if (!response.Headers.TryGetValues("Set-Cookie", out var headers)) return cookies;
        foreach (var header in headers)
        {
            var pair = header.Split(';', 2)[0].Split('=', 2);
            if (pair.Length != 2) continue;
            if (pair[1].Length == 0) cookies.Remove(pair[0]); else cookies[pair[0]] = pair[1];
        }
        return cookies;
    }
}
