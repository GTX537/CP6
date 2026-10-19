using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using CP6.Core.Utilities;
using CP6.WebApi.Controllers.Sys;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Entity.DTOs.Client;

namespace CP6.Tests.Platform;

[Collection("TenantManagementPostgres")]
public sealed partial class AuthenticationStatePostgreSqlTests(TenantManagementPostgresFixture server)
{
    [CurrentPermissionPostgresFact]
    public async Task Actual_Program_rejects_old_access_after_password_change()
    {
        var user = await Seed();
        await using var db = server.Context(new TenantContext { CurrentTenantId = user.TenantId });
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        var token = host.Token(user);
        using (var valid = await host.Profile(token)) Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        await db.Sys_Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Password, "changed"));
        using var rejected = await host.Profile(token);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Actual_Program_preserves_Web_native_pending_and_restricted_contracts()
    {
        var user = await Seed(); var tenant = new TenantContext { CurrentTenantId = user.TenantId };
        await using var db = server.Context(tenant);
        const string password = "Original1!Pwd";
        await db.Sys_Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Password, new BCryptPasswordHasher(4).Hash(password)));
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        var login = new { user.UserName, Password = password, TenantCode = "auth_" + user.TenantId.ToString("N") };
        using var web = await host.Send(new HttpRequestMessage(HttpMethod.Post, "api/auth/login") { Content = JsonContent.Create(login) });
        Assert.Equal(HttpStatusCode.OK, web.StatusCode);
        var cookies = Cookies(web); Assert.True(cookies.ContainsKey("cp6_at") && cookies.ContainsKey("cp6_rt"));
        using (var profile = await host.Profile(cookies["cp6_at"])) Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "api/auth/refresh");
        refreshRequest.Headers.Add("Cookie", string.Join("; ", cookies.Select(x => x.Key + "=" + x.Value)));
        refreshRequest.Headers.Add("X-CSRF-Token", cookies["cp6_csrf"]);
        using var refreshed = await host.Send(refreshRequest); Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.NotEqual(cookies["cp6_rt"], Cookies(refreshed)["cp6_rt"]);
        var webBody = await refreshed.Content.ReadAsStringAsync(); Assert.DoesNotContain("accessToken", webBody); Assert.DoesNotContain("refreshToken", webBody);
        var client = new ClientContextDto { ClientKind = "Windows", DeviceId = "owned-device", AppVersion = "1.0.0" };
        using var nativeLogin = await host.Send(new HttpRequestMessage(HttpMethod.Post, "api/client-auth/login")
            { Content = JsonContent.Create(new NativeLoginRequest { UserName = user.UserName, Password = password, TenantCode = login.TenantCode, Client = client }) });
        Assert.Equal(HttpStatusCode.OK, nativeLogin.StatusCode); Assert.False(nativeLogin.Headers.Contains("Set-Cookie"));
        var session = (await nativeLogin.Content.ReadFromJsonAsync<NativeAuthResult>())!.Session!;
        using var nativeRefresh = await host.Send(new HttpRequestMessage(HttpMethod.Post, "api/client-auth/refresh")
            { Content = JsonContent.Create(new NativeRefreshRequest { RefreshToken = session.RefreshToken, Client = client }) });
        Assert.Equal(HttpStatusCode.OK, nativeRefresh.StatusCode); Assert.False(nativeRefresh.Headers.Contains("Set-Cookie"));
        var rotated = (await nativeRefresh.Content.ReadFromJsonAsync<TokenSessionDto>())!;
        using (var profile = await host.Profile(rotated.AccessToken)) Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        await db.Sys_Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.MustChangePassword, true));
        using var restricted = await host.Send(new HttpRequestMessage(HttpMethod.Post, "api/auth/login") { Content = JsonContent.Create(login) });
        Assert.Equal(HttpStatusCode.OK, restricted.StatusCode);
        using (var profile = await host.Profile(Cookies(restricted)["cp6_at"])) Assert.Equal(HttpStatusCode.Forbidden, profile.StatusCode);
        await db.Sys_Tenants.Where(x => x.Id == user.TenantId).ExecuteUpdateAsync(s => s.SetProperty(x => x.TwoFactorMode, 2));
        using var pending = await host.Send(new HttpRequestMessage(HttpMethod.Post, "api/auth/login") { Content = JsonContent.Create(login) });
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        var pendingCookies = Cookies(pending); Assert.False(pendingCookies.ContainsKey("cp6_at")); Assert.False(pendingCookies.ContainsKey("cp6_rt"));
        using (var profile = await host.Profile(pendingCookies["cp6_2fa"])) Assert.Equal(HttpStatusCode.Unauthorized, profile.StatusCode);
        async Task<HttpResponseMessage> PendingPost(string path, object body)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
            request.Headers.Add("Cookie", string.Join("; ", pendingCookies.Select(x => x.Key + "=" + x.Value)));
            request.Headers.Add("X-CSRF-Token", pendingCookies["cp6_csrf"]); return await host.Send(request);
        }
        using var setup = await PendingPost("api/auth/2fa/setup", new { }); Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var secret = (await setup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("secret").GetString()!;
        var code = new OtpNet.Totp(OtpNet.Base32Encoding.ToBytes(secret)).ComputeTotp();
        using var enrolled = await PendingPost("api/auth/2fa/enroll", new { code }); Assert.Equal(HttpStatusCode.OK, enrolled.StatusCode);
        Assert.True(Cookies(enrolled).ContainsKey("cp6_at"));
        using (var profile = await host.Profile(Cookies(enrolled)["cp6_at"])) Assert.Equal(HttpStatusCode.Forbidden, profile.StatusCode); // Still restricted.
    }

    [CurrentPermissionPostgresFact]
    public async Task Actual_Program_requires_original_proof_and_current_enabled_tenant_and_account()
    {
        var user = await Seed(); var tenant = new TenantContext { CurrentTenantId = user.TenantId };
        await using var db = server.Context(tenant); await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        using (var noProof = await host.Profile(host.Token(user, ""))) Assert.Equal(HttpStatusCode.Unauthorized, noProof.StatusCode);
        var token = host.Token(user);
        await db.Sys_Tenants.Where(x => x.Id == user.TenantId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Enable, false));
        using (var disabled = await host.Profile(token)) Assert.Equal(HttpStatusCode.Unauthorized, disabled.StatusCode);
        await db.Sys_Tenants.Where(x => x.Id == user.TenantId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Enable, true));
        await db.Sys_Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Enable, false));
        using (var disabled = await host.Profile(token)) Assert.Equal(HttpStatusCode.Unauthorized, disabled.StatusCode);
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rotation_preserves_original_proof_and_concurrent_loser_does_not_revoke_winner(bool family)
    {
        var user = await Seed(); var tenant = new TenantContext { CurrentTenantId = user.TenantId };
        await using var issuer = server.Context(tenant); var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["CrmOidc:Enabled"] = family.ToString() }).Build();
        var source = new RefreshTokenService(issuer, Options.Create(new SecurityOptions()), tenant, config);
        var raw = await source.IssueAsync(user, null, null); await source.IssueAsync(user, null, null);
        var barrier = new ObserveBoth();
        await using var first = server.Context(tenant, null, barrier); await using var second = server.Context(tenant, null, barrier);
        async Task<(string? Raw, string? Error)> Attempt(CP6Context db)
        {
            try { return ((await new RefreshTokenService(db, Options.Create(new SecurityOptions()), tenant, config).RotateAsync(raw, null, null)).newToken, null); }
            catch (InvalidOperationException error) { return (null, error.Message); }
        }
        var results = await Task.WhenAll(Attempt(first), Attempt(second)).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Single(results, x => x.Raw != null); Assert.Equal("E-SEC-007", Assert.Single(results, x => x.Error != null).Error);
        await using var read = server.Context(tenant);
        var rows = await read.Sys_RefreshTokens.IgnoreQueryFilters().Where(x => x.UserId == user.Id).ToListAsync();
        Assert.Equal(2, rows.Count(x => x.RevokedAt == null)); Assert.All(rows, x => Assert.Equal(AuthSessionVersion.For(user), x.AuthenticationVersion));
        if (family) Assert.Equal(2, await read.Sys_BrowserSessions.IgnoreQueryFilters().CountAsync(x => x.UserId == user.Id));
    }

    [CurrentPermissionPostgresFact]
    public async Task Pending_and_rejected_completion_retries_preserve_other_sessions_but_true_reuse_is_separate()
    {
        var user = await Seed(); var tenant = new TenantContext { CurrentTenantId = user.TenantId };
        await using var db = server.Context(tenant); var service = new RefreshTokenService(db, Options.Create(new SecurityOptions()), tenant);
        var raw = await service.IssueAsync(user, null, null); var healthy = await service.IssueAsync(user, null, null);
        var (next, _) = await service.RotateForCompletionAsync(raw, null, null, RefreshTokenClientContext.Web);
        Assert.Equal("E-SEC-007", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.RotateAsync(raw, null, null))).Message);
        Assert.Equal("E-SEC-007", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.RotateAsync(next, null, null))).Message);
        await service.RejectCompletionAsync(next);
        Assert.Equal("E-SEC-007", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.RotateAsync(raw, null, null))).Message);
        await using var read = server.Context(tenant);
        var rejected = await read.Sys_RefreshTokens.IgnoreQueryFilters().SingleAsync(x => x.UserId == user.Id && x.CompletionRejectedAtUtc != null);
        Assert.NotNull(rejected.RevokedAt); Assert.Equal(DateTimeKind.Utc, rejected.CompletionRejectedAtUtc!.Value.Kind);
        var (valid, _) = await service.RotateAsync(healthy, null, null);
        Assert.Equal("E-SEC-008", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.RotateAsync(healthy, null, null))).Message);
        Assert.False(await read.Sys_RefreshTokens.IgnoreQueryFilters().AnyAsync(x => x.UserId == user.Id && x.RevokedAt == null));
    }

    [CurrentPermissionPostgresFact]
    public async Task Missing_original_version_and_wrong_client_do_not_revoke_other_sessions()
    {
        var user = await Seed(); var tenant = new TenantContext { CurrentTenantId = user.TenantId };
        await using var db = server.Context(tenant); var service = new RefreshTokenService(db, Options.Create(new SecurityOptions()), tenant);
        var raw = await service.IssueAsync(user, null, null); await service.IssueAsync(user, null, null);
        Assert.Equal("E-SEC-024", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.RotateAsync(raw, null, null,
            new RefreshTokenClientContext("Windows", "device", "1")))).Message);
        await db.Sys_RefreshTokens.IgnoreQueryFilters().Where(x => x.UserId == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.AuthenticationVersion, (string?)null));
        Assert.Equal("E-SEC-007", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.RotateAsync(raw, null, null))).Message);
        Assert.Equal(2, await db.Sys_RefreshTokens.IgnoreQueryFilters().CountAsync(x => x.UserId == user.Id && x.RevokedAt == null));
        Assert.False(await db.Sys_RefreshTokens.IgnoreQueryFilters().AnyAsync(x => x.UserId == user.Id && x.AuthenticationVersion != null));
    }

    private static Dictionary<string,string> Cookies(HttpResponseMessage response) => response.Headers.GetValues("Set-Cookie")
        .Select(x => x.Split(';')[0].Split('=', 2)).GroupBy(x => x[0]).Select(x => x.Last())
        .Where(x => x[1].Length > 0).ToDictionary(x => x[0], x => x[1]);

    private sealed class ObserveBoth : DbCommandInterceptor
    {
        private int observed; private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("Sys_RefreshTokens") && command.CommandText.Contains("@__hash") && !command.CommandText.Contains("FOR UPDATE"))
            {
                if (Interlocked.Increment(ref observed) == 2) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            return result;
        }
    }

    [TenantManagementPostgresTheory]
    [InlineData("password", "Web")]
    [InlineData("factor", "Windows")]
    [InlineData("disabled", "Android")]
    [InlineData("tenant", "Web")]
    [InlineData("tenant_disabled", "Web")]
    [InlineData("restricted", "Web")]
    public async Task Refresh_rejects_changed_original_state_without_rotating(string change, string kind)
    {
        var user = await Seed(); var tenant = new TenantContext { CurrentTenantId = user.TenantId };
        await using var db = server.Context(tenant);
        if (change == "factor")
        {
            user.TwoFactorEnabled = true; user.TwoFactorSecret = "original-active-factor";
            await db.Sys_Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.TwoFactorEnabled, true).SetProperty(x => x.TwoFactorSecret, user.TwoFactorSecret));
            // The baseline login occurs after this setup, so read the database-owned revision.
            // Subsequent changes below must still be rejected against this original login proof.
            user = await db.Sys_Users.AsNoTracking().SingleAsync(x => x.Id == user.Id);
        }
        var service = new RefreshTokenService(db, Options.Create(new SecurityOptions()), tenant);
        var client = new RefreshTokenClientContext(kind, kind == "Web" ? null : "device", "1");
        var raw = await service.IssueAsync(user, null, null, client);
        await using (var writer = server.Context(tenant))
        {
            var users = writer.Sys_Users.Where(x => x.Id == user.Id);
            if (change == "password") await users.ExecuteUpdateAsync(s => s.SetProperty(x => x.Password, "changed"));
            if (change == "factor") await users.ExecuteUpdateAsync(s => s.SetProperty(x => x.TwoFactorSecret, "changed"));
            if (change == "disabled") await users.ExecuteUpdateAsync(s => s.SetProperty(x => x.Enable, false));
            if (change == "tenant") await users.ExecuteUpdateAsync(s => s.SetProperty(x => x.TenantId, TenantContext.DefaultTenant));
            if (change == "tenant_disabled") await writer.Sys_Tenants.Where(x => x.Id == user.TenantId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Enable, false));
            if (change == "restricted") await users.ExecuteUpdateAsync(s => s.SetProperty(x => x.MustChangePassword, true));
        }
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RotateAsync(raw, null, null, client));
        Assert.Equal("E-SEC-007", error.Message);
        await using var read = server.Context(tenant);
        var row = Assert.Single(await read.Sys_RefreshTokens.IgnoreQueryFilters().Where(x => x.UserId == user.Id).ToListAsync());
        Assert.Null(row.RevokedAt); Assert.Null(row.ReplacedByTokenHash);
    }

    private async Task<Sys_User> Seed()
    {
        var tenant = Guid.NewGuid(); await using var db = server.Context(new TenantContext { CurrentTenantId = tenant });
        db.Sys_Tenants.Add(new Sys_Tenant { Id = tenant, TenantCode = "auth_" + tenant.ToString("N"), TenantName = "auth" });
        var user = new Sys_User { Id = Guid.NewGuid(), TenantId = tenant, UserName = "auth-user", Password = "original", Enable = true };
        db.Sys_Users.Add(user); await db.SaveChangesAsync(); return user;
    }
}

