using System.Net;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using Npgsql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task GrantEngineeringRevision(Harness h)
    {
        var menu = await h.Db.Sys_Menus.SingleOrDefaultAsync(m => m.MenuKey == "plm-engineering-revision");
        if (menu == null) { menu = new() { MenuId = 9152, MenuKey = "plm-engineering-revision", MenuName = "Engineering revisions" }; h.Db.Sys_Menus.Add(menu); }
        foreach (var action in new[] { "query", "add", "edit" })
        {
            if (!await h.Db.Sys_MenuActions.AnyAsync(a => a.MenuId == menu.MenuId && a.ActionCode == action))
                h.Db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = action, ActionName = action });
            h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = menu.MenuId, ActionCode = action });
        }
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = "plm-engineering-revision", ScopeType = 5 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
    }

    private static EngineeringRevisionOwnerV1 Revisions(Harness h, CP6Context? context = null)
    {
        var catalog = new AuthorizationCatalogStoreV1();
        foreach (var resource in new[] { "plm-engineering-revision", "plm-engineering-product" })
        { catalog.RegisterScope(resource, resource, [1, 2, 3, 4, 5], 5); catalog.RegisterFields(resource, []); }
        catalog.Seal(); var db = context ?? h.Db;
        return new(db, new(db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), catalog.Capture));
    }
    private static async Task RevisionRule(Harness h)
    {
        h.Db.Pub_DocSequences.Add(new() { BizKey = "ENG_REV", Prefix = "ENG", SeqLength = 4, ResetCycle = 0, CurrentValue = 0 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
    }
    private static async Task<EngineeringRevisionCommandV1> NewRevision(EngineeringRevisionOwnerV1 owner, Guid? department = null, EngineeringProductRefV1? product = null)
        => new() { OperationId = Guid.NewGuid(), RevisionId = Guid.NewGuid(), Action = "CREATE", Goal = "First working design", Reason = "Create a new product draft",
            Origin = new("NEW_PRODUCT", "No prior released product baseline"), ProductRef = product, DepartmentId = department,
            ExpectedAuthorityVersion = (await owner.CreationContextAsync()).AuthorityVersion };
    private static EngineeringRevisionCommandV1 PatchRevision(EngineeringRevisionSnapshotV1 value, params EngineeringDraftPatchV1[] patches)
        => new() { OperationId = Guid.NewGuid(), RevisionId = value.Reference.Id, Action = "PATCH", ExpectedVersion = value.Reference.Version,
            ExpectedEtag = value.Etag, Reason = "Explicit working draft edit", Patches = patches };
    private static async Task<EngineeringRevisionSnapshotV1> MakeRevision(EngineeringRevisionOwnerV1 owner, EngineeringProductRefV1? product = null)
    { var command = await NewRevision(owner, product: product); await owner.CommandAsync(command); return await owner.ReadAsync(command.RevisionId, null); }

    [CurrentPermissionPostgresFact]
    public async Task EngineeringRevision_actual_HTTP_create_patch_replay_and_current_revocation()
    {
        await using var h = await Seed(); await GrantEngineeringRevision(h); await RevisionRule(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        using var context = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/engineering-revisions/owner-v1/creation-context"));
        Assert.Equal(HttpStatusCode.OK, context.StatusCode);
        using var json = JsonDocument.Parse(await context.Content.ReadAsStringAsync());
        var command = new EngineeringRevisionCommandV1 { OperationId = Guid.NewGuid(), RevisionId = Guid.NewGuid(), Action = "CREATE",
            ExpectedAuthorityVersion = json.RootElement.GetProperty("data").GetProperty("authorityVersion").GetString(),
            Goal = "HTTP native revision", Reason = "New product", Origin = new("NEW_PRODUCT", "First design without a parent baseline") };
        async Task<HttpResponseMessage> Save(EngineeringRevisionCommandV1 body)
        {
            var request = OperationsRequest(cookies, HttpMethod.Post, "api/engineering-revisions/owner-v1/commands");
            request.Content = new StringContent(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json");
            return await host.Send(request);
        }
        using var first = await Save(command); Assert.Equal(HttpStatusCode.OK, first.StatusCode); Assert.True(first.Headers.CacheControl?.NoStore);
        using var replay = await Save(command); Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        using var read = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"api/engineering-revisions/owner-v1/{command.RevisionId}"));
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var data = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        var value = data.RootElement.GetProperty("data").Deserialize<EngineeringRevisionSnapshotV1>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        using var changed = await Save(PatchRevision(value, new EngineeringDraftPatchV1 { Op = "SET_GOAL", Goal = "Explicit HTTP edit" })); Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var menu = await h.Db.Sys_Menus.Where(m => m.MenuKey == "plm-engineering-revision").Select(m => m.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == menu && a.ActionCode == "query").ExecuteDeleteAsync();
        using var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"api/engineering-revisions/owner-v1/operations/{command.OperationId}"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task EngineeringRevision_native_number_rule_original_replay_and_immutable_history()
    {
        await using var h = await Seed(); await GrantEngineeringRevision(h); var owner = Revisions(h); var create = await NewRevision(owner);
        Assert.Equal("ENGINEERING_NUMBER_RULE_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(create))).Message);
        Assert.Empty(await owner.ListAsync(null)); Assert.Equal("UNKNOWN", (await owner.OperationAsync(create.OperationId, false)).State);
        await RevisionRule(h); var receipt = await owner.CommandAsync(create); Assert.Equal(receipt, await owner.CommandAsync(create));
        Assert.Equal(1, await h.Db.Pub_DocSequences.Select(row => row.CurrentValue).SingleAsync());
        var original = await owner.ReadAsync(create.RevisionId, null); Assert.Equal("ENG0001", original.RevisionCode);
        Assert.NotEqual(original.Reference.Id, original.CandidateRef.Id); Assert.Null(original.Draft.ProductRef);
        Assert.Contains("PRODUCT_UNRESOLVED", original.Missing!); Assert.Equal("DRAFT_ONLY", original.Readiness);
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(create with { Goal = "Changed input" }))).Message);
        var patch = PatchRevision(original, new EngineeringDraftPatchV1 { Op = "SET_GOAL", Goal = "Next design" }); await owner.CommandAsync(patch);
        var current = await owner.ReadAsync(create.RevisionId, null); Assert.Equal("2", current.Reference.Version); Assert.Equal("1", current.ParentWorkingVersion);
        Assert.Equal(original.CandidateRef.Id, current.CandidateRef.Id); Assert.NotEqual(original.Etag, current.Etag);
        Assert.Equal(original.OriginalJson, (await owner.ReadAsync(create.RevisionId, "1")).OriginalJson);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(patch with { OperationId = Guid.NewGuid() }))).Status);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(PatchRevision(current, new EngineeringDraftPatchV1 { Op = "SET_GOAL", Goal = "Wrong token" }) with { ExpectedEtag = original.Etag }))).Status);
        foreach (var sql in new[] { "UPDATE cp6_engineering_revision_v1.working_versions SET body_bytes=decode('7b7d','hex')", "DELETE FROM cp6_engineering_revision_v1.working_versions", "TRUNCATE cp6_engineering_revision_v1.working_versions CASCADE", "DELETE FROM cp6_engineering_revision_v1.heads" })
            Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlRawAsync(sql))).SqlState);
    }

    [CurrentPermissionPostgresFact]
    public async Task EngineeringRevision_configuration_precision_occurrence_identity_and_explicit_patch_diff()
    {
        await using var h = await Seed(); await GrantEngineeringRevision(h); await GrantEngineering(h); await RevisionRule(h);
        var product = await MakeEngineering(h); var owner = Revisions(h); var a = await MakeRevision(owner, product.Reference);
        var source = new EngineeringSourceRefV1(product.Reference.Owner, product.Reference.Id, product.Reference.Version, product.Reference.Digest);
        var left = new EngineeringComponentDraftV1(Guid.NewGuid(), "PRODUCT", "assembly/left", 20, "APPLIES", "", "left mount", source);
        var right = left with { OccurrenceId = Guid.NewGuid(), Path = "assembly/right", SemanticOrder = 10, Note = "right mount" };
        var missing = new EngineeringComponentDraftV1(Guid.NewGuid(), "EBOM", "assembly/bom", 30, "UNKNOWN", "BOM source not ready", "");
        const string exact = "123456789012345678901234567890.000000000000000000123400";
        var config = new EngineeringConfigurationInputV1([new("Thickness", "DECIMAL", Decimal: exact), new("Finish", "STRING", Text: "Black"), new("Extra", "BOOLEAN", Boolean: false)], "UNKNOWN", "Rule not yet available");
        await owner.CommandAsync(PatchRevision(a, new EngineeringDraftPatchV1 { Op = "SET_CONFIGURATION", Configuration = config },
            new EngineeringDraftPatchV1 { Op = "UPSERT_COMPONENT", Component = left }, new EngineeringDraftPatchV1 { Op = "UPSERT_COMPONENT", Component = right }, new EngineeringDraftPatchV1 { Op = "UPSERT_COMPONENT", Component = missing }));
        var b = await owner.ReadAsync(a.Reference.Id, null); Assert.Equal(exact, b.Draft.Configuration!.Input.Options[0].Decimal);
        Assert.Equal(3, b.Draft.Components.Length); Assert.Equal(new[] { 20, 10, 30 }, b.Draft.Components.Select(x => x.SemanticOrder));
        Assert.Equal(2, b.Draft.Components.Count(x => x.SourceRef == source)); Assert.NotEqual(b.Reference.Id, b.Draft.Configuration.Reference.Id);
        Assert.Contains("CONFIGURATION_RULE_OWNER_REQUIRED", b.Missing!); Assert.Contains("SOURCE_REQUIRED:" + missing.OccurrenceId, b.Missing!);
        await owner.CommandAsync(PatchRevision(b, new EngineeringDraftPatchV1 { Op = "REMOVE_COMPONENT", OccurrenceId = left.OccurrenceId }, new EngineeringDraftPatchV1 { Op = "SET_GOAL", Goal = "Retain right mount" }));
        var c = await owner.ReadAsync(a.Reference.Id, null); Assert.Equal(JsonSerializer.Serialize(b.Draft.Configuration), JsonSerializer.Serialize(c.Draft.Configuration)); Assert.Equal(product.Reference, c.Draft.ProductRef);
        Assert.Equal(new[] { right, missing }, c.Draft.Components); var diff = await owner.DiffAsync(a.Reference.Id, "2", "3");
        Assert.Equal(2, diff.Changes.Length); Assert.Contains(diff.Changes, x => x.Path == "components/" + left.OccurrenceId && x.Before != null && x.After == null);
        Assert.Equal(b.OriginalJson, (await owner.ReadAsync(a.Reference.Id, "2")).OriginalJson);
        await owner.CommandAsync(PatchRevision(c, new EngineeringDraftPatchV1 { Op = "SET_CONFIGURATION", Configuration = config }));
        var d = await owner.ReadAsync(a.Reference.Id, null); Assert.NotEqual(c.Draft.Configuration!.Reference.Id, d.Draft.Configuration!.Reference.Id);
        Assert.Equal("ENGINEERING_COMPONENT_SOURCE_OWNER_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(PatchRevision(d,
            new EngineeringDraftPatchV1 { Op = "UPSERT_COMPONENT", Component = missing with { SourceRef = source } })))).Message);
        Assert.Equal(400, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(PatchRevision(d,
            new EngineeringDraftPatchV1 { Op = "UPSERT_COMPONENT", Component = missing with { Applicability = "NOT_APPLICABLE", Reason = "" } })))).Status);
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(PatchRevision(d,
            new EngineeringDraftPatchV1 { Op = "SET_CONFIGURATION", Configuration = config with { RuleRef = source } })))).Status);
        foreach (var invalid in new[] { "-0.00", "01", "1e3", "+1", "NaN", new string('1', 1025), "0." + new string('1', 1025) })
            Assert.Equal(400, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(PatchRevision(d,
                new EngineeringDraftPatchV1 { Op = "SET_CONFIGURATION", Configuration = config with { Options = [new("Limit", "DECIMAL", Decimal: invalid)] } })))).Status);
        var boundary = new string('9', 1024) + "." + new string('1', 1024);
        await owner.CommandAsync(PatchRevision(d, new EngineeringDraftPatchV1 { Op = "SET_CONFIGURATION", Configuration = config with { Options = [new("Limit", "DECIMAL", Decimal: boundary)] } }));
        Assert.Equal(boundary, (await owner.ReadAsync(d.Reference.Id, null)).Draft.Configuration!.Input.Options[0].Decimal);

    }

    [CurrentPermissionPostgresFact]
    public async Task EngineeringRevision_product_resolution_is_once_and_current_source_access_controls_history()
    {
        await using var h = await Seed(); await GrantEngineeringRevision(h); await GrantEngineering(h); await RevisionRule(h);
        var product = await MakeEngineering(h); var another = await MakeEngineering(h); var owner = Revisions(h); var a = await MakeRevision(owner);
        await owner.CommandAsync(PatchRevision(a, new EngineeringDraftPatchV1 { Op = "SET_PRODUCT", ProductRef = product.Reference }));
        var b = await owner.ReadAsync(a.Reference.Id, null); Assert.Equal(product.Reference, b.Draft.ProductRef);
        Assert.Null((await owner.ReadAsync(a.Reference.Id, "1")).Draft.ProductRef);
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(PatchRevision(b, new EngineeringDraftPatchV1 { Op = "SET_PRODUCT", ProductRef = another.Reference })))).Status);
        await Engineering(h).CommandAsync(ReviseEngineering(product, product.Product with { Definition = "Changed specification" }));
        Assert.Contains("PRODUCT_SOURCE_CHANGED", (await owner.ReadAsync(a.Reference.Id, null)).Missing!);
        var stale = await NewRevision(owner, product: product.Reference);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(stale))).Status);
        var updated = await Engineering(h).ReadAsync(product.Reference.Id, null);
        var patch = PatchRevision(b, new EngineeringDraftPatchV1 { Op = "SET_PRODUCT", ProductRef = updated.Reference }); await owner.CommandAsync(patch);
        var menu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "plm-engineering-product").Select(x => x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == menu && x.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ReadAsync(a.Reference.Id, "2"))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.OperationAsync(patch.OperationId, false))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(patch))).Status);
    }

    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task EngineeringRevision_department_creation_lookup_and_revoked_recovery(int type)
    {
        await using var h = await Seed(); await GrantEngineeringRevision(h); await RevisionRule(h); var owner = Revisions(h);
        var hidden = await MakeRevision(owner); var admin = await MappingAdmin(h);
        Assert.Equal(hidden.Reference.Id, Assert.Single(await StandaloneAdmin(h, admin).FindObjectsAsync("plm-engineering-revision", "ENG0001", _ => Task.FromResult(h.User), default)).Id);
        await StandaloneScope(h, admin, type); Assert.Empty(await owner.ListAsync(null));
        var missing = await NewRevision(owner); Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(missing))).Status);
        var create = await NewRevision(owner, admin.Child); await owner.CommandAsync(create); Assert.Equal(create.RevisionId, Assert.Single(await owner.ListAsync(null)).Reference.Id);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE tenant_id={h.Tenant} AND object_kind='plm-engineering-revision' AND object_id={create.RevisionId}");
        Assert.Empty(await owner.ListAsync(null)); Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.OperationAsync(create.OperationId, false))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task EngineeringRevision_commit_failure_rolls_back_number_binding_original_and_close_prevents_late_write()
    {
        var failure = new InterruptCurrencyCommit(); await using var h = await Seed(failure); await GrantEngineeringRevision(h); await RevisionRule(h);
        var owner = Revisions(h); var admin = await MappingAdmin(h); var command = await NewRevision(owner, admin.Child); failure.Interrupt = true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(command))).Message);
        Assert.Empty(await owner.ListAsync(null)); Assert.Equal("UNKNOWN", (await owner.OperationAsync(command.OperationId, false)).State);
        Assert.Equal(0, await h.Db.Pub_DocSequences.Select(row => row.CurrentValue).SingleAsync());
        Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_operations_owner_v1.department_mappings WHERE tenant_id={h.Tenant} AND object_kind='plm-engineering-revision' AND object_id={command.RevisionId}").SingleAsync());
        Assert.Equal("CLOSED", (await owner.OperationAsync(command.OperationId, true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(command))).Message);
        var saved = await owner.CommandAsync(await NewRevision(owner, admin.Child)); Assert.Equal("ENG0001", (await owner.ReadAsync(saved.ObjectId, null)).RevisionCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task EngineeringRevision_competing_CAS_writers_and_creator_tenant_separation()
    {
        await using var h = await Seed(); await GrantEngineeringRevision(h); await RevisionRule(h); var owner = Revisions(h); var a = await MakeRevision(owner);
        await using var peer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        async Task<string> Save(EngineeringRevisionOwnerV1 writer)
        { try { await writer.CommandAsync(PatchRevision(a, new EngineeringDraftPatchV1 { Op = "SET_GOAL", Goal = "Competing edit" })); return "COMMITTED"; } catch (OrderPriceCorrectionException error) { Assert.Equal(412, error.Status); return error.Message; } }
        var results = await Task.WhenAll(Save(owner), Save(Revisions(h, peer))); Assert.Single(results, x => x == "COMMITTED"); Assert.Single(results, x => x == "ENGINEERING_WORKING_VERSION_CHANGED");
        await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == "plm-engineering-revision").ExecuteUpdateAsync(update => update.SetProperty(x => x.ScopeType, 1));
        await h.Db.Sys_Users.Where(x => x.Id == h.User).ExecuteUpdateAsync(update => update.SetProperty(x => x.UserName, "renamed-" + h.User.ToString("N")));
        Assert.Equal(a.Reference.Id, (await owner.ReadAsync(a.Reference.Id, null)).Reference.Id);
        await using var foreign = await Seed(); await GrantEngineeringRevision(foreign); await RevisionRule(foreign); var other = await MakeRevision(Revisions(foreign));
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ReadAsync(other.Reference.Id, null))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task EngineeringRevision_absent_parent_change_legacy_owners_cannot_create_false_origins()
    {
        await using var h = await Seed(); await GrantEngineeringRevision(h); await RevisionRule(h); var owner = Revisions(h);
        var command = await NewRevision(owner);
        foreach (var origin in new[] { new EngineeringRevisionOriginV1("CHANGE"), new EngineeringRevisionOriginV1("LEGACY_REVIEW") })
            Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(command with { Origin = origin }))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(command with { Origin = new("NEW_PRODUCT", "") }))).Status);
        Assert.Empty(await owner.ListAsync(null)); Assert.Equal(0, await h.Db.Pub_DocSequences.Select(row => row.CurrentValue).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task EngineeringRevision_resolving_duplicate_product_revision_code_rejects_without_publishing_snapshot()
    {
        await using var h = await Seed(); await GrantEngineeringRevision(h); await GrantEngineering(h); await RevisionRule(h);
        var owner = Revisions(h); var product = await MakeEngineering(h); var bound = await MakeRevision(owner, product.Reference);
        // Explicit fixture rule reset models a numbering administrator changing the period/policy; no production reset.
        await h.Db.Pub_DocSequences.ExecuteUpdateAsync(update => update.SetProperty(x => x.CurrentValue, 0));
        var unresolved = await MakeRevision(owner); Assert.Equal(bound.RevisionCode, unresolved.RevisionCode);
        var command = PatchRevision(unresolved, new EngineeringDraftPatchV1 { Op = "SET_PRODUCT", ProductRef = product.Reference });
        Assert.Equal("ENGINEERING_REVISION_CODE_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(command))).Message);
        Assert.Equal(unresolved.OriginalJson, (await owner.ReadAsync(unresolved.Reference.Id, null)).OriginalJson);
        Assert.Equal("UNKNOWN", (await owner.OperationAsync(command.OperationId, false)).State);
    }

    [CurrentPermissionPostgresFact]
    public async Task EngineeringRevision_forward_migration_preserves_original_product_and_number_rule_without_grants()
    {
        var tenant = new TenantContext { CurrentTenantId = Guid.NewGuid() }; await using var db = server.Context(tenant);
        var name = "w151_revision_" + Guid.NewGuid().ToString("N"); await db.Database.ExecuteSqlRawAsync($"CREATE DATABASE {name}");
        db.Database.SetConnectionString(new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()) { Database = name }.ConnectionString);
        await db.GetService<IMigrator>().MigrateAsync("20261026191000_EngineeringMappingV1");
        db.Sys_Tenants.Add(new() { Id = tenant.CurrentTenantId, TenantCode = name, TenantName = "Original tenant" });
        db.Pub_DocSequences.Add(new() { BizKey = "ENG_REV", Prefix = "ORIGINAL", SeqLength = 5, ResetCycle = 0, CurrentValue = 7 });
        db.Sys_Menus.Add(new() { MenuId = int.MaxValue, MenuKey = "original-menu-max", MenuName = "Original menu" });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear(); EngineeringProductVersionV1 original;
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var store = new EngineeringProductStoreV1(db); await store.Fence(true, default);
            original = await store.Append(Guid.NewGuid(), Guid.NewGuid(), EngineeringInput(), null, default); await transaction.CommitAsync();
        }
        await db.Database.MigrateAsync(); Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var store = new EngineeringProductStoreV1(db); await store.Fence(false, default);
            Assert.Equal(original.OriginalJson, (await store.Read(original.Reference.Id, null, default)).OriginalJson); await transaction.CommitAsync();
        }
        var rule = await db.Pub_DocSequences.SingleAsync(); Assert.Equal("ORIGINAL", rule.Prefix); Assert.Equal(7, rule.CurrentValue); Assert.Equal(5, rule.SeqLength);
        var menu = await db.Sys_Menus.Where(x => x.MenuKey == "plm-engineering-revision").SingleAsync(); Assert.NotEqual(int.MaxValue, menu.MenuId);
        Assert.False(await db.Sys_RoleActions.AnyAsync(x => x.MenuId == menu.MenuId));
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM cp6_engineering_revision_v1.heads").SingleAsync());
    }
}
