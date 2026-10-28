using System.Net;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Common;
using CP6.Core.Services.Mes;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task EngineeringSourceCheck_actual_HTTP_CSRF_strict_input_CAS_history_and_recovery()
    {
        await using var h = await Seed(); var f = await CheckSetup(h); var settings = await DocumentHostSettings(f.Files); settings["QualitySpecification__EvidenceMode"] = "DEMO";
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, settings); var cookies = await Login(host, h);
        const string url = "/api/engineering-source-checks/owner-v1";
        async Task<HttpResponseMessage> Post(object value, bool csrf = true)
        { using var request = OperationsRequest(cookies, HttpMethod.Post, url + "/commands", value); if (!csrf) request.Headers.Remove("X-CSRF-Token"); return await host.Send(request); }
        using (var denied = await Post(f.Command, false)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var stale = await Post(f.Command with { ExpectedEtag = Guid.NewGuid() })) Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        var extended = JsonSerializer.SerializeToElement(f.Command, EngineeringProductStoreV1.Json).EnumerateObject().ToDictionary(x => x.Name, x => (object?)x.Value.Clone()); extended["pass"] = true;
        using (var strict = await Post(extended)) Assert.Equal(HttpStatusCode.BadRequest, strict.StatusCode);
        EngineeringSourceCheckResultV1 result;
        using (var saved = await Post(f.Command)) { result = await DocumentHttpData<EngineeringSourceCheckResultV1>(saved); Assert.True(saved.Headers.CacheControl?.NoStore); }
        using (var again = await Post(f.Command)) Assert.Equal(result, await DocumentHttpData<EngineeringSourceCheckResultV1>(again));
        using (var read = await host.Send(OperationsRequest(cookies, HttpMethod.Get, url + "/" + result.CheckRunId)))
        { var view = await DocumentHttpData<EngineeringCheckViewV1>(read); Assert.Equal("PASS", view.Original.State); Assert.Equal("CURRENT", view.Freshness); }
        using (var history = await host.Send(OperationsRequest(cookies, HttpMethod.Get, url + "/revisions/" + f.Command.Revision.Id))) Assert.Single(await DocumentHttpData<EngineeringCheckViewV1[]>(history));
        using (var recovery = await host.Send(OperationsRequest(cookies, HttpMethod.Get, url + "/operations/" + f.Command.OperationId))) Assert.Equal(result, (await DocumentHttpData<EngineeringSourceCheckOperationV1>(recovery)).Result);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringSourceCheck_same_UoW_policy_change_is_rechecked_and_rolled_back()
    {
        await using var h = await Seed(); var f = await CheckSetup(h); var checks = Checks(h, f.Files); var result = await checks.CheckAsync(f.Command);
        var view = await checks.ReadAsync(result.CheckRunId);
        var error = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => CheckAuthority(h).ExecuteAsync(OperationsOwnerV1.EngineeringRevision, "query", true, async (a, t) =>
        {
            await checks.HoldExact(a, view.Reference, t);
            // The real native rule writer runs inside the same consumer transaction; no synthetic HTTP token.
            var store = new ConfigurationRuleStoreV1(h.Db); await store.Fence(true, t);
            await store.Append(ConfigurationAction(f.Policy, "WITHDRAW"), a.ActorId, f.Policy.Body, "WITHDRAWN", t);
            return true;
        }));
        Assert.Equal("ENGINEERING_CHECK_SOURCE_CHANGED_AT_COMMIT", error.Message);
        Assert.True((await ConfigurationRules(h).ReadAsync(f.Policy.Reference.Id, null)).IsActive);
        Assert.Equal(view.OriginalJson, (await checks.ReadAsync(result.CheckRunId)).OriginalJson);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringSourceCheck_native_hold_blocks_actual_policy_withdrawal_until_commit()
    {
        await using var h = await Seed(); var f = await CheckSetup(h); var checks = Checks(h, f.Files); var result = await checks.CheckAsync(f.Command); var view = await checks.ReadAsync(result.CheckRunId);
        Task<ConfigurationRuleResultV1>? writer = null; var started = new TaskCompletionSource(); var application = "w193-policy-" + Guid.NewGuid().ToString("N");
        await CheckAuthority(h).ExecuteAsync(OperationsOwnerV1.EngineeringRevision, "query", false, async (a, t) =>
        {
            await checks.HoldExact(a, view.Reference, t);
            writer = Task.Run(async () =>
            {
                await using var peer = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); await peer.Database.OpenConnectionAsync();
                await peer.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('application_name',{application},false)"); started.SetResult();
                return await ConfigurationRules(h, peer).CommandAsync(ConfigurationAction(f.Policy, "WITHDRAW"));
            });
            await started.Task; var waiting = false; var deadline = DateTime.UtcNow.AddSeconds(8);
            while (!waiting && DateTime.UtcNow < deadline)
            {
                waiting = await h.Db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM pg_stat_activity WHERE application_name={application} AND wait_event_type='Lock') AS \"Value\"").SingleAsync(t);
                if (!waiting) await Task.Delay(25, t);
            }
            Assert.True(waiting); Assert.False(writer.IsCompleted); return true;
        });
        await writer!; Assert.Equal("STALE", (await checks.ReadAsync(result.CheckRunId)).Freshness);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringSourceCheck_missing_insert_rolls_back_original_and_exact_key_can_recover()
    {
        var hook = new StartWriteHook(); await using var h = await Seed(hook); var f = await CheckSetup(h); var checks = Checks(h, f.Files);
        hook.Before = c => { if (c.CommandText.Contains("INSERT INTO cp6_engineering_check_v1.runs")) c.CommandText = "SELECT 1"; return Task.CompletedTask; };
        Assert.Equal("ENGINEERING_CHECK_WRITE_INCOMPLETE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => checks.CheckAsync(f.Command))).Message); hook.Before = null;
        Assert.Equal("UNKNOWN", (await checks.OperationAsync(f.Command.OperationId, false)).State); Assert.Empty(await checks.HistoryAsync(f.Command.Revision.Id));
        var saved = await checks.CheckAsync(f.Command); Assert.Equal(saved, (await checks.OperationAsync(f.Command.OperationId, false)).Result);
        foreach (var sql in new[] { "DELETE FROM cp6_engineering_check_v1.runs", "UPDATE cp6_engineering_check_v1.runs SET actor_id=tenant_id", "TRUNCATE cp6_engineering_check_v1.runs CASCADE" })
            Assert.Contains("ENGINEERING_CHECK_IMMUTABLE", (await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlRawAsync(sql))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringSourceCheck_unavailable_original_keeps_prior_evidence_and_new_UNKNOWN()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); var f = await CheckSetup(h); var checks = Checks(h, f.Files);
        var first = await checks.CheckAsync(f.Command); var before = await checks.ReadAsync(first.CheckRunId);
        var injected = false;
        hook.Before = c => { if (c.CommandText.Contains("FROM cp6_quality_specification_v1.records") && c.CommandText.Contains("SELECT"))
            { injected = true; throw new OrderPriceCorrectionException("QUALITY_SOURCE_TEMPORARILY_UNAVAILABLE", 503); } return Task.CompletedTask; };
        var second = await checks.CheckAsync(f.Command with { OperationId = Guid.NewGuid() });
        Assert.True(injected);
        var unknown = await checks.ReadAsync(second.CheckRunId); Assert.Equal("UNKNOWN", unknown.Original.State);
        var historical = await checks.ReadAsync(first.CheckRunId); Assert.Equal(before.OriginalJson, historical.OriginalJson); Assert.Equal("STALE", historical.Freshness);
        hook.Before = null;
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringSourceCheck_tenant_field_scope_and_closed_original_do_not_leak_or_repeat()
    {
        await using var h = await Seed(); var f = await CheckSetup(h); var checks = Checks(h, f.Files); var saved = await checks.CheckAsync(f.Command);
        await using var other = await Seed(); Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => Checks(other, f.Files).ReadAsync(saved.CheckRunId))).Status);
        var unused = Guid.NewGuid(); Assert.Equal("CLOSED", (await checks.OperationAsync(unused, true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => checks.CheckAsync(f.Command with { OperationId = unused }))).Message);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "quality-specification", FieldName = "Requirements", Access = 3 }); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => checks.ReadAsync(saved.CheckRunId))).Status);
    }
    [Fact]
    public void EngineeringSourceCheck_technical_digest_excludes_annotations_preserves_occurrences_and_semantic_order()
    {
        var product = new EngineeringProductRefV1(EngineeringProductValuesV1.Owner, Guid.NewGuid(), "1", new string('a', 64));
        var reference = new EngineeringRevisionRefV1(EngineeringRevisionValuesV1.RevisionOwner, Guid.NewGuid(), "1", new string('b', 64));
        var component = new EngineeringComponentDraftV1(Guid.NewGuid(), "PRODUCT", "/first", 0, "APPLIES", "", "comment", EngineeringSourceCheckValuesV1.Ref(product));
        var draft = new EngineeringWorkingDraftV1(product, "Design goal", null, [component, component with { OccurrenceId = Guid.NewGuid(), Path = "/second", SemanticOrder = 1 }]);
        var value = new EngineeringRevisionSnapshotV1(Guid.NewGuid(), reference, "R1", Guid.NewGuid(), reference, true, "DRAFT", new("NEW_PRODUCT", "new"), draft, "reason", null, DateTimeOffset.UtcNow, "{}");
        var digest = EngineeringSourceCheckValuesV1.Manifest(value).Digest;
        Assert.Equal(digest, EngineeringSourceCheckValuesV1.Manifest(value with { Reason = "another audit reason", Draft = draft with { Goal = "annotation", Components = draft.Components.Reverse().Select(x => x with { Note = "another comment" }).ToArray() } }).Digest);
        Assert.NotEqual(digest, EngineeringSourceCheckValuesV1.Manifest(value with { Draft = draft with { Components = [component] } }).Digest);
        Assert.NotEqual(digest, EngineeringSourceCheckValuesV1.Manifest(value with { Draft = draft with { Components = [component with { SemanticOrder = 4 }, draft.Components[1]] } }).Digest);
    }

    [CurrentPermissionPostgresFact]
    public async Task EngineeringSourceCheck_nested_BOM_route_tool_resource_unit_pins_keep_unknown_qualification_and_invalidate()
    {
        await using var h = await Seed(); var f = await CheckSetup(h);
        foreach (var key in new[] { "plm-engineering-routing", "plm-engineering-bom" })
        {
            foreach (var action in new[] { "query", "add", "edit" }) await EngineeringAction(h, key, action);
            if (!await h.Db.Sys_RoleDataScopes.AnyAsync(x => x.RoleId == 72 && x.ResourceKey == key))
                h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = key, ScopeType = 5 });
            await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        }
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var revisions = new EngineeringRevisionOwnerV1(h.Db, CheckAuthority(h), Documents(h, f.Files), Specifications(h, f.Files));
        var parent = await revisions.ReadAsync(f.Command.Revision.Id, null);
        if (!await h.Db.Sys_RoleDataScopes.AnyAsync(x => x.ResourceKey == "plm-tool-specification")) await GrantSpecification(h);
        var plate = await SeedPlate(h); var tools = Specifications(h); var toolInput = await ToolDraft(h, plate);
        toolInput = toolInput with { Requirements = toolInput.Requirements with { ProductRef = parent.Draft.ProductRef! } };
        await tools.CommandAsync(toolInput); var tool = await tools.ReadAsync(plate);
        if (!await h.Db.Sys_RoleDataScopes.AnyAsync(x => x.ResourceKey == "uom-profile")) await GrantUomProfile(h);
        var units = Units(h); var unit = await MakeUnit(units);
        await EngineeringAction(h, "mes-machine", "query");
        if (!await h.Db.Sys_RoleDataScopes.AnyAsync(x => x.ResourceKey == "mes-machine"))
            h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = "mes-machine", ScopeType = 5 });
        await SeedMachine(h, "CHECK-MACHINE");
        var machine = Assert.Single(await new ResourceSourceOwnerV1(h.Db, CheckAuthority(h)).ListAsync("MACHINE", "CHECK-MACHINE"));
        var routeInput = RoutingCreate(parent);
        routeInput = routeInput with { Body = routeInput.Body with { Operations = [routeInput.Body.Operations[0] with
            { Tools = [tool.Specification!.Reference], Resources = [machine.Reference], Parameters = [new("Width", "0.00000001", null, null, unit.Reference, "Exact measured width")] }] } };
        var routes = Routings(h); await routes.CommandAsync(routeInput); var route = await routes.ReadAsync(routeInput.RoutingId);
        var bomInput = BomCreate(parent); var material = (await MakeEngineering(h)).Reference;
        bomInput = bomInput with { Body = bomInput.Body with { Occurrences = [bomInput.Body.Occurrences[0] with
            { ItemRef = EngineeringSourceCheckValuesV1.Ref(material), UnitRef = unit.Reference, Quantity = "12345678901234567890.123456789" }] } };
        var boms = Boms(h); await boms.CommandAsync(bomInput); var bom = await boms.ReadAsync(bomInput.BomId);
        var rules = ConfigurationRules(h);
        await rules.CommandAsync(ConfigurationAction(f.Policy, "REVISE") with { Body = f.Policy.Body with
            { EngineeringChecks = f.Policy.Body.EngineeringChecks! with { Composition = f.Policy.Body.EngineeringChecks!.Composition.Select(x =>
                x.Kind is "EBOM" or "ROUTING" ? x with { Applicability = "APPLIES", MinimumOccurrences = 1, Reason = "This product requires exact structure and routing" } : x).ToArray() } } });
        var policy = await rules.ReadAsync(f.Policy.Reference.Id, null);
        policy = await Step(rules, await Step(rules, policy, "SUBMIT"), "APPROVE");
        await revisions.CommandAsync(PatchRevision(parent,
            new EngineeringDraftPatchV1 { Op = "SET_CONFIGURATION", Configuration = new(ValidOptions(), "APPLIES", "Exact amended policy", policy.Reference) },
            new EngineeringDraftPatchV1 { Op = "UPSERT_COMPONENT", Component = new(Guid.NewGuid(), "ROUTING", "/routing", 2, "APPLIES", "", "", route.Reference) },
            new EngineeringDraftPatchV1 { Op = "UPSERT_COMPONENT", Component = new(Guid.NewGuid(), "EBOM", "/structure", 3, "APPLIES", "", "", bom.Reference) }));
        parent = await revisions.ReadAsync(parent.Reference.Id, null);
        var command = f.Command with { Revision = parent.Reference, Candidate = parent.CandidateRef, ExpectedEtag = parent.Etag, Policy = policy.Reference };
        var checks = Checks(h, f.Files); var result = await checks.CheckAsync(command); var before = await checks.ReadAsync(result.CheckRunId);
        Assert.Equal("UNKNOWN", before.Original.State); Assert.Equal("CURRENT", before.Freshness);
        foreach (var schema in new[] { "cp6_engineering_bom_v1", "cp6_engineering_routing_v1", "cp6_tool_specification_v1", "cp6_resource_source_v1", "cp6_uom_profile_v1" })
            Assert.Contains(before.Original.Sources, x => x.NativeSchema == schema && x.Capability == "NATIVE_SAME_UOW_RECHECK" && x.OriginalJson.Length > 2);
        Assert.Contains(before.Original.Items, x => x.Code == "TOOL_APPROVAL_AND_PHYSICAL_QUALIFICATION_REQUIRED" && x.State == "UNKNOWN");
        Assert.Equal("ENGINEERING_CHECK_NOT_CURRENT_PASS", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            CheckAuthority(h).ExecuteAsync(OperationsOwnerV1.EngineeringRevision, "query", false, (a, t) => checks.HoldExact(a, before.Reference, t)))).Message);
        // A draft can preserve an invalid quantity, but a known violation must be BLOCK, not UNKNOWN.
        await boms.CommandAsync(BomRevise(bom) with { Body = bom.Body with { RevisionOrigin = parent.Reference,
            Occurrences = [bom.Body.Occurrences[0] with { Quantity = "-1" }] } });
        var invalidBom = await boms.ReadAsync(bom.Reference.Id);
        await revisions.CommandAsync(PatchRevision(parent, new EngineeringDraftPatchV1 { Op = "UPSERT_COMPONENT",
            Component = parent.Draft.Components.Single(x => x.Kind == "EBOM") with { SourceRef = invalidBom.Reference } }));
        var invalidParent = await revisions.ReadAsync(parent.Reference.Id, null);
        var invalidResult = await checks.CheckAsync(command with { OperationId = Guid.NewGuid(), Revision = invalidParent.Reference,
            Candidate = invalidParent.CandidateRef, ExpectedEtag = invalidParent.Etag });
        var invalid = await checks.ReadAsync(invalidResult.CheckRunId);
        Assert.Equal("BLOCK", invalid.Original.State);
        Assert.Contains(invalid.Original.Items, x => x.Code.StartsWith("QUANTITY_NONPOSITIVE:") && x.State == "BLOCK");
        await tools.CommandAsync(NextDraft(toolInput, tool));
        await units.CommandAsync(Revise(unit, unit.Profile with { Name = "New unit definition" }));
        var after = await checks.ReadAsync(result.CheckRunId);
        Assert.Equal(before.OriginalJson, after.OriginalJson); Assert.Equal("STALE", after.Freshness);
        var next = await checks.CheckAsync(command with { OperationId = Guid.NewGuid(), Revision = invalidParent.Reference,
            Candidate = invalidParent.CandidateRef, ExpectedEtag = invalidParent.Etag });
        Assert.Equal("BLOCK", (await checks.ReadAsync(next.CheckRunId)).Original.State);
    }
}
