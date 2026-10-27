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
    private static OperationsOwnerV1 ManufacturingAuthority(Harness h, CP6Context? db = null)
    {
        var catalog = new AuthorizationCatalogStoreV1();
        foreach (var key in new[] { "plm-engineering-manufacturing", "plm-engineering-bom", "plm-engineering-routing", "plm-engineering-revision", "plm-engineering-product", "plm-configuration-rule", "plm-tool-specification", "erp-plate-mold", "uom-profile", "mes-machine", "mes-work-center", "product", "space-site" })
        { catalog.RegisterScope(key, key, key == "space-site" ? [5] : [1, 2, 3, 4, 5], 5); catalog.RegisterFields(key, []); }
        catalog.Seal(); return new(db ?? h.Db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), catalog.Capture);
    }
    private static EngineeringManufacturingOwnerV1 Manufacturing(Harness h, CP6Context? db = null) => new(db ?? h.Db, ManufacturingAuthority(h, db));
    private static async Task<EngineeringManufacturingCommandV1> ManufacturingCreate(Harness h)
    {
        var parent = await BomParent(h);
        await BomRevisions(h).CommandAsync(PatchRevision(parent, new EngineeringDraftPatchV1 { Op = "SET_CONFIGURATION", Configuration = new([], "APPLIES", "Explicit configuration") }));
        parent = await BomRevisions(h).ReadAsync(parent.Reference.Id, null);
        foreach (var action in new[] { "query", "add", "edit" }) await EngineeringAction(h, "plm-engineering-manufacturing", action);
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = "plm-engineering-manufacturing", ScopeType = 5 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var route = RoutingCreate(parent); var first = route.Body.Operations[0];
        route = route with { Body = route.Body with { Operations = [first, first with { OccurrenceId = Guid.NewGuid(), Name = "Second step", SemanticOrder = 20, DependsOn = [first.OccurrenceId] }] } };
        await Routings(h).CommandAsync(route); var routing = await Routings(h).ReadAsync(route.RoutingId);
        var e = BomCreate(parent); await Boms(h).CommandAsync(e); var ebom = await Boms(h).ReadAsync(e.BomId);
        var m = BomCreate(parent); var line = m.Body.Occurrences[0];
        m = m with { Body = m.Body with { Kind = "MBOM", RoutingRef = routing.Reference, Occurrences = [line, line with { OccurrenceId = Guid.NewGuid(), Path = "right", SemanticOrder = 20 }] } };
        await Boms(h).CommandAsync(m); var mbom = await Boms(h).ReadAsync(m.BomId);
        var inputs = mbom.Body.Occurrences.Select(x => x.OccurrenceId).ToArray();
        var outputs = routing.Body.Operations.Select(op => new ManufacturingOutputV1(Guid.NewGuid(), "PRIMARY", "Repeated product", parent.Draft.ProductRef,
            op.OccurrenceId, inputs, [], parent.Draft.Configuration!.Reference, "FIXED_RATIO", "1.00000000000000001", null, null, null,
            "3", null, "Per input batch, not actual production", "Own technical specification required", "Directed process relationship draft", "", "No quality or sale classification")).ToArray();
        var map = new ManufacturingMapV1(Guid.NewGuid(), "MAP", ebom.Body.Occurrences.Select(x => x.OccurrenceId).ToArray(), inputs, routing.Body.Operations.Select(x => x.OccurrenceId).ToArray(), "One engineering position split into two manufacturing inputs", "Exact conversion pending professional evidence");
        return new(Guid.NewGuid(), Guid.NewGuid(), "CREATE", null, null, new("Manufacturing view", parent.Reference, parent.Draft.ProductRef!, parent.Draft.Configuration!.Reference,
            ebom.Reference, mbom.Reference, routing.Reference, null, [map], outputs), "Initial relationship draft");
    }
    private static EngineeringManufacturingCommandV1 ManufacturingRevise(EngineeringManufacturingSnapshotV1 value) => new(Guid.NewGuid(), value.Reference.Id, "REVISE", value.Reference.Version, value.Etag, value.Body, "Explicit new relationship version");
    [CurrentPermissionPostgresFact]
    public async Task EngineeringManufacturing_exact_many_to_many_and_repeated_output_history_CAS()
    {
        await using var h = await Seed(); var input = await ManufacturingCreate(h); var owner = Manufacturing(h);
        var quantity = new string('9', 1024) + "." + new string('1', 1024); input = input with { Body = input.Body with { Outputs = input.Body.Outputs.Select(x => x with { Quantity = quantity }).ToArray() } };
        var result = await owner.CommandAsync(input); Assert.Equal(result, await owner.CommandAsync(input)); var saved = await owner.ReadAsync(input.ManufacturingId);
        Assert.Equal(2, saved.Body.Outputs.Length); Assert.Equal(2, saved.Body.Outputs.Select(x => x.OutputOccurrenceId).Distinct().Count()); Assert.Single(saved.Body.Outputs.Select(x => x.ProductRef).Distinct());
        Assert.All(saved.Body.Outputs, x => Assert.Equal(quantity, x.Quantity)); Assert.Equal(2, saved.Body.Mappings[0].MbomOccurrenceIds.Length);
        Assert.DoesNotContain(saved.Missing!, x => x.StartsWith("UNMAPPED_")); Assert.Contains("FORMAL_RELATION_SET_PROOFS_REQUIRED", saved.Missing!);
        var changed = saved.Body.Outputs[1] with { Name = "Different output purpose" };
        await owner.CommandAsync(ManufacturingRevise(saved) with { Body = saved.Body with { Outputs = [saved.Body.Outputs[0], changed] } });
        Assert.Equal(saved.OriginalJson, (await owner.ReadAsync(input.ManufacturingId, "1")).OriginalJson);
        Assert.Equal("outputs/" + changed.OutputOccurrenceId, Assert.Single((await owner.DiffAsync(input.ManufacturingId, "1", "2")).Changes).Path);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(ManufacturingRevise(saved)))).Status);
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(input with { Reason = "Changed body" }))).Message);
        await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlRawAsync("DELETE FROM cp6_engineering_manufacturing_v1.versions"));
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringManufacturing_closure_keeps_unmapped_extra_and_unproven_relationships()
    {
        await using var h = await Seed(); var input = await ManufacturingCreate(h); var output = input.Body.Outputs[0];
        var unknown = Guid.NewGuid(); output = output with { ProductRef = null, GeneratingOperationOccurrenceId = unknown, InputOccurrenceIds = [unknown], ConfigurationRef = output.ConfigurationRef! with { Id = Guid.NewGuid() }, QuantityKind = "RANGE", Minimum = "3.000000000000000001", Maximum = "3", Denominator = "0" };
        input = input with { Body = input.Body with { Mappings = [], Outputs = [output] } }; var owner = Manufacturing(h); await owner.CommandAsync(input); var value = await owner.ReadAsync(input.ManufacturingId);
        foreach (var prefix in new[] { "UNMAPPED_EBOM_OCCURRENCE:", "UNMAPPED_MBOM_OCCURRENCE:", "UNUSED_MANUFACTURING_INPUT:", "GENERATING_OPERATION_MISSING:", "OUTPUT_INPUT_MISSING:", "OUTPUT_CONFIGURATION_MISMATCH:", "OUTPUT_PRODUCT_REQUIRED:", "QUANTITY_RANGE_REVERSED:", "QUANTITY_BOUNDARY_INVALID:" }) Assert.Contains(value.Missing!, x => x.StartsWith(prefix));
        Assert.Equal("DRAFT_ONLY", value.Readiness); Assert.Equal("PRIMARY_PRODUCT_OUTPUT_REQUIRED", Assert.Single(value.Missing!, x => x == "PRIMARY_PRODUCT_OUTPUT_REQUIRED"));
        var bad = ManufacturingRevise(value) with { Body = value.Body with { Outputs = [output, output] } };
        Assert.Equal(400, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(bad))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringManufacturing_intermediate_flow_uses_actual_dependencies_and_detects_cycles()
    {
        await using var h = await Seed(); var input = await ManufacturingCreate(h); var a = input.Body.Outputs[0] with { Role = "INTERMEDIATE" };
        var b = input.Body.Outputs[1] with { InputOccurrenceIds = [], FromOutputOccurrenceIds = [a.OutputOccurrenceId] };
        input = input with { Body = input.Body with { Outputs = [a, b] } }; var owner = Manufacturing(h); await owner.CommandAsync(input); var saved = await owner.ReadAsync(input.ManufacturingId);
        Assert.DoesNotContain(saved.Missing!, x => x.StartsWith("INTERMEDIATE_FLOW_ORDER_UNPROVEN:") || x.StartsWith("OUTPUT_FLOW_CYCLE:"));
        await owner.CommandAsync(ManufacturingRevise(saved) with { Body = saved.Body with { Outputs = [a with { FromOutputOccurrenceIds = [b.OutputOccurrenceId] }, b] } });
        Assert.Contains((await owner.ReadAsync(input.ManufacturingId)).Missing!, x => x.StartsWith("OUTPUT_FLOW_CYCLE:"));
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringManufacturing_current_sources_configuration_and_history_are_distinct()
    {
        await using var h = await Seed(); var input = await ManufacturingCreate(h); var owner = Manufacturing(h); await owner.CommandAsync(input); var saved = await owner.ReadAsync(input.ManufacturingId);
        var ebom = await Boms(h).ReadAsync(input.Body.EbomRef.Id); await Boms(h).CommandAsync(BomRevise(ebom) with { Body = ebom.Body with { Name = "Changed source" } });
        Assert.Equal(saved.OriginalJson, (await owner.ReadAsync(input.ManufacturingId)).OriginalJson); Assert.Contains("EBOM_SOURCE_CHANGED", (await owner.ReadAsync(input.ManufacturingId)).Missing!);
        Assert.Equal("BOM_SOURCE_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(ManufacturingRevise(saved)))).Message);
        var changed = await Boms(h).ReadAsync(ebom.Reference.Id); var next = ManufacturingRevise(saved) with { Body = saved.Body with { EbomRef = changed.Reference } }; await owner.CommandAsync(next);
        saved = await owner.ReadAsync(input.ManufacturingId); var parent = await BomRevisions(h).ReadAsync(input.Body.RevisionOrigin.Id, null);
        await BomRevisions(h).CommandAsync(PatchRevision(parent, new EngineeringDraftPatchV1 { Op = "SET_CONFIGURATION", Configuration = new([], "APPLIES", "Changed configuration") }));
        Assert.Contains("REVISION_SOURCE_CHANGED", (await owner.ReadAsync(input.ManufacturingId)).Missing!);
        Assert.Equal("MANUFACTURING_REVISION_SOURCE_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(ManufacturingRevise(saved)))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringManufacturing_wrong_kind_or_parent_sources_never_resolve_by_name()
    {
        await using var h = await Seed(); var input = await ManufacturingCreate(h); var owner = Manufacturing(h);
        Assert.Equal("BOM_REVISION_MISMATCH", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(input with { Body = input.Body with { EbomRef = input.Body.MbomRef } }))).Message);
        Assert.Equal("MANUFACTURING_REVISION_SOURCE_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(input with { Body = input.Body with { ConfigurationRef = input.Body.ConfigurationRef! with { Digest = new string('b', 64) } } }))).Message);
        Assert.Empty(await owner.ListAsync());
    }
    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task EngineeringManufacturing_department_source_and_revocation_protect_originals(int type)
    {
        await using var h = await Seed(); var input = await ManufacturingCreate(h); var owner = Manufacturing(h); var admin = await MappingAdmin(h);
        await BindStandalone(h, admin, "plm-engineering-product", input.Body.ProductRef.Id); await StandaloneScope(h, admin, type);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(input))).Status);
        await BindStandalone(h, admin, "plm-engineering-revision", input.Body.RevisionOrigin.Id); await owner.CommandAsync(input);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE tenant_id={h.Tenant} AND object_kind='plm-engineering-revision' AND object_id={input.Body.RevisionOrigin.Id}");
        Assert.Empty(await owner.ListAsync()); Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.OperationAsync(input.OperationId, false))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringManufacturing_two_connection_CAS_and_tenant_boundary()
    {
        await using var h = await Seed(); var input = await ManufacturingCreate(h); var owner = Manufacturing(h); await owner.CommandAsync(input); var saved = await owner.ReadAsync(input.ManufacturingId);
        await using var peer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        async Task<bool> Save(EngineeringManufacturingOwnerV1 writer) { try { await writer.CommandAsync(ManufacturingRevise(saved)); return true; } catch (OrderPriceCorrectionException e) { Assert.Equal(412, e.Status); return false; } }
        Assert.Single(await Task.WhenAll(Save(owner), Save(Manufacturing(h, peer))), x => x);
        await using var other = await Seed(); await ManufacturingCreate(other); Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => Manufacturing(other).ReadAsync(input.ManufacturingId))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringManufacturing_final_authority_and_store_guard_rollback()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); var input = await ManufacturingCreate(h); var owner = Manufacturing(h); var reached = false;
        hook.Before = async command =>
        {
            if (!command.CommandText.Contains("WITH field_subject")) return;
            await using var probe = command.Connection!.CreateCommand(); probe.Transaction = command.Transaction;
            probe.CommandText = $"SELECT count(*) FROM cp6_engineering_manufacturing_v1.versions WHERE tenant_id='{h.Tenant}'";
            if (Convert.ToInt32(await probe.ExecuteScalarAsync()) > 0) { reached = true; throw new IOException("Final manufacturing authority unavailable"); }
        };
        try { Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(input))).Message); } finally { hook.Before = null; }
        Assert.True(reached); Assert.Empty(await owner.ListAsync()); Assert.Equal("UNKNOWN", (await owner.OperationAsync(input.OperationId, false)).State);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_engineering_manufacturing_v1.versions DISABLE TRIGGER manufacturing_validate");
        try { Assert.Equal("MANUFACTURING_STORE_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(input))).Message); }
        finally { await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_engineering_manufacturing_v1.versions ENABLE TRIGGER manufacturing_validate"); }
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringManufacturing_unknown_close_and_current_read_only_replay()
    {
        var failure = new InterruptCurrencyCommit(); await using var h = await Seed(failure); var input = await ManufacturingCreate(h); var owner = Manufacturing(h); failure.Interrupt = true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(input))).Message);
        Assert.Equal("UNKNOWN", (await owner.OperationAsync(input.OperationId, false)).State); Assert.Equal("CLOSED", (await owner.OperationAsync(input.OperationId, true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(input))).Message);
        input = input with { OperationId = Guid.NewGuid() }; var result = await owner.CommandAsync(input);
        var menu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "plm-engineering-manufacturing").Select(x => x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == menu && x.ActionCode != "query").ExecuteDeleteAsync(); Assert.Equal(result, await owner.CommandAsync(input));
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringManufacturing_own_output_product_and_unit_versions_are_current_and_authorized()
    {
        await using var h = await Seed(); var input = await ManufacturingCreate(h); var owner = Manufacturing(h);
        var separateProduct = await MakeEngineering(h); await GrantUomProfile(h); var units = Units(h); var unit = await MakeUnit(units, BaseUnit(scale: 2));
        var output = input.Body.Outputs[1] with { Role = "BY_PRODUCT", ProductRef = separateProduct.Reference, UnitRef = unit.Reference, DenominatorUnitRef = unit.Reference, Quantity = "0.001", Denominator = "1.001" };
        input = input with { Body = input.Body with { Outputs = [input.Body.Outputs[0], output] } }; await owner.CommandAsync(input); var saved = await owner.ReadAsync(input.ManufacturingId);
        Assert.Equal(separateProduct.Reference, saved.Body.Outputs[1].ProductRef);
        Assert.Contains("OUTPUT_UNIT_PRECISION_CONFLICT:" + output.OutputOccurrenceId, saved.Missing!); Assert.Contains("DENOMINATOR_UNIT_PRECISION_CONFLICT:" + output.OutputOccurrenceId, saved.Missing!);
        await Engineering(h).CommandAsync(ReviseEngineering(separateProduct, separateProduct.Product with { Definition = "Changed by-product specification" }));
        Assert.Contains("OUTPUT_PRODUCT_SOURCE_CHANGED:" + separateProduct.Reference.Id, (await owner.ReadAsync(input.ManufacturingId)).Missing!);
        Assert.Equal(saved.OriginalJson, (await owner.ReadAsync(input.ManufacturingId)).OriginalJson);
        Assert.Equal("MANUFACTURING_OUTPUT_PRODUCT_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(ManufacturingRevise(saved)))).Message);
        output = output with { ProductRef = (await Engineering(h).ReadAsync(separateProduct.Reference.Id, null)).Reference };
        await owner.CommandAsync(ManufacturingRevise(saved) with { Body = saved.Body with { Outputs = [saved.Body.Outputs[0], output] } }); saved = await owner.ReadAsync(input.ManufacturingId);
        await units.CommandAsync(Revise(unit, unit.Profile with { Name = "Changed unit definition" }));
        Assert.Contains("UNIT_SOURCE_CHANGED:" + unit.Reference.Id, (await owner.ReadAsync(input.ManufacturingId)).Missing!);
        Assert.Equal("UOM_PROFILE_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(ManufacturingRevise(saved)))).Message);
        var menu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "erp-uom-profile").Select(x => x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == menu && x.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.OperationAsync(input.OperationId, false))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringManufacturing_no_input_reason_does_not_become_approval_or_hide_other_inputs()
    {
        await using var h = await Seed(); var input = await ManufacturingCreate(h); var owner = Manufacturing(h);
        var output = input.Body.Outputs[0] with { InputOccurrenceIds = [], FromOutputOccurrenceIds = [], NoInputBasis = "" };
        input = input with { Body = input.Body with { Outputs = [output] } }; await owner.CommandAsync(input); var saved = await owner.ReadAsync(input.ManufacturingId);
        Assert.Contains("NO_INPUT_BASIS_REQUIRED:" + output.OutputOccurrenceId, saved.Missing!);
        await owner.CommandAsync(ManufacturingRevise(saved) with { Body = saved.Body with { Outputs = [output with { NoInputBasis = "Generated during process; technical evidence to be reviewed" }] } });
        var revised = await owner.ReadAsync(input.ManufacturingId); Assert.DoesNotContain(revised.Missing!, x => x.StartsWith("NO_INPUT_BASIS_REQUIRED:"));
        Assert.Equal(2, revised.Missing!.Count(x => x.StartsWith("UNUSED_MANUFACTURING_INPUT:"))); Assert.Equal("DRAFT_ONLY", revised.Readiness);
        Assert.Contains("OUTPUT_RELATION_PROOF_REQUIRED:" + output.OutputOccurrenceId, revised.Missing!);
        foreach (var malformed in new[] { "NaN", "1e3", "01", "-0", new string('1', 1025) })
            Assert.Equal(400, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(ManufacturingRevise(revised) with { Body = revised.Body with { Outputs = [revised.Body.Outputs[0] with { Quantity = malformed }] } }))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringManufacturing_HTTP_closed_body_no_grants_and_nested_source_revocation()
    {
        await using var h = await Seed(); var menu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "plm-engineering-manufacturing").Select(x => x.MenuId).SingleAsync(); Assert.False(await h.Db.Sys_RoleActions.AnyAsync(x => x.MenuId == menu));
        var input = await ManufacturingCreate(h); await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        async Task<HttpResponseMessage> Save(object value) { var request = OperationsRequest(cookies, HttpMethod.Post, "/api/engineering-manufacturing/owner-v1/commands"); request.Content = JsonContent.Create(value); return await host.Send(request); }
        using var first = await Save(input); Assert.Equal(HttpStatusCode.OK, first.StatusCode); Assert.True(first.Headers.CacheControl?.NoStore);
        using var replay = await Save(input); Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        var json = JsonSerializer.SerializeToNode(input, new JsonSerializerOptions(JsonSerializerDefaults.Web))!; json["body"]!["approved"] = true; using var invalid = await Save(json); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var sourceMenu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "plm-engineering-bom").Select(x => x.MenuId).SingleAsync(); await h.Db.Sys_RoleActions.Where(x => x.MenuId == sourceMenu && x.ActionCode == "query").ExecuteDeleteAsync();
        using var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "/api/engineering-manufacturing/owner-v1/" + input.ManufacturingId)); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }
}
