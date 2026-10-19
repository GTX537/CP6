using System.Data.Common;
using System.Data;
using System.Security.Claims;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace CP6.Tests.Platform;

[Collection("TenantManagementPostgres")]
public sealed class TenantManagementCurrentActorPostgreSqlTests(TenantManagementPostgresFixture server)
{
    [TenantManagementPostgresTheory]
    [InlineData("create", "revoke")]
    [InlineData("update", "disable")]
    [InlineData("suspend", "delete")]
    [InlineData("reactivate", "revoke")]
    public async Task Http_actor_removed_after_filter_cannot_manage_tenant(string operation, string removal)
    {
        var (tenant, actor, target) = await Seed(operation != "reactivate");
        var barrier = new BeforeMutation();
        await using var host = await TenantManagementHttpHost.Start(server, interceptor: barrier);
        using var request = Request(host.Token(actor.Id, tenant), target, operation);
        var pending = host.Client.SendAsync(request);
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using var revoke = Context(tenant);
            var row = revoke.Sys_Users.Where(x => x.Id == actor.Id);
            if (removal == "revoke") await row.ExecuteUpdateAsync(x => x.SetProperty(u => u.IsPlatformAdmin, false));
            else if (removal == "disable") await row.ExecuteUpdateAsync(x => x.SetProperty(u => u.Enable, false));
            else await row.ExecuteDeleteAsync();
        }
        finally { barrier.Release.TrySetResult(); }
        using var response = await pending;
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("E-SEC-031", await response.Content.ReadAsStringAsync());
        await AssertUnchanged(tenant, target);
    }

    [CurrentPermissionPostgresFact]
    public async Task Service_without_authenticated_actor_cannot_create_tenant()
    {
        var (tenant, _, target) = await Seed();
        await using var db = Context(tenant);
        var service = new TenantAdminService(db, new BCryptPasswordHasher(4));
        var error = await Record.ExceptionAsync(() => service.CreateAsync(target.TenantCode + "new", "new", null, null, "admin"));
        Assert.NotNull(error); Assert.Equal("E-SEC-031", error.Message);
        await AssertUnchanged(tenant, target);
    }

    [TenantManagementPostgresTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Effect_first_keeps_actor_authority_until_commit_or_rollback(bool commit)
    {
        var (tenant, actor, target) = await Seed();
        await using var db = Context(tenant); await using var revoke = Context(tenant);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await Service(db, actor.Id).UpdateAsync(target.Id, "changed", null, null);
        var pending = revoke.Sys_Users.Where(x => x.Id == actor.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsPlatformAdmin, false));
        try
        {
            await AssertLockWait(pending, revoke);
            if (commit) await transaction.CommitAsync(); else await transaction.RollbackAsync();
            Assert.Equal(1, await pending.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally { if (!pending.IsCompleted) await transaction.RollbackAsync(); }
        await using var read = Context(tenant);
        Assert.Equal(commit ? "changed" : "original", await read.Sys_Tenants.Where(x => x.Id == target.Id).Select(x => x.TenantName).SingleAsync());
        var logs = await read.Sys_SecurityLogs.IgnoreQueryFilters().Where(x => x.RequestTenantCode == target.TenantCode).ToArrayAsync();
        if (commit) { var log = Assert.Single(logs); Assert.Equal(actor.Id, log.UserId); Assert.Equal(actor.UserName, log.UserName); }
        else Assert.Empty(logs);
    }

    [TenantManagementPostgresTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Revocation_first_rechecks_the_committed_or_rolled_back_actor(bool commit)
    {
        var (tenant, actor, target) = await Seed();
        await using var revoke = Context(tenant); await using var db = Context(tenant);
        await using var transaction = await revoke.Database.BeginTransactionAsync();
        await revoke.Sys_Users.Where(x => x.Id == actor.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsPlatformAdmin, false));
        var pending = Record.ExceptionAsync(() => Service(db, actor.Id).SuspendAsync(target.Id));
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
        Assert.Equal(commit, await read.Sys_Tenants.Where(x => x.Id == target.Id).Select(x => x.Enable).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Existing_tracking_cannot_overwrite_newer_target_state()
    {
        var (tenant, actor, target) = await Seed(false); await using var db = Context(tenant);
        var tracked = await db.Sys_Tenants.SingleAsync(x => x.Id == target.Id);
        await using (var other = Context(tenant))
            await other.Sys_Tenants.Where(x => x.Id == target.Id).ExecuteUpdateAsync(x => x.SetProperty(t => t.Enable, true));
        await Service(db, actor.Id).UpdateAsync(target.Id, "changed", null, null);
        Assert.True(tracked.Enable);
        await using var read = Context(tenant);
        Assert.True((await read.Sys_Tenants.SingleAsync(x => x.Id == target.Id)).Enable);
    }

    [CurrentPermissionPostgresFact]
    public async Task Http_valid_foreign_management_preserves_business_errors_and_required_audit()
    {
        var (tenant, actor, target) = await Seed();
        await using var host = await TenantManagementHttpHost.Start(server);
        var token = host.Token(actor.Id, tenant);
        foreach (var operation in new[] { "update", "suspend", "reactivate", "create" })
        {
            using var request = Request(token, target, operation);
            using var response = await host.Client.SendAsync(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using (var duplicate = Request(token, target, "create"))
        using (var response = await host.Client.SendAsync(duplicate))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("E-SEC-033", await response.Content.ReadAsStringAsync());
        }
        using (var missing = Request(token, new Sys_Tenant { Id = Guid.NewGuid() }, "suspend"))
        using (var response = await host.Client.SendAsync(missing))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("E-SEC-032", await response.Content.ReadAsStringAsync());
        }
        await using var read = Context(tenant);
        var logs = await read.Sys_SecurityLogs.IgnoreQueryFilters().Where(x => x.RequestTenantCode != null && x.RequestTenantCode.StartsWith(target.TenantCode)).ToArrayAsync();
        Assert.Equal(4, logs.Length);
        Assert.All(logs, x => { Assert.Equal(actor.Id, x.UserId); Assert.Equal(actor.UserName, x.UserName); Assert.Equal(TenantContext.DefaultTenant, x.TenantId); });
    }

    private static TenantAdminService Service(CP6Context db, Guid actor)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", actor.ToString()), new Claim("tenant_id", db.CurrentTenantId.ToString()),
            new Claim("is_platform_admin", "true")], "fixture"));
        return new(db, new BCryptPasswordHasher(4), http: new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } });
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
        Assert.Fail("The competing command must wait in PostgreSQL until transaction completion.");
    }

    private CP6Context Context(Guid tenant) => server.Context(new TenantContext { CurrentTenantId = tenant });
    private async Task<(Guid Tenant, Sys_User Actor, Sys_Tenant Target)> Seed(bool enabled = true)
    {
        var tenant = Guid.NewGuid(); await using var db = Context(tenant);
        var actor = new Sys_User { Id = Guid.NewGuid(), UserName = "w25_" + Guid.NewGuid().ToString("N"), Password = "fixture", Enable = true, IsPlatformAdmin = true };
        var target = new Sys_Tenant { Id = Guid.NewGuid(), TenantCode = "w25_" + Guid.NewGuid().ToString("N"), TenantName = "original", Enable = enabled };
        db.Sys_Tenants.AddRange(new Sys_Tenant { Id = tenant, TenantCode = "actor_" + tenant.ToString("N"), TenantName = "actor tenant" }, target);
        db.Sys_Users.AddRange(actor, new Sys_User { Id = Guid.NewGuid(), UserName = "spare_" + Guid.NewGuid().ToString("N"), Password = "fixture", Enable = true, IsPlatformAdmin = true });
        await db.SaveChangesAsync(); return (tenant, actor, target);
    }

    private static HttpRequestMessage Request(string token, Sys_Tenant target, string operation)
    {
        var path = operation == "create" ? "api/platform/tenant" : $"api/platform/tenant/{target.Id}" + (operation == "update" ? "" : "/" + operation);
        var request = new HttpRequestMessage(operation == "update" ? HttpMethod.Put : HttpMethod.Post, path);
        if (operation == "create") request.Content = JsonContent.Create(new { Code = target.TenantCode + "new", Name = "new", AdminUserName = "admin" });
        if (operation == "update") request.Content = JsonContent.Create(new { Name = "changed", Remark = "changed" });
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); return request;
    }

    private async Task AssertUnchanged(Guid tenant, Sys_Tenant target)
    {
        await using var read = Context(tenant);
        var row = await read.Sys_Tenants.SingleAsync(x => x.Id == target.Id);
        Assert.Equal((target.TenantName, target.Enable, target.Remark), (row.TenantName, row.Enable, row.Remark));
        Assert.False(await read.Sys_Tenants.AnyAsync(x => x.TenantCode == target.TenantCode + "new"));
        Assert.False(await read.Sys_SecurityLogs.IgnoreQueryFilters().AnyAsync(x => x.RequestTenantCode != null && x.RequestTenantCode.StartsWith(target.TenantCode)));
    }

    // Both before/after implementations pause after the real HTTP filter and before the
    // first service database action. The pre-fix service has no administrator guard.
    private sealed class BeforeMutation : DbCommandInterceptor
    {
        private int _entered;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if ((command.CommandText.Contains("cp6_platform_admin_guard_v1.acquire()") || command.CommandText.Contains("FROM \"Sys_Tenants\""))
                && Interlocked.Exchange(ref _entered, 1) == 0)
            {
                Entered.TrySetResult(); await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
