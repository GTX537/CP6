using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text.Json;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static readonly Guid ScopeEnvironment = Guid.Parse("9911b33a-2811-4620-81d8-590044f92c23");
    private static ScopeAuthorizationFenceV1 ScopeFence(Harness h, Guid? environment = null)
        => new(h.Db, Options.Create(new ScopeAuthorizationOptionsV1 { EnvironmentId = environment ?? ScopeEnvironment }));
    private static ScopeAuthorizationAdministrationV1 ScopeAdmin(Harness h, Guid? environment = null)
        => new(h.Db, new HttpContextAccessor { HttpContext = h.Http }, ScopeFence(h, environment),
            _ => Task.FromResult(Guid.Parse(h.Http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value)));
    private static async Task<ScopeAuthorizationRegisteredV1> RegisterScope(Harness h)
    {
        await EngineeringAction(h, "pub-role-perm", "edit");
        return await ScopeAdmin(h).RegisterAsync(new(Guid.NewGuid(), "absent", true, "owned local fixture"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Scope_fence_registration_uses_trusted_scope_and_preserves_unique_domain_across_disable_enable()
    {
        await using var h = await Seed();
        Assert.Equal(403, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => ScopeAdmin(h).ContextAsync())).Status);
        var saved = await RegisterScope(h); var admin = ScopeAdmin(h);
        Assert.Equal(h.Tenant, saved.Mapping.TenantId); Assert.Equal(ScopeEnvironment, saved.Mapping.EnvironmentId);
        Assert.Equal("WF_AUTH", saved.Mapping.LogicalDomain); Assert.NotEqual(Guid.Empty, saved.Mapping.PermissionDomain);
        Assert.Equal(0, saved.Mapping.Epoch); Assert.NotEqual(Guid.Empty, saved.Mapping.RowVersion);
        var disabled = await admin.RegisterAsync(new(Guid.NewGuid(), saved.Mapping.RowVersion.ToString("D"), false, "revoke scope"));
        Assert.False(disabled.Mapping.Enabled); Assert.True(disabled.Mapping.Epoch > saved.Mapping.Epoch);
        await using (var tx = await h.Db.Database.BeginTransactionAsync())
            Assert.Equal("SCOPE_MAPPING_DISABLED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => ScopeFence(h).AcquireAsync())).Message);
        var enabled = await admin.RegisterAsync(new(Guid.NewGuid(), disabled.Mapping.RowVersion.ToString("D"), true, "restore scope"));
        Assert.Equal(saved.Mapping.PermissionDomain, enabled.Mapping.PermissionDomain);
        Assert.True(enabled.Mapping.Epoch > disabled.Mapping.Epoch);
        Assert.Equal(412, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => admin.RegisterAsync(new(Guid.NewGuid(), "absent", true, "stale")))).Status);
        Assert.Equal(saved.Mapping, (await admin.OperationAsync(saved.OperationId)).Result!.Mapping);
    }

    [CurrentPermissionPostgresFact]
    public async Task Scope_fence_missing_environment_mapping_or_actual_transaction_closes_admission()
    {
        await using var h = await Seed();
        Assert.Equal("SCOPE_ENVIRONMENT_UNAVAILABLE", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => ScopeAdmin(h, Guid.Empty).ContextAsync())).Message);
        Assert.Equal("SCOPE_TRANSACTION_REQUIRED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => ScopeFence(h).AcquireAsync())).Message);
        await using (var tx = await h.Db.Database.BeginTransactionAsync())
            Assert.Equal("SCOPE_MAPPING_REQUIRED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => ScopeFence(h).AcquireAsync())).Message);
        await RegisterScope(h);
        Assert.Equal("SCOPE_ENVIRONMENT_MISMATCH", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => ScopeAdmin(h, Guid.NewGuid()).ContextAsync())).Message);
        await using var old = await h.Db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        Assert.Equal("SCOPE_TRANSACTION_REQUIRED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => ScopeFence(h).AcquireAsync())).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Scope_fence_every_registered_native_writer_advances_epoch_even_raw_sql_and_ABA()
    {
        await using var h = await Seed(); await RegisterScope(h);
        var tables = new[] { "Sys_Tenants", "Sys_Users", "Sys_TenantSsoConfigs", "Sys_Roles", "Sys_UserRole",
            "Sys_RoleMenus", "Sys_RoleAction", "Sys_RoleDataScope", "Sys_RoleFieldPerm", "Sys_MenuAction", "Sys_Menus",
            "Sys_Dept", "Wf_FlowDelegate", "Wf_ApproverMap" };
        foreach (var table in tables)
        {
            var before = (await ScopeAdmin(h).ContextAsync()).Mapping!;
            // Empty SQL is conservative invalidation and exercises every installed statement trigger.
            await h.Db.Database.ExecuteSqlRawAsync($"DELETE FROM public.\"{table}\" WHERE false");
            var after = (await ScopeAdmin(h).ContextAsync()).Mapping!;
            Assert.True(after.Epoch > before.Epoch, table); Assert.NotEqual(before.RowVersion, after.RowVersion);
        }
        h.Db.Wf_FlowDelegates.Add(new Wf_FlowDelegate { GrantorId = h.User, DelegateId = h.User, ValidFrom = DateTime.Now.AddMinutes(-1), ValidTo = DateTime.Now.AddHours(1) });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var start = (await ScopeAdmin(h).ContextAsync()).Mapping!;
        await h.Db.Wf_FlowDelegates.ExecuteUpdateAsync(x => x.SetProperty(d => d.Enable, false));
        await h.Db.Wf_FlowDelegates.ExecuteUpdateAsync(x => x.SetProperty(d => d.Enable, true));
        var restored = (await ScopeAdmin(h).ContextAsync()).Mapping!;
        Assert.True(restored.Epoch >= start.Epoch + 2); Assert.NotEqual(start.RowVersion, restored.RowVersion);
    }

    [CurrentPermissionPostgresFact]
    public async Task Scope_fence_real_concurrent_revoke_waits_for_transaction_then_invalidates_next_epoch()
    {
        await using var h = await Seed(); await RegisterScope(h); var gate = ScopeFence(h);
        await using var tx = await h.Db.Database.BeginTransactionAsync(); var proof = await gate.AcquireAsync();
        await using var other = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var app = "scope-writer-" + Guid.NewGuid().ToString("N");
        other.Database.SetConnectionString(other.Database.GetConnectionString() + ";Application Name=" + app);
        var revoke = other.Sys_Roles.ExecuteUpdateAsync(x => x.SetProperty(r => r.Enable, false));
        await using var monitor = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var waiting = false;
        for (var i = 0; i < 100 && !revoke.IsCompleted; i++)
        {
            waiting = await monitor.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM pg_stat_activity WHERE application_name={app} AND wait_event_type='Lock') AS \"Value\"").SingleAsync();
            if (waiting) break; await Task.Delay(20);
        }
        Assert.True(waiting); Assert.False(revoke.IsCompleted);
        await gate.ValidateAsync(proof); await tx.CommitAsync(); await tx.DisposeAsync();
        Assert.Equal(1, await revoke.WaitAsync(TimeSpan.FromSeconds(10)));
        await using var next = await h.Db.Database.BeginTransactionAsync(); var after = await gate.AcquireAsync();
        Assert.True(after.Snapshot.Epoch > proof.Snapshot.Epoch);
        Assert.False(await h.Db.Sys_Roles.AnyAsync(r => r.Enable));
        Assert.Equal("SCOPE_TRANSACTION_MISMATCH", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => gate.ValidateAsync(proof))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Scope_fence_same_transaction_revoke_rolls_back_business_and_epoch_when_final_proof_changes()
    {
        await using var h = await Seed(); await RegisterScope(h); var before = (await ScopeAdmin(h).ContextAsync()).Mapping!;
        await using (var tx = await h.Db.Database.BeginTransactionAsync())
        {
            var gate = ScopeFence(h); var proof = await gate.AcquireAsync();
            await h.Db.OrderDetails.ExecuteUpdateAsync(x => x.SetProperty(d => d.Amount, 999m));
            await h.Db.Sys_RoleActions.ExecuteDeleteAsync();
            Assert.Equal("SCOPE_AUTHORITY_CHANGED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => gate.ValidateAsync(proof))).Message);
            await tx.RollbackAsync();
        }
        Assert.Equal(before, (await ScopeAdmin(h).ContextAsync()).Mapping);
        Assert.All(await h.Db.OrderDetails.AsNoTracking().ToArrayAsync(), r => Assert.Equal(6m, r.Amount));
    }

    [CurrentPermissionPostgresFact]
    public async Task Scope_fence_missing_writer_guard_and_replica_mode_do_not_issue_proof()
    {
        await using var h = await Seed(); await RegisterScope(h);
        await using var tx = await h.Db.Database.BeginTransactionAsync();
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"Wf_FlowDelegate\" DISABLE TRIGGER cp6_scope_authority_writer_v1");
        Assert.Equal("SCOPE_WRITER_COVERAGE_UNAVAILABLE", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => ScopeFence(h).AcquireAsync())).Message);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"Wf_FlowDelegate\" ENABLE TRIGGER cp6_scope_authority_writer_v1");
        await h.Db.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role=replica");
        Assert.Equal("SCOPE_WRITER_COVERAGE_UNAVAILABLE", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => ScopeFence(h).AcquireAsync())).Message);
        await tx.RollbackAsync();
    }

    [CurrentPermissionPostgresFact]
    public async Task Scope_fence_original_result_survives_lost_ACK_and_revoke_without_granting_new_write()
    {
        var fault = new CommitFault(); await using var h = await Seed(fault); await EngineeringAction(h, "pub-role-perm", "edit");
        var command = new ScopeAuthorizationCommandV1(Guid.NewGuid(), "absent", true, "register"); var admin = ScopeAdmin(h); fault.Armed = true;
        Assert.Equal("SCOPE_OPERATION_UNKNOWN", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => admin.RegisterAsync(command))).Message);
        await h.Db.Sys_RoleActions.Where(x => x.ActionCode == "edit").ExecuteDeleteAsync();
        var original = await admin.RegisterAsync(command); Assert.Equal(original, (await admin.OperationAsync(command.OperationId)).Result);
        Assert.Equal("COMMAND_BODY_CONFLICT", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => admin.RegisterAsync(command with { Reason = "changed" }))).Message);
        Assert.Equal(403, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => admin.RegisterAsync(command with { OperationId = Guid.NewGuid() }))).Status);
        await using var foreign = await Seed();
        Assert.Equal("NOT_OBSERVED", (await ScopeAdmin(foreign).OperationAsync(command.OperationId)).State);
    }

    [CurrentPermissionPostgresFact]
    public async Task Scope_fence_final_registration_revoke_rolls_back_mapping_audit_and_operation()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); await EngineeringAction(h, "pub-role-perm", "edit");
        var admin = ScopeAdmin(h); var command = new ScopeAuthorizationCommandV1(Guid.NewGuid(), "absent", true, "register");
        var probes = 0; var injected = false;
        hook.Before = async c =>
        {
            if (!c.CommandText.Contains("writers_ready") || ++probes != 2) return;
            hook.Before = null; injected = true;
            await using var revoke = c.Connection!.CreateCommand(); revoke.Transaction = c.Transaction;
            revoke.CommandText = "DELETE FROM public.\"Sys_RoleAction\" WHERE \"TenantId\"=@tenant AND \"ActionCode\"='edit'";
            revoke.Parameters.Add(new Npgsql.NpgsqlParameter("tenant", h.Tenant)); await revoke.ExecuteNonQueryAsync();
        };
        Assert.Equal("ACTION_FORBIDDEN", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => admin.RegisterAsync(command))).Message);
        Assert.True(injected); Assert.Null((await admin.ContextAsync()).Mapping);
        Assert.Equal("NOT_OBSERVED", (await admin.OperationAsync(command.OperationId)).State);
    }

    [CurrentPermissionPostgresFact]
    public async Task Scope_fence_final_actor_resolution_cannot_hide_an_epoch_change_before_commit()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); await EngineeringAction(h, "pub-role-perm", "edit");
        var admin = ScopeAdmin(h); var command = new ScopeAuthorizationCommandV1(Guid.NewGuid(), "absent", true, "register");
        var reads = 0; var injected = false;
        hook.Before = async c =>
        {
            if (!c.CommandText.Contains("permission_user AS MATERIALIZED") || ++reads != 3) return;
            hook.Before = null; injected = true;
            await using var change = c.Connection!.CreateCommand(); change.Transaction = c.Transaction;
            // Authority remains sufficient, but its epoch changed during final actor resolution.
            change.CommandText = "UPDATE public.\"Sys_Roles\" SET \"RoleName\"='same-UoW change' WHERE \"TenantId\"=@tenant";
            change.Parameters.Add(new Npgsql.NpgsqlParameter("tenant", h.Tenant)); await change.ExecuteNonQueryAsync();
        };
        Assert.Equal("SCOPE_AUTHORITY_CHANGED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => admin.RegisterAsync(command))).Message);
        Assert.True(injected); Assert.Null((await admin.ContextAsync()).Mapping);
        Assert.Equal("NOT_OBSERVED", (await admin.OperationAsync(command.OperationId)).State);
        Assert.Equal("price-correction", (await h.Db.Sys_Roles.AsNoTracking().SingleAsync()).RoleName);
    }

    [CurrentPermissionPostgresFact]
    public async Task Scope_fence_database_mapping_cannot_rebind_or_rewind_and_proof_cannot_cross_contexts()
    {
        await using var h = await Seed(); var saved = await RegisterScope(h);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_scope_authorization_v1.fences SET epoch=-1,row_version={Guid.Empty} WHERE tenant_id={h.Tenant}");
        var current = (await ScopeAdmin(h).ContextAsync()).Mapping!;
        Assert.Equal(saved.Mapping.Epoch + 1, current.Epoch); Assert.NotEqual(Guid.Empty, current.RowVersion);
        Assert.Equal("23514", (await Assert.ThrowsAsync<Npgsql.PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE cp6_scope_authorization_v1.fences SET permission_domain={Guid.NewGuid()} WHERE tenant_id={h.Tenant}"))).SqlState);
        Assert.Equal("23514", (await Assert.ThrowsAsync<Npgsql.PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE cp6_scope_authorization_v1.environment SET environment_id={Guid.NewGuid()}"))).SqlState);
        Assert.Equal(current, (await ScopeAdmin(h).ContextAsync()).Mapping);
        await using var tx = await h.Db.Database.BeginTransactionAsync(); var lease = await ScopeFence(h).AcquireAsync();
        await using var foreignDb = server.Context(new TenantContext { CurrentTenantId = Guid.NewGuid() });
        await using var foreignTx = await foreignDb.Database.BeginTransactionAsync();
        var foreignGate = new ScopeAuthorizationFenceV1(foreignDb, Options.Create(new ScopeAuthorizationOptionsV1 { EnvironmentId = ScopeEnvironment }));
        Assert.Equal("SCOPE_TRANSACTION_MISMATCH", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => foreignGate.ValidateAsync(lease))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Scope_fence_parallel_initial_registration_has_one_domain_and_one_committed_operation()
    {
        await using var h = await Seed(); await EngineeringAction(h, "pub-role-perm", "edit");
        async Task<ScopeAuthorizationRegisteredV1?> Attempt()
        {
            await using var db = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
            var admin = ScopeAdmin(h with { Db = db });
            try { return await admin.RegisterAsync(new(Guid.NewGuid(), "absent", true, "parallel")); }
            catch (ScopeAuthorizationExceptionV1 e) when (e.Status == 412) { return null; }
        }
        var results = await Task.WhenAll(Attempt(), Attempt()); Assert.Single(results, r => r != null);
        Assert.Equal(1, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_scope_authorization_v1.fences WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(1, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_main_owner_v1.operations WHERE tenant_id={h.Tenant} AND owner='Main.ScopeAuthorization.v1'").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Scope_fence_original_results_require_original_current_actor_and_active_tenant()
    {
        await using var h = await Seed(); var saved = await RegisterScope(h);
        var other = new CP6.Entity.DomainModels.Sys.Sys_User { UserName = "other", Password = "unused", RoleId = 72 };
        h.Db.Sys_Users.Add(other); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        h.Http.User = CurrentPermissionContextTests.Principal(other.Id, h.Tenant);
        Assert.Equal("OBJECT_FORBIDDEN", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => ScopeAdmin(h).OperationAsync(saved.OperationId))).Message);
        h.Http.User = CurrentPermissionContextTests.Principal(h.User, h.Tenant);
        await h.Db.Sys_Tenants.Where(t => t.Id == h.Tenant).ExecuteUpdateAsync(x => x.SetProperty(t => t.Enable, false));
        Assert.Equal("REAUTH_REQUIRED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => ScopeAdmin(h).OperationAsync(saved.OperationId))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Scope_fence_IAM_actor_expiry_at_completion_rolls_back_mapping_and_operation()
    {
        await using var h = await Seed(); await EngineeringAction(h, "pub-role-perm", "edit");
        var command = new ScopeAuthorizationCommandV1(Guid.NewGuid(), "absent", true, "register"); var calls = 0;
        var admin = new ScopeAuthorizationAdministrationV1(h.Db, new HttpContextAccessor { HttpContext = h.Http }, ScopeFence(h),
            _ => ++calls == 3 ? Task.FromException<Guid>(new ScopeAuthorizationExceptionV1("REAUTH_REQUIRED", 401)) : Task.FromResult(h.User));
        Assert.Equal("REAUTH_REQUIRED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => admin.RegisterAsync(command))).Message);
        Assert.Equal(3, calls); Assert.Null((await ScopeAdmin(h).ContextAsync()).Mapping);
        Assert.Equal("NOT_OBSERVED", (await ScopeAdmin(h).OperationAsync(command.OperationId)).State);
    }

    [CurrentPermissionPostgresFact]
    public async Task Scope_fence_impersonation_cannot_register_or_read_an_original_admin_operation()
    {
        await using var h = await Seed(); var saved = await RegisterScope(h);
        ((System.Security.Claims.ClaimsIdentity)h.Http.User.Identity!).AddClaim(new("impersonator_id", Guid.NewGuid().ToString("D")));
        Assert.Equal("REAUTH_REQUIRED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => ScopeAdmin(h).OperationAsync(saved.OperationId))).Message);
        Assert.Equal("REAUTH_REQUIRED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => ScopeAdmin(h).RegisterAsync(
            new(Guid.NewGuid(), saved.Mapping.RowVersion.ToString("D"), false, "impersonated")))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Scope_fence_actual_HTTP_current_permission_csrf_closed_shape_environment_and_original_result()
    {
        await using var h = await Seed();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,
            new Dictionary<string, string> { ["ScopeAuthorization__EnvironmentId"] = ScopeEnvironment.ToString("D") });
        var cookies = await Login(host, h); const string path = "/api/iam/scope-authorization";
        var command = new ScopeAuthorizationCommandV1(Guid.NewGuid(), "absent", true, "HTTP registration");
        using (var forbidden = await host.Send(BoundRequest(cookies, path, command))) Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        await EngineeringAction(h, "pub-role-perm", "edit");
        using (var csrf = await host.Send(BoundRequest(cookies, path, command, false))) Assert.Equal(HttpStatusCode.Forbidden, csrf.StatusCode);
        using (var forged = await host.Send(BoundRequest(cookies, path, new { command.OperationId, command.ExpectedVersion, command.Enabled, command.Reason, environmentId = Guid.NewGuid() }))) Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        using (var created = await host.Send(BoundRequest(cookies, path, command)))
        {
            Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
            Assert.True(created.Headers.CacheControl?.NoStore);
            var data = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
            Assert.Equal(ScopeEnvironment, data.GetProperty("mapping").GetProperty("environmentId").GetGuid());
        }
        using (var result = await host.Send(BoundRequest(cookies, path + "/operations/" + command.OperationId))) Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }
}
