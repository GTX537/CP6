using System.Data.Common;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DTOs.Client;
using CP6.WebApi.Controllers;
using CP6.WebApi.Controllers.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace CP6.Tests.Platform;

public sealed partial class AuthenticationStatePostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Cancellation_after_completion_commit_quarantines_replacement_before_cookie_output()
    {
        var user = await Seed(); var tenant = new TenantContext { CurrentTenantId = user.TenantId };
        await using var db = server.Context(tenant); var options = Options.Create(new SecurityOptions());
        var refresh = new RefreshTokenService(db, options, tenant); var raw = await refresh.IssueAsync(user, null, null);
        var healthy = await refresh.IssueAsync(user, null, null); using var cancel = new CancellationTokenSource();
        var wrapper = new Mock<IRefreshTokenService>(MockBehavior.Strict);
        wrapper.Setup(x => x.RotateForCompletionAsync(raw, It.IsAny<string>(), It.IsAny<string>(), RefreshTokenClientContext.Web))
            .Returns(() => refresh.RotateForCompletionAsync(raw, null, null, RefreshTokenClientContext.Web));
        wrapper.Setup(x => x.CompleteRotationAsync(It.IsAny<string>())).Returns<string>(async next =>
        { await refresh.CompleteRotationAsync(next); cancel.Cancel(); });
        wrapper.Setup(x => x.RejectCompletionAsync(It.IsAny<string>())).Returns<string>(refresh.RejectCompletionAsync);
        var sessions = new Mock<IAuthSessionService>();
        sessions.Setup(x => x.BuildAccessToken(It.IsAny<Sys_User>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>())).Returns("prepared-access");
        var http = new DefaultHttpContext { RequestAborted = cancel.Token }; http.Request.Headers.Cookie = "cp6_rt=" + raw;
        var controller = new AuthController(db, new ConfigurationBuilder().Build(), Mock.Of<ICurrentPermissionContext>(), tenant,
            Mock.Of<IPasswordHasher>(), Mock.Of<IPasswordPolicyService>(), Mock.Of<ILoginSecurityService>(), new SecurityAuditService(db),
            wrapper.Object, Mock.Of<ITokenBlacklistService>(), new AuthCookieWriter(options), options, Mock.Of<ITenantSsoConfigService>(),
            Mock.Of<ISsoService>(), Mock.Of<ITwoFactorService>(), Mock.Of<IPendingTokenStore>(), sessions.Object)
            { ControllerContext = new ControllerContext { HttpContext = http } };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => controller.Refresh());
        Assert.False(http.Response.Headers.ContainsKey("Set-Cookie"));
        Assert.Equal(1, await db.Sys_RefreshTokens.IgnoreQueryFilters().CountAsync(x => x.UserId == user.Id && x.CompletionRejectedAtUtc != null));
        Assert.Equal("E-SEC-007", (await Assert.ThrowsAsync<InvalidOperationException>(() => refresh.RotateAsync(raw, null, null))).Message);
        await refresh.RotateAsync(healthy, null, null);
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_expiring_during_persistence_or_completion_cannot_publish_a_fresh_session(bool duringCompletion)
    {
        var user = await Seed(); var tenant = new TenantContext { CurrentTenantId = user.TenantId };
        await using var issuer = server.Context(tenant); var service = new RefreshTokenService(issuer, Options.Create(new SecurityOptions()), tenant);
        var raw = await service.IssueAsync(user, null, null); var deadline = DateTime.Now.AddSeconds(2);
        await issuer.Sys_RefreshTokens.Where(x => x.UserId == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, deadline));
        if (duringCompletion)
        {
            var (next, _) = await service.RotateForCompletionAsync(raw, null, null, RefreshTokenClientContext.Web);
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, (deadline - DateTime.Now).TotalMilliseconds) + 50));
            Assert.Equal("E-SEC-007", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteRotationAsync(next))).Message);
            await service.RejectCompletionAsync(next);
            Assert.False(await issuer.Sys_RefreshTokens.IgnoreQueryFilters().AnyAsync(x => x.UserId == user.Id && x.RevokedAt == null));
        }
        else
        {
            var barrier = new BeforeReplacement(); await using var db = server.Context(tenant, null, barrier);
            var rotating = new RefreshTokenService(db, Options.Create(new SecurityOptions()), tenant).RotateAsync(raw, null, null);
            try
            {
                await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, (deadline - DateTime.Now).TotalMilliseconds) + 50));
            }
            finally { barrier.Release.TrySetResult(); }
            Assert.Equal("E-SEC-007", (await Assert.ThrowsAsync<InvalidOperationException>(() => rotating)).Message);
            var old = Assert.Single(await issuer.Sys_RefreshTokens.IgnoreQueryFilters().AsNoTracking().Where(x => x.UserId == user.Id).ToListAsync());
            Assert.Null(old.RevokedAt); Assert.Null(old.ReplacedByTokenHash);
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Actual_Program_allows_self_enrollment_setup_then_revokes_original_authentication_on_activation()
    {
        var user = await Seed(); var tenant = new TenantContext { CurrentTenantId = user.TenantId };
        await using var db = server.Context(tenant); await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        var token = host.Token(user);
        async Task<HttpResponseMessage> Post(string path, object body)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); return await host.Send(request);
        }
        using var setup = await Post("api/auth/2fa/setup-self", new { }); Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var secret = (await setup.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("secret").GetString()!;
        var code = new OtpNet.Totp(OtpNet.Base32Encoding.ToBytes(secret)).ComputeTotp();
        using var enrollment = await Post("api/auth/2fa/enroll-self", new { code }); Assert.Equal(HttpStatusCode.OK, enrollment.StatusCode);
        using var profile = await host.Profile(token); Assert.Equal(HttpStatusCode.Unauthorized, profile.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Actual_Program_accepts_simulation_round_trip_and_rejects_changed_real_actor()
    {
        var actor = await Seed(); var target = await Seed(); var tenant = new TenantContext { CurrentTenantId = actor.TenantId };
        await using var db = server.Context(tenant);
        db.Sys_Users.Add(new Sys_User { UserName = "recovery", Password = "fixture", IsPlatformAdmin = true, Enable = true }); await db.SaveChangesAsync();
        await db.Sys_Users.Where(x => x.Id == actor.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsPlatformAdmin, true)); actor.IsPlatformAdmin = true;
        await db.Sys_Users.IgnoreQueryFilters().Where(x => x.Id == target.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.MustChangePassword, true));
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);
        async Task<HttpResponseMessage> Switch(string token, bool end)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "api/platform/impersonation/" + (end ? "end" : "start"));
            if (!end) request.Content = JsonContent.Create(new { target.TenantId, UserId = target.Id, Reason = "owned validation" });
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); return await host.Send(request);
        }
        using var start = await Switch(host.Token(actor), false); Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var imp = Cookies(start)["cp6_at"];
        using (var profile = await host.Profile(imp)) Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        using var end = await Switch(imp, true); Assert.Equal(HttpStatusCode.OK, end.StatusCode);
        using (var profile = await host.Profile(Cookies(end)["cp6_at"])) Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        using var again = await Switch(Cookies(end)["cp6_at"], false); Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        await db.Sys_Users.Where(x => x.Id == actor.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.QuickPinHash, "changed-credential"));
        using (var profile = await host.Profile(Cookies(again)["cp6_at"])) Assert.Equal(HttpStatusCode.Unauthorized, profile.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Just_persisted_credential_times_have_the_same_version_as_PostgreSql_reads()
    {
        var user = await Seed(); var tenant = new TenantContext { CurrentTenantId = user.TenantId };
        await using var db = server.Context(tenant); var current = await db.Sys_Users.SingleAsync(x => x.Id == user.Id);
        current.PasswordChangedAt = new DateTime(2026,10,19,10,0,0).AddTicks(123457);
        current.TwoFactorEnrolledAt = current.PasswordChangedAt; await db.SaveChangesAsync();
        await using var read = server.Context(tenant); var stored = await read.Sys_Users.SingleAsync(x => x.Id == user.Id);
        Assert.NotEqual(current.PasswordChangedAt, stored.PasswordChangedAt); Assert.Equal(AuthSessionVersion.For(current), AuthSessionVersion.For(stored));
        var raw = await new RefreshTokenService(db, Options.Create(new SecurityOptions()), tenant).IssueAsync(current, null, null);
        await new RefreshTokenService(read, Options.Create(new SecurityOptions()), tenant).RotateAsync(raw, null, null);
    }

    [CurrentPermissionPostgresFact]
    public async Task Forward_migration_preserves_legacy_rows_without_fabricating_authentication_evidence()
    {
        var tenant = new TenantContext { CurrentTenantId = Guid.NewGuid() };
        await using var owner = server.Context(tenant); var name = "w25_auth_" + Guid.NewGuid().ToString("N");
        await owner.Database.ExecuteSqlRawAsync($"CREATE DATABASE {name}"); await using var db = server.Context(tenant);
        db.Database.SetConnectionString(new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()) { Database = name }.ConnectionString);
        await db.GetService<IMigrator>().MigrateAsync("20261019093000_SecurityAuditContextV1");
        var user = Guid.NewGuid(); var id = Guid.NewGuid(); var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        db.Sys_Tenants.Add(new Sys_Tenant { Id = tenant.CurrentTenantId, TenantCode = "legacy", TenantName = "legacy" });
        db.Sys_Users.Add(new Sys_User { Id = user, TenantId = tenant.CurrentTenantId, UserName = "legacy-auth", Password = "fixture", Enable = true });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public."Sys_RefreshTokens" ("Id","TenantId","UserId","TokenHash","ExpiresAt","ClientKind","CreateDate")
            VALUES ({id},{tenant.CurrentTenantId},{user},{hash},LOCALTIMESTAMP+INTERVAL '1 day','Web',LOCALTIMESTAMP);
            """);
        await db.Database.MigrateAsync(); Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var old = await db.Sys_RefreshTokens.IgnoreQueryFilters().SingleAsync(x => x.Id == id);
        Assert.Equal(hash, old.TokenHash); Assert.Null(old.AuthenticationVersion); Assert.Null(old.CompletionRejectedAtUtc); Assert.False(old.CompletionPending);
        Assert.Equal("E-SEC-007", (await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RefreshTokenService(db, Options.Create(new SecurityOptions()), tenant).RotateAsync(raw, null, null))).Message);
    }

    [TenantManagementPostgresTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Controller_completion_failure_quarantines_only_replacement_or_returns_unknown(bool native, bool quarantineFails)
    {
        var user = await Seed(); var tenant = new TenantContext { CurrentTenantId = user.TenantId };
        await using var db = server.Context(tenant, null, quarantineFails ? [new FailQuarantine()] : []);
        var options = Options.Create(new SecurityOptions()); var refresh = new RefreshTokenService(db, options, tenant);
        var client = new RefreshTokenClientContext(native ? "Windows" : "Web", native ? "device" : null, "1.0.0");
        var raw = await refresh.IssueAsync(user, null, null, client); var healthy = await refresh.IssueAsync(user, null, null, client);
        var sessions = new Mock<IAuthSessionService>();
        sessions.Setup(x => x.BuildAccessToken(It.IsAny<Sys_User>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>())).Returns("prepared-access");
        sessions.Setup(x => x.BuildProfileAsync(It.IsAny<Sys_User>(), It.IsAny<bool>())).ThrowsAsync(new IOException("fixture profile failure"));
        var http = new DefaultHttpContext(); object? result = null;
        async Task Invoke()
        {
            if (native)
            {
                var controller = new ClientAuthController(db, tenant, Mock.Of<IPasswordHasher>(), Mock.Of<IPasswordPolicyService>(),
                    Mock.Of<ILoginSecurityService>(), new SecurityAuditService(db), refresh, Mock.Of<ITokenBlacklistService>(),
                    Mock.Of<ITenantSsoConfigService>(), Mock.Of<ISsoService>(), Mock.Of<ISsoStateStore>(), Mock.Of<INativeSsoGrantStore>(),
                    Mock.Of<ITwoFactorService>(), Mock.Of<IPendingTokenStore>(), sessions.Object, Mock.Of<IClientDeviceService>(), options)
                    { ControllerContext = new ControllerContext { HttpContext = http } };
                result = (await controller.Refresh(new NativeRefreshRequest { RefreshToken = raw,
                    Client = new ClientContextDto { ClientKind = "Windows", DeviceId = "device", AppVersion = "1.0.0" } })).Result;
            }
            else
            {
                var cookies = new Mock<IAuthCookieWriter>();
                cookies.Setup(x => x.WriteAuthCookies(It.IsAny<HttpResponse>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                    .Callback<HttpResponse,string,string,string>((response, _, _, _) =>
                    { response.Cookies.Append("cp6_at", "partial-staged"); throw new IOException("fixture cookie failure"); });
                var controller = new AuthController(db, new ConfigurationBuilder().Build(), Mock.Of<ICurrentPermissionContext>(), tenant,
                    Mock.Of<IPasswordHasher>(), Mock.Of<IPasswordPolicyService>(), Mock.Of<ILoginSecurityService>(), new SecurityAuditService(db),
                    refresh, Mock.Of<ITokenBlacklistService>(), cookies.Object, options, Mock.Of<ITenantSsoConfigService>(), Mock.Of<ISsoService>(),
                    Mock.Of<ITwoFactorService>(), Mock.Of<IPendingTokenStore>(), sessions.Object)
                    { ControllerContext = new ControllerContext { HttpContext = http } };
                http.Request.Headers.Cookie = "cp6_rt=" + raw; result = await controller.Refresh();
            }
        }
        if (quarantineFails)
        {
            await Invoke(); Assert.Equal(503, Assert.IsType<ObjectResult>(result).StatusCode);
        }
        else await Assert.ThrowsAsync<IOException>(Invoke);
        Assert.False(http.Response.Headers.ContainsKey("Set-Cookie"));
        await using var read = server.Context(tenant);
        var rows = await read.Sys_RefreshTokens.IgnoreQueryFilters().Where(x => x.UserId == user.Id).ToListAsync();
        var replacement = Assert.Single(rows, x => x.TokenHash == rows.Single(x => x.ReplacedByTokenHash != null).ReplacedByTokenHash);
        Assert.Equal(quarantineFails, replacement.CompletionPending);
        Assert.Equal(!quarantineFails, replacement.CompletionRejectedAtUtc != null);
        Assert.Equal("E-SEC-007", (await Assert.ThrowsAsync<InvalidOperationException>(() => refresh.RotateAsync(raw, null, null, client))).Message);
        await refresh.RotateAsync(healthy, null, null, client);
    }

    [CurrentPermissionPostgresFact]
    public async Task Credential_writer_waits_for_rotation_and_old_original_proof_is_never_refreshed()
    {
        var user = await Seed(); var tenant = new TenantContext { CurrentTenantId = user.TenantId };
        await using var issuer = server.Context(tenant); var source = new RefreshTokenService(issuer, Options.Create(new SecurityOptions()), tenant);
        var raw = await source.IssueAsync(user, null, null);
        var barrier = new BeforeReplacement(); await using var db = server.Context(tenant, null, barrier);
        var rotating = new RefreshTokenService(db, Options.Create(new SecurityOptions()), tenant).RotateAsync(raw, null, null);
        await using var writer = server.Context(tenant); Task<int>? write = null;
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            write = writer.Sys_Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Password, "after-rotation"));
            await Task.Delay(150); Assert.False(write.IsCompleted);
        }
        finally { barrier.Release.TrySetResult(); }
        var (next, _) = await rotating; await write!;
        Assert.Equal("E-SEC-007", (await Assert.ThrowsAsync<InvalidOperationException>(() => source.RotateAsync(next, null, null))).Message);
        Assert.All(await issuer.Sys_RefreshTokens.IgnoreQueryFilters().AsNoTracking().Where(x => x.UserId == user.Id).ToListAsync(),
            x => Assert.Equal(AuthSessionVersion.For(user), x.AuthenticationVersion));
    }

    [CurrentPermissionPostgresFact]
    public async Task Commit_rejection_rolls_back_source_and_does_not_return_replacement()
    {
        var user = await Seed(); var tenant = new TenantContext { CurrentTenantId = user.TenantId };
        await using var db = server.Context(tenant); var service = new RefreshTokenService(db, Options.Create(new SecurityOptions()), tenant);
        var raw = await service.IssueAsync(user, null, null); var suffix = user.Id.ToString("N");
        await db.Database.ExecuteSqlRawAsync($"""
            CREATE FUNCTION public.auth_fail_{suffix}() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'fixture deferred refresh rejection'; END $$;
            CREATE CONSTRAINT TRIGGER auth_commit_{suffix} AFTER INSERT ON public."Sys_RefreshTokens"
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (NEW."UserId"='{user.Id:D}'::uuid)
            EXECUTE FUNCTION public.auth_fail_{suffix}();
            """);
        await Assert.ThrowsAsync<RefreshCompletionUnknownException>(() => service.RotateForCompletionAsync(raw, null, null, RefreshTokenClientContext.Web));
        await using var read = server.Context(tenant);
        var original = Assert.Single(await read.Sys_RefreshTokens.IgnoreQueryFilters().Where(x => x.UserId == user.Id).ToListAsync());
        Assert.Null(original.RevokedAt); Assert.Null(original.ReplacedByTokenHash); Assert.Null(db.Database.CurrentTransaction);
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [CurrentPermissionPostgresFact]
    public async Task Completion_rechecks_current_state_after_rotation_and_cannot_publish_a_stale_proof()
    {
        var user = await Seed(); var tenant = new TenantContext { CurrentTenantId = user.TenantId };
        await using var db = server.Context(tenant); var service = new RefreshTokenService(db, Options.Create(new SecurityOptions()), tenant);
        var raw = await service.IssueAsync(user, null, null); var (next, _) = await service.RotateForCompletionAsync(raw, null, null, RefreshTokenClientContext.Web);
        await db.Sys_Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.AuthenticationEpoch, Guid.NewGuid()));
        Assert.Equal("E-SEC-007", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteRotationAsync(next))).Message);
        await service.RejectCompletionAsync(next);
        Assert.False(await db.Sys_RefreshTokens.IgnoreQueryFilters().AnyAsync(x => x.UserId == user.Id && x.RevokedAt == null));
    }

    private sealed class FailQuarantine : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => command.CommandText.StartsWith("UPDATE") && command.CommandText.Contains("\"CompletionRejectedAtUtc\" =")
                ? throw new IOException("fixture quarantine unavailable") : ValueTask.FromResult(result);
    }
    private sealed class BeforeReplacement : SaveChangesInterceptor
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken); return result;
        }
    }
}
