using System.Data.Common;
using System.Net;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Core.Services.Wms.Transfer;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static readonly Guid TransferSandbox = Guid.Parse("e30c8d4c-783f-4b4c-a9ac-e3af60d992c5");
    private static TransferEnvironmentOptionsV1 TransferOptions() => new() { Mode = "SIMULATION", SandboxScopeId = TransferSandbox };
    private static TransferEnvironmentOwnerV1 TransferEnvironment(Harness h, TransferEnvironmentOptionsV1? options = null, string mode = "DEMO", Guid? environment = null)
        => new(h.Db, InventoryIdentityAuthority(h), ScopeFence(h, environment), Options.Create(options ?? TransferOptions()), new(mode));
    private static TransferProductSourcesV1 TransferSources(Harness h, ContentStore? files = null)
        => new(h.Db, InventoryIdentityAuthority(h), ScopeFence(h), Options.Create(TransferOptions()), new("DEMO"), InventoryIdentity(h, files));
    private static async Task GrantTransfer(Harness h, bool registerScope = true)
    {
        if (registerScope) await RegisterScope(h);
        foreach (var (key, action) in new[] { ("wms-transfer-environment", "query"), ("wms-transfer-environment", "edit"), ("wms-transfer-source", "query") })
            await EngineeringAction(h, key, action);
        foreach (var key in new[] { "wms-transfer-environment", "wms-transfer-source" })
            h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = key, ScopeType = 5 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
    }
    private static TransferEnvironmentCommandV1 RegisterTransfer(string version = "absent", bool enabled = true)
        => new(Guid.NewGuid(), version, enabled, "Register actual native source adapters for this tenant");
    private async Task<Harness> SeedUnboundTransferDatabase(params IInterceptor[] interceptors)
    {
        // Database binding is intentionally global, so first-binding cases need their own actual database.
        var tenant = Guid.NewGuid(); var db = server.Context(new TenantContext { CurrentTenantId = tenant }, null, interceptors);
        var connection = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()); var database = "transfer_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(connection.ConnectionString))
        { await admin.OpenAsync(); await using var command = new NpgsqlCommand("CREATE DATABASE " + database, admin); await command.ExecuteNonQueryAsync(); }
        connection.Database = database; db.Database.SetConnectionString(connection.ConnectionString); await db.Database.MigrateAsync();
        var user = new CP6.Entity.DomainModels.Sys.Sys_User { UserName = "transfer-owner", Password = new BCryptPasswordHasher(4).Hash("Price1!Password"), RoleId = 72 };
        db.Sys_Tenants.Add(new() { Id = tenant, TenantCode = tenant.ToString("N"), TenantName = "Owned transfer fixture" });
        db.Sys_Roles.Add(new() { RoleId = 72, RoleName = "transfer" }); db.Sys_Users.Add(user); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var catalog = new AuthorizationCatalogStoreV1(); catalog.Seal();
        return new(db, tenant, user.Id, "", new Microsoft.AspNetCore.Http.DefaultHttpContext { User = CurrentPermissionContextTests.Principal(user.Id, tenant) }, catalog, new());
    }
    private static async Task RevokeTransferAction(Harness h, string resource, string action)
    {
        var menu = await h.Db.Sys_Menus.Where(x => x.MenuKey == resource).Select(x => x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == menu && x.ActionCode == action).ExecuteDeleteAsync();
    }
    [CurrentPermissionPostgresFact]
    public async Task TransferEnvironment_registration_CAS_noop_original_history_and_closed_unknown_are_durable()
    {
        await using var h = await Seed(); await GrantTransfer(h); var o = TransferEnvironment(h); var c = RegisterTransfer() with { Reason = "Actual native adapters\nApproved for controlled source consumption" };
        Assert.Equal("absent", (await o.ContextAsync()).Version);
        var r = await o.SaveAsync(c); Assert.True(r.Changed); Assert.Equal("1", r.Reference.Version); Assert.Equal(r, await o.SaveAsync(c));
        var first = await o.HistoryAsync("1"); Assert.Equal(h.Tenant, first.Original.TenantId); Assert.Equal(h.User, first.Original.ActorId); Assert.Equal(c.Reason, first.Original.Reason);
        Assert.Equal(TransferSandbox, first.Original.Deployment.SandboxScopeId); Assert.Equal(9, first.Original.Owners.Length);
        var noop = await o.SaveAsync(RegisterTransfer("1")); Assert.False(noop.Changed); Assert.Equal(r.Reference, noop.Reference);
        var disabled = await o.SaveAsync(RegisterTransfer("1", false)); Assert.False(disabled.Enabled); Assert.Equal("2", disabled.Reference.Version);
        Assert.Equal(first.OriginalJson, (await o.HistoryAsync("1")).OriginalJson); Assert.Equal(r, (await o.OperationAsync(c.OperationId, false)).Result);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.SaveAsync(RegisterTransfer("1")))).Status);
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.SaveAsync(c with { Reason = "Changed intent" }))).Message);
        var id = Guid.NewGuid(); Assert.Equal("UNKNOWN", (await o.OperationAsync(id, false)).State); Assert.Equal("CLOSED", (await o.OperationAsync(id, true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.SaveAsync(RegisterTransfer("2") with { OperationId = id }))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task TransferEnvironment_configuration_is_explicit_and_database_namespace_cannot_be_rebound()
    {
        await using var h = await Seed(); await GrantTransfer(h);
        foreach (var invalid in new[] { new TransferEnvironmentOptionsV1(), new() { Mode = "PRODUCTION", SandboxScopeId = TransferSandbox },
            new() { Mode = "SIMULATION" }, new() { Mode = "SIMULATION", SandboxScopeId = Guid.Empty } })
            Assert.Equal("TRANSFER_DEPLOYMENT_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => TransferEnvironment(h, invalid).ContextAsync())).Message);
        Assert.Equal("TRANSFER_DEPLOYMENT_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => TransferEnvironment(h, mode: "REAL").ContextAsync())).Message);
        var o = TransferEnvironment(h); await o.SaveAsync(RegisterTransfer());
        Assert.Equal("TRANSFER_DATABASE_ENVIRONMENT_MISMATCH", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => TransferEnvironment(h, new() { Mode = "PRODUCTION" }, "REAL").ContextAsync())).Message);
        Assert.Equal("TRANSFER_DATABASE_ENVIRONMENT_MISMATCH", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => TransferEnvironment(h, new() { Mode = "SIMULATION", SandboxScopeId = Guid.NewGuid() }).ContextAsync())).Message);
        await Assert.ThrowsAnyAsync<Exception>(() => TransferEnvironment(h, environment: Guid.NewGuid()).ContextAsync());
        Assert.Equal("1", (await o.ContextAsync()).Version);
    }
    [CurrentPermissionPostgresFact]
    public async Task TransferEnvironment_production_binding_rejects_simulation_original_capture_without_partial_source()
    {
        await using var h = await SeedUnboundTransferDatabase(); await GrantTransfer(h);
        var env = TransferEnvironment(h, new() { Mode = "PRODUCTION" }, "REAL"); var saved = await env.SaveAsync(RegisterTransfer());
        Assert.Null((await env.ContextAsync()).Deployment.SandboxScopeId); Assert.Equal("PRODUCTION", (await env.ContextAsync()).Deployment.Mode);
        await GrantInventoryIdentity(h, false); var simulation = InventoryIdentity(h); var c = await IdentityCreate(simulation, PurposeIdentity());
        await Assert.ThrowsAnyAsync<Exception>(() => simulation.CommandAsync(c)); Assert.Equal("UNKNOWN", (await simulation.OperationAsync(c.OperationId, false)).State);
        Assert.False(await h.Db.Database.SqlQuery<Guid>($"SELECT object_id AS \"Value\" FROM cp6_inventory_identity_v1.heads WHERE tenant_id={h.Tenant}").AnyAsync());
        var actual = new InventoryIdentityOwnerV1(h.Db, InventoryIdentityAuthority(h), ScopeFence(h), new("REAL"), Baselines(h, new()));
        var result = await actual.CommandAsync(c); Assert.Equal("1", result.Version);
        Assert.Equal("PRODUCTION", await h.Db.Database.SqlQuery<string>($"SELECT mode AS \"Value\" FROM cp6_transfer_environment_v1.source_namespaces WHERE tenant_id={h.Tenant} AND object_id={c.Id}").SingleAsync());
        Assert.Equal(saved, (await env.OperationAsync(saved.OperationId, false)).Result);
    }
    [CurrentPermissionPostgresFact]
    public async Task TransferEnvironment_rejects_foreign_context_even_with_a_valid_active_native_scope_lease()
    {
        await using var h = await Seed(); await GrantTransfer(h);
        await using var foreign = await SeedUnboundTransferDatabase(); await RegisterScope(foreign);
        await using var transaction = await foreign.Db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
        var foreignScope = ScopeFence(foreign); _ = await foreignScope.AcquireAsync();
        var mixed = new TransferEnvironmentOwnerV1(h.Db, InventoryIdentityAuthority(h), foreignScope, Options.Create(TransferOptions()), new("DEMO"));
        Assert.Equal("TRANSFER_SCOPE_CONTEXT_MISMATCH", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => mixed.SaveAsync(RegisterTransfer()))).Message);
        Assert.Equal("absent", (await TransferEnvironment(h).ContextAsync()).Version);
        await transaction.RollbackAsync();
    }
    [CurrentPermissionPostgresFact]
    public async Task TransferEnvironment_current_permissions_tenant_and_original_actor_remain_required()
    {
        await using var h = await Seed(); await GrantTransfer(h); var o = TransferEnvironment(h); var c = RegisterTransfer(); var r = await o.SaveAsync(c);
        var reviewer = await GrantInventoryIdentity(h, false);
        Assert.Equal("ORIGINAL_OPERATION_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => TransferEnvironment(reviewer).OperationAsync(c.OperationId, false))).Message);
        await RevokeTransferAction(h, "wms-transfer-environment", "edit"); Assert.Equal(r, await o.SaveAsync(c));
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.SaveAsync(RegisterTransfer("1", false)))).Status);
        await RevokeTransferAction(h, "wms-transfer-environment", "query");
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.OperationAsync(c.OperationId, false))).Status);
        await using var other = await Seed(); await GrantTransfer(other); var different = TransferEnvironment(other);
        Assert.Null((await different.ContextAsync()).Registration); Assert.Equal("UNKNOWN", (await different.OperationAsync(c.OperationId, false)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task TransferEnvironment_lost_ack_and_guard_failures_never_rewrite_the_saved_original()
    {
        var fault = new CommitFault(); await using var h = await Seed(fault); await GrantTransfer(h); var o = TransferEnvironment(h); var c = RegisterTransfer();
        fault.Armed = true; await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.SaveAsync(c));
        var r = (await o.OperationAsync(c.OperationId, false)).Result!; Assert.Equal(r, await o.SaveAsync(c));
        foreach (var sql in new[] { "UPDATE cp6_transfer_environment_v1.binding SET mode=mode", "DELETE FROM cp6_transfer_environment_v1.tenants", "TRUNCATE cp6_transfer_environment_v1.versions CASCADE", "UPDATE cp6_transfer_environment_v1.tenants SET revision=revision+2", "UPDATE cp6_transfer_environment_v1.source_namespaces SET digest=digest" })
            await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlRawAsync(sql));
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_uom_profile_v1.versions DISABLE TRIGGER transfer_source_namespace");
        try { Assert.Equal("TRANSFER_ENVIRONMENT_STORE_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.ContextAsync())).Message); }
        finally { await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_uom_profile_v1.versions ENABLE TRIGGER transfer_source_namespace"); }
        Assert.Equal(r, (await o.OperationAsync(c.OperationId, false)).Result);
    }
    [CurrentPermissionPostgresFact]
    public async Task TransferEnvironment_final_revocation_rolls_back_namespace_registration_and_operation_together()
    {
        var hook = new StartWriteHook(); await using var h = await SeedUnboundTransferDatabase(hook); await GrantTransfer(h); var o = TransferEnvironment(h); var c = RegisterTransfer(); var injected = false;
        hook.Before = async command =>
        {
            if (!command.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations")) return;
            hook.Before = null; injected = true;
            await using var revoke = new NpgsqlCommand("DELETE FROM public.\"Sys_RoleAction\" WHERE \"MenuId\" IN (SELECT \"MenuId\" FROM public.\"Sys_Menus\" WHERE \"MenuKey\"='wms-transfer-environment') AND \"ActionCode\"='edit'", (NpgsqlConnection)command.Connection!, (NpgsqlTransaction)command.Transaction!);
            await revoke.ExecuteNonQueryAsync();
        };
        await Assert.ThrowsAnyAsync<Exception>(() => o.SaveAsync(c)); Assert.True(injected); Assert.Equal("absent", (await o.ContextAsync()).Version);
        Assert.Equal("UNKNOWN", (await o.OperationAsync(c.OperationId, false)).State);
        Assert.False(await h.Db.Database.SqlQueryRaw<bool>("SELECT singleton AS \"Value\" FROM cp6_transfer_environment_v1.binding").AnyAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task TransferEnvironment_concurrent_CAS_and_close_have_one_authoritative_outcome()
    {
        await using var h = await Seed(); await GrantTransfer(h); var o = TransferEnvironment(h); await o.SaveAsync(RegisterTransfer());
        await using var db = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); var peer = TransferEnvironment(h with { Db = db });
        var a = RegisterTransfer("1", false); var b = RegisterTransfer("1", false);
        async Task<bool> Save(TransferEnvironmentOwnerV1 service, TransferEnvironmentCommandV1 command)
        { try { await service.SaveAsync(command); return true; } catch (OrderPriceCorrectionException e) { Assert.True(e.Status is 409 or 412); return false; } }
        Assert.Single((await Task.WhenAll(Save(o, a), Save(peer, b))).Where(x => x));
        var c = RegisterTransfer("2"); await Task.WhenAll(Save(o, c), peer.OperationAsync(c.OperationId, true));
        var result = await o.OperationAsync(c.OperationId, false); Assert.True(result.State is "COMMITTED" or "CLOSED");
        Assert.Equal(result.State == "COMMITTED" ? "3" : "2", (await o.ContextAsync()).Version);
    }
    [CurrentPermissionPostgresFact]
    public async Task TransferSource_adopts_actual_native_product_in_same_transaction_and_disable_blocks_consumption()
    {
        await using var h = await Seed(); await GrantTransfer(h); var env = TransferEnvironment(h); var registration = await env.SaveAsync(RegisterTransfer());
        var f = await InventoryProductSetup(h, ratio: true, registerScope: false); var approved = await ApprovedIdentity(h, f.Reviewer, f.Definition);
        var source = TransferSources(h); var result = await source.ReadAsync(approved.Reference);
        Assert.Equal(approved.Reference, result.CatalogueRef); Assert.Equal(f.Definition.Product!.Product, result.Product.ProductRef);
        Assert.Equal(registration.Reference, result.RegistrationRef); Assert.Equal(ScopeEnvironment, result.Environment.EnvironmentId);
        Assert.Equal(TransferSandbox, result.Environment.SandboxScopeId); Assert.Equal("SIMULATION", result.Environment.Mode);
        Assert.Equal(f.Unit.Reference.Id.ToString(), result.Product.PrecisionPolicyRef.Id);
        Assert.Equal("REQUIRES_FINAL_OWNER_GATES", result.Qualification);
        await env.SaveAsync(RegisterTransfer("1", false));
        Assert.Equal("TRANSFER_TENANT_REGISTRATION_DISABLED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => source.ReadAsync(approved.Reference))).Message);
        await env.SaveAsync(RegisterTransfer("2")); Assert.Equal("3", (await source.ReadAsync(approved.Reference)).Environment.RegistryRevision);
        await Units(h).CommandAsync(Revise(f.Unit, f.Unit.Profile with { Enabled = false }));
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => source.ReadAsync(approved.Reference))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task TransferSource_old_originals_are_not_backfilled_or_relabelled_by_registration()
    {
        await using var h = await SeedUnboundTransferDatabase(); var f = await InventoryProductSetup(h); var approved = await ApprovedIdentity(h, f.Reviewer, f.Definition);
        await GrantTransfer(h, false); var source = TransferSources(h);
        Assert.Equal("TRANSFER_TENANT_REGISTRATION_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => source.ReadAsync(approved.Reference))).Message);
        await TransferEnvironment(h).SaveAsync(RegisterTransfer());
        Assert.Equal("TRANSFER_SOURCE_NAMESPACE_UNPROVEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => source.ReadAsync(approved.Reference))).Message);
        Assert.Equal(approved.OriginalJson, (await InventoryIdentity(h).ReadAsync(approved.Reference.Id)).OriginalJson);
        Assert.Equal(0, await h.Db.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM cp6_transfer_environment_v1.source_namespaces").SingleAsync());
        await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_transfer_environment_v1.source_namespaces(tenant_id,owner,object_id,revision,digest,environment_id,mode,sandbox_scope_id)
            VALUES({h.Tenant},{approved.Reference.Owner},{approved.Reference.Id},{approved.Reference.Version},{approved.Reference.Digest},{ScopeEnvironment},'SIMULATION',{TransferSandbox})
            """));
    }
    private sealed class TransferReadRevocation : DbCommandInterceptor
    {
        internal bool Armed;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (Armed && command.CommandText.Contains("FROM cp6_transfer_environment_v1.source_namespaces"))
            {
                Armed = false;
                await using var revoke = new NpgsqlCommand("DELETE FROM public.\"Sys_RoleAction\" WHERE \"MenuId\" IN (SELECT \"MenuId\" FROM public.\"Sys_Menus\" WHERE \"MenuKey\"='wms-inventory-identity') AND \"ActionCode\"='query'", (NpgsqlConnection)command.Connection!, (NpgsqlTransaction)command.Transaction!);
                await revoke.ExecuteNonQueryAsync(ct);
            }
            return result;
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task TransferSource_final_native_permission_revocation_rejects_result_and_rolls_back_the_injected_change()
    {
        var hook = new TransferReadRevocation(); await using var h = await Seed(hook); await GrantTransfer(h); await TransferEnvironment(h).SaveAsync(RegisterTransfer());
        var f = await InventoryProductSetup(h, registerScope: false); var approved = await ApprovedIdentity(h, f.Reviewer, f.Definition); var o = TransferSources(h);
        hook.Armed = true; await Assert.ThrowsAnyAsync<Exception>(() => o.ReadAsync(approved.Reference)); Assert.False(hook.Armed);
        Assert.Equal(approved.Reference, (await o.ReadAsync(approved.Reference)).CatalogueRef);
        await RevokeTransferAction(h, "wms-inventory-identity", "query");
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.ReadAsync(approved.Reference))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task TransferEnvironment_actual_HTTP_authentication_CSRF_closed_contract_and_current_source_consumption()
    {
        await using var h = await Seed(); await GrantTransfer(h); var settings = await DocumentHostSettings(new());
        settings["QualitySpecification__EvidenceMode"] = "DEMO"; settings["TransferEnvironment__Mode"] = "SIMULATION"; settings["TransferEnvironment__SandboxScopeId"] = TransferSandbox.ToString();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, settings); var cookies = await Login(host, h);
        const string root = "/api/wms/transfer/environment-v1"; var c = RegisterTransfer();
        async Task<HttpResponseMessage> Post(string path, object body, bool csrf = true)
        { using var req = OperationsRequest(cookies, HttpMethod.Post, path, body); if (!csrf) req.Headers.Remove("X-CSRF-Token"); return await host.Send(req); }
        using (var response = await host.Send(new HttpRequestMessage(HttpMethod.Get, root))) Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using (var response = await Post(root + "/commands", c, false)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var forged = JsonSerializer.SerializeToNode(c, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject(); forged["environmentId"] = Guid.NewGuid();
        using (var response = await Post(root + "/commands", forged)) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        forged.Remove("environmentId"); forged.Remove("enabled");
        using (var response = await Post(root + "/commands", forged)) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        TransferEnvironmentResultV1 registered;
        using (var response = await Post(root + "/commands", c)) { Assert.True(response.Headers.CacheControl?.NoStore); registered = await DocumentHttpData<TransferEnvironmentResultV1>(response); }
        using (var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, root + "/operations/" + c.OperationId)))
            Assert.Equal(registered, (await DocumentHttpData<TransferEnvironmentOperationV1>(response)).Result);
        var f = await InventoryProductSetup(h, registerScope: false); var approved = await ApprovedIdentity(h, f.Reviewer, f.Definition);
        using (var response = await Post("/api/wms/transfer/sources-v1/product", approved.Reference))
        { Assert.True(response.Headers.CacheControl?.NoStore); Assert.Equal(approved.Reference, (await DocumentHttpData<TransferProductSourceV1>(response)).CatalogueRef); }
        await RevokeTransferAction(h, "wms-transfer-source", "query");
        using (var response = await Post("/api/wms/transfer/sources-v1/product", approved.Reference)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
