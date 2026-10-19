using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace CP6.Tests.Platform;

[Collection("TenantManagementPostgres")]
public sealed class PlatformAdminCurrentActorPostgreSqlTests(TenantManagementPostgresFixture server)
{
    [TenantManagementPostgresTheory]
    [InlineData("revoke", true)]
    [InlineData("disable", true)]
    [InlineData("delete", true)]
    [InlineData("revoke", false)]
    [InlineData("disable", false)]
    [InlineData("delete", false)]
    public async Task Http_actor_removed_after_filter_cannot_change_platform_authority(string operation, bool grant)
    {
        var (tenant, actor, target) = await Seed();
        if (!grant)
        {
            await using var setup = Context(tenant);
            await setup.Sys_Users.Where(x => x.Id == target.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsPlatformAdmin, true));
            target.IsPlatformAdmin = true;
        }
        var barrier = new BeforeGuard();
        await using var host = await TenantManagementHttpHost.Start(server, includePlatformAdmins: true, interceptor: barrier);
        using var request = Request(host.Token(actor.Id, tenant), target.Id, grant ? "grant" : "revoke");
        var responseTask = host.Client.SendAsync(request);
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using var revoke = Context(tenant);
            var row = revoke.Sys_Users.Where(x => x.Id == actor.Id);
            if (operation == "revoke") await row.ExecuteUpdateAsync(x => x.SetProperty(u => u.IsPlatformAdmin, false));
            else if (operation == "disable") await row.ExecuteUpdateAsync(x => x.SetProperty(u => u.Enable, false));
            else await row.ExecuteDeleteAsync();
        }
        finally { barrier.Release.TrySetResult(); }
        using var response = await responseTask;
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("E-SEC-031", await response.Content.ReadAsStringAsync());
        await AssertNoGrant(tenant, target);
    }

    [CurrentPermissionPostgresFact]
    public async Task Http_actor_cannot_borrow_another_tenant_binding()
    {
        var (tenant, actor, target) = await Seed();
        await using var host = await TenantManagementHttpHost.Start(server, includePlatformAdmins: true);
        using var request = Request(host.Token(actor.Id, Guid.NewGuid()), target.Id);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertNoGrant(tenant, target);
    }

    [CurrentPermissionPostgresFact]
    public async Task Service_without_authenticated_actor_fails_closed()
    {
        var (tenant, _, target) = await Seed();
        await using var db = Context(tenant);
        var service = new PlatformAdminService(db, new SecurityAuditService(db), new TenantContext { CurrentTenantId = tenant });
        var error = await Record.ExceptionAsync(() => service.GrantAsync(target.Id));
        Assert.NotNull(error);
        Assert.Equal("E-SEC-031", error.Message);
        await AssertNoGrant(tenant, target);
    }

    [TenantManagementPostgresTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Authorized_effect_first_orders_revocation_after_commit_or_rollback(bool commit)
    {
        var (tenant, actor, target) = await Seed();
        await using var db = Context(tenant); await using var revoke = Context(tenant);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await Service(db, actor.Id).GrantAsync(target.Id);
        var pending = revoke.Sys_Users.Where(x => x.Id == actor.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsPlatformAdmin, false));
        try
        {
            await AssertLockWait(pending, revoke);
            if (commit) await transaction.CommitAsync(); else await transaction.RollbackAsync();
            Assert.Equal(1, await pending.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally { if (!pending.IsCompleted) await transaction.RollbackAsync(); }
        await using var read = Context(tenant);
        Assert.Equal(commit, await read.Sys_Users.Where(x => x.Id == target.Id).Select(x => x.IsPlatformAdmin).SingleAsync());
        Assert.False(await read.Sys_Users.Where(x => x.Id == actor.Id).Select(x => x.IsPlatformAdmin).SingleAsync());
        var logs = await read.Sys_SecurityLogs.IgnoreQueryFilters().Where(x => x.Reason == "target=" + target.UserName).ToArrayAsync();
        if (commit) { var audit = Assert.Single(logs); Assert.Equal(actor.Id, audit.UserId); Assert.Equal(actor.UserName, audit.UserName); }
        else Assert.Empty(logs);
    }

    [TenantManagementPostgresTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Revocation_first_waits_then_rechecks_committed_or_rolled_back_actor(bool commit)
    {
        var (tenant, actor, target) = await Seed();
        await using var revoke = Context(tenant); await using var db = Context(tenant);
        await using var transaction = await revoke.Database.BeginTransactionAsync();
        await revoke.Sys_Users.Where(x => x.Id == actor.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsPlatformAdmin, false));
        var pending = Record.ExceptionAsync(() => Service(db, actor.Id).GrantAsync(target.Id));
        try
        {
            await AssertLockWait(pending, db);
            if (commit) await transaction.CommitAsync(); else await transaction.RollbackAsync();
            var error = await pending.WaitAsync(TimeSpan.FromSeconds(10));
            if (commit) { Assert.NotNull(error); Assert.Equal("E-SEC-031", error.Message); }
            else Assert.Null(error);
        }
        finally { if (!pending.IsCompleted) await transaction.RollbackAsync(); }
        await using var read = Context(tenant);
        Assert.Equal(!commit, await read.Sys_Users.Where(x => x.Id == target.Id).Select(x => x.IsPlatformAdmin).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Denial_ignores_stale_tracking_and_preserves_callers_prior_work()
    {
        var (tenant, actor, target) = await Seed(); await using var db = Context(tenant);
        var tracked = await db.Sys_Users.SingleAsync(x => x.Id == actor.Id);
        await using (var revoke = Context(tenant))
            await revoke.Sys_Users.Where(x => x.Id == actor.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsPlatformAdmin, false));
        await using var transaction = await db.Database.BeginTransactionAsync();
        var prior = new Sys_SecurityLog { EventType = 0, Reason = Guid.NewGuid().ToString(), CreatedAt = DateTime.Now };
        db.Sys_SecurityLogs.Add(prior); await db.SaveChangesAsync();
        var error = await Record.ExceptionAsync(() => Service(db, actor.Id).GrantAsync(target.Id));
        Assert.NotNull(error); Assert.Equal("E-SEC-031", error.Message);
        Assert.True(tracked.IsPlatformAdmin); Assert.False(db.ChangeTracker.HasChanges());
        Assert.True(await db.Sys_SecurityLogs.AnyAsync(x => x.Id == prior.Id));
        await transaction.CommitAsync();
        await AssertNoGrant(tenant, target);
    }

    [TenantManagementPostgresTheory]
    [InlineData(IsolationLevel.RepeatableRead)]
    [InlineData(IsolationLevel.Serializable)]
    public async Task Stale_snapshot_cannot_restore_revoked_authority(IsolationLevel isolation)
    {
        var (tenant, actor, target) = await Seed(); await using var db = Context(tenant);
        await using var tx = await db.Database.BeginTransactionAsync(isolation);
        Assert.True(await db.Sys_Users.AnyAsync(x => x.Id == actor.Id && x.IsPlatformAdmin));
        await using (var revoke = Context(tenant))
            await revoke.Sys_Users.Where(x => x.Id == actor.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsPlatformAdmin, false));
        var error = await Record.ExceptionAsync(() => Service(db, actor.Id).GrantAsync(target.Id));
        Assert.Equal(PostgresErrorCodes.SerializationFailure, Assert.IsType<PostgresException>(error?.GetBaseException()).SqlState);
        await tx.RollbackAsync(); await AssertNoGrant(tenant, target);
    }

    [CurrentPermissionPostgresFact]
    public async Task Http_valid_actor_can_manage_foreign_target_and_audit_real_actor()
    {
        var (tenant, actor, _) = await Seed(); var (foreign, _, target) = await Seed();
        await using var host = await TenantManagementHttpHost.Start(server, includePlatformAdmins: true);
        foreach (var operation in new[] { "grant", "revoke" })
        {
            using var request = Request(host.Token(actor.Id, tenant), target.Id, operation);
            using var response = await host.Client.SendAsync(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        await using var read = Context(foreign);
        Assert.False(await read.Sys_Users.Where(x => x.Id == target.Id).Select(x => x.IsPlatformAdmin).SingleAsync());
        var logs = await read.Sys_SecurityLogs.IgnoreQueryFilters().Where(x => x.Reason == "target=" + target.UserName).ToArrayAsync();
        Assert.Equal(2, logs.Length);
        Assert.All(logs, x => { Assert.Equal(actor.Id, x.UserId); Assert.Equal(actor.UserName, x.UserName); Assert.Equal(TenantContext.DefaultTenant, x.TenantId); });
    }

    [CurrentPermissionPostgresFact]
    public async Task Actor_binding_row_stays_locked_until_effect_transaction_ends()
    {
        var (tenant, actor, target) = await Seed(); var (foreign, _, _) = await Seed();
        await using var db = Context(tenant); await using var move = Context(tenant);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await Service(db, actor.Id).GrantAsync(target.Id);
        // TenantId is not one of the administrator-root trigger's UPDATE columns.
        var pending = move.Sys_Users.Where(x => x.Id == actor.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.TenantId, foreign));
        try
        {
            await AssertLockWait(pending, move); await transaction.CommitAsync();
            Assert.Equal(1, await pending.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally { if (!pending.IsCompleted) await transaction.RollbackAsync(); }
        var error = await Record.ExceptionAsync(() => Service(db, actor.Id).RevokeAsync(target.Id));
        Assert.NotNull(error); Assert.Equal("E-SEC-031", error.Message);
        await using var read = Context(tenant);
        Assert.True(await read.Sys_Users.Where(x => x.Id == target.Id).Select(x => x.IsPlatformAdmin).SingleAsync());
        Assert.Single(await read.Sys_SecurityLogs.IgnoreQueryFilters().Where(x => x.Reason == "target=" + target.UserName).ToArrayAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Cancelled_actor_row_wait_rolls_back_owned_root_and_leaves_no_effect()
    {
        var (tenant, actor, target) = await Seed(); await using var locker = Context(tenant); await using var db = Context(tenant);
        await using var transaction = await locker.Database.BeginTransactionAsync();
        await locker.Sys_Users.Where(x => x.Id == actor.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.NickName, "concurrent-profile"));
        using var cancel = new CancellationTokenSource();
        var pending = Record.ExceptionAsync(() => Service(db, actor.Id, cancel.Token).GrantAsync(target.Id));
        try
        {
            await AssertLockWait(pending, db); cancel.Cancel();
            Assert.IsAssignableFrom<OperationCanceledException>(await pending.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Null(db.Database.CurrentTransaction); Assert.False(db.ChangeTracker.HasChanges());
        }
        finally { await transaction.RollbackAsync(); }
        await AssertNoGrant(tenant, target);
        // The cancelled attempt must not retain the global root lock.
        await Service(db, actor.Id).GrantAsync(target.Id);
    }

    private CP6Context Context(Guid tenant) => server.Context(new TenantContext { CurrentTenantId = tenant });
    private async Task<(Guid Tenant, Sys_User Actor, Sys_User Target)> Seed()
    {
        var tenant = Guid.NewGuid(); await using var db = Context(tenant);
        var actor = User(true); var spare = User(true); var target = User(false);
        db.Sys_Tenants.Add(new Sys_Tenant { Id = tenant, TenantCode = "w24_" + tenant.ToString("N"), TenantName = "fixture" });
        db.Sys_Users.AddRange(actor, spare, target); await db.SaveChangesAsync();
        return (tenant, actor, target);
    }
    private static Sys_User User(bool platform) => new() { Id = Guid.NewGuid(), UserName = "w24_" + Guid.NewGuid().ToString("N"), Password = "fixture", Enable = true, IsPlatformAdmin = platform };
    private static HttpRequestMessage Request(string token, Guid target, string operation = "grant")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"api/platform/admin/{target}/{operation}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); return request;
    }
    private async Task AssertNoGrant(Guid tenant, Sys_User target)
    {
        await using var read = Context(tenant);
        Assert.Equal(target.IsPlatformAdmin, await read.Sys_Users.AsNoTracking().Where(x => x.Id == target.Id).Select(x => x.IsPlatformAdmin).SingleAsync());
        Assert.False(await read.Sys_SecurityLogs.IgnoreQueryFilters().AnyAsync(x => x.Reason == "target=" + target.UserName));
    }
    private static PlatformAdminService Service(CP6Context db, Guid actor, CancellationToken cancellationToken = default)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", actor.ToString()), new Claim("tenant_id", db.CurrentTenantId.ToString()),
            new Claim("is_platform_admin", "true")], "fixture"));
        return new(db, new SecurityAuditService(db), new TenantContext { CurrentTenantId = db.CurrentTenantId },
            http: new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal, RequestAborted = cancellationToken } });
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
        Assert.Fail("Owned competing command must be blocked by PostgreSQL until transaction completion.");
    }
    private sealed class BeforeGuard : DbCommandInterceptor
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("cp6_platform_admin_guard_v1.acquire()"))
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
