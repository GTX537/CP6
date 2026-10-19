using System.Data.Common;
using System.Data;
using System.Security.Claims;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Platform;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CP6.Tests.Platform;

[Collection("TenantManagementPostgres")]
public sealed class ImpersonationPostgreSqlTests(TenantManagementPostgresFixture server)
{
    [CurrentPermissionPostgresFact]
    public async Task Http_start_actor_revoked_after_filter_has_no_switch_or_cookie()
    {
        var (actor, target) = await Seed(); var barrier = new BeforeStart();
        await using var host = await TenantManagementHttpHost.Start(server, interceptor: barrier, includeImpersonation: true);
        using var request = StartRequest(host.Token(actor.Id, actor.TenantId, authenticationVersion: AuthSessionVersion.For(actor), jti: actor.Id.ToString()), target);
        var pending = host.Client.SendAsync(request);
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using var revoke = Context(actor.TenantId);
            await revoke.Sys_Users.Where(x => x.Id == actor.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsPlatformAdmin, false));
        }
        finally { barrier.Release.TrySetResult(); }
        using var response = await pending;
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); Assert.False(response.Headers.Contains("Set-Cookie"));
        await AssertNoAudit(actor.Id, SecurityEventType.ImpersonationStarted);
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Http_required_audit_rejection_never_outputs_a_switch_cookie(bool end)
    {
        var (actor, target) = await Seed();
        await using var host = await TenantManagementHttpHost.Start(server, includeImpersonation: true);
        var token = host.Token(actor.Id, actor.TenantId, authenticationVersion: AuthSessionVersion.For(actor), jti: actor.Id.ToString());
        if (end)
        {
            using var start = StartRequest(token, target); using var success = await host.Client.SendAsync(start);
            Assert.Equal(HttpStatusCode.OK, success.StatusCode); token = AccessToken(success);
        }
        var type = end ? SecurityEventType.ImpersonationEnded : SecurityEventType.ImpersonationStarted;
        await using (var ddl = Context(actor.TenantId))
            await ddl.Database.ExecuteSqlRawAsync($"ALTER TABLE public.\"Sys_SecurityLogs\" ADD CONSTRAINT imp_{actor.Id:N} CHECK (\"UserId\" <> '{actor.Id:D}'::uuid OR \"EventType\" <> {(int)type})");
        using var request = end ? EndRequest(token) : StartRequest(token, target);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie")); await AssertNoAudit(actor.Id, type);
        using var scope = host.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<ITokenBlacklistService>()
            .IsBlacklistedAsync(new JwtSecurityTokenHandler().ReadJwtToken(token).Id));
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Http_switch_cannot_reissue_after_original_actor_credentials_changed(bool end)
    {
        var (actor, target) = await Seed();
        await using var host = await TenantManagementHttpHost.Start(server, includeImpersonation: true);
        var token = host.Token(actor.Id, actor.TenantId, authenticationVersion: AuthSessionVersion.For(actor), jti: actor.Id.ToString());
        if (end)
        {
            using var start = StartRequest(token, target); using var success = await host.Client.SendAsync(start);
            Assert.Equal(HttpStatusCode.OK, success.StatusCode); token = AccessToken(success);
        }
        await using (var update = Context(actor.TenantId))
            await update.Sys_Users.Where(x => x.Id == actor.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.Password, "changed-after-authentication"));
        using var request = end ? EndRequest(token) : StartRequest(token, target);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); Assert.False(response.Headers.Contains("Set-Cookie"));
        await AssertNoAudit(actor.Id, end ? SecurityEventType.ImpersonationEnded : SecurityEventType.ImpersonationStarted);
    }

    [CurrentPermissionPostgresFact]
    public async Task Http_round_trip_preserves_identity_cookie_audit_and_effective_role_boundaries()
    {
        var (actor, target) = await Seed();
        await using (var seed = Context(target.TenantId))
        {
            seed.Sys_Roles.AddRange(new Sys_Role { RoleId = 1, RoleName = "active", Enable = true },
                new Sys_Role { RoleId = 2, RoleName = "disabled", Enable = false }, new Sys_Role { RoleId = 3, RoleName = "foreign-membership", Enable = true });
            foreach (var i in new[] { 1, 2, 3 })
            {
                seed.Sys_Menus.Add(new Sys_Menu { MenuId = 71025 + i, MenuName = "menu" + i, MenuKey = "w25-imp-" + i, Enable = true });
                seed.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = i, MenuId = 71025 + i });
            }
            seed.Sys_UserRoles.AddRange(new Sys_UserRole { UserId = target.Id, RoleId = 2 },
                new Sys_UserRole { TenantId = actor.TenantId, UserId = target.Id, RoleId = 3 });
            await seed.SaveChangesAsync();
        }
        await using var host = await TenantManagementHttpHost.Start(server, includeImpersonation: true);
        var source = host.Token(actor.Id, actor.TenantId, authenticationVersion: AuthSessionVersion.For(actor), jti: actor.Id.ToString());
        var reason = new string('理', 512);
        using var start = StartRequest(source, target, reason); using var response = await host.Client.SendAsync(start);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(response.Headers.GetValues("Set-Cookie"));
        var result = (await response.Content.ReadFromJsonAsync<ImpersonationStartResult>())!;
        Assert.Equal(71026, Assert.Single(result.Menus).Id);
        var impToken = AccessToken(response); var imp = new JwtSecurityTokenHandler().ReadJwtToken(impToken);
        Assert.Equal(target.TenantId.ToString(), imp.Claims.Single(x => x.Type == "tenant_id").Value);
        Assert.Equal(actor.TenantId.ToString(), imp.Claims.Single(x => x.Type == "impersonator_tenant_id").Value);
        Assert.Equal(AuthSessionVersion.For(actor), imp.Claims.Single(x => x.Type == "impersonator_auth_version").Value);
        Assert.Equal(AuthSessionVersion.For(target), imp.Claims.Single(x => x.Type == "cp6_auth_version").Value);
        Assert.Equal("false", imp.Claims.Single(x => x.Type == "must_change_password").Value);
        Assert.DoesNotContain(imp.Claims, x => x.Type == "is_platform_admin");
        using (var nested = StartRequest(impToken, target))
        using (var rejected = await host.Client.SendAsync(nested))
        { Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode); Assert.Contains("E-SEC-034", await rejected.Content.ReadAsStringAsync()); }
        using var end = EndRequest(impToken); using var restored = await host.Client.SendAsync(end);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode); Assert.Single(restored.Headers.GetValues("Set-Cookie"));
        var actual = new JwtSecurityTokenHandler().ReadJwtToken(AccessToken(restored));
        Assert.Equal(actor.Id.ToString(), actual.Claims.Single(x => x.Type == ClaimTypes.NameIdentifier).Value);
        Assert.Equal(actor.TenantId.ToString(), actual.Claims.Single(x => x.Type == "tenant_id").Value);
        Assert.Equal(AuthSessionVersion.For(actor), actual.Claims.Single(x => x.Type == "cp6_auth_version").Value);
        Assert.DoesNotContain(actual.Claims, x => x.Type.StartsWith("impersonator_", StringComparison.Ordinal));
        foreach (var replay in new[] { StartRequest(source, target), EndRequest(impToken) })
        { using (replay) using (var rejected = await host.Client.SendAsync(replay)) Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode); }
        await using var db = Context(TenantContext.DefaultTenant);
        var logs = await db.Sys_SecurityLogs.Where(x => x.UserId == actor.Id && (x.EventType == (int)SecurityEventType.ImpersonationStarted || x.EventType == (int)SecurityEventType.ImpersonationEnded)).ToArrayAsync();
        Assert.Equal(2, logs.Length);
        foreach (var log in logs)
        {
            Assert.Equal(actor.UserName, log.UserName);
            using var document = JsonDocument.Parse(log.ContextJson!); var context = document.RootElement;
            Assert.Equal(actor.Id, context.GetProperty("actualActorId").GetGuid());
            Assert.Equal(target.Id, context.GetProperty("effectiveActorId").GetGuid());
            Assert.Equal(imp.Id, context.GetProperty("sessionRef").GetString());
            Assert.Equal(imp.ValidTo, context.GetProperty("expiresAtUtc").GetDateTimeOffset().UtcDateTime);
            if (log.EventType == (int)SecurityEventType.ImpersonationStarted)
            { Assert.Equal(reason, context.GetProperty("reason").GetString()); Assert.Equal(reason[..256], log.Reason); }
        }
    }

    [TenantManagementPostgresTheory]
    [InlineData("actor_credentials")]
    [InlineData("target_tenant")]
    [InlineData("target_binding")]
    public async Task Switch_holds_current_actor_and_target_until_commit(string change)
    {
        var (actor, target) = await Seed(); var barrier = new BeforeAudit();
        await using var host = await TenantManagementHttpHost.Start(server, interceptor: barrier, includeImpersonation: true);
        using var request = StartRequest(host.Token(actor.Id, actor.TenantId, authenticationVersion: AuthSessionVersion.For(actor), jti: actor.Id.ToString()), target);
        var switching = host.Client.SendAsync(request); await using var writer = Context(actor.TenantId);
        Task<int>? mutation = null;
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            mutation = change switch
            {
                "actor_credentials" => writer.Sys_Users.Where(x => x.Id == actor.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.Password, "changed-later")),
                "target_tenant" => writer.Sys_Tenants.Where(x => x.Id == target.TenantId).ExecuteUpdateAsync(x => x.SetProperty(t => t.Enable, false)),
                _ => writer.Sys_Users.IgnoreQueryFilters().Where(x => x.Id == target.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.TenantId, actor.TenantId))
            };
            await AssertLockWait(mutation, writer);
        }
        finally { barrier.Release.TrySetResult(); }
        using var response = await switching; Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, await mutation!.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [CurrentPermissionPostgresFact]
    public async Task Cookie_preparation_failure_does_not_consume_old_access_or_create_audit()
    {
        var (actor, target) = await Seed(); var tenant = new TenantContext { CurrentTenantId = actor.TenantId };
        await using var db = server.Context(tenant); var blacklist = new ControlledBlacklist(); var response = new DefaultHttpContext().Response;
        var service = Service(db, tenant, blacklist, new BrokenCookieWriter());
        var error = await Record.ExceptionAsync(() => service.StartAsync(target.TenantId, target.Id, null, Principal(actor), response));
        Assert.Equal("fixture-cookie-failure", error?.Message); Assert.Empty(blacklist.Retired); Assert.Empty(response.Headers);
        await AssertNoAudit(actor.Id, SecurityEventType.ImpersonationStarted);
    }

    [CurrentPermissionPostgresFact]
    public async Task Blacklist_unknown_rolls_back_audit_and_never_replays_or_returns_new_cookie()
    {
        var (actor, target) = await Seed(); var tenant = new TenantContext { CurrentTenantId = actor.TenantId };
        await using var db = server.Context(tenant); var blacklist = new ControlledBlacklist { FailAfterRetirement = true };
        var response = new DefaultHttpContext().Response; var principal = Principal(actor);
        var service = Service(db, tenant, blacklist);
        var error = await Record.ExceptionAsync(() => service.StartAsync(target.TenantId, target.Id, null, principal, response));
        Assert.Equal("IMPERSONATION_SWITCH_OUTCOME_UNKNOWN", error?.Message); Assert.Empty(response.Headers); Assert.Single(blacklist.Retired);
        await AssertNoAudit(actor.Id, SecurityEventType.ImpersonationStarted);
        var replay = await Record.ExceptionAsync(() => service.StartAsync(target.TenantId, target.Id, null, principal, response));
        Assert.Equal("E-SEC-031", replay?.Message); Assert.Single(blacklist.Retired);
    }

    [CurrentPermissionPostgresFact]
    public async Task Database_commit_rejection_after_retirement_never_outputs_new_cookie()
    {
        var (actor, target) = await Seed(); var tenant = new TenantContext { CurrentTenantId = actor.TenantId };
        await using var db = server.Context(tenant); var suffix = actor.Id.ToString("N");
        await db.Database.ExecuteSqlRawAsync($"""
            CREATE FUNCTION public.imp_fail_{suffix}() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'fixture deferred audit rejection'; END $$;
            CREATE CONSTRAINT TRIGGER imp_commit_{suffix} AFTER INSERT ON public."Sys_SecurityLogs"
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
            WHEN (NEW."UserId"='{actor.Id:D}'::uuid) EXECUTE FUNCTION public.imp_fail_{suffix}();
            """);
        var blacklist = new ControlledBlacklist(); var response = new DefaultHttpContext().Response;
        var error = await Record.ExceptionAsync(() => Service(db, tenant, blacklist).StartAsync(target.TenantId, target.Id, null, Principal(actor), response));
        Assert.Equal("IMPERSONATION_SWITCH_OUTCOME_UNKNOWN", error?.Message); Assert.Empty(response.Headers); Assert.Single(blacklist.Retired);
        await AssertNoAudit(actor.Id, SecurityEventType.ImpersonationStarted);
    }

    [CurrentPermissionPostgresFact]
    public async Task Cancellation_during_real_audit_lock_wait_has_no_cookie_or_retirement()
    {
        var (actor, target) = await Seed(); var tenant = new TenantContext { CurrentTenantId = actor.TenantId };
        await using var locker = server.Context(tenant); await using var db = server.Context(tenant);
        await using var transaction = await locker.Database.BeginTransactionAsync();
        await locker.Database.ExecuteSqlRawAsync("LOCK TABLE public.\"Sys_SecurityLogs\" IN ACCESS EXCLUSIVE MODE");
        using var cancel = new CancellationTokenSource(); var response = new DefaultHttpContext { RequestAborted = cancel.Token }.Response;
        var blacklist = new ControlledBlacklist();
        var pending = Record.ExceptionAsync(() => Service(db, tenant, blacklist).StartAsync(target.TenantId, target.Id, null, Principal(actor), response));
        try
        {
            await AssertLockWait(pending, db); cancel.Cancel();
            Assert.IsAssignableFrom<OperationCanceledException>(await pending.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally { await transaction.RollbackAsync(); }
        Assert.Empty(response.Headers); Assert.Empty(blacklist.Retired); Assert.Null(db.Database.CurrentTransaction);
        await AssertNoAudit(actor.Id, SecurityEventType.ImpersonationStarted);
    }

    [CurrentPermissionPostgresFact]
    public async Task Caller_transaction_cannot_emit_an_uncommitted_identity_cookie()
    {
        var (actor, target) = await Seed(); var tenant = new TenantContext { CurrentTenantId = actor.TenantId };
        await using var db = server.Context(tenant); await using var transaction = await db.Database.BeginTransactionAsync();
        var blacklist = new ControlledBlacklist(); var response = new DefaultHttpContext().Response;
        var error = await Record.ExceptionAsync(() => Service(db, tenant, blacklist).StartAsync(target.TenantId, target.Id, null, Principal(actor), response));
        Assert.Equal("IMPERSONATION_REQUIRES_OWNED_TRANSACTION", error?.Message); Assert.Empty(response.Headers); Assert.Empty(blacklist.Retired);
        await transaction.RollbackAsync(); await AssertNoAudit(actor.Id, SecurityEventType.ImpersonationStarted);
    }

    [CurrentPermissionPostgresFact]
    public async Task Source_expiring_while_waiting_for_the_root_cannot_mint_a_fresh_identity()
    {
        var (actor, target) = await Seed(); var tenant = new TenantContext { CurrentTenantId = actor.TenantId };
        await using var locker = server.Context(tenant); await using var db = server.Context(tenant);
        await using var transaction = await locker.Database.BeginTransactionAsync();
        await locker.Database.SqlQueryRaw<long>("SELECT cp6_platform_admin_guard_v1.acquire() AS \"Value\"").SingleAsync();
        var principal = Principal(actor); var identity = (ClaimsIdentity)principal.Identity!;
        identity.RemoveClaim(identity.FindFirst("exp")!); var expires = DateTimeOffset.UtcNow.AddSeconds(2).ToUnixTimeSeconds();
        identity.AddClaim(new Claim("exp", expires.ToString()));
        var blacklist = new ControlledBlacklist(); var response = new DefaultHttpContext().Response;
        var pending = Record.ExceptionAsync(() => Service(db, tenant, blacklist).StartAsync(target.TenantId, target.Id, null, principal, response));
        try
        {
            await AssertLockWait(pending, db);
            while (DateTimeOffset.UtcNow.ToUnixTimeSeconds() < expires) await Task.Delay(20);
        }
        finally { await transaction.RollbackAsync(); }
        Assert.Equal("E-SEC-031", (await pending.WaitAsync(TimeSpan.FromSeconds(10)))?.Message);
        Assert.Empty(response.Headers); Assert.Empty(blacklist.Retired); await AssertNoAudit(actor.Id, SecurityEventType.ImpersonationStarted);
    }

    [CurrentPermissionPostgresFact]
    public async Task Forward_audit_context_migration_preserves_populated_history_and_does_not_backfill_it()
    {
        await using var owner = Context(TenantContext.DefaultTenant); var name = "w25_audit_" + Guid.NewGuid().ToString("N");
        await owner.Database.ExecuteSqlRawAsync($"CREATE DATABASE {name}");
        await using var db = Context(TenantContext.DefaultTenant);
        db.Database.SetConnectionString(new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()) { Database = name }.ConnectionString);
        await db.GetService<IMigrator>().MigrateAsync("20261019073000_PlatformAdminGuardV1");
        var id = Guid.NewGuid(); var tenant = TenantContext.DefaultTenant; var reason = new string('旧', 256);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public."Sys_SecurityLogs" ("Id","TenantId","CreateDate","CreatedAt","EventType","Reason")
            VALUES ({id},{tenant},LOCALTIMESTAMP,LOCALTIMESTAMP,34,{reason})
            """);
        await db.Database.MigrateAsync(); await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var old = await db.Sys_SecurityLogs.SingleAsync(x => x.Id == id); Assert.Equal(reason, old.Reason); Assert.Null(old.ContextJson);
        old.ContextJson = "{\"version\":1,\"fixture\":true}"; await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        using var context = JsonDocument.Parse((await db.Sys_SecurityLogs.SingleAsync(x => x.Id == id)).ContextJson!);
        Assert.True(context.RootElement.GetProperty("fixture").GetBoolean());
    }

    private static ImpersonationService Service(CP6Context db, ITenantContext tenant, ITokenBlacklistService blacklist, IAuthCookieWriter? cookies = null)
    {
        var options = Options.Create(new SecurityOptions());
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["JWT:Secret"] = "fixture-only-impersonation-secret-at-least-32", ["JWT:Issuer"] = "fixture", ["JWT:Audience"] = "fixture" }).Build();
        return new(db, blacklist, new SecurityAuditService(db), tenant, options, cookies ?? new AuthCookieWriter(options), config);
    }
    private static ClaimsPrincipal Principal(Sys_User actor)
        => new(new ClaimsIdentity([new Claim("sub", actor.Id.ToString()), new Claim("tenant_id", actor.TenantId.ToString()),
            new Claim("is_platform_admin", "true"), new Claim("cp6_auth_version", AuthSessionVersion.For(actor)),
            new Claim("jti", actor.Id.ToString()), new Claim("exp", DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds().ToString())], "fixture"));
    private sealed class ControlledBlacklist : ITokenBlacklistService
    {
        internal HashSet<string> Retired { get; } = [];
        internal bool FailAfterRetirement { get; init; }
        public Task<bool> IsBlacklistedAsync(string jti) => Task.FromResult(Retired.Contains(jti));
        public Task BlacklistAsync(string jti, TimeSpan ttl)
        { Retired.Add(jti); return FailAfterRetirement ? Task.FromException(new TimeoutException("fixture-cache-unknown")) : Task.CompletedTask; }
    }
    private sealed class BrokenCookieWriter : IAuthCookieWriter
    {
        public void WriteAccessCookieOnly(HttpResponse response, string token) => throw new InvalidOperationException("fixture-cookie-failure");
        public void WriteAuthCookies(HttpResponse response, string access, string refresh, string csrf) => throw new NotSupportedException();
        public void ClearAuthCookies(HttpResponse response) => throw new NotSupportedException();
        public void WritePendingCookies(HttpResponse response, string pending, string csrf) => throw new NotSupportedException();
        public void ClearPendingCookies(HttpResponse response) => throw new NotSupportedException();
    }
    private async Task AssertLockWait(Task pending, CP6Context blocked)
    {
        await using var observer = Context(TenantContext.DefaultTenant); var until = DateTimeOffset.UtcNow.AddSeconds(8);
        while (!pending.IsCompleted && DateTimeOffset.UtcNow < until)
        {
            var connection = (NpgsqlConnection)blocked.Database.GetDbConnection();
            if (connection.State == ConnectionState.Open)
            {
                var pid = connection.ProcessID;
                if (await observer.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM pg_stat_activity WHERE pid={pid} AND wait_event_type='Lock') AS \"Value\"").SingleAsync()) return;
            }
            await Task.Delay(20);
        }
        Assert.Fail("The mutation must be blocked in PostgreSQL until the switch transaction completes.");
    }
    private sealed class BeforeAudit : DbCommandInterceptor
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"Sys_SecurityLogs\"", StringComparison.Ordinal))
            { Entered.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); }
            return result;
        }
    }

    private CP6Context Context(Guid tenant) => server.Context(new TenantContext { CurrentTenantId = tenant });

    [TenantManagementPostgresTheory]
    [InlineData("disabled", "E-SEC-035")]
    [InlineData("wrong_tenant", "E-SEC-035")]
    [InlineData("tenant_disabled", "E-SEC-032")]
    [InlineData("expired", "E-SEC-032")]
    public async Task Http_target_current_state_rejects_invalid_switches(string change, string code)
    {
        var (actor, target) = await Seed();
        await using (var update = Context(target.TenantId))
        {
            if (change == "disabled") await update.Sys_Users.Where(x => x.Id == target.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.Enable, false));
            if (change == "wrong_tenant") await update.Sys_Users.Where(x => x.Id == target.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.TenantId, actor.TenantId));
            if (change == "tenant_disabled") await update.Sys_Tenants.Where(x => x.Id == target.TenantId).ExecuteUpdateAsync(x => x.SetProperty(t => t.Enable, false));
            if (change == "expired") await update.Sys_Tenants.Where(x => x.Id == target.TenantId).ExecuteUpdateAsync(x => x.SetProperty(t => t.ExpireDate, DateTime.Now.AddDays(-1)));
        }
        await using var host = await TenantManagementHttpHost.Start(server, includeImpersonation: true);
        using var request = StartRequest(host.Token(actor.Id, actor.TenantId, authenticationVersion: AuthSessionVersion.For(actor), jti: actor.Id.ToString()), target);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Contains(code, await response.Content.ReadAsStringAsync());
        Assert.False(response.Headers.Contains("Set-Cookie")); await AssertNoAudit(actor.Id, SecurityEventType.ImpersonationStarted);
    }

    [TenantManagementPostgresTheory]
    [InlineData("revoke")]
    [InlineData("disable")]
    [InlineData("move")]
    public async Task Http_end_cannot_restore_an_invalid_original_actor(string change)
    {
        var (actor, target) = await Seed();
        await using var host = await TenantManagementHttpHost.Start(server, includeImpersonation: true);
        using var request = StartRequest(host.Token(actor.Id, actor.TenantId, authenticationVersion: AuthSessionVersion.For(actor), jti: actor.Id.ToString()), target);
        using var response = await host.Client.SendAsync(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using (var update = Context(actor.TenantId))
        {
            var row = update.Sys_Users.Where(x => x.Id == actor.Id);
            if (change == "revoke") await row.ExecuteUpdateAsync(x => x.SetProperty(u => u.IsPlatformAdmin, false));
            else if (change == "disable") await row.ExecuteUpdateAsync(x => x.SetProperty(u => u.Enable, false));
            else await row.ExecuteUpdateAsync(x => x.SetProperty(u => u.TenantId, target.TenantId));
        }
        using var end = EndRequest(AccessToken(response)); using var rejected = await host.Client.SendAsync(end);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode); Assert.False(rejected.Headers.Contains("Set-Cookie"));
        await AssertNoAudit(actor.Id, SecurityEventType.ImpersonationEnded);
    }

    [CurrentPermissionPostgresFact]
    public async Task Http_missing_original_authentication_version_is_not_backfilled()
    {
        var (actor, target) = await Seed();
        await using var host = await TenantManagementHttpHost.Start(server, includeImpersonation: true);
        using var request = StartRequest(host.Token(actor.Id, actor.TenantId), target);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); Assert.False(response.Headers.Contains("Set-Cookie"));
        await AssertNoAudit(actor.Id, SecurityEventType.ImpersonationStarted);
    }

    [CurrentPermissionPostgresFact]
    public async Task Http_legacy_null_target_selects_a_current_enabled_primary_administrator()
    {
        var (actor, target) = await Seed();
        await using var host = await TenantManagementHttpHost.Start(server, includeImpersonation: true);
        using var request = StartRequest(host.Token(actor.Id, actor.TenantId, authenticationVersion: AuthSessionVersion.For(actor), jti: actor.Id.ToString()), target);
        request.Content = JsonContent.Create(new { TenantId = target.TenantId, UserId = (Guid?)null });
        using var response = await host.Client.SendAsync(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(target.Id, (await response.Content.ReadFromJsonAsync<ImpersonationStartResult>())!.UserId);
    }

    [CurrentPermissionPostgresFact]
    public async Task Concurrent_start_requests_with_one_source_access_commit_only_one_switch()
    {
        var (actor, target) = await Seed(); var barrier = new BeforeStart();
        await using var host = await TenantManagementHttpHost.Start(server, interceptor: barrier, includeImpersonation: true);
        var token = host.Token(actor.Id, actor.TenantId, authenticationVersion: AuthSessionVersion.For(actor), jti: actor.Id.ToString());
        using var first = StartRequest(token, target); var pending = host.Client.SendAsync(first);
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var second = StartRequest(token, target); using var response = await host.Client.SendAsync(second);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally { barrier.Release.TrySetResult(); }
        using var rejected = await pending; Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode); Assert.False(rejected.Headers.Contains("Set-Cookie"));
        await using var read = Context(TenantContext.DefaultTenant);
        Assert.Equal(1, await read.Sys_SecurityLogs.CountAsync(x => x.UserId == actor.Id && x.EventType == (int)SecurityEventType.ImpersonationStarted));
    }
    private async Task<(Sys_User Actor, Sys_User Target)> Seed()
    {
        var actualTenant = Guid.NewGuid(); var effectiveTenant = Guid.NewGuid(); await using var db = Context(actualTenant);
        db.Sys_Tenants.AddRange(new Sys_Tenant { Id = actualTenant, TenantCode = "a_" + actualTenant.ToString("N"), TenantName = "actor" },
            new Sys_Tenant { Id = effectiveTenant, TenantCode = "e_" + effectiveTenant.ToString("N"), TenantName = "effective" });
        var actor = new Sys_User { Id = Guid.NewGuid(), TenantId = actualTenant, UserName = "actor", Password = new BCryptPasswordHasher(4).Hash(AuthenticationFixtureSessionV1.Password), IsPlatformAdmin = true, Enable = true };
        var target = new Sys_User { Id = Guid.NewGuid(), TenantId = effectiveTenant, UserName = "effective", Password = "fixture", RoleId = 1, Enable = true, MustChangePassword = true };
        db.Sys_Users.AddRange(actor, target, new Sys_User { Id = Guid.NewGuid(), UserName = "recovery", Password = "fixture", IsPlatformAdmin = true, Enable = true });
        await db.SaveChangesAsync();
        await AuthenticationFixtureSessionV1.IssueAsync(db, new RefreshTokenService(db, Options.Create(new SecurityOptions()),
            new TenantContext { CurrentTenantId = actualTenant }), actor, jti: actor.Id.ToString());
        return (actor, target);
    }
    private static HttpRequestMessage StartRequest(string token, Sys_User target, string reason = "support investigation")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/platform/impersonation/start")
        { Content = JsonContent.Create(new { TenantId = target.TenantId, UserId = target.Id, Reason = reason }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); return request;
    }
    private static HttpRequestMessage EndRequest(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/platform/impersonation/end");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); return request;
    }
    private static string AccessToken(HttpResponseMessage response)
        => response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith(AuthCookieWriter.AccessCookie + "=", StringComparison.Ordinal))
            .Split(';')[0][(AuthCookieWriter.AccessCookie.Length + 1)..];
    private async Task AssertNoAudit(Guid actor, SecurityEventType type)
    {
        await using var db = Context(TenantContext.DefaultTenant);
        Assert.False(await db.Sys_SecurityLogs.AnyAsync(x => x.UserId == actor && x.EventType == (int)type));
    }
    private sealed class BeforeStart : DbCommandInterceptor
    {
        private int _entered;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if ((command.CommandText.Contains("cp6_platform_admin_guard_v1.acquire()") || command.CommandText.Contains("FROM \"Sys_Tenants\""))
                && Interlocked.Exchange(ref _entered, 1) == 0)
            { Entered.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); }
            return result;
        }
    }
}
