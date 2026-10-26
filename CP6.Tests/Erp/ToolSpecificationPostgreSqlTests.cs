using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static OperationsOwnerV1 SpecificationAuthority(Harness h, CP6Context? db = null)
    {
        var catalog = new AuthorizationCatalogStoreV1();
        foreach (var key in new[] { "plm-tool-specification", "plm-engineering-product", "erp-plate-mold" })
        { catalog.RegisterScope(key, key, [1, 2, 3, 4, 5], 5); catalog.RegisterFields(key, []); }
        catalog.Seal(); return new(db ?? h.Db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), catalog.Capture);
    }
    private static ToolSpecificationOwnerV1 Specifications(Harness h, CP6Context? db = null) => new(db ?? h.Db, SpecificationAuthority(h, db));
    private static async Task GrantSpecification(Harness h)
    {
        if (!await h.Db.Sys_RoleDataScopes.AnyAsync(x => x.ResourceKey == "erp-plate-mold")) await GrantPlateSource(h);
        if (!await h.Db.Sys_RoleDataScopes.AnyAsync(x => x.ResourceKey == "plm-engineering-product")) await GrantEngineering(h);
        foreach (var action in new[] { "query", "add", "edit" }) await EngineeringAction(h, "plm-tool-specification", action);
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = "plm-tool-specification", ScopeType = 5 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
    }
    private static async Task<ToolSpecificationCommandV1> ToolDraft(Harness h, Guid id)
    {
        var current = await Specifications(h).ReadAsync(id); var product = await MakeEngineering(h);
        return new(Guid.NewGuid(), "CREATE", current.Source.Reference, current.Source.CurrentNativeRowVersion, current.CurrentControl,
            new(product.Reference, "Product A exact dimensions", "Cut profile A; tolerance 0.0001mm", "Configuration A only",
                "Use original technical dimensions; do not round", "Drawing and validation originals pending controlled DOC"), "Initial engineering draft");
    }
    private static ToolSpecificationCommandV1 NextDraft(ToolSpecificationCommandV1 previous, ToolSpecificationViewV1 current)
        => previous with { OperationId = Guid.NewGuid(), Action = "REVISE", Source = current.Source.Reference,
            ExpectedNativeRowVersion = current.Source.CurrentNativeRowVersion, ExpectedTechnicalControl = current.CurrentControl, Reason = "Revised engineering requirements" };
    private static Task<int> SpecificationCount(Harness h) => h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_tool_specification_v1.versions WHERE tenant_id={h.Tenant}").SingleAsync();
    [CurrentPermissionPostgresFact]
    public async Task Tool_specification_HTTP_requires_professional_owner_not_legacy_edit()
    {
        await using var h = await Seed(); await GrantLegacyTool(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "/api/tool-specifications/owner-v1/scope"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_specification_original_draft_preserves_source_precision_and_distinct_control_without_approval()
    {
        await using var h = await Seed(); await GrantSpecification(h); var id = await SeedPlate(h); var owner = Specifications(h); var input = await ToolDraft(h, id);
        var saved = await owner.CommandAsync(input); var view = await owner.ReadAsync(id); var draft = view.Specification!;
        Assert.Equal(saved.Digest, draft.Reference.Digest); Assert.Equal(input.Source, draft.Source); Assert.Equal(input.Requirements, draft.Requirements);
        Assert.Equal("DRAFT", draft.State); Assert.Equal("NOT_REQUESTED", draft.Approval); Assert.Equal("NOT_REQUESTED", draft.Freeze); Assert.Equal("UNKNOWN", draft.PhysicalQualification);
        Assert.True(view.SourceMatches); Assert.True(view.ProductMatches); Assert.Equal(input.ExpectedNativeRowVersion, view.Source.CurrentNativeRowVersion);
        Assert.Equal("99999999999.9999", view.Source.Source.GetProperty("technical").GetProperty("sheetWidth").GetString());
        Assert.Equal("TECHNICAL_SPECIFICATION_DRAFT", view.CurrentControl.Knowledge); Assert.Equal("Main.Plm.ToolSpecification.v1", view.CurrentControl.Owner);
        Assert.Equal(saved, await owner.CommandAsync(input)); Assert.Equal(1, await SpecificationCount(h)); Assert.Equal(1, await h.Db.Orders.CountAsync());
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(input with { Reason = "Different" }))).Message);
        Assert.Equal(404, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ReadAsync(id, "2"))).Status);
        await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_tool_specification_v1.versions WHERE tenant_id={h.Tenant}"));
        await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_tool_specification_v1.heads SET revision=revision+2 WHERE tenant_id={h.Tenant}"));
        await using var foreign = await Seed(); await GrantSpecification(foreign);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => Specifications(foreign).ReadAsync(id, "1"))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_specification_revision_invalidates_legacy_control_even_with_same_native_and_old_writes_never_overwrite_draft()
    {
        await using var h = await Seed(); var (legacy, service) = await GrantLegacyTool(h); await GrantSpecification(h);
        var native = await legacy.ExecuteAsync("CREATE", null, 1, await NewLegacyTool(legacy));
        var before = Command((await service.GetByNoAsync(native.Number, 1))!); var owner = Specifications(h); var input = await ToolDraft(h, native.ToolId); await owner.CommandAsync(input);
        Assert.Equal("TECHNICAL_CONTROL_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => legacy.ExecuteAsync("UPDATE", native.Number, 1, before))).Message);
        var stale = Command((await service.GetByNoAsync(native.Number, 1))!); var first = await owner.ReadAsync(native.ToolId);
        await owner.CommandAsync(NextDraft(input, first)); var second = await owner.ReadAsync(native.ToolId);
        Assert.Equal(first.Source.CurrentNativeRowVersion, second.Source.CurrentNativeRowVersion); Assert.Equal("2", second.Specification!.Reference.Version);
        Assert.Equal("TECHNICAL_CONTROL_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => legacy.ExecuteAsync("UPDATE", native.Number, 1, stale))).Message);
        var fresh = Command((await service.GetByNoAsync(native.Number, 1))!); fresh.SheetWidth = 12.3456m;
        var written = await legacy.ExecuteAsync("UPDATE", native.Number, 1, fresh);
        Assert.Equal(second.CurrentControl, written.TechnicalControl); Assert.Equal("REQUESTED", written.CommercialState);
        var changed = await owner.ReadAsync(native.ToolId); Assert.False(changed.SourceMatches); Assert.Equal(second.Specification.OriginalJson, changed.Specification!.OriginalJson);
        Assert.Equal(first.Specification!.OriginalJson, (await owner.ReadAsync(native.ToolId, "1")).Specification!.OriginalJson);
        await owner.CommandAsync(NextDraft(input, changed)); Assert.True((await owner.ReadAsync(native.ToolId)).SourceMatches);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_specification_source_native_product_and_due_requirements_are_explicit_no_latest_substitution()
    {
        await using var h = await Seed(); await GrantSpecification(h); var id = await SeedPlate(h); var owner = Specifications(h); var input = await ToolDraft(h, id);
        await h.Db.PlateMolds.Where(p => p.Id == id).ExecuteUpdateAsync(u => u.SetProperty(p => p.DecisionAmount, 1m));
        Assert.Equal("TOOL_NATIVE_VERSION_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(input))).Message);
        input = input with { ExpectedNativeRowVersion = (await owner.ReadAsync(id)).Source.CurrentNativeRowVersion };
        await h.Db.PlateMolds.Where(p => p.Id == id).ExecuteUpdateAsync(u => u.SetProperty(p => p.SheetWidth, 1.2345m));
        Assert.Equal("TOOL_SOURCE_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(input))).Message);
        var view = await owner.ReadAsync(id); input = input with { Source = view.Source.Reference, ExpectedNativeRowVersion = view.Source.CurrentNativeRowVersion, ExpectedTechnicalControl = view.CurrentControl };
        var product = await Engineering(h).ReadAsync(input.Requirements.ProductRef.Id, null);
        await Engineering(h).CommandAsync(ReviseEngineering(product, product.Product with { Definition = "Changed requirement" }));
        Assert.Equal("ENGINEERING_PRODUCT_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(input))).Message);
        input = input with { Requirements = input.Requirements with { ProductRef = (await Engineering(h).ReadAsync(product.Reference.Id, null)).Reference } };
        Assert.Equal(400, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(input with { Requirements = input.Requirements with { ProcessRequirements = "" } }))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(input with { Action = "APPROVE" }))).Status);
        Assert.Equal(0, await SpecificationCount(h)); await owner.CommandAsync(input); Assert.Equal(1, await SpecificationCount(h));
        product = await Engineering(h).ReadAsync(product.Reference.Id, null);
        await Engineering(h).CommandAsync(ReviseEngineering(product, product.Product with { Enabled = false }));
        Assert.False((await owner.ReadAsync(id)).ProductMatches);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_specification_lost_ack_replays_original_after_write_revocation_and_close_prevents_late_write()
    {
        var fault = new CommitFault(); await using var h = await Seed(fault); await GrantSpecification(h); var id = await SeedPlate(h); var owner = Specifications(h); var input = await ToolDraft(h, id);
        fault.Armed = true; Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(input))).Message);
        var original = (await owner.OperationAsync(input.OperationId, false)).Result; Assert.NotNull(original);
        var menu = await h.Db.Sys_Menus.Where(m => m.MenuKey == "plm-tool-specification").Select(m => m.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == menu && a.ActionCode != "query").ExecuteDeleteAsync();
        Assert.Equal(original, await owner.CommandAsync(input)); Assert.Equal(1, await SpecificationCount(h));
        var unknown = Guid.NewGuid(); Assert.Equal("UNKNOWN", (await owner.OperationAsync(unknown, false)).State); Assert.Equal("CLOSED", (await owner.OperationAsync(unknown, true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(input with { OperationId = unknown }))).Message);
        var productMenu = await h.Db.Sys_Menus.Where(m => m.MenuKey == "plm-engineering-product").Select(m => m.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == productMenu && a.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.OperationAsync(input.OperationId, false))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_specification_final_authority_failure_rolls_back_head_original_and_journal()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); await GrantSpecification(h); var id = await SeedPlate(h); var owner = Specifications(h); var input = await ToolDraft(h, id); var reached = false;
        hook.Before = async c => { if (!c.CommandText.Contains("WITH field_subject")) return;
            await using var probe = c.Connection!.CreateCommand(); probe.Transaction = c.Transaction; probe.CommandText = $"SELECT count(*) FROM cp6_tool_specification_v1.versions WHERE tenant_id='{h.Tenant}'";
            if (Convert.ToInt32(await probe.ExecuteScalarAsync()) > 0) { reached = true; throw new IOException("final authority unavailable"); } };
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(input))).Status); hook.Before = null;
        Assert.True(reached); Assert.Equal(0, await SpecificationCount(h)); Assert.Null((await owner.ReadAsync(id)).Specification); Assert.Equal("UNKNOWN", (await owner.OperationAsync(input.OperationId, false)).State);
        Assert.Equal(input.ExpectedNativeRowVersion, (await owner.ReadAsync(id)).Source.CurrentNativeRowVersion);
    }
    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Tool_specification_professional_department_scope_is_independent_and_revoked_mapping_hides_history(int scope)
    {
        await using var h = await Seed(); await GrantSpecification(h); var id = await SeedPlate(h); var owner = Specifications(h); var input = await ToolDraft(h, id); await owner.CommandAsync(input);
        var admin = await MappingAdmin(h); await StandaloneScope(h, admin, scope);
        Assert.Empty(await owner.ListAsync()); await BindStandalone(h, admin, "plate-mold", id);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ListAsync())).Status);
        await BindStandalone(h, admin, "plm-engineering-product", input.Requirements.ProductRef.Id);
        Assert.Equal(id, Assert.Single(await owner.ListAsync()).Source.Reference.Id);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE tenant_id={h.Tenant} AND object_kind='plate-mold' AND object_id={id}");
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ReadAsync(id, "1"))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.OperationAsync(input.OperationId, false))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_specification_creator_scope_and_original_owner_query_both_constrain_read_and_write()
    {
        await using var h = await Seed(); await GrantSpecification(h); var id = await SeedPlate(h); var hidden = await SeedPlate(h, "B"); var owner = Specifications(h); var input = await ToolDraft(h, id);
        await h.Db.PlateMolds.Where(p => p.Id == hidden).ExecuteUpdateAsync(u => u.SetProperty(p => p.Creator, "another"));
        await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == "plm-tool-specification").ExecuteUpdateAsync(u => u.SetProperty(x => x.ScopeType, 1));
        Assert.Equal(id, Assert.Single(await owner.ListAsync()).Source.Reference.Id);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ReadAsync(hidden))).Status);
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == 215 && a.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(input))).Status); Assert.Equal(0, await SpecificationCount(h));
        await using var peer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => new ToolSpecificationOwnerV1(peer, SpecificationAuthority(h)).ScopeAsync())).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_specification_concurrent_creates_and_revisions_accept_one_exact_predecessor()
    {
        await using var h = await Seed(); await GrantSpecification(h); var id = await SeedPlate(h); var owner = Specifications(h); var input = await ToolDraft(h, id);
        await using var peer = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); var other = Specifications(h, peer);
        async Task<bool> Apply(ToolSpecificationOwnerV1 target, ToolSpecificationCommandV1 command)
        { try { await target.CommandAsync(command); return true; } catch (OrderPriceCorrectionException e) { Assert.Equal(412, e.Status); return false; } }
        Assert.Single(await Task.WhenAll(Apply(owner, input), Apply(other, input with { OperationId = Guid.NewGuid() })), x => x);
        var next = NextDraft(input, await owner.ReadAsync(id));
        Assert.Single(await Task.WhenAll(Apply(owner, next), Apply(other, next with { OperationId = Guid.NewGuid() })), x => x);
        Assert.Equal(2, await SpecificationCount(h)); Assert.Equal("2", (await owner.ReadAsync(id)).Specification!.Reference.Version);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_specification_actual_HTTP_commands_history_csrf_recovery_and_current_revocation()
    {
        await using var h = await Seed(); await GrantSpecification(h); var id = await SeedPlate(h); var input = await ToolDraft(h, id);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        async Task<HttpResponseMessage> Send(ToolSpecificationCommandV1 command, bool csrf = true)
        {
            var request = OperationsRequest(cookies, HttpMethod.Post, "/api/tool-specifications/owner-v1/commands"); request.Content = JsonContent.Create(command);
            if (!csrf) request.Headers.Remove("X-CSRF-TOKEN"); return await host.Send(request);
        }
        using var denied = await Send(input, false); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); Assert.Equal(0, await SpecificationCount(h));
        using var saved = await Send(input); Assert.Equal(HttpStatusCode.OK, saved.StatusCode); Assert.True(saved.Headers.CacheControl?.NoStore);
        using var history = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"/api/tool-specifications/owner-v1/{id}?version=1")); Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        using var body = JsonDocument.Parse(await history.Content.ReadAsStringAsync()); Assert.Equal("DRAFT", body.RootElement.GetProperty("data").GetProperty("specification").GetProperty("state").GetString());
        using var recovered = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"/api/tool-specifications/owner-v1/operations/{input.OperationId}")); Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == 215 && a.ActionCode == "query").ExecuteDeleteAsync();
        using var revoked = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"/api/tool-specifications/owner-v1/{id}?version=1")); Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
    }
}