// Starts the actual Program, routes, authentication events and middleware against this run's owned PG database.
// No existing appsettings/DEV process or port is used. The child process is always stopped on disposal.
internal sealed class AuthenticationApiProcess(Process process, HttpClient client, string secret) : IAsyncDisposable
{
    internal Task<HttpResponseMessage> Send(HttpRequestMessage request) => client.SendAsync(request);
    internal string Token(Sys_User user, string? version = null) => JwtHelper.GenerateToken(user.Id.ToString(), user.UserName,
        secret, "auth-state-component", "auth-state-component", 5, user.TenantId,
        mustChangePassword: user.MustChangePassword, isPlatformAdmin: user.IsPlatformAdmin, authenticationVersion: version ?? AuthSessionVersion.For(user));
    internal Task<HttpResponseMessage> Profile(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "api/auth/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }
    internal static async Task<AuthenticationApiProcess> Start(string connection)
    {
        var directory = Path.Combine(Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!, "actual-api", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var executable = Path.Combine(Environment.GetEnvironmentVariable("DOTNET_ROOT")!, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        var start = new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(AuthController).Assembly.Location);
        start.ArgumentList.Add("--contentRoot"); start.ArgumentList.Add(directory);
        foreach (var pair in new Dictionary<string,string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development", ["ASPNETCORE_URLS"] = "http://127.0.0.1:0",
            ["Database__Provider"] = "PostgreSql", ["ConnectionStrings__DefaultConnection"] = connection,
            ["ConnectionStrings__Redis"] = "", ["Startup__SkipDatabaseInitialization"] = "true", ["Startup__SkipHostedServices"] = "true",
            ["JWT__Secret"] = secret, ["JWT__Issuer"] = "auth-state-component", ["JWT__Audience"] = "auth-state-component",
            ["Logging__LogLevel__Default"] = "Warning", ["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "Information",
            ["Security__Cookie__Secure"] = "false", ["Security__Csrf__Enabled"] = "true", ["CrmOidc__Enabled"] = "false"
            , ["Space__Files__RootPath"] = Path.Combine(directory, "space-files")
        }) start.Environment[pair.Key] = pair.Value;
        var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var log = new StringBuilder(); var child = new Process { StartInfo = start, EnableRaisingEvents = true };
        void Read(string? line)
        {
            if (line == null) return;
            lock(log) log.AppendLine(line.Replace(connection, "[owned connection]").Replace(secret, "[key]"));
            var at = line.IndexOf("Now listening on: ", StringComparison.Ordinal);
            if (at >= 0) ready.TrySetResult(line[(at + "Now listening on: ".Length)..].Trim());
        }
        child.OutputDataReceived += (_, e) => Read(e.Data); child.ErrorDataReceived += (_, e) => Read(e.Data);
        child.Exited += (_, _) => ready.TrySetException(new InvalidOperationException("Owned API exited before readiness."));
        try
        {
            if (!child.Start()) throw new InvalidOperationException("Owned API failed to start.");
            child.BeginOutputReadLine(); child.BeginErrorReadLine();
            var url = await ready.Task.WaitAsync(TimeSpan.FromSeconds(40));
            return new AuthenticationApiProcess(child, new HttpClient(new HttpClientHandler { UseCookies = false })
                { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(15) }, secret);
        }
        catch
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(); child.Dispose();
            lock(log) File.WriteAllText(Path.Combine(directory, "startup.log"), log.ToString());
            throw;
        }
    }
    public async ValueTask DisposeAsync()
    {
        client.Dispose(); if (!process.HasExited) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync(); process.Dispose();
    }
}
