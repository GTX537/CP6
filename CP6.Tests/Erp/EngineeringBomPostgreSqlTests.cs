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
    private static OperationsOwnerV1 BomAuthority(Harness h, CP6Context? db = null)
    {
        var catalog = new AuthorizationCatalogStoreV1();
        foreach (var key in new[] { "plm-engineering-bom", "plm-engineering-routing", "plm-engineering-revision", "plm-engineering-product", "plm-tool-specification", "erp-plate-mold", "uom-profile", "mes-machine", "mes-work-center", "product", "space-site" })
        { catalog.RegisterScope(key, key, key == "space-site" ? [5] : [1, 2, 3, 4, 5], 5); catalog.RegisterFields(key, []); }
        catalog.Seal(); return new(db ?? h.Db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), catalog.Capture);
    }
    private static EngineeringBomOwnerV1 Boms(Harness h, CP6Context? db = null) => new(db ?? h.Db, BomAuthority(h, db));
    private static EngineeringRevisionOwnerV1 BomRevisions(Harness h) => new(h.Db, BomAuthority(h));
    private static async Task<EngineeringRevisionSnapshotV1> BomParent(Harness h)
    {
        var parent = await RoutingParent(h);
        foreach (var action in new[] { "query", "add", "edit" }) await EngineeringAction(h, "plm-engineering-bom", action);
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = "plm-engineering-bom", ScopeType = 5 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); return parent;
    }
    private static EngineeringBomCommandV1 BomCreate(EngineeringRevisionSnapshotV1 parent) => new(Guid.NewGuid(), Guid.NewGuid(), "CREATE", null, null,
        new("Native structure", "EBOM", parent.Reference, parent.Draft.ProductRef!, null, null,
            [new(Guid.NewGuid(), null, "left", 10, "Left material", null, "1", null, "Per root assembly", null, "", "APPLIES", "", "", "", null, "")]), "Initial structure");
    private static EngineeringBomCommandV1 BomRevise(EngineeringBomSnapshotV1 value) => new(Guid.NewGuid(), value.Reference.Id, "REVISE", value.Reference.Version, value.Etag, value.Body, "Explicit structure revision");
    [CurrentPermissionPostgresFact]
    public async Task EngineeringBom_occurrences_exact_quantity_history_where_used_and_CAS()
    {
        await using var h = await Seed(); var parent = await BomParent(h); var owner = Boms(h); var input = BomCreate(parent);
        var item = (await MakeEngineering(h)).Reference; var reference = new EngineeringSourceRefV1(item.Owner, item.Id, item.Version, item.Digest);
        var exact = new string('9', 1024) + "." + new string('1', 1024);
        var left = input.Body.Occurrences[0] with { ItemRef = reference, Quantity = exact };
        var right = left with { OccurrenceId = Guid.NewGuid(), Path = "right", SemanticOrder = 20 };
        input = input with { Body = input.Body with { Occurrences = [left, right] } };
        var receipt = await owner.CommandAsync(input); Assert.Equal(receipt, await owner.CommandAsync(input));
        var saved = await owner.ReadAsync(input.BomId); Assert.Equal(exact, saved.Body.Occurrences[0].Quantity);
        var uses = await owner.WhereUsedAsync(reference); Assert.Equal(2, uses.Length); Assert.Equal(2, uses.Select(x => x.OccurrenceId).Distinct().Count());
        await owner.CommandAsync(BomRevise(saved) with { Body = saved.Body with { Occurrences = [left, right with { Note = "Changed right occurrence" }] } });
        Assert.Equal(saved.OriginalJson, (await owner.ReadAsync(input.BomId, "1")).OriginalJson);
        Assert.Equal("occurrences/" + right.OccurrenceId, Assert.Single((await owner.DiffAsync(input.BomId, "1", "2")).Changes).Path);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(BomRevise(saved)))).Status);
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(input with { Reason = "Changed body" }))).Message);
        foreach (var sql in new[] { "DELETE FROM cp6_engineering_bom_v1.versions", "UPDATE cp6_engineering_bom_v1.versions SET actor_id=gen_random_uuid()", "TRUNCATE cp6_engineering_bom_v1.versions CASCADE" })
            await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlRawAsync(sql));
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringBom_cycles_unresolved_items_units_and_substitution_remain_drafts()
    {
        await using var h = await Seed(); var parent = await BomParent(h); var owner = Boms(h); var input = BomCreate(parent);
        var left = input.Body.Occurrences[0]; var right = left with { OccurrenceId = Guid.NewGuid(), ParentOccurrenceId = left.OccurrenceId, Path = "right" };
        left = left with { ParentOccurrenceId = right.OccurrenceId, SubstitutionRequirement = "Proposal, not approval", Quantity = "-2" };
        input = input with { Body = input.Body with { Occurrences = [left, right] } }; await owner.CommandAsync(input);
        var value = await owner.ReadAsync(input.BomId); Assert.Equal("DRAFT_ONLY", value.Readiness);
        foreach (var prefix in new[] { "STRUCTURE_CYCLE:", "ITEM_REQUIRED:", "UNIT_REQUIRED:", "SUBSTITUTION_APPROVAL_REQUIRED:", "QUANTITY_NONPOSITIVE:" }) Assert.Contains(value.Missing!, x => x.StartsWith(prefix));
        Assert.Equal(400, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(BomRevise(value) with { Body = value.Body with { Occurrences = [left, left] } }))).Status);
        foreach (var bad in new[] { "NaN", "1e3", "01", "-0", new string('1', 1025) })
            Assert.Equal(400, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(BomRevise(value) with { Body = value.Body with { Occurrences = [left with { Quantity = bad }] } }))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringBom_business_material_versions_and_units_are_actual_sources()
    {
        await using var h = await Seed(); var parent = await BomParent(h); var owner = Boms(h); await GrantProductIdentity(h);
        var id = await SeedIdentityProduct(h, "BOM-ITEM"); var code = await h.Db.ProductMasters.Where(x => x.Id == id).Select(x => x.ProductCd).SingleAsync();
        var choice = Assert.Single(await owner.ItemChoicesAsync("BUSINESS", code)); Assert.Equal(id, choice.Reference.Id);
        await GrantUomProfile(h); var unit = await MakeUnit(Units(h)); var input = BomCreate(parent);
        input = input with { Body = input.Body with { Occurrences = [input.Body.Occurrences[0] with { ItemRef = choice.Reference, UnitRef = unit.Reference, Quantity = "2.123456789" }] } };
        await owner.CommandAsync(input); var saved = await owner.ReadAsync(input.BomId);
        Assert.Contains(saved.Missing!, x => x.StartsWith("ITEM_TECHNICAL_REVISION_REQUIRED:"));
        await h.Db.ProductMasters.Where(x => x.Id == id).ExecuteUpdateAsync(x => x.SetProperty(p => p.ProductCd, "CHANGED-" + id.ToString().Substring(0, 8)));
        Assert.Contains((await owner.ReadAsync(input.BomId)).Missing!, x => x.StartsWith("ITEM_SOURCE_CHANGED:"));
        Assert.Equal("BOM_ITEM_SOURCE_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(BomRevise(saved)))).Message);
        var menu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "erp-product").Select(x => x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == menu && x.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.OperationAsync(input.OperationId, false))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringBom_composition_and_routing_keep_exact_kinds_and_historical_origin()
    {
        await using var h = await Seed(); var parent = await BomParent(h); var owner = Boms(h); var revisions = BomRevisions(h);
        var route = RoutingCreate(parent); await Routings(h).CommandAsync(route); var routing = await Routings(h).ReadAsync(route.RoutingId);
        var input = BomCreate(parent); input = input with { Body = input.Body with { Kind = "MBOM", RoutingRef = routing.Reference, Occurrences = [input.Body.Occurrences[0] with { RoutingOccurrenceId = routing.Body.Operations[0].OccurrenceId }] } };
        await owner.CommandAsync(input); var bom = await owner.ReadAsync(input.BomId);
        var component = new EngineeringComponentDraftV1(Guid.NewGuid(), "EBOM", "bom", 0, "APPLIES", "", "", bom.Reference);
        Assert.Equal("BOM_REVISION_MISMATCH", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => revisions.CommandAsync(PatchRevision(parent, new EngineeringDraftPatchV1 { Op = "UPSERT_COMPONENT", Component = component })))).Message);
        component = component with { Kind = "MBOM" }; await revisions.CommandAsync(PatchRevision(parent, new EngineeringDraftPatchV1 { Op = "UPSERT_COMPONENT", Component = component }));
        var bound = await revisions.ReadAsync(parent.Reference.Id, null); Assert.Equal(bom.Reference, Assert.Single(bound.Draft.Components).SourceRef);
        Assert.Equal(bom.OriginalJson, (await owner.ReadAsync(input.BomId)).OriginalJson);
        await owner.CommandAsync(BomRevise(bom) with { Body = bom.Body with { RevisionOrigin = bound.Reference, Name = "New BOM version" } });
        Assert.Contains("SOURCE_CHANGED:" + component.OccurrenceId, (await revisions.ReadAsync(parent.Reference.Id, null)).Missing!);
        Assert.Equal(bound.OriginalJson, (await revisions.ReadAsync(parent.Reference.Id, null)).OriginalJson);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringBom_legacy_import_preserves_rows_and_never_resolves_material_aliases()
    {
        await using var h = await Seed(); var parent = await BomParent(h); var owner = Boms(h);
        await GrantProductIdentity(h); await EngineeringAction(h, "space-site", "query"); await EngineeringAction(h, "erp-product", "confirm-engineering");
        var candidate = await MappingCandidate(h, await Engineering(h).ReadAsync(parent.Draft.ProductRef!.Id, null), "BOM"); var draft = await MakeMapping(h, candidate);
        await EngineeringMappings(h).CommandAsync(MappingAction(draft, "CONFIRM")); var mapping = await EngineeringMappings(h).ReadAsync(draft.Reference.Id, null);
        var code = await h.Db.ProductMasters.Where(x => x.Id == candidate.BusinessRef!.Id).Select(x => x.ProductCd).SingleAsync();
        var row = new CP6.Entity.DomainModels.Erp.ProductMaterial { ProductCd = code, ProcessCd = "CUT", MaterialCd = "RAW-ALIAS", UnitUsage = 1234567890123.12345678m, UsageUnit = "kg", UsageType = 2 };
        h.Db.ProductMaterials.Add(row); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var preview = await owner.LegacyPreviewAsync(mapping.Reference); var line = Assert.Single(preview.SuggestedOccurrences);
        Assert.Equal("1234567890123.12345678", line.Quantity); Assert.Null(line.ItemRef); Assert.Null(line.UnitRef); Assert.Null(line.RoutingOccurrenceId);
        var input = BomCreate(parent); input = input with { Body = input.Body with { SiteRef = candidate.SiteRef, Occurrences = preview.SuggestedOccurrences }, LegacyMappingRef = mapping.Reference, LegacyDigest = preview.Digest };
        await h.Db.ProductMaterials.Where(x => x.Id == row.Id).ExecuteUpdateAsync(x => x.SetProperty(p => p.UnitUsage, 2m));
        Assert.Equal("BOM_LEGACY_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(input))).Message);
        var refreshed = await owner.LegacyPreviewAsync(mapping.Reference); input = input with { Body = input.Body with { Occurrences = refreshed.SuggestedOccurrences }, LegacyDigest = refreshed.Digest };
        await owner.CommandAsync(input); var saved = await owner.ReadAsync(input.BomId); Assert.Equal("RAW-ALIAS", saved.Legacy!.Rows[0].GetProperty("MaterialCd").GetString());
        await h.Db.ProductMaterials.Where(x => x.Id == row.Id).ExecuteUpdateAsync(x => x.SetProperty(p => p.UsageUnit, "m"));
        Assert.Equal(saved.OriginalJson, (await owner.ReadAsync(input.BomId)).OriginalJson);
    }
    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task EngineeringBom_trusted_parent_department_and_revocation(int type)
    {
        await using var h = await Seed(); var parent = await BomParent(h); var owner = Boms(h); var admin = await MappingAdmin(h);
        await BindStandalone(h, admin, "plm-engineering-product", parent.Draft.ProductRef!.Id); await StandaloneScope(h, admin, type);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(BomCreate(parent)))).Status);
        await BindStandalone(h, admin, "plm-engineering-revision", parent.Reference.Id); var input = BomCreate(parent); await owner.CommandAsync(input);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE tenant_id={h.Tenant} AND object_kind='plm-engineering-revision' AND object_id={parent.Reference.Id}");
        Assert.Empty(await owner.ListAsync()); Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.OperationAsync(input.OperationId, false))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringBom_two_connections_CAS_and_other_tenant_denial()
    {
        await using var h = await Seed(); var parent = await BomParent(h); var owner = Boms(h); var input = BomCreate(parent); await owner.CommandAsync(input); var saved = await owner.ReadAsync(input.BomId);
        await using var peer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        async Task<bool> Save(EngineeringBomOwnerV1 writer) { try { await writer.CommandAsync(BomRevise(saved)); return true; } catch (OrderPriceCorrectionException error) { Assert.Equal(412, error.Status); return false; } }
        Assert.Single(await Task.WhenAll(Save(owner), Save(Boms(h, peer))), x => x);
        await using var other = await Seed(); await BomParent(other); Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => Boms(other).ReadAsync(input.BomId))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringBom_unknown_commit_close_and_read_only_original_recovery()
    {
        var failure = new InterruptCurrencyCommit(); await using var h = await Seed(failure); var parent = await BomParent(h); var owner = Boms(h); var input = BomCreate(parent); failure.Interrupt = true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(input))).Message);
        Assert.Empty(await owner.ListAsync()); Assert.Equal("UNKNOWN", (await owner.OperationAsync(input.OperationId, false)).State);
        Assert.Equal("CLOSED", (await owner.OperationAsync(input.OperationId, true)).State); Assert.Equal("ORIGINAL_OPERATION_CLOSED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(input))).Message);
        input = BomCreate(parent); var result = await owner.CommandAsync(input); var menu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "plm-engineering-bom").Select(x => x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == menu && x.ActionCode != "query").ExecuteDeleteAsync(); Assert.Equal(result, await owner.CommandAsync(input));
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringBom_final_authority_fault_rolls_back_and_disabled_guard_fails_closed()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); var parent = await BomParent(h); var owner = Boms(h); var input = BomCreate(parent); var reached = false;
        hook.Before = async command =>
        {
            if (!command.CommandText.Contains("WITH field_subject")) return;
            await using var probe = command.Connection!.CreateCommand(); probe.Transaction = command.Transaction;
            probe.CommandText = $"SELECT count(*) FROM cp6_engineering_bom_v1.versions WHERE tenant_id='{h.Tenant}'";
            if (Convert.ToInt32(await probe.ExecuteScalarAsync()) > 0) { reached = true; throw new IOException("Final BOM authority unavailable"); }
        };
        try { Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(input))).Message); }
        finally { hook.Before = null; }
        Assert.True(reached); Assert.Empty(await owner.ListAsync()); Assert.Equal("UNKNOWN", (await owner.OperationAsync(input.OperationId, false)).State);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_engineering_bom_v1.versions DISABLE TRIGGER bom_validate");
        try { Assert.Equal("BOM_STORE_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(input))).Message); }
        finally { await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_engineering_bom_v1.versions ENABLE TRIGGER bom_validate"); }
        Assert.Equal("UNKNOWN", (await owner.OperationAsync(input.OperationId, false)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringBom_stale_unit_blocks_new_adoption_but_keeps_historical_original()
    {
        await using var h = await Seed(); var parent = await BomParent(h); var owner = Boms(h); await GrantUomProfile(h); var units = Units(h); var unit = await MakeUnit(units); var input = BomCreate(parent);
        input = input with { Body = input.Body with { Occurrences = [input.Body.Occurrences[0] with { UnitRef = unit.Reference }] } };
        await owner.CommandAsync(input); var saved = await owner.ReadAsync(input.BomId); await units.CommandAsync(Revise(unit, unit.Profile with { Name = "Changed unit" }));
        Assert.Equal(saved.OriginalJson, (await owner.ReadAsync(input.BomId)).OriginalJson); Assert.Contains((await owner.ReadAsync(input.BomId)).Missing!, x => x.StartsWith("UNIT_SOURCE_CHANGED:"));
        Assert.Equal("UOM_PROFILE_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(BomRevise(saved)))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringBom_HTTP_closed_input_current_source_and_no_implicit_grants()
    {
        await using var h = await Seed(); var menu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "plm-engineering-bom").Select(x => x.MenuId).SingleAsync();
        Assert.False(await h.Db.Sys_RoleActions.AnyAsync(x => x.MenuId == menu));
        var parent = await BomParent(h); var input = BomCreate(parent); await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        async Task<HttpResponseMessage> Save(object value) { var request = OperationsRequest(cookies, HttpMethod.Post, "/api/engineering-bom/owner-v1/commands"); request.Content = JsonContent.Create(value); return await host.Send(request); }
        using var first = await Save(input); Assert.Equal(HttpStatusCode.OK, first.StatusCode); Assert.True(first.Headers.CacheControl?.NoStore);
        using var replay = await Save(input); Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        var extra = JsonSerializer.SerializeToNode(input, new JsonSerializerOptions(JsonSerializerDefaults.Web))!; extra["approved"] = true; using var invalid = await Save(extra); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var parentMenu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "plm-engineering-revision").Select(x => x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == parentMenu && x.ActionCode == "query").ExecuteDeleteAsync();
        using var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "/api/engineering-bom/owner-v1/" + input.BomId)); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }
}
