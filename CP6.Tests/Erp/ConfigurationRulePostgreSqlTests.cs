using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using Npgsql;
using CP6.Tests.Platform;
using System.Net;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task ConfigurationRule_actual_HTTP_entry_exists()
    {
        await using var h = await Seed();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/configuration-rules/owner-v1/creation-context"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task GrantConfiguration(Harness h)
    {
        var menu = await h.Db.Sys_Menus.SingleAsync(m => m.MenuKey == "plm-configuration-rule");
        foreach (var action in new[] { "query", "add", "edit", "submit", "approve", "withdraw" })
        {
            if (!await h.Db.Sys_MenuActions.AnyAsync(a => a.MenuId == menu.MenuId && a.ActionCode == action))
                h.Db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = action, ActionName = action });
            h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = menu.MenuId, ActionCode = action });
        }
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = "plm-configuration-rule", ScopeType = 5 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
    }
    private static OperationsOwnerV1 ConfigurationAuthority(Harness h, CP6Context db)
    {
        var catalog = new AuthorizationCatalogStoreV1();
        foreach (var resource in new[] { "plm-configuration-rule", "plm-engineering-revision", "plm-engineering-product" })
        { catalog.RegisterScope(resource, resource, [1, 2, 3, 4, 5], 5); catalog.RegisterFields(resource, []); }
        catalog.Seal(); return new(db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), catalog.Capture);
    }
    private static ConfigurationRuleOwnerV1 ConfigurationRules(Harness h, CP6Context? context = null)
    { var db = context ?? h.Db; return new(db, ConfigurationAuthority(h, db)); }
    private static EngineeringRevisionOwnerV1 ConfigurationRevisions(Harness h) => new(h.Db, ConfigurationAuthority(h, h.Db));
    private static ConfigurationRuleBodyV1 OptionsBody(EngineeringProductRefV1 product) => new("ENGINEERING_OPTIONS_ONLY", "Explicit engineering option constraints", product,
        [new("Finish", "STRING", true, false, [new("Finish", "STRING", Text: "Black"), new("Finish", "STRING", Text: "White")]),
         new("Thickness", "DECIMAL", true, false, [new("Thickness", "DECIMAL", Decimal: "123456789012345678901234567890.00000000000000100")]),
         new("Extra", "BOOLEAN", false, true, []), new("Guard", "BOOLEAN", false, true, [])],
        [new("require-guard", "REQUIRES", new("Extra", "BOOLEAN", Boolean: true), new("Guard", "BOOLEAN", Boolean: true), "Extra requires a guard"),
         new("white-extra", "MUTEX", new("Finish", "STRING", Text: "White"), new("Extra", "BOOLEAN", Boolean: true), "Extra cannot use white finish")]);
    private static EngineeringOptionV1[] ValidOptions() => [new("Finish", "STRING", Text: "Black"), new("Thickness", "DECIMAL", Decimal: "123456789012345678901234567890.000000000000001")];
    private static async Task<ConfigurationRuleCommandV1> NewConfiguration(ConfigurationRuleOwnerV1 owner, EngineeringProductRefV1 product, Guid? department = null)
        => new() { OperationId = Guid.NewGuid(), RuleId = Guid.NewGuid(), Action = "CREATE", Reason = "Actual option scope from engineering basis", Body = OptionsBody(product),
            DepartmentId = department, ExpectedAuthorityVersion = (await owner.CreationContextAsync()).AuthorityVersion };
    private static ConfigurationRuleCommandV1 ConfigurationAction(ConfigurationRuleSnapshotV1 value, string action, ConfigurationRuleBodyV1? body = null)
        => new() { OperationId = Guid.NewGuid(), RuleId = value.Reference.Id, Action = action, ExpectedVersion = value.Reference.Version, ExpectedEtag = value.Etag,
            ExpectedActiveRef = value.ActiveRef, Reason = "Authorized explicit option rule decision", Body = body };
    private static async Task<ConfigurationRuleSnapshotV1> Step(ConfigurationRuleOwnerV1 owner, ConfigurationRuleSnapshotV1 value, string action, ConfigurationRuleBodyV1? body = null)
    { await owner.CommandAsync(ConfigurationAction(value, action, body)); return await owner.ReadAsync(value.Reference.Id, null); }
    private static async Task<ConfigurationRuleSnapshotV1> ApprovedConfiguration(Harness h, EngineeringProductRefV1 product)
    {
        var owner = ConfigurationRules(h); var command = await NewConfiguration(owner, product); await owner.CommandAsync(command);
        var value = await owner.ReadAsync(command.RuleId, null); value = await Step(owner, value, "SUBMIT"); return await Step(owner, value, "APPROVE");
    }
    [CurrentPermissionPostgresFact]
    public async Task ConfigurationRule_HTTP_create_review_approve_and_revoke_original_disclosure()
    {
        await using var h = await Seed(); await GrantConfiguration(h); await GrantEngineering(h); var product = await MakeEngineering(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        using var creation = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/configuration-rules/owner-v1/creation-context"));
        Assert.Equal(HttpStatusCode.OK, creation.StatusCode); var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        using var json = JsonDocument.Parse(await creation.Content.ReadAsStringAsync());
        var input = (await NewConfiguration(ConfigurationRules(h), product.Reference)) with { ExpectedAuthorityVersion = json.RootElement.GetProperty("data").GetProperty("authorityVersion").GetString() };
        async Task<HttpResponseMessage> Save(ConfigurationRuleCommandV1 body)
        { var request = OperationsRequest(cookies, HttpMethod.Post, "api/configuration-rules/owner-v1/commands"); request.Content = new StringContent(JsonSerializer.Serialize(body, options), Encoding.UTF8, "application/json"); return await host.Send(request); }
        using var first = await Save(input); Assert.Equal(HttpStatusCode.OK, first.StatusCode); Assert.True(first.Headers.CacheControl?.NoStore);
        using var replay = await Save(input); Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        var owner = ConfigurationRules(h); var draft = await owner.ReadAsync(input.RuleId, null);
        using var submit = await Save(ConfigurationAction(draft, "SUBMIT")); Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        var review = await owner.ReadAsync(input.RuleId, null);
        using var approve = await Save(ConfigurationAction(review, "APPROVE")); Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.True((await owner.ReadAsync(input.RuleId, null)).IsActive);
        var menu = await h.Db.Sys_Menus.Where(m => m.MenuKey == "plm-configuration-rule").Select(m => m.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == menu && a.ActionCode == "query").ExecuteDeleteAsync();
        using var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"api/configuration-rules/owner-v1/operations/{input.OperationId}"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }
    [CurrentPermissionPostgresFact]
    public async Task ConfigurationRule_draft_does_not_replace_active_and_withdraw_preserves_draft_and_history()
    {
        await using var h = await Seed(); await GrantConfiguration(h); await GrantEngineering(h); var product = await MakeEngineering(h); var owner = ConfigurationRules(h);
        var approved = await ApprovedConfiguration(h, product.Reference); Assert.Equal("3", approved.Reference.Version);
        var draft = await Step(owner, approved, "REVISE", approved.Body with { Name = "A new draft" });
        Assert.Equal(approved.Reference, draft.ActiveRef); Assert.False(draft.IsActive); Assert.True((await owner.ReadAsync(draft.Reference.Id, "3")).IsActive);
        var withdrawn = await Step(owner, draft, "WITHDRAW"); Assert.Null(withdrawn.ActiveRef); Assert.Equal("DRAFT", withdrawn.State); Assert.Equal(draft.Body.Name, withdrawn.Body.Name);
        Assert.False((await owner.ReadAsync(approved.Reference.Id, "3")).IsActive); Assert.Equal(approved.OriginalJson, (await owner.ReadAsync(approved.Reference.Id, "3")).OriginalJson);
        var review = await Step(owner, withdrawn, "SUBMIT"); var returned = await Step(owner, review, "RETURN"); Assert.Equal("DRAFT", returned.State);
        var next = await Step(owner, await Step(owner, returned, "SUBMIT"), "APPROVE"); Assert.True(next.IsActive);
        var final = await Step(owner, next, "WITHDRAW"); Assert.Equal("WITHDRAWN", final.State); Assert.Null(final.ActiveRef);
        foreach (var sql in new[] { "UPDATE cp6_configuration_rule_v1.versions SET body_bytes=decode('7b7d','hex')", "DELETE FROM cp6_configuration_rule_v1.versions", "TRUNCATE cp6_configuration_rule_v1.versions CASCADE", "DELETE FROM cp6_configuration_rule_v1.heads" })
            Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlRawAsync(sql))).SqlState);
    }
    [CurrentPermissionPostgresFact]
    public async Task ConfigurationRule_explicit_supersession_authority_source_and_stale_versions()
    {
        await using var h = await Seed(); await GrantConfiguration(h); await GrantEngineering(h); var product = await MakeEngineering(h); var owner = ConfigurationRules(h);
        var old = await ApprovedConfiguration(h, product.Reference); var draft = await Step(owner, old, "REVISE", old.Body);
        var review = await Step(owner, draft, "SUBMIT"); var approval = ConfigurationAction(review, "APPROVE");
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(approval with { ExpectedActiveRef = null }))).Status);
        var menu = await h.Db.Sys_Menus.Where(m => m.MenuKey == "plm-configuration-rule").Select(m => m.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == menu && a.ActionCode == "approve").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(approval))).Status);
        h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = menu, ActionCode = "approve" }); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var receipt = await owner.CommandAsync(approval); Assert.Equal(receipt, await owner.CommandAsync(approval));
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(approval with { Reason = "another reason" }))).Message);
        Assert.False((await owner.ReadAsync(old.Reference.Id, old.Reference.Version)).IsActive);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(approval with { OperationId = Guid.NewGuid() }))).Status);
        var current = await owner.ReadAsync(old.Reference.Id, null); await Engineering(h).CommandAsync(ReviseEngineering(product, product.Product with { Definition = "New source" }));
        Assert.Contains("PRODUCT_SOURCE_CHANGED", (await owner.ReadAsync(old.Reference.Id, null)).Missing!);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(ConfigurationAction(current, "REVISE", current.Body)))).Status);
        Assert.Null((await Step(owner, current, "WITHDRAW")).ActiveRef);
    }
    [CurrentPermissionPostgresFact]
    public async Task ConfigurationRule_typed_checks_preserve_precision_missing_conflicts_and_known_mismatch()
    {
        await using var h = await Seed(); await GrantConfiguration(h); await GrantEngineering(h); var product = await MakeEngineering(h); var rule = await ApprovedConfiguration(h, product.Reference);
        ConfigurationOptionCheckV1 Check(params EngineeringOptionV1[] values) => ConfigurationRuleValuesV1.Evaluate(rule, values);
        Assert.Equal("OPTIONS_VALIDATED", Check(ValidOptions()).Status);
        Assert.Equal("UNKNOWN", Check().Status); Assert.Equal("UNKNOWN", Check([..ValidOptions(), new("Extra", "BOOLEAN", Boolean: true)]).Status);
        Assert.Equal("CONFLICT", Check([..ValidOptions(), new("Extra", "BOOLEAN", Boolean: true), new("Guard", "BOOLEAN", Boolean: false)]).Status);
        Assert.Equal("CONFLICT", Check([new("Finish", "STRING", Text: "White"), ValidOptions()[1], new("Extra", "BOOLEAN", Boolean: true), new("Guard", "BOOLEAN", Boolean: true)]).Status);
        var invalid = Check([ValidOptions()[0], ValidOptions()[1] with { Decimal = "123456789012345678901234567890.00000000000000101" }]); Assert.Equal("NO_MATCH", invalid.Status);
        Assert.Equal("NO_MATCH", Check([..ValidOptions(), new("Undeclared", "STRING", Text: "x")]).Status);
        Assert.Equal("UNKNOWN", Check([new("Undeclared", "STRING", Text: "x")]).Status);
        Assert.Equal("OPTIONS_VALIDATED", Check([..ValidOptions(), new("Extra", "BOOLEAN", Boolean: false)]).Status);
        var owner = ConfigurationRules(h); var edit = ConfigurationAction(rule, "REVISE", rule.Body with { Definitions = [rule.Body.Definitions[0] with { AllowedValues = [new("Finish", "STRING", Text: "Black"), new("Finish", "STRING", Text: "Black")] }] });
        Assert.Equal(400, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(edit))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(edit with { Body = rule.Body with { Constraints = [rule.Body.Constraints[0] with { Then = new("Missing", "BOOLEAN", Boolean: true) }] } }))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(edit with { Body = rule.Body with { Scope = "ALL_CONFIGURATION" } }))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task ConfigurationRule_revision_same_transaction_adoption_immutable_observation_and_revocation()
    {
        await using var h = await Seed(); await GrantConfiguration(h); await GrantEngineering(h); await GrantEngineeringRevision(h); await RevisionRule(h);
        var product = await MakeEngineering(h); var rule = await ApprovedConfiguration(h, product.Reference); var owner = ConfigurationRevisions(h); var draft = await MakeRevision(owner, product.Reference);
        var config = new EngineeringConfigurationInputV1(ValidOptions(), "UNKNOWN", "Only option constraints reviewed", rule.Reference);
        var save = PatchRevision(draft, new EngineeringDraftPatchV1 { Op = "SET_CONFIGURATION", Configuration = config }); await owner.CommandAsync(save);
        var adopted = await owner.ReadAsync(draft.Reference.Id, null); Assert.Equal("OPTIONS_VALIDATED", adopted.Draft.Configuration!.OptionCheck!.Status);
        Assert.Equal(rule.Reference, adopted.Draft.Configuration.OptionCheck.RuleRef); Assert.Equal("DRAFT_ONLY", adopted.Readiness);
        Assert.Contains("CONFIGURATION_APPLICABILITY_REQUIRED", adopted.Missing!);
        await Step(ConfigurationRules(h), rule, "WITHDRAW"); var observed = await owner.ReadAsync(draft.Reference.Id, null);
        Assert.Equal(adopted.OriginalJson, observed.OriginalJson); Assert.Equal(adopted.CandidateRef, observed.CandidateRef);
        Assert.Contains("CONFIGURATION_RULE_CHANGED", observed.Missing!); Assert.Equal("COMMITTED", (await owner.OperationAsync(save.OperationId, false)).State);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(PatchRevision(observed, new EngineeringDraftPatchV1 { Op = "SET_CONFIGURATION", Configuration = config })))).Status);
        var menu = await h.Db.Sys_Menus.Where(m => m.MenuKey == "plm-configuration-rule").Select(m => m.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == menu && a.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.OperationAsync(save.OperationId, false))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ReadAsync(draft.Reference.Id, null))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task ConfigurationRule_unapproved_or_wrong_product_cannot_be_adopted_but_missing_options_stay_draft()
    {
        await using var h = await Seed(); await GrantConfiguration(h); await GrantEngineering(h); await GrantEngineeringRevision(h); await RevisionRule(h);
        var product = await MakeEngineering(h); var rules = ConfigurationRules(h); var create = await NewConfiguration(rules, product.Reference); await rules.CommandAsync(create);
        var rule = await rules.ReadAsync(create.RuleId, null); var owner = ConfigurationRevisions(h); var draft = await MakeRevision(owner, product.Reference);
        async Task Apply(EngineeringSourceRefV1 reference, EngineeringOptionV1[] values) => await owner.CommandAsync(PatchRevision(draft,
            new EngineeringDraftPatchV1 { Op = "SET_CONFIGURATION", Configuration = new(values, "UNKNOWN", "work draft", reference) }));
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => Apply(rule.Reference, []))).Status);
        rule = await Step(rules, await Step(rules, rule, "SUBMIT"), "APPROVE");
        await Apply(rule.Reference, []); draft = await owner.ReadAsync(draft.Reference.Id, null);
        Assert.Equal("UNKNOWN", draft.Draft.Configuration!.OptionCheck!.Status); Assert.Contains("CONFIGURATION_OPTIONS_UNRESOLVED", draft.Missing!);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => Apply(rule.Reference with { Digest = new string('a', 64) }, ValidOptions()))).Status);
        var other = await MakeEngineering(h); draft = await MakeRevision(owner, other.Reference);
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => Apply(rule.Reference, ValidOptions()))).Status);
    }
    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task ConfigurationRule_department_creation_lookup_and_revoked_recovery(int type)
    {
        await using var h = await Seed(); await GrantConfiguration(h); await GrantEngineering(h); var owner = ConfigurationRules(h); var admin = await MappingAdmin(h);
        var product = await MakeEngineering(h); await BindStandalone(h, admin, "plm-engineering-product", product.Reference.Id, admin.Child);
        await StandaloneScope(h, admin, type);
        var create = await NewConfiguration(owner, product.Reference); Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(create))).Status);
        create = await NewConfiguration(owner, product.Reference, admin.Child); await owner.CommandAsync(create);
        Assert.Equal(create.RuleId, Assert.Single(await owner.ListAsync(null)).Reference.Id);
        Assert.Equal(create.RuleId, Assert.Single(await StandaloneAdmin(h, admin).FindObjectsAsync("plm-configuration-rule", create.RuleId.ToString(), _ => Task.FromResult(h.User), default)).Id);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE tenant_id={h.Tenant} AND object_kind='plm-configuration-rule' AND object_id={create.RuleId}");
        Assert.Empty(await owner.ListAsync(null)); Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.OperationAsync(create.OperationId, false))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task ConfigurationRule_commit_failure_rolls_back_rule_mapping_and_original_and_close_fences_late_write()
    {
        var failure = new InterruptCurrencyCommit(); await using var h = await Seed(failure); await GrantConfiguration(h); await GrantEngineering(h);
        var product = await MakeEngineering(h); var owner = ConfigurationRules(h); var admin = await MappingAdmin(h); var command = await NewConfiguration(owner, product.Reference, admin.Child); failure.Interrupt = true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(command))).Message);
        Assert.Empty(await owner.ListAsync(null)); Assert.Equal("UNKNOWN", (await owner.OperationAsync(command.OperationId, false)).State);
        Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_operations_owner_v1.department_mappings WHERE tenant_id={h.Tenant} AND object_kind='plm-configuration-rule'").SingleAsync());
        Assert.Equal("CLOSED", (await owner.OperationAsync(command.OperationId, true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(command))).Message);
        var saved = await owner.CommandAsync(await NewConfiguration(owner, product.Reference, admin.Child)); Assert.Equal("1", saved.Version);
    }
    [CurrentPermissionPostgresFact]
    public async Task ConfigurationRule_competing_approvers_creator_uuid_and_tenant_separation()
    {
        await using var h = await Seed(); await GrantConfiguration(h); await GrantEngineering(h); var product = await MakeEngineering(h); var owner = ConfigurationRules(h);
        var command = await NewConfiguration(owner, product.Reference); await owner.CommandAsync(command); var review = await Step(owner, await owner.ReadAsync(command.RuleId, null), "SUBMIT");
        await using var peer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        async Task<string> Save(ConfigurationRuleOwnerV1 writer)
        { try { await writer.CommandAsync(ConfigurationAction(review, "APPROVE")); return "COMMITTED"; } catch (OrderPriceCorrectionException error) { Assert.Equal(412, error.Status); return error.Message; } }
        var results = await Task.WhenAll(Save(owner), Save(ConfigurationRules(h, peer))); Assert.Single(results, x => x == "COMMITTED"); Assert.Single(results, x => x == "CONFIGURATION_RULE_CHANGED");
        await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == "plm-configuration-rule").ExecuteUpdateAsync(update => update.SetProperty(x => x.ScopeType, 1));
        await h.Db.Sys_Users.Where(x => x.Id == h.User).ExecuteUpdateAsync(update => update.SetProperty(x => x.UserName, "renamed-" + h.User.ToString("N")));
        Assert.Equal(command.RuleId, (await owner.ReadAsync(command.RuleId, null)).Reference.Id);
        await using var foreign = await Seed(); await GrantConfiguration(foreign); await GrantEngineering(foreign); var foreignProduct = await MakeEngineering(foreign);
        var other = await ApprovedConfiguration(foreign, foreignProduct.Reference);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ReadAsync(other.Reference.Id, null))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task ConfigurationRule_forward_migration_preserves_legacy_configuration_original_candidate_and_grants()
    {
        var tenant = new TenantContext { CurrentTenantId = Guid.NewGuid() }; await using var db = server.Context(tenant);
        var name = "w152_cfg_" + Guid.NewGuid().ToString("N"); await db.Database.ExecuteSqlRawAsync($"CREATE DATABASE {name}");
        db.Database.SetConnectionString(new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()) { Database = name }.ConnectionString);
        await db.GetService<IMigrator>().MigrateAsync("20261026200000_EngineeringRevisionDraftV1");
        db.Sys_Tenants.Add(new() { Id = tenant.CurrentTenantId, TenantCode = name, TenantName = "Existing tenant" });
        db.Sys_Menus.Add(new() { MenuId = int.MaxValue, MenuKey = "original-cfg-max", MenuName = "Original menu" });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear(); EngineeringRevisionSnapshotV1 original;
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            var store = new EngineeringRevisionStoreV1(db); await store.Fence(true, default);
            var config = EngineeringRevisionValuesV1.Configuration(new([new("Original", "DECIMAL", Decimal: "12.3400")], "UNKNOWN", "Existing configuration draft"));
            original = await store.Append(Guid.NewGuid(), Guid.NewGuid(), new(null, "Original working draft", config, []), "Original", null, null,
                "OLD0007", new("NEW_PRODUCT", "No old baseline"), new(Guid.NewGuid(), "ENG_REV", "OLD", null, 4, 0, null, 7, DateTimeOffset.UtcNow), default);
            Assert.DoesNotContain("optionCheck", original.OriginalJson); await tx.CommitAsync();
        }
        await db.Database.MigrateAsync(); Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            var store = new EngineeringRevisionStoreV1(db); await store.Fence(false, default); var current = await store.Read(original.Reference.Id, null, default);
            Assert.Equal(original.OriginalJson, current.OriginalJson); Assert.Equal(original.CandidateRef, current.CandidateRef); Assert.Equal(original.Draft.Configuration!.Reference, current.Draft.Configuration!.Reference);
            await tx.CommitAsync();
        }
        var menu = await db.Sys_Menus.Where(x => x.MenuKey == "plm-configuration-rule").SingleAsync(); Assert.NotEqual(int.MaxValue, menu.MenuId);
        Assert.False(await db.Sys_RoleActions.AnyAsync(x => x.MenuId == menu.MenuId));
        Assert.Equal(6, await db.Sys_MenuActions.Where(x => x.MenuId == menu.MenuId).CountAsync());
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM cp6_configuration_rule_v1.heads").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task ConfigurationRule_adoption_commit_failure_leaves_original_working_snapshot_and_no_receipt()
    {
        var failure = new InterruptCurrencyCommit(); await using var h = await Seed(failure); await GrantConfiguration(h); await GrantEngineering(h); await GrantEngineeringRevision(h); await RevisionRule(h);
        var product = await MakeEngineering(h); var rule = await ApprovedConfiguration(h, product.Reference); var owner = ConfigurationRevisions(h); var draft = await MakeRevision(owner, product.Reference);
        Assert.Equal(rule.Reference.Id, Assert.Single(await ConfigurationRules(h).ListAsync(product.Reference.Id.ToString())).Reference.Id);
        var command = PatchRevision(draft, new EngineeringDraftPatchV1 { Op = "SET_CONFIGURATION", Configuration = new(ValidOptions(), "UNKNOWN", "Actual option check", rule.Reference) });
        failure.Interrupt = true; Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(command))).Message);
        Assert.Equal(draft.OriginalJson, (await owner.ReadAsync(draft.Reference.Id, null)).OriginalJson); Assert.Equal("UNKNOWN", (await owner.OperationAsync(command.OperationId, false)).State);
        await owner.CommandAsync(command); Assert.Equal("2", (await owner.ReadAsync(draft.Reference.Id, null)).Reference.Version);
        Assert.Equal("COMMITTED", (await owner.OperationAsync(command.OperationId, false)).State);
    }
}
