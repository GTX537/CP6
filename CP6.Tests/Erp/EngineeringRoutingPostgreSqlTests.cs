using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static OperationsOwnerV1 RoutingAuthority(Harness h, CP6Context? context = null)
    {
        var catalog = new AuthorizationCatalogStoreV1();
        foreach (var key in new[] { "plm-engineering-routing", "plm-engineering-revision", "plm-engineering-product", "plm-tool-specification",
            "erp-plate-mold", "uom-profile", "mes-machine", "mes-work-center", "product", "space-site" })
        { catalog.RegisterScope(key, key, key == "space-site" ? [5] : [1, 2, 3, 4, 5], 5); catalog.RegisterFields(key, []); }
        catalog.Seal(); return new(context ?? h.Db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), catalog.Capture);
    }
    private static EngineeringRoutingOwnerV1 Routings(Harness h, CP6Context? context = null) => new(context ?? h.Db, RoutingAuthority(h, context));
    private static EngineeringRevisionOwnerV1 RoutingRevisions(Harness h) => new(h.Db, RoutingAuthority(h));
    private static async Task<EngineeringRevisionSnapshotV1> RoutingParent(Harness h)
    {
        await GrantEngineering(h); await GrantEngineeringRevision(h); await RevisionRule(h);
        foreach (var action in new[] { "query", "add", "edit" }) await EngineeringAction(h, "plm-engineering-routing", action);
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = "plm-engineering-routing", ScopeType = 5 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        return await MakeRevision(RoutingRevisions(h), (await MakeEngineering(h)).Reference);
    }
    private static EngineeringRoutingCommandV1 RoutingCreate(EngineeringRevisionSnapshotV1 parent)
        => new(Guid.NewGuid(), Guid.NewGuid(), "CREATE", null, null,
            new("Native routing", parent.Reference, parent.Draft.ProductRef!, null, "Manufacturing view pending",
                [new(Guid.NewGuid(), "CUT", "First cutting", 10, [], null, "APPLIES", "", "Cut to exact dimensions", [], [], [], "Controlled drawing pending")]), "Initial routing definition");
    private static EngineeringRoutingCommandV1 RoutingRevise(EngineeringRoutingSnapshotV1 value)
        => new(Guid.NewGuid(), value.Reference.Id, "REVISE", value.Reference.Version, value.Etag, value.Body, "Explicit routing revision");

    [CurrentPermissionPostgresFact]
    public async Task EngineeringRouting_original_history_same_process_occurrences_and_version_CAS()
    {
        await using var h = await Seed(); var parent = await RoutingParent(h); var owner = Routings(h); var input = RoutingCreate(parent);
        var first = input.Body.Operations[0]; var second = first with { OccurrenceId = Guid.NewGuid(), SemanticOrder = 20, DependsOn = [first.OccurrenceId], Name = "Second cutting" };
        input = input with { Body = input.Body with { Operations = [first, second] } };
        var result = await owner.CommandAsync(input); Assert.Equal(result, await owner.CommandAsync(input));
        var saved = await owner.ReadAsync(input.RoutingId); Assert.Equal(2, saved.Body.Operations.Length); Assert.Equal("DRAFT_ONLY", saved.Readiness);
        var next = RoutingRevise(saved) with { Body = saved.Body with { Operations = [second with { Instructions = "Changed second occurrence" }, first] } };
        await owner.CommandAsync(next); var current = await owner.ReadAsync(input.RoutingId); Assert.Equal("2", current.Reference.Version);
        Assert.Equal(saved.OriginalJson, (await owner.ReadAsync(input.RoutingId, "1")).OriginalJson);
        var diff = await owner.DiffAsync(input.RoutingId, "1", "2"); Assert.Equal("operations/" + second.OccurrenceId, Assert.Single(diff.Changes).Path);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(next with { OperationId = Guid.NewGuid() }))).Status);
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(input with { Reason = "Different request" }))).Message);
        foreach (var sql in new[] { "DELETE FROM cp6_engineering_routing_v1.versions", "UPDATE cp6_engineering_routing_v1.versions SET actor_id=gen_random_uuid()", "TRUNCATE cp6_engineering_routing_v1.versions CASCADE", "DELETE FROM cp6_engineering_routing_v1.heads" })
            await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlRawAsync(sql));
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringRouting_dependency_cycles_exact_numbers_and_unknown_units_remain_unqualified()
    {
        await using var h = await Seed(); var parent = await RoutingParent(h); var owner = Routings(h); var input = RoutingCreate(parent);
        var a = input.Body.Operations[0]; var b = a with { OccurrenceId = Guid.NewGuid(), SemanticOrder = 20, DependsOn = [a.OccurrenceId] };
        var exact = new string('9', 1024) + "." + new string('1', 1024);
        a = a with { DependsOn = [b.OccurrenceId], Parameters = [new("Exact", exact, null, null, null, ""), new("Range", "5", "7", "3", null, "")] };
        input = input with { Body = input.Body with { Operations = [a, b] } }; await owner.CommandAsync(input);
        var saved = await owner.ReadAsync(input.RoutingId); Assert.Equal(exact, saved.Body.Operations[0].Parameters[0].Value);
        Assert.Contains("DEPENDENCY_CYCLE_OR_UNRESOLVED", saved.Missing!); Assert.Contains(saved.Missing!, x => x.StartsWith("PARAMETER_LIMIT_CONFLICT:"));
        Assert.Contains(saved.Missing!, x => x.StartsWith("PARAMETER_UNIT_REQUIRED:")); Assert.Equal("DRAFT", saved.State);
        foreach (var bad in new[] { "-0.00", "01", "1e3", "NaN", new string('1', 1025) })
            Assert.Equal(400, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(RoutingRevise(saved) with
                { Body = saved.Body with { Operations = [a with { Parameters = [new("Invalid", bad, null, null, null, "")] }] } }))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(RoutingRevise(saved) with { Body = saved.Body with { Operations = [a, a] } }))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringRouting_composition_uses_exact_route_without_parent_recursion_and_preserves_old_binding()
    {
        await using var h = await Seed(); var parent = await RoutingParent(h); var owner = Routings(h); var revisions = RoutingRevisions(h);
        var input = RoutingCreate(parent); await owner.CommandAsync(input); var route = await owner.ReadAsync(input.RoutingId);
        var component = new EngineeringComponentDraftV1(Guid.NewGuid(), "ROUTING", "manufacture/primary", 10, "APPLIES", "", "Explicit route", route.Reference);
        await revisions.CommandAsync(PatchRevision(parent, new EngineeringDraftPatchV1 { Op = "UPSERT_COMPONENT", Component = component }));
        var bound = await revisions.ReadAsync(parent.Reference.Id, null); Assert.Equal(route.Reference, Assert.Single(bound.Draft.Components).SourceRef);
        Assert.Equal(route.OriginalJson, (await owner.ReadAsync(route.Reference.Id)).OriginalJson);
        await owner.CommandAsync(RoutingRevise(route) with { Body = route.Body with { RevisionOrigin = bound.Reference, Name = "Second route" } });
        var oldBinding = await revisions.ReadAsync(parent.Reference.Id, null); Assert.Contains("SOURCE_CHANGED:" + component.OccurrenceId, oldBinding.Missing!);
        Assert.Equal(bound.OriginalJson, oldBinding.OriginalJson);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => revisions.CommandAsync(PatchRevision(oldBinding,
            new EngineeringDraftPatchV1 { Op = "UPSERT_COMPONENT", Component = component })))).Status);
        var other = await MakeRevision(revisions, parent.Draft.ProductRef);
        var latest = await owner.ReadAsync(input.RoutingId);
        Assert.Equal("ROUTING_REVISION_MISMATCH", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => revisions.CommandAsync(PatchRevision(other,
            new EngineeringDraftPatchV1 { Op = "UPSERT_COMPONENT", Component = component with { SourceRef = latest.Reference } })))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringRouting_interrupted_commit_close_and_current_read_only_recovery()
    {
        var failure = new InterruptCurrencyCommit(); await using var h = await Seed(failure); var parent = await RoutingParent(h);
        var owner = Routings(h); var input = RoutingCreate(parent); failure.Interrupt = true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(input))).Message);
        Assert.Empty(await owner.ListAsync()); Assert.Equal("UNKNOWN", (await owner.OperationAsync(input.OperationId, false)).State);
        Assert.Equal("CLOSED", (await owner.OperationAsync(input.OperationId, true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(input))).Message);
        input = RoutingCreate(parent); var result = await owner.CommandAsync(input);
        var menu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "plm-engineering-routing").Select(x => x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == menu && x.ActionCode != "query").ExecuteDeleteAsync();
        Assert.Equal(result, await owner.CommandAsync(input)); Assert.Equal(result, (await owner.OperationAsync(input.OperationId, false)).Result);
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == menu).ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.OperationAsync(input.OperationId, false))).Status);
    }
    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task EngineeringRouting_department_scope_inherits_exact_parent_and_revoked_mapping_hides_history(int type)
    {
        await using var h = await Seed(); var parent = await RoutingParent(h); var owner = Routings(h); var admin = await MappingAdmin(h);
        await BindStandalone(h, admin, "plm-engineering-product", parent.Draft.ProductRef!.Id); await StandaloneScope(h, admin, type);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(RoutingCreate(parent)))).Status);
        await BindStandalone(h, admin, "plm-engineering-revision", parent.Reference.Id);
        var command = RoutingCreate(parent); await owner.CommandAsync(command); Assert.Single(await owner.ListAsync());
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE tenant_id={h.Tenant} AND object_kind='plm-engineering-revision' AND object_id={parent.Reference.Id}");
        Assert.Empty(await owner.ListAsync()); Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.OperationAsync(command.OperationId, false))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringRouting_two_connections_choose_one_version_and_other_tenant_cannot_read()
    {
        await using var h = await Seed(); var parent = await RoutingParent(h); var owner = Routings(h); var input = RoutingCreate(parent);
        await owner.CommandAsync(input); var saved = await owner.ReadAsync(input.RoutingId);
        await using var peer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        async Task<string> Save(EngineeringRoutingOwnerV1 writer)
        { try { await writer.CommandAsync(RoutingRevise(saved)); return "COMMITTED"; } catch (OrderPriceCorrectionException error) { Assert.Equal(412, error.Status); return error.Message; } }
        var outcomes = await Task.WhenAll(Save(owner), Save(Routings(h, peer))); Assert.Single(outcomes, x => x == "COMMITTED");
        Assert.Single(outcomes, x => x == "ROUTING_VERSION_CHANGED");
        await using var foreign = await Seed(); await RoutingParent(foreign);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => Routings(foreign).ReadAsync(input.RoutingId))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringRouting_resource_unit_versions_and_revocation_are_real_source_dependencies()
    {
        await using var h = await Seed(); var parent = await RoutingParent(h); var owner = Routings(h);
        var machineAuthority = await GrantMachine(h); await SeedMachine(h); var resources = new ResourceSourceOwnerV1(h.Db, machineAuthority);
        var machine = Assert.Single(await resources.ListAsync("MACHINE")); await GrantUomProfile(h); var unit = await MakeUnit(Units(h));
        var input = RoutingCreate(parent); input = input with { Body = input.Body with { Operations = [input.Body.Operations[0] with
            { Resources = [machine.Reference], Parameters = [new("Count", "12345678901234567890.123400", null, null, unit.Reference, "Exact original quantity")] }] } };
        await owner.CommandAsync(input); var saved = await owner.ReadAsync(input.RoutingId);
        await resources.SaveAsync("MACHINE", new(Guid.NewGuid(), machine.Reference, [new("name", "Changed actual resource")]));
        Assert.Contains((await owner.ReadAsync(input.RoutingId)).Missing!, x => x.StartsWith("RESOURCE_SOURCE_CHANGED:"));
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(RoutingRevise(saved)))).Status);
        var menu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "mes-machine").Select(x => x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == menu && x.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.OperationAsync(input.OperationId, false))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringRouting_HTTP_enforces_entry_replay_closed_input_and_current_source_access()
    {
        await using var h = await Seed(); var parent = await RoutingParent(h); var input = RoutingCreate(parent);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        async Task<HttpResponseMessage> Save(object body)
        { var request = OperationsRequest(cookies, HttpMethod.Post, "/api/engineering-routing/owner-v1/commands"); request.Content = JsonContent.Create(body); return await host.Send(request); }
        using var first = await Save(input); Assert.Equal(HttpStatusCode.OK, first.StatusCode); Assert.True(first.Headers.CacheControl?.NoStore);
        using var replay = await Save(input); Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        var invalid = JsonSerializer.SerializeToNode(input, new JsonSerializerOptions(JsonSerializerDefaults.Web))!; invalid["approved"] = true;
        using var extra = await Save(invalid); Assert.Equal(HttpStatusCode.BadRequest, extra.StatusCode);
        var menu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "plm-engineering-revision").Select(x => x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == menu && x.ActionCode == "query").ExecuteDeleteAsync();
        using var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"/api/engineering-routing/owner-v1/{input.RoutingId}")); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringRouting_legacy_preview_uses_actual_rows_keeps_decimal_originals_and_rejects_changed_import()
    {
        await using var h = await Seed(); var parent = await RoutingParent(h); var owner = Routings(h);
        await GrantProductIdentity(h); await EngineeringAction(h, "space-site", "query"); await EngineeringAction(h, "erp-product", "confirm-engineering");
        var product = await Engineering(h).ReadAsync(parent.Draft.ProductRef!.Id, null);
        var candidate = await MappingCandidate(h, product, "ROU"); var draft = await MakeMapping(h, candidate);
        var code = await h.Db.ProductMasters.Where(x => x.Id == candidate.BusinessRef!.Id).Select(x => x.ProductCd).SingleAsync();
        var mappings = EngineeringMappings(h); await mappings.CommandAsync(MappingAction(draft, "CONFIRM")); var mapping = await mappings.ReadAsync(draft.Reference.Id, null);
        var row = new CP6.Entity.DomainModels.Erp.ProductProcess { ProductCd = code, TaskCd = "CUT-1", ProcessCd = "CUT", SortOrder = 20,
            SetupHour = 1234567890123.12345678m, LossRate = 0.00000001m, StandardCrewSize = null, PlateNo1 = "ALIAS-ONLY", MachineOrVendor = "LEGACY-MACHINE" };
        var second = new CP6.Entity.DomainModels.Erp.ProductProcess { ProductCd = code, TaskCd = "CUT-2", ProcessCd = "CUT", SortOrder = 20 };
        h.Db.ProductProcesses.AddRange(row, second); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var preview = await owner.LegacyPreviewAsync(mapping.Reference); Assert.Equal(2, preview.Rows.Length);
        Assert.Equal("1234567890123.12345678", preview.SuggestedOperations[0].Parameters.Single(p => p.Name == "SetupHour").Value);
        Assert.DoesNotContain(preview.SuggestedOperations[0].Parameters, p => p.Name == "StandardCrewSize");
        Assert.All(preview.SuggestedOperations, x => { Assert.Empty(x.Tools); Assert.Empty(x.Resources); Assert.Equal("UNKNOWN", x.Applicability); Assert.Empty(x.DependsOn); });
        var input = RoutingCreate(parent); input = input with { Body = input.Body with { SiteRef = candidate.SiteRef, Operations = preview.SuggestedOperations }, LegacyMappingRef = mapping.Reference, LegacyDigest = preview.Digest };
        await h.Db.ProductProcesses.Where(x => x.Id == row.Id).ExecuteUpdateAsync(x => x.SetProperty(p => p.SetupHour, 2m));
        Assert.Equal("ROUTING_LEGACY_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(input))).Message);
        Assert.Equal("UNKNOWN", (await owner.OperationAsync(input.OperationId, false)).State);
        var refreshed = await owner.LegacyPreviewAsync(mapping.Reference);
        input = input with { Body = input.Body with { Operations = refreshed.SuggestedOperations }, LegacyDigest = refreshed.Digest };
        await owner.CommandAsync(input); var saved = await owner.ReadAsync(input.RoutingId);
        Assert.Equal("ALIAS-ONLY", saved.Legacy!.Rows[0].GetProperty("PlateNo1").GetString()); Assert.Contains("LEGACY_TECHNICAL_REVIEW_REQUIRED", saved.Missing!);
        await h.Db.ProductProcesses.Where(x => x.Id == row.Id).ExecuteUpdateAsync(x => x.SetProperty(p => p.ProcessNote1, "Later legacy change"));
        Assert.Equal(saved.OriginalJson, (await owner.ReadAsync(input.RoutingId)).OriginalJson); Assert.Equal(2m, await h.Db.ProductProcesses.Where(x => x.Id == row.Id).Select(x => x.SetupHour).SingleAsync());
        var productMenu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "erp-product").Select(x => x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == productMenu && x.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.OperationAsync(input.OperationId, false))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringRouting_exact_tool_specification_not_plate_alias_and_stale_unit_prevents_new_binding()
    {
        await using var h = await Seed(); var parent = await RoutingParent(h); var owner = Routings(h);
        await GrantSpecification(h); var plate = await SeedPlate(h); var tools = Specifications(h); var input = await ToolDraft(h, plate);
        input = input with { Requirements = input.Requirements with { ProductRef = parent.Draft.ProductRef! } };
        await tools.CommandAsync(input); var tool = await tools.ReadAsync(plate); await GrantUomProfile(h); var units = Units(h); var unit = await MakeUnit(units);
        var create = RoutingCreate(parent); create = create with { Body = create.Body with { Operations = [create.Body.Operations[0] with
            { Tools = [tool.Specification!.Reference], Parameters = [new("Width", "0.00000001", null, null, unit.Reference, "Exact measured width")] }] } };
        await owner.CommandAsync(create); var saved = await owner.ReadAsync(create.RoutingId); Assert.Contains(saved.Missing!, x => x.StartsWith("TOOL_APPROVAL_AND_PHYSICAL_QUALIFICATION_REQUIRED:"));
        await units.CommandAsync(Revise(unit, unit.Profile with { Name = "New unit definition" }));
        Assert.Contains((await owner.ReadAsync(create.RoutingId)).Missing!, x => x.StartsWith("UNIT_SOURCE_CHANGED:"));
        var component = new EngineeringComponentDraftV1(Guid.NewGuid(), "ROUTING", "primary", 0, "APPLIES", "", "", saved.Reference);
        Assert.Equal("UOM_PROFILE_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => RoutingRevisions(h).CommandAsync(PatchRevision(parent,
            new EngineeringDraftPatchV1 { Op = "UPSERT_COMPONENT", Component = component })))).Message);
        Assert.Empty((await RoutingRevisions(h).ReadAsync(parent.Reference.Id, null)).Draft.Components);
        await tools.CommandAsync(NextDraft(input, tool));
        Assert.Contains((await owner.ReadAsync(create.RoutingId)).Missing!, x => x.StartsWith("TOOL_SOURCE_CHANGED:"));
        var bad = RoutingRevise(saved) with { Body = saved.Body with { Operations = [saved.Body.Operations[0] with { Tools = [new("PlateNo", plate, "1", "a".PadLeft(64, 'a'))] }] } };
        Assert.Equal(400, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(bad))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringRouting_missing_store_guard_fails_closed_without_original_operation()
    {
        await using var h = await Seed(); var parent = await RoutingParent(h); var owner = Routings(h); var command = RoutingCreate(parent);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_engineering_routing_v1.versions DISABLE TRIGGER routing_validate");
        try
        {
            Assert.Equal("ROUTING_STORE_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(command))).Message);
            Assert.Equal("UNKNOWN", (await owner.OperationAsync(command.OperationId, false)).State);
        }
        finally { await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_engineering_routing_v1.versions ENABLE TRIGGER routing_validate"); }
        Assert.Empty(await owner.ListAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringRouting_final_authority_fault_rolls_back_version_and_original_together()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); var parent = await RoutingParent(h); var owner = Routings(h);
        var input = RoutingCreate(parent); var reached = false;
        hook.Before = async command =>
        {
            if (!command.CommandText.Contains("WITH field_subject")) return;
            await using var probe = command.Connection!.CreateCommand(); probe.Transaction = command.Transaction;
            probe.CommandText = $"SELECT count(*) FROM cp6_engineering_routing_v1.versions WHERE tenant_id='{h.Tenant}'";
            if (Convert.ToInt32(await probe.ExecuteScalarAsync()) > 0) { reached = true; throw new IOException("Final routing authority unavailable"); }
        };
        try { Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(input))).Message); }
        finally { hook.Before = null; }
        Assert.True(reached); Assert.Empty(await owner.ListAsync()); Assert.Equal("UNKNOWN", (await owner.OperationAsync(input.OperationId, false)).State);
        Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_main_owner_v1.operations WHERE tenant_id={h.Tenant} AND owner='Main.Plm.EngineeringRouting.v1' AND operation_id={input.OperationId}").SingleAsync());
    }
}
