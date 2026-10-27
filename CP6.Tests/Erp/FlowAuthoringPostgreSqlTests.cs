using System.Text;
using System.Text.Json;
using System.Net;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.Contracts.Wire;
using CP6.Entity.DomainModels.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Core.Services.Common;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed class AuthoringHttpAccessor : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } }
    private const string AuthoringSchema = "{\"start\":\"s\",\"nodes\":[{\"id\":\"s\",\"type\":\"start\"},{\"id\":\"e\",\"type\":\"end\"}],\"edges\":[{\"from\":\"s\",\"to\":\"e\"}],\"layout\":{\"number\":1.0,\"name\":\"原文\"}}";
    private FlowAuthoringOwnerV2 Authoring(Harness h, Func<CancellationToken, Task<Guid>>? actor = null)
    {
        var catalog = new AuthorizationCatalogStoreV1();
        catalog.RegisterScope(FlowAuthoringOwnerV2.Resource, "flow", [1, 2, 3, 4, 5], 1);
        catalog.RegisterFields(FlowAuthoringOwnerV2.Resource, FlowAuthoringOwnerV2.Fields.Select(f => new FieldRegistry.Field(f, f))); catalog.Seal();
        return new(h.Db, new AuthoringHttpAccessor { HttpContext = h.Http }, ScopeFence(h), actor ?? (_ => Task.FromResult(h.User)), [], [], catalog.Capture);
    }
    private async Task<FlowAuthoringOwnerV2> PrepareAuthoring(Harness h)
    {
        if (!await h.Db.Sys_Menus.AnyAsync(m => m.MenuKey == "oa-designer"))
        { h.Db.Sys_Menus.Add(new() { MenuId = 9172, MenuKey = "oa-designer", MenuName = "designer" }); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); }
        await RegisterScope(h); await EngineeringAction(h, "oa-designer", "add"); await EngineeringAction(h, "oa-designer", "edit");
        var menu = await h.Db.Sys_Menus.SingleAsync(m => m.MenuKey == "oa-designer");
        if (!await h.Db.Sys_RoleMenus.AnyAsync(x => x.RoleId == 72 && x.MenuId == menu.MenuId))
        { h.Db.Sys_RoleMenus.Add(new() { RoleId = 72, MenuId = menu.MenuId }); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); }
        var owner = Authoring(h); await owner.ConfigureAsync(new(Guid.NewGuid(), "absent", true, [], "local direct authoring policy")); return owner;
    }
    private static byte[] AuthoringCommand(string id = "first", string? version = null, string name = "draft", string schema = AuthoringSchema, string? function = null)
        => JsonSerializer.SerializeToUtf8Bytes(new { commandId = id, expectedVersion = version, body = new { name, schemaJson = schema, formKey = (string?)null, functionId = function, flowCode = (string?)null } });

    [Fact]
    public void Authoring_canonical_preserves_numeric_lexemes_orders_UTF16_and_rejects_duplicates_and_invalid_unicode()
    {
        using var one = WorkflowCommandJsonV1.Parse(Encoding.UTF8.GetBytes("{\"b\":1.0,\"a\":\"\\n原文\"}"));
        using var same = WorkflowCommandJsonV1.Parse(Encoding.UTF8.GetBytes("{ \"a\":\"\\u000a原文\",\"b\":1.0 }"));
        using var other = WorkflowCommandJsonV1.Parse(Encoding.UTF8.GetBytes("{\"a\":\"\\n原文\",\"b\":1}"));
        Assert.Equal("{\"a\":\"\\u000a原文\",\"b\":1.0}", WorkflowCommandJsonV1.Canonical(one.RootElement));
        Assert.Equal(WorkflowCommandJsonV1.Digest(one.RootElement, null), WorkflowCommandJsonV1.Digest(same.RootElement, null));
        Assert.NotEqual(WorkflowCommandJsonV1.Digest(one.RootElement, null), WorkflowCommandJsonV1.Digest(other.RootElement, null));
        Assert.Throws<JsonException>(() => WorkflowCommandJsonV1.Parse(Encoding.UTF8.GetBytes("{\"a\":1,\"\\u0061\":2}")));
        Assert.Throws<JsonException>(() => WorkflowCommandJsonV1.Parse(Encoding.UTF8.GetBytes("\"\\ud800\"")));
    }
    [CurrentPermissionPostgresFact]
    public async Task Authoring_creates_disabled_atomic_head_draft_exact_original_and_edits_CAS_preserving_extensions()
    {
        await using var h = await Seed(); var owner = await PrepareAuthoring(h);
        var first = await owner.SaveAsync("authoring", AuthoringCommand());
        Assert.False(first.Result!.Enabled); Assert.Equal(AuthoringSchema, first.Result.Fields["SchemaJson"]);
        Assert.Empty(await h.Db.Wf_FlowDefs.ToArrayAsync());
        var read = await owner.ReadAsync("authoring"); Assert.Equal(first.ResourceVersion, read.Version);
        var replay = await owner.SaveAsync("authoring", AuthoringCommand()); Assert.Equal(first.ResourceVersion, replay.ResourceVersion);
        Assert.Equal("COMMAND_BODY_CONFLICT", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("authoring", AuthoringCommand(name: "changed")))).Message);
        Assert.Equal("DEFINITION_EXISTS", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("authoring", AuthoringCommand("new")))).Message);
        var next = await owner.SaveAsync("authoring", AuthoringCommand("edit", read.Version, "updated", function: "F172"));
        Assert.NotEqual(read.Version, next.ResourceVersion); Assert.Equal("updated", (await owner.ReadAsync("authoring")).Fields["Name"]);
        Assert.Equal(read.Version, (await owner.OriginalAsync("authoring", "first")).ResourceVersion);
        Assert.Equal("DEFINITION_CHANGED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("authoring", AuthoringCommand("stale", read.Version)))).Message);
        Assert.Equal(2, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_workflow_authoring_v2.audit WHERE tenant_id={h.Tenant}").SingleAsync());
        var evidence = await h.Db.Database.SqlQuery<string>($"SELECT evidence_json AS \"Value\" FROM cp6_workflow_authoring_v2.audit WHERE tenant_id={h.Tenant} AND command_id='first'").SingleAsync();
        using var proof = JsonDocument.Parse(evidence);
        Assert.Equal(h.User, proof.RootElement.GetProperty("actualActor").GetGuid());
        Assert.Equal("oa-designer:add", proof.RootElement.GetProperty("action").GetString());
        Assert.Contains(proof.RootElement.GetProperty("effectiveRoles").EnumerateArray(), x => x.GetInt32() == 72);
        Assert.NotEmpty(proof.RootElement.GetProperty("fields").GetProperty("sourcePermissionVersion").GetString()!);
    }
    [CurrentPermissionPostgresFact]
    public async Task Authoring_policy_and_action_fail_closed_and_missing_scope_cannot_create()
    {
        await using var h = await Seed(); var owner = Authoring(h);
        Assert.Equal("SCOPE_MAPPING_REQUIRED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("closed", AuthoringCommand()))).Message);
        owner = await PrepareAuthoring(h);
        await h.Db.Sys_RoleActions.Where(a => a.ActionCode == "add").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("closed", AuthoringCommand()))).Message);
        await EngineeringAction(h, "oa-designer", "add");
        var policy = await owner.PolicyAsync();
        await owner.ConfigureAsync(new(Guid.NewGuid(), policy!.Version.ToString("D"), true, [72], "SOD restriction"));
        Assert.Equal("AUTHORING_SOD_FORBIDDEN", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("closed", AuthoringCommand()))).Message);
        Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_workflow_authoring_v2.heads WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Authoring_lost_ACK_uses_original_after_edit_revoke_and_scope_disable()
    {
        var fault = new CommitFault(); await using var h = await Seed(fault); var owner = await PrepareAuthoring(h);
        fault.Armed = true;
        Assert.Equal("COMMAND_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("lost", AuthoringCommand()))).Message);
        await h.Db.Sys_RoleActions.Where(a => a.ActionCode == "add" || a.ActionCode == "edit").ExecuteDeleteAsync();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_scope_authorization_v1.fences SET enabled=false WHERE tenant_id={h.Tenant}");
        var original = await owner.OriginalAsync("lost", "first"); Assert.Equal("SUCCEEDED", original.Outcome);
        Assert.Equal(original.ResourceVersion, (await owner.SaveAsync("lost", AuthoringCommand())).ResourceVersion);
        Assert.Equal("SCOPE_MAPPING_DISABLED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("new", AuthoringCommand("next")))).Message);
        Assert.Equal("NOT_OBSERVED", (await owner.OriginalAsync("lost", "missing")).Outcome);
    }
    [CurrentPermissionPostgresFact]
    public async Task Authoring_field_hidden_filters_live_and_original_and_blocks_whole_mutation()
    {
        await using var h = await Seed(); var owner = await PrepareAuthoring(h); var saved = await owner.SaveAsync("private", AuthoringCommand());
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = FlowAuthoringOwnerV2.Resource, FieldName = "SchemaJson", Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.DoesNotContain("SchemaJson", (await owner.ReadAsync("private")).Fields.Keys);
        Assert.DoesNotContain("SchemaJson", (await owner.OriginalAsync("private", "first")).Result!.Fields.Keys);
        Assert.Equal("FIELD_WRITE_FORBIDDEN", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("private", AuthoringCommand("edit", saved.ResourceVersion, "hidden edit")))).Message);
        Assert.Equal("draft", (await owner.ReadAsync("private")).Fields["Name"]);
    }
    [CurrentPermissionPostgresFact]
    public async Task Authoring_legacy_identity_writes_cannot_overwrite_or_duplicate_controlled_keys()
    {
        await using var h = await Seed(); var owner = await PrepareAuthoring(h); await owner.SaveAsync("managed", AuthoringCommand(function: "F"));
        h.Db.Wf_FlowDefs.Add(new Wf_FlowDef { FlowKey = "managed", FlowName = "bypass", SchemaJson = AuthoringSchema });
        await Assert.ThrowsAsync<DbUpdateException>(() => h.Db.SaveChangesAsync()); h.Db.ChangeTracker.Clear();
        h.Db.Wf_FlowDefs.Add(new Wf_FlowDef { FlowKey = "other", FlowName = "bypass", SchemaJson = AuthoringSchema, FunctionId = "F" });
        await Assert.ThrowsAsync<DbUpdateException>(() => h.Db.SaveChangesAsync()); h.Db.ChangeTracker.Clear();
        h.Db.Wf_FlowDefs.Add(new Wf_FlowDef { FlowKey = "native", FlowName = "old", SchemaJson = AuthoringSchema });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Equal("DEFINITION_IDENTITY_CONFLICT", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("native", AuthoringCommand()))).Message);
        Assert.Equal("draft", (await owner.ReadAsync("managed")).Fields["Name"]);
        Assert.Equal("old", (await h.Db.Wf_FlowDefs.SingleAsync()).FlowName);
        Assert.Equal("DEFINITION_IDENTITY_CONFLICT", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("MANAGED", AuthoringCommand("case")))).Message);
        Assert.Equal("DEFINITION_IDENTITY_CONFLICT", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("other", AuthoringCommand("function", function: "f")))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Authoring_final_actor_failure_rolls_back_head_identity_command_and_audit()
    {
        await using var h = await Seed(); await PrepareAuthoring(h); var calls = 0;
        var owner = Authoring(h, _ => ++calls == 1 ? Task.FromResult(h.User) : throw new ScopeAuthorizationExceptionV1("REAUTH_REQUIRED", 401));
        Assert.Equal(401, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("rollback", AuthoringCommand()))).Status);
        foreach (var table in new[] { "heads", "drafts", "commands", "audit" })
            Assert.Equal(0, await h.Db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM cp6_workflow_authoring_v2." + table + " WHERE tenant_id={0}", h.Tenant).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Authoring_actual_HTTP_uses_current_session_csrf_closed_body_and_original_slot()
    {
        await using var h = await Seed(); await PrepareAuthoring(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,
            new Dictionary<string, string> { ["ScopeAuthorization__EnvironmentId"] = ScopeEnvironment.ToString("D") });
        var cookies = await Login(host, h); const string path = "/api/oa/v2/definitions/FLOW/http-draft/draft";
        var policy = await Authoring(h).PolicyAsync();
        var policyCommand = new FlowAuthoringPolicyCommandV2(Guid.NewGuid(), policy!.Version.ToString("D"), true, [], "HTTP policy management");
        using (var configured = await host.Send(BoundRequest(cookies, "/api/iam/workflow-authoring-policy", policyCommand)))
            Assert.True(configured.IsSuccessStatusCode, await configured.Content.ReadAsStringAsync());
        using (var policyOriginal = await host.Send(BoundRequest(cookies, "/api/iam/workflow-authoring-policy/operations/" + policyCommand.OperationId)))
            Assert.Equal(HttpStatusCode.OK, policyOriginal.StatusCode);
        using var command = JsonDocument.Parse(AuthoringCommand());
        var noCsrf = BoundRequest(cookies, path, command.RootElement, false); noCsrf.Method = HttpMethod.Put;
        using (var denied = await host.Send(noCsrf)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var request = BoundRequest(cookies, path, command.RootElement); request.Method = HttpMethod.Put;
        using (var saved = await host.Send(request))
        {
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync()); Assert.True(saved.Headers.CacheControl?.NoStore);
        }
        using (var original = await host.Send(BoundRequest(cookies, "/api/oa/v2/commands/first?operation=FLOW_SAVE_DRAFT&resource=http-draft")))
            Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        var forged = BoundRequest(cookies, path, new { commandId = "forged", expectedVersion = (string?)null, tenantId = Guid.NewGuid(), body = command.RootElement.GetProperty("body") }); forged.Method = HttpMethod.Put;
        using (var rejected = await host.Send(forged)) Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using (var legacy = await host.Send(BoundRequest(cookies, "/api/oa/designer/save", new
        { flowKey = "http-draft", flowName = "bypass", formKey = "", schemaJson = AuthoringSchema })))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, legacy.StatusCode);
            Assert.Contains("AUTHORITY_FENCE_UNAVAILABLE", await legacy.Content.ReadAsStringAsync());
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Authoring_current_owner_scope_foreign_actor_and_tenant_never_share_original_slots()
    {
        await using var h = await Seed(); var owner = await PrepareAuthoring(h); var saved = await owner.SaveAsync("private", AuthoringCommand());
        var other = new Sys_User { UserName = "other-author", Password = "unused", RoleId = 72 };
        h.Db.Sys_Users.Add(other); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        h.Http.User = CurrentPermissionContextTests.Principal(other.Id, h.Tenant);
        var reader = Authoring(h, _ => Task.FromResult(other.Id));
        Assert.Empty(await reader.ListAsync());
        Assert.Equal(404, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => reader.ReadAsync("private"))).Status);
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = FlowAuthoringOwnerV2.Resource, ScopeType = 5 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Equal(saved.ResourceId, (await reader.ReadAsync("private")).DefinitionId);
        Assert.Equal("NOT_OBSERVED", (await reader.OriginalAsync("private", "first")).Outcome);
        await using var foreign = await Seed(); var foreignOwner = await PrepareAuthoring(foreign);
        var separate = await foreignOwner.SaveAsync("private", AuthoringCommand());
        Assert.NotEqual(saved.ResourceId, separate.ResourceId); Assert.Equal(separate.ResourceId, (await foreignOwner.OriginalAsync("private", "first")).ResourceId);
    }

    [CurrentPermissionPostgresFact]
    public async Task Authoring_department_scope_uses_current_healthy_tree_and_filters_before_pagination()
    {
        await using var h = await Seed(); var owner = await PrepareAuthoring(h);
        var parent = new Sys_Dept { Id = Guid.NewGuid(), DeptCode = "P" + h.Tenant.ToString("N"), DeptName = "parent" };
        parent.Path = "/" + parent.Id + "/";
        var child = new Sys_Dept { Id = Guid.NewGuid(), DeptCode = "C" + h.Tenant.ToString("N"), DeptName = "child", ParentId = parent.Id };
        child.Path = parent.Path + child.Id + "/";
        h.Db.Sys_Depts.AddRange(parent, child); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await h.Db.Sys_Users.Where(u => u.Id == h.User).ExecuteUpdateAsync(u => u.SetProperty(x => x.DeptId, child.Id));
        await owner.SaveAsync("child-draft", AuthoringCommand());
        await h.Db.Sys_Users.Where(u => u.Id == h.User).ExecuteUpdateAsync(u => u.SetProperty(x => x.DeptId, parent.Id));
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = FlowAuthoringOwnerV2.Resource, ScopeType = 2 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Empty(await owner.ListAsync());
        await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == FlowAuthoringOwnerV2.Resource).ExecuteUpdateAsync(x => x.SetProperty(s => s.ScopeType, 3));
        Assert.Single(await owner.ListAsync());
        await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == FlowAuthoringOwnerV2.Resource).ExecuteUpdateAsync(x => x.SetProperty(s => s.ScopeType, 4).SetProperty(s => s.CustomDeptIds, child.Id.ToString("D")));
        Assert.Single(await owner.ListAsync());
        await h.Db.Sys_Depts.Where(d => d.Id == parent.Id).ExecuteUpdateAsync(x => x.SetProperty(d => d.Enable, false));
        Assert.Empty(await owner.ListAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Authoring_same_transaction_policy_ABA_and_expiry_roll_back_every_effect()
    {
        await using var h = await Seed(); await PrepareAuthoring(h); var calls = 0;
        var owner = Authoring(h, async _ =>
        {
            if (++calls == 2)
            {
                await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_workflow_authoring_v2.policy SET enabled=false WHERE tenant_id={h.Tenant}");
                await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_workflow_authoring_v2.policy SET enabled=true WHERE tenant_id={h.Tenant}");
            }
            return h.User;
        });
        Assert.Equal("SCOPE_AUTHORITY_CHANGED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("aba", AuthoringCommand()))).Message);
        Assert.Equal("NOT_OBSERVED", (await Authoring(h).OriginalAsync("aba", "first")).Outcome);
        calls = 0;
        owner = Authoring(h, _ =>
        {
            if (++calls == 2) ((System.Security.Claims.ClaimsIdentity)h.Http.User.Identity!).AddClaim(new("exp", DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeSeconds().ToString()));
            return Task.FromResult(h.User);
        });
        Assert.Equal("REAUTH_REQUIRED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("expired", AuthoringCommand()))).Message);
        Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_workflow_authoring_v2.commands WHERE tenant_id={h.Tenant}").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Authoring_policy_revoke_waits_for_actual_owner_commit_then_blocks_the_next_write()
    {
        await using var h = await Seed(); await PrepareAuthoring(h);
        await using var writer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var app = "authoring-revoke-" + Guid.NewGuid().ToString("N"); writer.Database.SetConnectionString(writer.Database.GetConnectionString() + ";Application Name=" + app);
        await using var monitor = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        Task<int>? revoke = null; var calls = 0;
        var owner = Authoring(h, async _ =>
        {
            if (++calls == 2)
            {
                revoke = writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_workflow_authoring_v2.policy SET enabled=false WHERE tenant_id={h.Tenant}");
                var waiting = false;
                for (var i = 0; i < 100 && !revoke.IsCompleted; i++)
                {
                    waiting = await monitor.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM pg_stat_activity WHERE application_name={app} AND wait_event_type='Lock') AS \"Value\"").SingleAsync();
                    if (waiting) break; await Task.Delay(20);
                }
                Assert.True(waiting); Assert.False(revoke.IsCompleted);
            }
            return h.User;
        });
        var saved = await owner.SaveAsync("linearized", AuthoringCommand()); Assert.Equal(1, await revoke!.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(saved.ResourceId, (await owner.OriginalAsync("linearized", "first")).ResourceId);
        Assert.Equal("AUTHORING_POLICY_DISABLED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("after-revoke", AuthoringCommand("next")))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Authoring_receipts_are_immutable_and_missing_policy_writer_prevents_new_effect()
    {
        await using var h = await Seed(); var owner = await PrepareAuthoring(h); await owner.SaveAsync("history", AuthoringCommand());
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_workflow_authoring_v2.commands SET result_json='{{}}' WHERE tenant_id={h.Tenant}"));
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_workflow_authoring_v2.policy DISABLE TRIGGER authoring_policy_fence");
        try
        {
            Assert.Equal("AUTHORING_WRITER_COVERAGE_UNAVAILABLE", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("closed", AuthoringCommand("new")))).Message);
            Assert.Equal("SUCCEEDED", (await owner.OriginalAsync("history", "first")).Outcome);
        }
        finally { await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_workflow_authoring_v2.policy ENABLE TRIGGER authoring_policy_fence"); }
    }

    [CurrentPermissionPostgresFact]
    public async Task Authoring_incomplete_graph_is_a_draft_but_unknown_execution_and_duplicate_JSON_are_rejected()
    {
        await using var h = await Seed(); var owner = await PrepareAuthoring(h);
        var incomplete = await owner.SaveAsync("incomplete", AuthoringCommand(schema: "{\"nodes\":[],\"edges\":[],\"extra\":1.0}"));
        Assert.NotEmpty(incomplete.Result!.Diagnostics); Assert.False(incomplete.Result.Enabled);
        var unconnectedService = "{\"start\":\"s\",\"nodes\":[{\"id\":\"s\",\"type\":\"start\"},{\"id\":\"timer\",\"type\":\"serviceTask\",\"serviceKind\":\"timer\",\"serviceDelayMode\":\"duration\",\"serviceDelayValue\":\"PT2H\"}],\"edges\":[]}";
        var service = await owner.SaveAsync("unconnected", AuthoringCommand(schema: unconnectedService));
        Assert.Contains("E-WF-016", service.Result!.Diagnostics); Assert.Equal(unconnectedService, service.Result.Fields["SchemaJson"]);
        Assert.Equal(422, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("unsafe-service", AuthoringCommand(schema: unconnectedService.Replace("\"timer\",\"serviceDelayMode\"", "\"script\",\"serviceDelayMode\""))))).Status);
        Assert.Equal(422, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("unknown", AuthoringCommand(schema: "{\"nodes\":[{\"id\":\"x\",\"type\":\"shell\"}],\"edges\":[]}")))).Status);
        Assert.Equal(422, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("duplicate", AuthoringCommand(schema: "{\"nodes\":[],\"nodes\":[],\"edges\":[]}")))).Status);
        var duplicate = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(AuthoringCommand()).Replace("\"commandId\":\"first\"", "\"commandId\":\"first\",\"commandId\":\"other\""));
        Assert.Equal(400, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("duplicate", duplicate))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Authoring_readonly_same_value_is_allowed_but_changed_field_rejects_all_and_correlation_is_not_command_identity()
    {
        await using var h = await Seed(); var owner = await PrepareAuthoring(h); var saved = await owner.SaveAsync("readonly", AuthoringCommand());
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = FlowAuthoringOwnerV2.Resource, FieldName = "Name", Access = 2 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var raw = AuthoringCommand("edit", saved.ResourceVersion, function: "IDENTITY");
        var next = await owner.SaveAsync("readonly", raw);
        Assert.Equal("draft", next.Result!.Fields["Name"]); Assert.DoesNotContain("Name", next.Result.WritableFields);
        var diagnostic = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(raw).Insert(1, "\"clientCorrelationId\":\"new-observation\","));
        Assert.Equal(next.ResourceVersion, (await owner.SaveAsync("readonly", diagnostic)).ResourceVersion);
        Assert.Equal("FIELD_WRITE_FORBIDDEN", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("readonly", AuthoringCommand("denied", next.ResourceVersion, "changed", function: "OTHER")))).Message);
        Assert.Equal("IDENTITY", (await owner.ReadAsync("readonly")).Fields["FunctionId"]);
    }
}
