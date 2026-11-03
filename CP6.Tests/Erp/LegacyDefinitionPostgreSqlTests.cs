using System.Net;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private async Task<(FlowAuthoringOwnerV2 Flow, FormAuthoringOwnerV2 Form, string FlowKey)> LegacyReadSetup(Harness h)
    {
        var flow = await PrepareAuthoring(h); var form = FormAuthoring(h);
        await EngineeringAction(h, "oa-designer", "form-save");
        await form.ConfigureAsync(new(Guid.NewGuid(), "absent", true, [], "simulated form authoring policy"));
        foreach (var resource in new[] { FlowAuthoringOwnerV2.Resource, FormAuthoringOwnerV2.Resource })
            h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = resource, ScopeType = 5 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); var (key, _) = await PublishedDraftSource(h);
        h.Db.Wf_FormDefs.Add(new() { Id = Guid.NewGuid(), FormKey = "legacy-form", FormName = "Legacy form", SchemaJson = FormAuthoringSchema, Version = 9, Category = "current-only" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); return (flow, form, key);
    }
    [CurrentPermissionPostgresFact]
    public async Task DefinitionLegacy_source_classification_preserves_exact_bytes_without_manufacturing_publication_facts()
    {
        await using var h = await Seed(); var f = await LegacyReadSetup(h);
        var row = await h.Db.Wf_FormDefs.SingleAsync();
        h.Db.Wf_FormDefVersions.Add(new() { Id = Guid.NewGuid(), FormDefId = row.Id, Version = 8, Status = 1,
            FormNameSnapshot = "Reconstructed", SchemaJson = FormAuthoringSchema, Creator = "oa-p0-backfill", PublishedAtUtc = DateTime.UtcNow, PublishedBy = h.User });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var forms = await f.Form.LegacyAsync("legacy-form"); Assert.Equal(2, forms.Length);
        Assert.Equal("CURRENT_HEAD_RECONSTRUCTION", forms[0].Source); Assert.Null(forms[0].VersionId); Assert.Null(forms[0].RecordedPublishedAtUtc);
        Assert.Equal("LEGACY_HEAD_BACKFILL", forms[1].Source); Assert.Null(forms[1].RecordedPublishedAtUtc); Assert.Null(forms[1].RecordedPublishedBy);
        Assert.False(forms[1].Fields.ContainsKey("Category")); Assert.Equal(FormAuthoringSchema, forms[1].Fields["SchemaJson"]);
        var flows = await f.Flow.LegacyAsync(f.FlowKey); var snapshot = Assert.Single(flows.Where(x => x.VersionId != null));
        Assert.Equal("LEGACY_VERSION_SNAPSHOT", snapshot.Source); Assert.Equal(h.User, snapshot.RecordedPublishedBy);
        Assert.Equal(DraftFlowSchema, snapshot.Fields["SchemaJson"]); Assert.False(snapshot.Fields.ContainsKey("FormKey"));
        Assert.All(forms.Concat(flows), x => { Assert.Equal("UNVERIFIED_LEGACY", x.EvidenceState); Assert.False(x.CanWrite); });
        Assert.NotNull((await h.Db.Wf_FormDefVersions.AsNoTracking().SingleAsync()).PublishedAtUtc); // Read did not rewrite the stored historical row.
        Assert.Empty(h.Db.ChangeTracker.Entries()); Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task DefinitionLegacy_current_scope_fields_and_tenant_are_applied_before_disclosure()
    {
        await using var h = await Seed(); var f = await LegacyReadSetup(h);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = FlowAuthoringOwnerV2.Resource, FieldName = "SchemaJson", Access = 3 });
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = FormAuthoringOwnerV2.Resource, FieldName = "SchemaJson", Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.All(await f.Flow.LegacyAsync(f.FlowKey), x => Assert.False(x.Fields.ContainsKey("SchemaJson")));
        Assert.All(await f.Form.LegacyAsync("legacy-form"), x => Assert.False(x.Fields.ContainsKey("SchemaJson")));
        await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == FlowAuthoringOwnerV2.Resource).ExecuteUpdateAsync(x => x.SetProperty(p => p.ScopeType, 1));
        Assert.Equal("LEGACY_OWNER_SCOPE_REQUIRED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => f.Flow.LegacyAsync(f.FlowKey))).Message);
        await using var other = await Seed(); var foreign = await LegacyReadSetup(other);
        Assert.Equal(404, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => foreign.Flow.LegacyAsync(f.FlowKey))).Status);
        await h.Db.Sys_RoleMenus.Where(x => x.MenuId == 9172).ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => f.Form.LegacyAsync("legacy-form"))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task DefinitionLegacy_pages_do_not_drop_old_versions_or_infer_a_requested_foreign_version()
    {
        await using var h = await Seed(); var f = await LegacyReadSetup(h); var head = await h.Db.Wf_FlowDefs.SingleAsync();
        h.Db.Wf_FlowDefVersions.AddRange(Enumerable.Range(2, 100).Select(n => new Wf_FlowDefVersion { Id = Guid.NewGuid(), FlowDefId = head.Id,
            Version = n, Status = 1, FlowNameSnapshot = "Recorded " + n, SchemaJson = DraftFlowSchema }));
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var first = await f.Flow.LegacyAsync(f.FlowKey); var second = await f.Flow.LegacyAsync(f.FlowKey, page: 2);
        Assert.Equal(100, first.Count(x => x.VersionId != null)); Assert.Equal(1, Assert.Single(second).RecordedVersion);
        Assert.Equal(101, first.Concat(second).Where(x => x.VersionId != null).Select(x => x.VersionId).Distinct().Count());
        Assert.Equal(404, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => f.Flow.LegacyAsync(f.FlowKey, Guid.NewGuid()))).Status);
        Assert.Equal(1, (await f.Flow.LegacyAsync(f.FlowKey, second[0].VersionId)).Single().RecordedVersion);
    }
    [CurrentPermissionPostgresFact]
    public async Task DefinitionLegacy_helpers_reject_before_any_definition_mutation_and_runtime_cannot_fabricate_a_pin()
    {
        await using var h = await Seed(); var f = await LegacyReadSetup(h); var flow = new FlowDefService(h.Db); var form = new FormService(h.Db);
        var designer = new DesignerService(h.Db, flow, [], []);
        foreach (var call in new Func<Task>[] {
            () => flow.SaveDefAsync("new", "New", null, "{}"), () => flow.SaveDraftAsync(f.FlowKey, "Changed", null, "{}", null),
            () => flow.PublishAsync(f.FlowKey, null, h.User), () => form.SaveDefAsync("new-form", "New", "{}"),
            () => form.SaveDraftAsync("legacy-form", "Changed", "{}", null), () => form.PublishAsync("legacy-form", null, h.User),
            () => designer.SaveAsync(new("new", "New", "form", null, null, "{}"), "forged"),
            () => designer.CloneAsync(new(f.FlowKey, "copy", "Copy"), "forged"), () => new FlowAdminService(h.Db).SetEnabledAsync(f.FlowKey, true) })
            Assert.Equal(LegacyDefinitionWrites.Code, (await Assert.ThrowsAsync<InvalidOperationException>(call)).Message);
        Assert.Empty(h.Db.ChangeTracker.Entries()); Assert.Single(await h.Db.Wf_FlowDefs.ToArrayAsync()); Assert.Single(await h.Db.Wf_FormDefs.ToArrayAsync());
        h.Db.Wf_FlowDefs.Add(new() { Id = Guid.NewGuid(), FlowKey = "unverified", FlowName = "No source", SchemaJson = DraftFlowSchema });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); var engine = new FlowEngine(h.Db, new ApproverResolver(h.Db));
        Assert.Equal("UNVERIFIED_LEGACY", (await Assert.ThrowsAsync<InvalidOperationException>(() => engine.SubmitAsync("unverified", h.User, "{}"))).Message);
        Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync()); Assert.Single(await h.Db.Wf_FlowDefVersions.ToArrayAsync());
        await h.Db.Wf_FlowDefs.Where(x => x.FlowKey == f.FlowKey).ExecuteUpdateAsync(x => x.SetProperty(d => d.Enable, true));
        var id = await engine.SubmitAsync(f.FlowKey, h.User, "{}");
        var instance = await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal((await h.Db.Wf_FlowDefVersions.SingleAsync()).Id, instance.FlowDefVersionId); Assert.Equal(FlowInstanceStatus.Approved, instance.Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task DefinitionLegacy_missing_instance_pin_is_rejected_before_vote_and_missing_child_pin_never_guesses_latest()
    {
        await using var h = await Seed();
        var raw = JsonSerializer.Serialize(new { start = "s", nodes = new object[] { new { id = "s", type = "start" },
            new { id = "a", type = "approval", approverStrategy = "Specified", approverUserId = h.User }, new { id = "e", type = "end" } },
            edges = new[] { new { from = "s", to = "a" }, new { from = "a", to = "e" } } });
        await CP6.Tests.Wf.LegacyDefinitionFixture.Flow(h.Db, "recorded", "Recorded", null, raw);
        var engine = new FlowEngine(h.Db, new ApproverResolver(h.Db)); var id = await engine.SubmitAsync("recorded", h.User, "{}");
        h.Db.ChangeTracker.Clear(); var task = await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(x => x.InstanceId == id);
        // Simulated pre-version historical row: removing its pin is fixture setup, never a production repair.
        await h.Db.Wf_FlowInstances.Where(x => x.Id == id).ExecuteUpdateAsync(x => x.SetProperty(i => i.FlowDefVersionId, (Guid?)null));
        var history = await h.Db.Wf_FlowHistories.CountAsync();
        Assert.Equal("UNVERIFIED_LEGACY", (await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ActAsync(task.Id, h.User, true))).Message);
        Assert.False(h.Db.ChangeTracker.HasChanges());
        Assert.Equal(FlowTaskStatus.Pending, (await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(x => x.Id == task.Id)).Status);
        Assert.Equal(history, await h.Db.Wf_FlowHistories.CountAsync());
        Assert.Null((await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync(x => x.Id == id)).FlowDefVersionId);
        var source = await h.Db.Wf_FlowDefVersions.AsNoTracking().SingleAsync();
        var parent = new Wf_FlowInstance { Id = Guid.NewGuid(), FlowKey = "recorded", FlowDefVersionId = source.Id };
        Assert.Equal("UNVERIFIED_LEGACY", (await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ResolvePinnedSubFlowVersionAsync(parent, "missing-child", "recorded"))).Message);
        Assert.Empty(await h.Db.Wf_FlowDefVersionDependencies.ToArrayAsync()); Assert.Single(await h.Db.Wf_FlowInstances.ToArrayAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task DefinitionLegacy_original_create_result_remains_readable_but_never_creates_a_new_intent()
    {
        await using var h = await Seed(); var f = await LegacyReadSetup(h);
        var source = await h.Db.Wf_FlowDefVersions.AsNoTracking().SingleAsync();
        var historicalDraft = new Wf_FlowDefVersion { Id = Guid.NewGuid(), FlowDefId = source.FlowDefId, Version = 2,
            Status = WfDefinitionVersionStatus.Draft, FlowNameSnapshot = source.FlowNameSnapshot, SchemaJson = source.SchemaJson };
        h.Db.Wf_FlowDefVersions.Add(historicalDraft); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var command = new FlowDraftCreateV1(Guid.NewGuid(), source.Id, new string('a', 64));
        var original = new FlowDraftCreatedV1("Main.Workflow.Draft.v1", command.OperationId, source.Id,
            MainOwnerOperationJournalV1.Digest(Encoding.UTF8.GetBytes(source.SchemaJson)), new(source.FlowDefId, historicalDraft.Id, 2, source.FlowNameSnapshot, source.SchemaJson, historicalDraft.RowVersion, 0));
        await using (var tx = await h.Db.Database.BeginTransactionAsync())
        {
            await new MainOwnerOperationJournalV1(h.Db).Store("Main.Workflow.Draft.v1", "Main.Workflow.Draft.create.v1", command.OperationId, h.User,
                MainOwnerOperationJournalV1.Encode(new { Key = f.FlowKey, Request = command }), original, [source.FlowDefId], new { simulatedHistoricalOriginal = true }, default);
            await tx.CommitAsync();
        }
        var owner = DraftOwner(h); await h.Db.Sys_RoleActions.Where(x => x.ActionCode == "edit").ExecuteDeleteAsync();
        Assert.Equal(original.Draft.VersionId, (await owner.CreateAsync(f.FlowKey, command)).Draft.VersionId);
        Assert.Equal(original.Draft.VersionId, (await owner.OperationAsync(f.FlowKey, command.OperationId)).Result!.Draft.VersionId);
        Assert.Equal(410, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CreateAsync(f.FlowKey, command with { OperationId = Guid.NewGuid() }))).Status);
        Assert.Equal("COMMAND_BODY_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CreateAsync(f.FlowKey, command with { SourceVersionId = Guid.NewGuid() }))).Message);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = FlowAuthoringOwnerV2.Resource, FieldName = "SchemaJson", Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Equal("LEGACY_RESULT_FIELD_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.OperationAsync(f.FlowKey, command.OperationId))).Message);
        ((System.Security.Claims.ClaimsIdentity)h.Http.User.Identity!).AddClaim(new("impersonator_id", Guid.NewGuid().ToString("D")));
        Assert.Equal(401, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.OperationAsync(f.FlowKey, command.OperationId))).Status);
        Assert.Equal(2, await h.Db.Wf_FlowDefVersions.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task DefinitionLegacy_actual_HTTP_old_routes_fail_explicitly_and_versioned_read_is_private_and_read_only()
    {
        await using var h = await Seed(); var f = await LegacyReadSetup(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, new Dictionary<string, string> { ["ScopeAuthorization__EnvironmentId"] = ScopeEnvironment.ToString("D") });
        var cookies = await Login(host, h);
        foreach (var (method, path) in new[] {
            ("POST", "/api/wf/flow/def"), ("POST", "/api/wf/form/def"), ("GET", "/api/wf/form/def/legacy-form"), ("GET", "/api/wf/flow/def/old"),
            ("PUT", "/api/oa/flow-defs/old/draft"), ("POST", "/api/oa/flow-defs/old/publish"), ("GET", "/api/oa/flow-defs/old/versions"),
            ("PUT", "/api/oa/form-defs/legacy-form/draft"), ("POST", "/api/oa/form-defs/legacy-form/publish"), ("GET", "/api/oa/form-defs/legacy-form/draft"),
            ("POST", "/api/oa/designer/save"), ("POST", "/api/oa/designer/clone"), ("GET", "/api/oa/designer/load/old"), ("GET", "/api/oa/designer/list"),
            ("POST", "/api/oa/flow-admin/enable"), ("GET", "/api/oa/flow-admin/list") })
        {
            using var request = BoundRequest(cookies, path, method == "GET" ? null : new { }); request.Method = new HttpMethod(method);
            using var response = await host.Send(request); Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
            Assert.Contains(LegacyDefinitionWrites.Code, await response.Content.ReadAsStringAsync());
        }
        using var read = await host.Send(BoundRequest(cookies, "/api/oa/v2/legacy-definitions/FORM/legacy-form"));
        Assert.Equal(HttpStatusCode.OK, read.StatusCode); Assert.True(read.Headers.CacheControl?.NoStore);
        using var body = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        var view = body.RootElement.GetProperty("data")[0]; Assert.Equal("UNVERIFIED_LEGACY", view.GetProperty("evidenceState").GetString());
        Assert.Equal(JsonValueKind.Null, view.GetProperty("versionId").ValueKind); Assert.Equal(FormAuthoringSchema, view.GetProperty("fields").GetProperty("SchemaJson").GetString());
        Assert.Empty(await h.Db.Wf_FormDefVersions.ToArrayAsync()); Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());
    }
}
