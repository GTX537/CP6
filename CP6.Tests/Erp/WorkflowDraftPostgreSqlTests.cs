using CP6.Core.Services.Wf;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Common;
using Microsoft.AspNetCore.Http;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string DraftFlowSchema = """{"start":"s","nodes":[{"id":"s","type":"start"},{"id":"e","type":"end"}],"edges":[{"from":"s","to":"e"}],"layout":{"label":"原件 é","scale":1.00}}""";
    private static async Task<(string Key, Guid Source)> PublishedDraftSource(Harness h)
    {
        var key = "draft-" + h.Tenant.ToString("N");
        var head = new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = key, FlowName = "Original", SchemaJson = DraftFlowSchema, Enable = false };
        var version = new Wf_FlowDefVersion { Id = Guid.NewGuid(), FlowDefId = head.Id, Version = 1,
            Status = WfDefinitionVersionStatus.Published, FlowNameSnapshot = "Original", SchemaJson = DraftFlowSchema,
            PublishedAtUtc = DateTime.UtcNow, PublishedBy = h.User };
        h.Db.Wf_FlowDefs.Add(head); h.Db.Wf_FlowDefVersions.Add(version);
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); return (key, version.Id);
    }

    private FlowDraftOwnerV1 DraftOwner(Harness h) => new(h.Db, Authoring(h));
    private async Task<(FlowAuthoringOwnerV2 Owner, WorkflowLifecycleResultV2 Published)> DraftV2Source(Harness h)
    {
        var owner = await PreparePublication(h); var saved = await owner.SaveAsync("draft-current", AuthoringCommand(schema: DraftFlowSchema));
        return (owner, await owner.PublishAsync("draft-current", PublicationCommand(await Preview(owner, "draft-current", saved))));
    }
    [CurrentPermissionPostgresFact]
    public async Task Workflow_draft_read_does_not_create_version_or_track_mutable_entities()
    {
        await using var h = await Seed(); var source = await PublishedDraftSource(h); var service = new FlowDefService(h.Db);
        Assert.Null(await service.GetDraftAsync(source.Key)); Assert.Null(await service.GetDraftAsync(source.Key, true));
        h.Db.Wf_FormDefs.Add(new() { Id = Guid.NewGuid(), FormKey = "head-only", FormName = "Legacy", SchemaJson = "{}" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); var form = new FormService(h.Db);
        Assert.Null(await form.GetDraftAsync("head-only")); Assert.Null(await form.GetDraftAsync("head-only", true));
        Assert.Empty(await h.Db.Wf_FormDefVersions.ToArrayAsync()); Assert.Equal(1, await h.Db.Wf_FlowDefVersions.CountAsync()); Assert.Empty(h.Db.ChangeTracker.Entries());
    }

    [CurrentPermissionPostgresFact]
    public async Task Workflow_draft_explicit_copy_keeps_exact_source_raw_schema_and_published_head()
    {
        await using var h = await Seed(); var (owner, p) = await DraftV2Source(h); var source = p.Result!.Publication!;
        var made = await owner.CreateNextDraftAsync("draft-current", NextDraftCommand(p.ResourceVersion!, source.VersionId));
        Assert.Equal(2, made.Result!.Draft!.VersionNumber); Assert.Equal(DraftFlowSchema, made.Result.Draft.Fields["SchemaJson"]);
        var edited = await owner.SaveAsync("draft-current", AuthoringCommand("edit", made.ResourceVersion, "Edited", DraftFlowSchema));
        Assert.Equal(made.Result.Draft.DraftId, edited.Result!.DraftId); Assert.False(edited.Result.Enabled);
        Assert.Equal(source.VersionId, (await owner.VersionsAsync("draft-current")).Single().VersionId);
        Assert.Equal(made.ResourceVersion, (await owner.LifecycleOriginalAsync("draft-current", FlowAuthoringOwnerV2.NextDraftOperation, "next")).ResourceVersion);
        Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Workflow_draft_existing_head_requires_explicit_source_and_existing_draft_requires_token()
    {
        await using var h = await Seed(); var (owner, p) = await DraftV2Source(h);
        Assert.Equal("DRAFT_REQUIRED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("draft-current", AuthoringCommand("implicit", p.ResourceVersion)))).Message);
        var made = await owner.CreateNextDraftAsync("draft-current", NextDraftCommand(p.ResourceVersion!, p.Result!.Publication!.VersionId));
        Assert.Equal("DEFINITION_EXISTS", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("draft-current", AuthoringCommand("no-token")))).Message);
        var edited = await owner.SaveAsync("draft-current", AuthoringCommand("edit", made.ResourceVersion, "winner"));
        Assert.Equal("DEFINITION_CHANGED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("draft-current", AuthoringCommand("stale", made.ResourceVersion)))).Message);
        Assert.Equal(edited.ResourceVersion, (await owner.ReadAsync("draft-current")).Version);
    }

    [CurrentPermissionPostgresFact]
    public async Task Workflow_draft_wrong_source_stale_head_and_existing_draft_do_not_write()
    {
        await using var h = await Seed(); var (owner, p) = await DraftV2Source(h);
        Assert.Equal("VERSION_NOT_FOUND", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.CreateNextDraftAsync("draft-current", NextDraftCommand(p.ResourceVersion!, Guid.NewGuid())))).Message);
        Assert.Equal("DEFINITION_CHANGED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.CreateNextDraftAsync("draft-current", NextDraftCommand(Guid.NewGuid().ToString("D"), p.Result!.Publication!.VersionId)))).Message);
        var made = await owner.CreateNextDraftAsync("draft-current", NextDraftCommand(p.ResourceVersion!, p.Result!.Publication!.VersionId));
        Assert.Equal("DRAFT_EXISTS", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.CreateNextDraftAsync("draft-current", NextDraftCommand(made.ResourceVersion!, p.Result.Publication.VersionId, "another")))).Message);
        Assert.Equal("NOT_OBSERVED", (await owner.LifecycleOriginalAsync("draft-current", FlowAuthoringOwnerV2.NextDraftOperation, "another")).Outcome);
    }

    [CurrentPermissionPostgresFact]
    public async Task Workflow_draft_lost_commit_ACK_recovers_original_after_edit_revocation_and_conflicts_on_changed_input()
    {
        var fault = new CommitFault(); await using var h = await Seed(fault); var (owner, p) = await DraftV2Source(h);
        var command = NextDraftCommand(p.ResourceVersion!, p.Result!.Publication!.VersionId); fault.Armed = true;
        Assert.Equal("COMMAND_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.CreateNextDraftAsync("draft-current", command))).Message);
        await h.Db.Sys_RoleActions.Where(x => x.ActionCode == "edit").ExecuteDeleteAsync();
        var replay = await owner.CreateNextDraftAsync("draft-current", command);
        Assert.Equal(replay.ResourceVersion, (await owner.LifecycleOriginalAsync("draft-current", FlowAuthoringOwnerV2.NextDraftOperation, "next")).ResourceVersion);
        Assert.Equal("COMMAND_BODY_CONFLICT", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.CreateNextDraftAsync("draft-current", NextDraftCommand(p.ResourceVersion!, Guid.NewGuid())))).Message);
        Assert.Equal(403, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.CreateNextDraftAsync("draft-current", NextDraftCommand(replay.ResourceVersion!, p.Result.Publication.VersionId, "new")))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Workflow_draft_parallel_creations_have_one_winner_and_one_version_number()
    {
        await using var h = await Seed(); var (_, p) = await DraftV2Source(h);
        async Task<bool> Attempt(string id)
        {
            await using var db = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
            try { await Authoring(h with { Db = db }).CreateNextDraftAsync("draft-current", NextDraftCommand(p.ResourceVersion!, p.Result!.Publication!.VersionId, id)); return true; }
            catch (ScopeAuthorizationExceptionV1 e) when (e.Message == "DEFINITION_CHANGED") { return false; }
        }
        Assert.Equal(1, (await Task.WhenAll(Attempt("one"), Attempt("two"))).Count(x => x));
        Assert.Equal(new[] { 2 }, await h.Db.Database.SqlQuery<int>($"SELECT version_number AS \"Value\" FROM cp6_workflow_authoring_v2.drafts WHERE tenant_id={h.Tenant} ORDER BY version_number").ToArrayAsync());
        Assert.Equal(1, (await Authoring(h).VersionsAsync("draft-current")).Single().Version);
    }

    [CurrentPermissionPostgresFact]
    public async Task Workflow_draft_final_source_change_rolls_back_new_draft_and_original_result()
    {
        await using var h = await Seed(); var (owner, p) = await DraftV2Source(h); var calls = 0;
        var guarded = Authoring(h, async _ =>
        {
            if (++calls == 2) await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_workflow_authoring_v2.versions SET snapshot_json='{{}}' WHERE tenant_id={h.Tenant}");
            return h.User;
        });
        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => guarded.CreateNextDraftAsync("draft-current", NextDraftCommand(p.ResourceVersion!, p.Result!.Publication!.VersionId)));
        Assert.Equal("23514", error.SqlState); Assert.Equal("SCOPE_AUTHORITY_IMMUTABLE", error.MessageText);
        Assert.Equal(p.ResourceVersion, (await owner.ReadHeadAsync("draft-current")).Version);
        Assert.Equal("NOT_OBSERVED", (await owner.LifecycleOriginalAsync("draft-current", FlowAuthoringOwnerV2.NextDraftOperation, "next")).Outcome);
        Assert.Equal(DraftFlowSchema, (await owner.VersionsAsync("draft-current")).Single().Fields["SchemaJson"]);
    }

    [CurrentPermissionPostgresFact]
    public async Task Workflow_draft_actual_HTTP_reads_are_safe_and_explicit_creation_requires_current_edit_csrf_closed_shape()
    {
        await using var h = await Seed(); var (owner, p) = await DraftV2Source(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, new Dictionary<string, string> { ["ScopeAuthorization__EnvironmentId"] = ScopeEnvironment.ToString("D") });
        var cookies = await Login(host, h); const string path = "/api/oa/v2/definitions/FLOW/draft-current";
        using (var get = await host.Send(BoundRequest(cookies, path + "/draft"))) Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.True((await owner.ReadHeadAsync("draft-current")).Published);
        using var command = JsonDocument.Parse(NextDraftCommand(p.ResourceVersion!, p.Result!.Publication!.VersionId));
        using (var csrf = await host.Send(BoundRequest(cookies, path + "/drafts", command.RootElement, false))) Assert.Equal(HttpStatusCode.Forbidden, csrf.StatusCode);
        using (var extra = await host.Send(BoundRequest(cookies, path + "/drafts", new { commandId = "extra", expectedVersion = p.ResourceVersion, body = new { sourceVersionId = p.Result.Publication.VersionId, approved = true } }))) Assert.Equal(HttpStatusCode.BadRequest, extra.StatusCode);
        await h.Db.Sys_RoleActions.Where(x => x.ActionCode == "edit").ExecuteDeleteAsync();
        using (var denied = await host.Send(BoundRequest(cookies, path + "/drafts", command.RootElement))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await EngineeringAction(h, "oa-designer", "edit");
        using var created = await host.Send(BoundRequest(cookies, path + "/drafts", command.RootElement)); Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        using var original = await host.Send(BoundRequest(cookies, "/api/oa/v2/commands/next?operation=FLOW_CREATE_DRAFT&resource=draft-current")); Assert.Equal(HttpStatusCode.OK, original.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Workflow_draft_older_published_source_is_explicit_and_does_not_rewind_head()
    {
        await using var h = await Seed(); var (owner, p) = await DraftV2Source(h); var source = p.Result!.Publication!;
        var next = await owner.CreateNextDraftAsync("draft-current", NextDraftCommand(p.ResourceVersion!, source.VersionId));
        var edit = await owner.SaveAsync("draft-current", AuthoringCommand("edit", next.ResourceVersion, "Later", DraftFlowSchema.Replace("1.00", "2.00")));
        var two = await owner.PublishAsync("draft-current", PublicationCommand(await Preview(owner, "draft-current", edit), "publish-two"));
        var made = await owner.CreateNextDraftAsync("draft-current", NextDraftCommand(two.ResourceVersion!, source.VersionId, "older"));
        Assert.Equal(3, made.Result!.Draft!.VersionNumber); Assert.Equal(DraftFlowSchema, made.Result.Draft.Fields["SchemaJson"]);
        Assert.Equal(new[] { 2, 1 }, (await owner.VersionsAsync("draft-current")).Select(x => x.Version));
    }

    [CurrentPermissionPostgresFact]
    public async Task Workflow_draft_foreign_tenant_source_and_locked_actor_cannot_create_or_disclose_original()
    {
        await using var h = await Seed(); var (owner, p) = await DraftV2Source(h);
        await using var foreign = await Seed(); var (other, foreignPublication) = await DraftV2Source(foreign);
        Assert.Equal(404, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.CreateNextDraftAsync("draft-current", NextDraftCommand(p.ResourceVersion!, foreignPublication.Result!.Publication!.VersionId)))).Status);
        await owner.CreateNextDraftAsync("draft-current", NextDraftCommand(p.ResourceVersion!, p.Result!.Publication!.VersionId));
        Assert.Equal("NOT_OBSERVED", (await other.LifecycleOriginalAsync("draft-current", FlowAuthoringOwnerV2.NextDraftOperation, "next")).Outcome);
        await h.Db.Sys_Users.Where(x => x.Id == h.User).ExecuteUpdateAsync(x => x.SetProperty(u => u.LockedUntil, DateTime.Now.AddHours(1)));
        Assert.Equal(401, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.LifecycleOriginalAsync("draft-current", FlowAuthoringOwnerV2.NextDraftOperation, "next"))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Workflow_draft_final_edit_revocation_rolls_back_both_creation_and_original_journal()
    {
        await using var h = await Seed(); var (owner, p) = await DraftV2Source(h); var calls = 0;
        var guarded = Authoring(h, async _ => { if (++calls == 2) await h.Db.Sys_RoleActions.Where(x => x.ActionCode == "edit").ExecuteDeleteAsync(); return h.User; });
        Assert.Equal(403, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => guarded.CreateNextDraftAsync("draft-current", NextDraftCommand(p.ResourceVersion!, p.Result!.Publication!.VersionId)))).Status);
        Assert.Equal(p.ResourceVersion, (await owner.ReadHeadAsync("draft-current")).Version);
        Assert.True(await h.Db.Sys_RoleActions.AnyAsync(x => x.ActionCode == "edit"));
        Assert.Equal("NOT_OBSERVED", (await owner.LifecycleOriginalAsync("draft-current", FlowAuthoringOwnerV2.NextDraftOperation, "next")).Outcome);
    }

    [CurrentPermissionPostgresFact]
    public async Task Workflow_draft_publication_requires_the_draft_token_and_does_not_recreate_after_publish()
    {
        await using var h = await Seed(); var (owner, p) = await DraftV2Source(h);
        var command = NextDraftCommand(p.ResourceVersion!, p.Result!.Publication!.VersionId); var created = await owner.CreateNextDraftAsync("draft-current", command);
        Assert.Equal("DEFINITION_CHANGED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.PreviewPublicationAsync("draft-current", new(created.Result!.Draft!.DraftId, p.ResourceVersion!)))).Message);
        var preview = await owner.PreviewPublicationAsync("draft-current", new(created.Result!.Draft!.DraftId, created.ResourceVersion!));
        await owner.PublishAsync("draft-current", PublicationCommand(preview, "publish-next"));
        Assert.Equal(404, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.ReadAsync("draft-current"))).Status);
        var replay = await owner.CreateNextDraftAsync("draft-current", command); Assert.Equal(created.ResourceVersion, replay.ResourceVersion); Assert.False(replay.Result!.Draft!.Published);
        Assert.Equal(2, (await owner.VersionsAsync("draft-current")).Length); Assert.False((await owner.ReadHeadAsync("draft-current")).Enabled);
    }
}
