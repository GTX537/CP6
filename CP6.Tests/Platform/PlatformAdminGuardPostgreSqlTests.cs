using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Tests.Platform;

[Collection("TenantManagementPostgres")]
public sealed class PlatformAdminGuardPostgreSqlTests(TenantManagementPostgresFixture server)
{
    [CurrentPermissionPostgresFact]
    public async Task Concurrent_service_revocations_preserve_one_enabled_administrator()
    {
        var ids = await Seed(2);
        await using var first = Context(); await using var second = Context();
        await using var tx = await first.Database.BeginTransactionAsync();
        await Service(first).RevokeAsync(ids[0]);
        var pending = Record.ExceptionAsync(() => Service(second).RevokeAsync(ids[1]));
        await CompletedOrWaiting(pending, second);
        await tx.CommitAsync();
        var error = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(error);
        Assert.Equal(1, await Count());
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Direct_disable_or_delete_cannot_remove_last_administrator(bool delete)
    {
        var id = (await Seed(1))[0]; await using var db = Context();
        var error = await Record.ExceptionAsync(async () =>
        {
            if (delete) await db.Sys_Users.IgnoreQueryFilters().Where(x => x.Id == id).ExecuteDeleteAsync();
            else await db.Sys_Users.IgnoreQueryFilters().Where(x => x.Id == id).ExecuteUpdateAsync(x => x.SetProperty(u => u.Enable, false));
        });
        Assert.NotNull(error);
        Assert.Equal(1, await Count());
    }

    [CurrentPermissionPostgresFact]
    public async Task Grant_audit_failure_rolls_back_flag()
    {
        await Seed(1); await using var db = Context();
        var user = User(false); db.Sys_Users.Add(user); await db.SaveChangesAsync();
        var constraint = "w23_audit_" + Guid.NewGuid().ToString("N");
        await db.Database.ExecuteSqlRawAsync($"ALTER TABLE public.\"Sys_SecurityLogs\" ADD CONSTRAINT \"{constraint}\" CHECK (\"EventType\" <> 23) NOT VALID");
        Exception? error;
        try { error = await Record.ExceptionAsync(() => Service(db).GrantAsync(user.Id)); }
        finally { await db.Database.ExecuteSqlRawAsync($"ALTER TABLE public.\"Sys_SecurityLogs\" DROP CONSTRAINT \"{constraint}\""); }
        await using var read = Context();
        Assert.NotNull(error);
        Assert.False((await read.Sys_Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == user.Id)).IsPlatformAdmin);
    }

