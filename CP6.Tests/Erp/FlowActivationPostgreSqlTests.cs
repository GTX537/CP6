using System.Net;
using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private async Task<(FlowAuthoringOwnerV2 Owner, WorkflowLifecycleResultV2 Published)> PrepareActivation(Harness h, string key = "activation")
    {
        var owner = await PreparePublication(h); await EngineeringAction(h, "oa-flow-admin", "enable");
        var menu = await h.Db.Sys_Menus.SingleAsync(x => x.MenuKey == "oa-flow-admin");
        h.Db.Sys_RoleMenus.Add(new() { RoleId = 72, MenuId = menu.MenuId }); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var saved = await owner.SaveAsync(key, AuthoringCommand());
        return (owner, await owner.PublishAsync(key, PublicationCommand(await Preview(owner, key, saved))));
    }
    private static byte[] ActivationCommand(FlowActivationV2 current, Guid version, bool enabled = true, string id = "activate", string reason = "explicit activation")
        => JsonSerializer.SerializeToUtf8Bytes(new { commandId = id, expectedVersion = current.Revision.ToString("D"),
            body = new { enabled, expectedActivationRevision = current.Revision.ToString("D"), publishedVersionId = version, reason } });

    [CurrentPermissionPostgresFact]
    public async Task Activation_exact_version_is_independent_of_drafts_publication_and_immutable_history()
    {
        await using var h = await Seed(); var (owner, one) = await PrepareActivation(h);
        var original = one.Result!.Publication!; var current = (await owner.ActivationPreviewAsync("activation")).Current;
        Assert.False(current.Enabled); Assert.Null(current.PublishedVersionId);
        var command = ActivationCommand(current, original.VersionId); var first = await owner.SetActivationAsync("activation", command);
        Assert.True(first.Result!.Enabled); Assert.Equal("NOT_ADOPTED", first.Result.ConsumerState);
        Assert.Equal(original.VersionId, first.Result.PublishedVersionId); Assert.True((await owner.ReadHeadAsync("activation")).Enabled);
        var draft = await owner.CreateNextDraftAsync("activation", NextDraftCommand(one.ResourceVersion!, original.VersionId));
        var edit = await owner.SaveAsync("activation", AuthoringCommand("edit", draft.ResourceVersion, "second"));
        var two = await owner.PublishAsync("activation", PublicationCommand(await Preview(owner, "activation", edit), "publish2"));
        Assert.Equal(first.Result, (await owner.ActivationPreviewAsync("activation")).Current);
        Assert.Equal(first.ResourceVersion, (await owner.SetActivationAsync("activation", command)).ResourceVersion);
        Assert.Equal("COMMAND_BODY_CONFLICT", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SetActivationAsync("activation", ActivationCommand(current, original.VersionId, reason: "changed")))).Message);
        var disabled = await owner.SetActivationAsync("activation", ActivationCommand(first.Result, original.VersionId, false, "disable"));
        Assert.False(disabled.Result!.Enabled); Assert.Equal(2, (await owner.ActivationHistoryAsync("activation")).Length);
        Assert.Equal(first.ResourceVersion, (await owner.SetActivationAsync("activation", command)).ResourceVersion);
        Assert.False((await owner.ActivationPreviewAsync("activation")).Current.Enabled); // Original replay cannot re-enable.
        var selectedOld = await owner.SetActivationAsync("activation", ActivationCommand(disabled.Result, original.VersionId, id: "old"));
        Assert.Equal(original.VersionId, selectedOld.Result!.PublishedVersionId); Assert.NotEqual(two.Result!.Publication!.VersionId, selectedOld.Result.PublishedVersionId);
        Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync()); Assert.Empty(await h.Db.Wf_FlowDefs.ToArrayAsync());
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_workflow_authoring_v2.activation_history SET reason='rewrite' WHERE tenant_id={h.Tenant}"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_workflow_authoring_v2.activation_current SET enabled=false WHERE tenant_id={h.Tenant}"));
    }
    [CurrentPermissionPostgresFact]
    public async Task Activation_admin_does_not_need_or_receive_draft_edit_or_publish_permission()
    {
        await using var h = await Seed(); var (owner, publication) = await PrepareActivation(h);
        var designer = await h.Db.Sys_Menus.SingleAsync(x => x.MenuKey == "oa-designer");
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == designer.MenuId).ExecuteDeleteAsync();
        await h.Db.Sys_RoleMenus.Where(x => x.MenuId == designer.MenuId).ExecuteDeleteAsync();
        Assert.Single(await owner.ActivationListAsync());
        var preview = await owner.ActivationPreviewAsync("activation");
        Assert.Equal("SUCCEEDED", (await owner.SetActivationAsync("activation", ActivationCommand(preview.Current, publication.Result!.Publication!.VersionId))).Outcome);
        Assert.Equal("OBJECT_FORBIDDEN", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.ReadHeadAsync("activation"))).Message);
        Assert.Empty(await h.Db.Sys_RoleActions.Where(x => x.MenuId == designer.MenuId).ToArrayAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Activation_unknown_commit_and_write_revoke_keep_original_available_without_new_history()
    {
        var fault = new CommitFault(); await using var h = await Seed(fault); var (owner, p) = await PrepareActivation(h);
        var current = (await owner.ActivationPreviewAsync("activation")).Current; var command = ActivationCommand(current, p.Result!.Publication!.VersionId);
        fault.Armed = true; Assert.Equal("COMMAND_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SetActivationAsync("activation", command))).Message);
        await h.Db.Sys_RoleActions.Where(x => x.ActionCode == "enable").ExecuteDeleteAsync();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_scope_authorization_v1.fences SET enabled=false WHERE tenant_id={h.Tenant}");
        var original = await owner.ActivationOriginalAsync("activation", "activate"); Assert.Equal("SUCCEEDED", original.Outcome);
        Assert.Equal(original, await owner.SetActivationAsync("activation", command)); Assert.Single(await owner.ActivationHistoryAsync("activation"));
        Assert.Equal("NOT_OBSERVED", (await owner.ActivationOriginalAsync("activation", "unseen")).Outcome);
    }
    [CurrentPermissionPostgresFact]
    public async Task Activation_final_permission_revoke_rolls_back_history_projection_command_and_scope()
    {
        await using var h = await Seed(); var (normal, p) = await PrepareActivation(h); var current = (await normal.ActivationPreviewAsync("activation")).Current;
        var calls = 0; var owner = Authoring(h, async _ => { if (++calls == 2) await h.Db.Sys_RoleActions.Where(x => x.ActionCode == "enable").ExecuteDeleteAsync(); return h.User; });
        Assert.Equal("ACTION_FORBIDDEN", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SetActivationAsync("activation", ActivationCommand(current, p.Result!.Publication!.VersionId)))).Message);
        Assert.Equal(current, (await normal.ActivationPreviewAsync("activation")).Current); Assert.Empty(await normal.ActivationHistoryAsync("activation"));
        Assert.Equal("NOT_OBSERVED", (await normal.ActivationOriginalAsync("activation", "activate")).Outcome);
        Assert.NotEmpty(await h.Db.Sys_RoleActions.Where(x => x.ActionCode == "enable").ToArrayAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Activation_stale_CAS_wrong_definition_hidden_source_and_missing_guard_are_refused()
    {
        await using var h = await Seed(); var (owner, p) = await PrepareActivation(h);
        var current = (await owner.ActivationPreviewAsync("activation")).Current;
        var saved = await owner.SaveAsync("another", AuthoringCommand()); var other = await owner.PublishAsync("another", PublicationCommand(await Preview(owner, "another", saved)));
        Assert.Equal("VERSION_NOT_FOUND", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SetActivationAsync("activation", ActivationCommand(current, other.Result!.Publication!.VersionId)))).Message);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = FlowAuthoringOwnerV2.Resource, FieldName = "SchemaJson", Access = 3 }); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Equal("FIELD_READ_FORBIDDEN", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SetActivationAsync("activation", ActivationCommand(current, p.Result!.Publication!.VersionId)))).Message);
        await h.Db.Sys_RoleFieldPerms.Where(x => x.ResourceKey == FlowAuthoringOwnerV2.Resource).ExecuteDeleteAsync();
        var enabled = await owner.SetActivationAsync("activation", ActivationCommand(current, p.Result!.Publication!.VersionId));
        Assert.Equal("ACTIVATION_CHANGED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SetActivationAsync("activation", ActivationCommand(current, p.Result.Publication.VersionId, false, "stale")))).Message);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_workflow_authoring_v2.activation_current DISABLE TRIGGER activation_scope");
        try
        {
            Assert.Equal("ACTIVATION_WRITER_COVERAGE_UNAVAILABLE", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SetActivationAsync("activation", ActivationCommand(enabled.Result!, p.Result.Publication.VersionId, false, "guard")))).Message);
            Assert.Equal("SUCCEEDED", (await owner.ActivationOriginalAsync("activation", "activate")).Outcome);
        }
        finally { await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_workflow_authoring_v2.activation_current ENABLE TRIGGER activation_scope"); }
    }
    [CurrentPermissionPostgresFact]
    public async Task Activation_competing_connections_wait_and_stale_token_cannot_overwrite_winner()
    {
        await using var h = await Seed(); var (normal, p) = await PrepareActivation(h); var current = (await normal.ActivationPreviewAsync("activation")).Current;
        await using var db2 = server.Context(new CP6.Core.Services.Common.TenantContext { CurrentTenantId = h.Tenant });
        var app = "activation-race-" + Guid.NewGuid().ToString("N"); db2.Database.SetConnectionString(db2.Database.GetConnectionString() + ";Application Name=" + app);
        await using var monitor = server.Context(new CP6.Core.Services.Common.TenantContext { CurrentTenantId = h.Tenant });
        var second = Authoring(h with { Db = db2 }); Task<FlowActivationResultV2>? loser = null; var calls = 0;
        var first = Authoring(h, async _ =>
        {
            if (++calls == 2)
            {
                loser = second.SetActivationAsync("activation", ActivationCommand(current, p.Result!.Publication!.VersionId, id: "loser")); var waiting = false;
                for (var i = 0; i < 100 && !loser.IsCompleted; i++)
                {
                    waiting = await monitor.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM pg_stat_activity WHERE application_name={app} AND wait_event_type='Lock') AS \"Value\"").SingleAsync();
                    if (waiting) break; await Task.Delay(20);
                }
                Assert.True(waiting); Assert.False(loser.IsCompleted);
            }
            return h.User;
        });
        var winner = await first.SetActivationAsync("activation", ActivationCommand(current, p.Result!.Publication!.VersionId));
        Assert.NotNull(loser); Assert.Equal("ACTIVATION_CHANGED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(async () => await loser!)).Message);
        Assert.Equal(winner.Result, (await normal.ActivationPreviewAsync("activation")).Current); Assert.Single(await normal.ActivationHistoryAsync("activation"));
    }
    [CurrentPermissionPostgresFact]
    public async Task Activation_requires_explicit_boolean_and_does_not_share_another_tenants_source_or_receipt()
    {
        await using var h = await Seed(); var (owner, p) = await PrepareActivation(h); var current = (await owner.ActivationPreviewAsync("activation")).Current;
        var missing = JsonSerializer.SerializeToUtf8Bytes(new { commandId = "missing", expectedVersion = current.Revision.ToString("D"),
            body = new { expectedActivationRevision = current.Revision.ToString("D"), publishedVersionId = p.Result!.Publication!.VersionId, reason = "missing enabled" } });
        Assert.Equal("COMMAND_SHAPE_INVALID", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SetActivationAsync("activation", missing))).Message);
        await owner.SetActivationAsync("activation", ActivationCommand(current, p.Result!.Publication!.VersionId));
        await using var other = await Seed(); var (foreign, _) = await PrepareActivation(other);
        var foreignCurrent = (await foreign.ActivationPreviewAsync("activation")).Current;
        Assert.Equal("VERSION_NOT_FOUND", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => foreign.SetActivationAsync("activation", ActivationCommand(foreignCurrent, p.Result.Publication.VersionId)))).Message);
        Assert.Equal("NOT_OBSERVED", (await foreign.ActivationOriginalAsync("activation", "activate")).Outcome);
        Assert.Empty(await foreign.ActivationHistoryAsync("activation"));
        Assert.False(foreignCurrent.Enabled); Assert.Equal(current.DefinitionId, (await owner.ActivationOriginalAsync("activation", "activate")).ResourceId);
    }
    [CurrentPermissionPostgresFact]
    public async Task Activation_final_same_value_authority_ABA_rolls_back_whole_command()
    {
        await using var h = await Seed(); var (normal, p) = await PrepareActivation(h); var current = (await normal.ActivationPreviewAsync("activation")).Current;
        var calls = 0; var owner = Authoring(h, async _ =>
        {
            if (++calls == 2) await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_scope_authorization_v1.fences SET enabled=enabled WHERE tenant_id={h.Tenant}");
            return h.User;
        });
        Assert.Equal("SCOPE_AUTHORITY_CHANGED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SetActivationAsync("activation", ActivationCommand(current, p.Result!.Publication!.VersionId)))).Message);
        Assert.Empty(await normal.ActivationHistoryAsync("activation")); Assert.Equal(current, (await normal.ActivationPreviewAsync("activation")).Current);
        Assert.Equal("NOT_OBSERVED", (await normal.ActivationOriginalAsync("activation", "activate")).Outcome);
    }
    [CurrentPermissionPostgresFact]
    public async Task Activation_actual_HTTP_uses_session_CSRF_admin_permission_and_original_command()
    {
        await using var h = await Seed(); var (owner, p) = await PrepareActivation(h, "http-activation");
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, new Dictionary<string, string> { ["ScopeAuthorization__EnvironmentId"] = ScopeEnvironment.ToString("D") });
        var cookies = await Login(host, h); const string root = "/api/oa/v2/definitions/FLOW/http-activation";
        using var response = await host.Send(BoundRequest(cookies, root + "/activation")); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var current = json.RootElement.GetProperty("data").GetProperty("current").Deserialize<FlowActivationV2>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        using var command = JsonDocument.Parse(ActivationCommand(current, p.Result!.Publication!.VersionId));
        using var success = await host.Send(BoundRequest(cookies, root + "/activation", command.RootElement)); Assert.True(success.IsSuccessStatusCode, await success.Content.ReadAsStringAsync()); Assert.True(success.Headers.CacheControl?.NoStore);
        using var original = await host.Send(BoundRequest(cookies, "/api/oa/v2/commands/activate?operation=FLOW_SET_ACTIVATION&resource=http-activation")); Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        using var history = await host.Send(BoundRequest(cookies, root + "/activation-history")); Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        await h.Db.Sys_RoleActions.Where(x => x.ActionCode == "enable").ExecuteDeleteAsync();
        current = (await owner.ActivationPreviewAsync("http-activation")).Current;
        using var disable = JsonDocument.Parse(ActivationCommand(current, p.Result.Publication.VersionId, false, "disable"));
        using var denied = await host.Send(BoundRequest(cookies, root + "/activation", disable.RootElement)); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }
}