    [TenantManagementPostgresTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Direct_disable_and_service_revoke_share_gate_until_commit_or_rollback(bool commit)
    {
        var ids = await Seed(2);
        await using var first = Context(); await using var second = Context();
        await using var tx = await first.Database.BeginTransactionAsync();
        await first.Sys_Users.IgnoreQueryFilters().Where(x => x.Id == ids[0]).ExecuteUpdateAsync(x => x.SetProperty(u => u.Enable, false));
        var pending = Record.ExceptionAsync(() => Service(second).RevokeAsync(ids[1]));
        await CompletedOrWaiting(pending, second); Assert.False(pending.IsCompleted);
        if (commit) await tx.CommitAsync(); else await tx.RollbackAsync();
        var error = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        if (commit) Assert.Equal("E-SEC-037", Assert.IsType<InvalidOperationException>(error).Message);
        else Assert.Null(error);
        Assert.Equal(1, await Count());
    }

    [CurrentPermissionPostgresFact]
    public async Task Service_revoke_then_direct_delete_waits_and_rechecks_after_commit()
    {
        var ids = await Seed(2); await using var first = Context(); await using var second = Context();
        await using var tx = await first.Database.BeginTransactionAsync();
        await Service(first).RevokeAsync(ids[0]);
        var pending = Record.ExceptionAsync(async () => await second.Sys_Users.IgnoreQueryFilters().Where(x => x.Id == ids[1]).ExecuteDeleteAsync());
        await CompletedOrWaiting(pending, second); Assert.False(pending.IsCompleted);
        await tx.CommitAsync();
        Assert.True(PlatformAdminMutationV1.IsSurvivorViolation((await pending.WaitAsync(TimeSpan.FromSeconds(10)))!));
        Assert.Equal(1, await Count());
    }

    [TenantManagementPostgresTheory]
    [InlineData(IsolationLevel.RepeatableRead)]
    [InlineData(IsolationLevel.Serializable)]
    public async Task Stale_transaction_cannot_use_pre_revocation_survivor_set(IsolationLevel isolation)
    {
        var ids = await Seed(2); await using var stale = Context();
        await using var tx = await stale.Database.BeginTransactionAsync(isolation);
        Assert.Equal(2, await stale.Sys_Users.IgnoreQueryFilters().CountAsync(x => x.Enable && x.IsPlatformAdmin));
        await using (var current = Context()) await Service(current).RevokeAsync(ids[0]);
        var error = await Record.ExceptionAsync(async () => await stale.Sys_Users.IgnoreQueryFilters().Where(x => x.Id == ids[1]).ExecuteDeleteAsync());
        // The non-retrying Npgsql EF strategy wraps transient PostgreSQL errors.
        Assert.Equal(PostgresErrorCodes.SerializationFailure, Assert.IsType<PostgresException>(error?.GetBaseException()).SqlState);
        await tx.RollbackAsync(); Assert.Equal(1, await Count());
    }

    [CurrentPermissionPostgresFact]
    public async Task Multirow_replacement_is_atomic_and_removing_entire_set_is_rejected()
    {
        var ids = await Seed(2); await using var db = Context(); var replacement = User(false);
        db.Sys_Users.Add(replacement); await db.SaveChangesAsync();
        await db.Sys_Users.IgnoreQueryFilters().Where(x => ids.Contains(x.Id) || x.Id == replacement.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(u => u.IsPlatformAdmin, u => u.Id == replacement.Id));
        Assert.Equal(1, await Count());
        Assert.True(PlatformAdminMutationV1.IsSurvivorViolation((await Record.ExceptionAsync(async () =>
            await db.Sys_Users.IgnoreQueryFilters().Where(x => x.IsPlatformAdmin).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsPlatformAdmin, false))))!));
        Assert.Equal(1, await Count());
    }

    [TenantManagementPostgresTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Caller_transaction_audit_failure_preserves_prior_work_and_tracking(bool grant)
    {
        var ids = await Seed(2); await using var db = Context(); var target = User(false);
        if (grant) { db.Sys_Users.Add(target); await db.SaveChangesAsync(); }
        else target = await db.Sys_Users.IgnoreQueryFilters().SingleAsync(x => x.Id == ids[0]);
        var before = target.IsPlatformAdmin;
        var constraint = "w23_audit_" + Guid.NewGuid().ToString("N"); var kind = grant ? 23 : 24;
        await db.Database.ExecuteSqlRawAsync($"ALTER TABLE public.\"Sys_SecurityLogs\" ADD CONSTRAINT \"{constraint}\" CHECK (\"EventType\" <> {kind}) NOT VALID");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var prior = new Sys_SecurityLog { EventType = 0, Reason = Guid.NewGuid().ToString(), CreatedAt = DateTime.Now };
            db.Sys_SecurityLogs.Add(prior); await db.SaveChangesAsync();
            var error = await Record.ExceptionAsync(() => grant ? Service(db).GrantAsync(target.Id) : Service(db).RevokeAsync(target.Id));
            Assert.IsType<DbUpdateException>(error); Assert.False(db.ChangeTracker.HasChanges());
            Assert.Equal(before, target.IsPlatformAdmin);
            Assert.Equal(before, await db.Sys_Users.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id == target.Id).Select(x => x.IsPlatformAdmin).SingleAsync());
            Assert.True(await db.Sys_SecurityLogs.AnyAsync(x => x.Id == prior.Id));
            await tx.CommitAsync();
        }
        finally { await db.Database.ExecuteSqlRawAsync($"ALTER TABLE public.\"Sys_SecurityLogs\" DROP CONSTRAINT \"{constraint}\""); }
    }

    [CurrentPermissionPostgresFact]
    public async Task Cancelled_service_wait_restores_callers_savepoint_and_leaves_no_effect()
    {
        var ids = await Seed(2); await using var first = Context(); await using var second = Context();
        await using var tx = await first.Database.BeginTransactionAsync();
        await Service(first).RevokeAsync(ids[0]);
        await using var caller = await second.Database.BeginTransactionAsync();
        using var cancel = new CancellationTokenSource();
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { RequestAborted = cancel.Token } };
        var service = new PlatformAdminService(second, new SecurityAuditService(second), new TenantContext(), http: http);
        var pending = Record.ExceptionAsync(() => service.RevokeAsync(ids[1]));
        await CompletedOrWaiting(pending, second); Assert.False(pending.IsCompleted); cancel.Cancel();
        Assert.IsAssignableFrom<OperationCanceledException>(await pending.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, await second.Database.SqlQuery<int>($"SELECT 1 AS \"Value\"").SingleAsync());
        await caller.CommitAsync(); await tx.RollbackAsync(); Assert.Equal(2, await Count());
    }

    [CurrentPermissionPostgresFact]
    public async Task Repeated_grants_reload_stale_tracking_and_caller_rollback_undoes_flag_and_audit()
    {
        var ids = await Seed(2); await using var stale = Context();
        var target = await stale.Sys_Users.IgnoreQueryFilters().SingleAsync(x => x.Id == ids[0]);
        await using (var writer = Context()) await Service(writer).RevokeAsync(target.Id);
        await Service(stale).GrantAsync(target.Id); Assert.Equal(2, await Count());
        await Service(stale).GrantAsync(target.Id); Assert.Equal(2, await Count());
        await using var tx = await stale.Database.BeginTransactionAsync();
        var before = await stale.Sys_SecurityLogs.IgnoreQueryFilters().CountAsync();
        await Service(stale).RevokeAsync(target.Id);
        Assert.Equal(before + 1, await stale.Sys_SecurityLogs.IgnoreQueryFilters().CountAsync());
        await tx.RollbackAsync();
        await using var read = Context(); Assert.Equal(2, await Count());
        Assert.Equal(before, await read.Sys_SecurityLogs.IgnoreQueryFilters().CountAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Missing_guard_fails_closed_without_recreating_root_and_truncate_is_protected()
    {
        var ids = await Seed(2); await using var db = Context();
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM cp6_platform_admin_guard_v1.root");
            var error = await Record.ExceptionAsync(() => Service(db).RevokeAsync(ids[0]));
            Assert.Equal("PLATFORM_ADMIN_GUARD_UNAVAILABLE", Assert.IsType<PostgresException>(error).MessageText);
            Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_platform_admin_guard_v1.root").SingleAsync());
            await tx.RollbackAsync();
        }
        var truncate = await Record.ExceptionAsync(() => db.Database.ExecuteSqlRawAsync("TRUNCATE public.\"Sys_Users\" CASCADE"));
        Assert.True(PlatformAdminMutationV1.IsSurvivorViolation(truncate!)); Assert.Equal(2, await Count());
    }

    [CurrentPermissionPostgresFact]
    public async Task Actual_http_admin_and_user_routes_preserve_survivor_and_localized_rejection()
    {
        var actor = (await Seed(1))[0]; await using var setup = Context();
        if (!await setup.Sys_Tenants.AnyAsync(x => x.Id == TenantContext.DefaultTenant))
            setup.Sys_Tenants.Add(new Sys_Tenant { Id = TenantContext.DefaultTenant, TenantCode = "w23-platform", TenantName = "fixture" });
        setup.Sys_Roles.Add(new Sys_Role { RoleId = 72301, RoleName = "w23-user-editor" });
        setup.Sys_Menus.Add(new Sys_Menu { MenuId = 72301, MenuKey = "user", MenuName = "fixture" });
        setup.Sys_RoleActions.AddRange(new Sys_RoleAction { RoleId = 72301, MenuId = 72301, ActionCode = "edit" }, new Sys_RoleAction { RoleId = 72301, MenuId = 72301, ActionCode = "delete" });
        var admin = await setup.Sys_Users.SingleAsync(x => x.Id == actor); admin.RoleId = 72301; await setup.SaveChangesAsync();
        await using var host = await TenantManagementHttpHost.Start(server, includePlatformAdmins: true);
        var token = host.Token(actor, TenantContext.DefaultTenant);
        async Task Rejected(HttpMethod method, string path, object? body = null)
        {
            using var request = new HttpRequestMessage(method, path); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await host.Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("E-SEC-037", await response.Content.ReadAsStringAsync());
        }
        await Rejected(HttpMethod.Post, $"api/platform/admin/{actor}/revoke");
        await Rejected(HttpMethod.Put, "api/user", new { Id = actor, UserName = admin.UserName, Enable = false, RoleId = 72301 });
        await Rejected(HttpMethod.Delete, "api/user", new[] { actor });
        Assert.Equal(1, await Count());
        var target = User(false); setup.Sys_Users.Add(target); await setup.SaveChangesAsync();
        foreach (var operation in new[] { "grant", "revoke" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"api/platform/admin/{target.Id}/{operation}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await host.Client.SendAsync(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        await using var read = Context();
        var audit = await read.Sys_SecurityLogs.IgnoreQueryFilters().Where(x => x.Reason == "target=" + target.UserName).ToArrayAsync();
        Assert.Equal(2, audit.Length); Assert.All(audit, x => { Assert.Equal(actor, x.UserId); Assert.Equal(TenantContext.DefaultTenant, x.TenantId); });
        Assert.Equal(1, await Count());
        using var anonymous = await host.Client.GetAsync("api/platform/admin");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Cross_tenant_grant_revoke_audits_stay_in_platform_tenant()
    {
        var ids = await Seed(1); var tenant = new TenantContext { CurrentTenantId = Guid.NewGuid() };
        await using var db = server.Context(tenant); var user = User(false); db.Sys_Users.Add(user); await db.SaveChangesAsync();
        var service = new PlatformAdminService(db, new SecurityAuditService(db), tenant);
        await service.GrantAsync(user.Id); Assert.Equal(2, await Count());
        await service.RevokeAsync(ids[0]); Assert.Equal(1, await Count());
        Assert.Equal("E-SEC-037", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.RevokeAsync(user.Id))).Message);
        var logs = await db.Sys_SecurityLogs.IgnoreQueryFilters().Where(x => x.Reason == "target=" + user.UserName).ToArrayAsync();
        Assert.Single(logs); Assert.Equal(TenantContext.DefaultTenant, logs[0].TenantId);
        Assert.Equal(23, logs[0].EventType); Assert.Equal(tenant.CurrentTenantId, user.TenantId);
    }

    [CurrentPermissionPostgresFact]
    public async Task Ordinary_dml_role_cannot_bypass_guard_by_lacking_internal_permissions()
    {
        var id = (await Seed(1))[0]; await using var db = Context();
        var role = "w23_dml_" + Guid.NewGuid().ToString("N");
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync($"CREATE ROLE {role}; GRANT USAGE ON SCHEMA public TO {role}; GRANT SELECT, UPDATE ON public.\"Sys_Users\" TO {role}");
        Exception? error;
        try
        {
            await db.Database.ExecuteSqlRawAsync($"SET ROLE {role}");
            error = await Record.ExceptionAsync(async () => await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_Users\" SET \"Enable\"=false WHERE \"Id\"={id}"));
        }
        finally { await db.Database.ExecuteSqlRawAsync("RESET ROLE"); }
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, Assert.IsType<PostgresException>(error).SqlState);
        Assert.Equal(1, await Count());
    }

    [CurrentPermissionPostgresFact]
    public async Task Forward_migration_on_populated_isolated_baseline_preserves_users_and_installs_guard()
    {
        await using var owner = Context(); var name = "w23_upgrade_" + Guid.NewGuid().ToString("N");
        await owner.Database.ExecuteSqlRawAsync($"CREATE DATABASE {name}");
        await using var db = Context(); var connection = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()) { Database = name };
        db.Database.SetConnectionString(connection.ConnectionString);
        await db.GetService<IMigrator>().MigrateAsync("20261002161715_PostgreSqlCoreBaselineV1");
        var first = User(true); var second = User(true); db.Sys_Users.AddRange(first, second); await db.SaveChangesAsync();
        await db.Database.MigrateAsync(); await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(2, await db.Sys_Users.CountAsync(x => x.Enable && x.IsPlatformAdmin));
        Assert.True(PlatformAdminMutationV1.IsSurvivorViolation((await Record.ExceptionAsync(async () =>
            await db.Sys_Users.ExecuteDeleteAsync()))!));
        Assert.Equal(2, await db.Sys_Users.CountAsync());
    }

    private CP6Context Context() => server.Context(new TenantContext());
    private static PlatformAdminService Service(CP6Context db) => new(db, new SecurityAuditService(db), new TenantContext());
    private static Sys_User User(bool admin) => new() { Id = Guid.NewGuid(), UserName = "w23_" + Guid.NewGuid().ToString("N"), Password = "fixture", Enable = true, IsPlatformAdmin = admin };
    private async Task<Guid[]> Seed(int count)
    {
        await using var db = Context(); var users = Enumerable.Range(0, count).Select(_ => User(true)).ToArray();
        db.Sys_Users.AddRange(users); await db.SaveChangesAsync(); var ids = users.Select(x => x.Id).ToArray();
        await db.Sys_Users.IgnoreQueryFilters().Where(x => x.IsPlatformAdmin && !ids.Contains(x.Id)).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsPlatformAdmin, false));
        return ids;
    }
    private async Task<int> Count() { await using var db = Context(); return await db.Sys_Users.IgnoreQueryFilters().CountAsync(x => x.IsPlatformAdmin && x.Enable); }
    private async Task CompletedOrWaiting(Task pending, CP6Context db)
    {
        var until = DateTimeOffset.UtcNow.AddSeconds(8);
        await using var observer = Context();
        while (DateTimeOffset.UtcNow < until)
        {
            if (pending.IsCompleted) return;
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            if (connection.State == System.Data.ConnectionState.Open)
            {
                var pid = connection.ProcessID;
                if (await observer.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM pg_stat_activity WHERE pid={pid} AND wait_event_type='Lock') AS \"Value\"").SingleAsync()) return;
            }
            await Task.Delay(20);
        }
        Assert.Fail("Owned writer neither completed nor entered a database lock wait.");
    }
}
