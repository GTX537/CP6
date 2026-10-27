using System.Net;
using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private async Task<FlowAuthoringOwnerV2> PreparePublication(Harness h)
    {
        var owner = await PrepareAuthoring(h);
        await owner.ConfigurePublicationAsync(new(Guid.NewGuid(), "absent", true, false, [], "explicit direct publication policy"));
        await EngineeringAction(h, "oa-designer", "publish"); return owner;
    }
    private static byte[] PublicationCommand(FlowPublicationPreviewV2 p, string id = "publish", string comment = "reviewed")
        => JsonSerializer.SerializeToUtf8Bytes(new { commandId = id, expectedVersion = p.ExpectedVersion,
            body = new { draftVersionId = p.DraftVersionId, previewId = p.Id, previewDigest = p.Digest, expectedVersion = p.ExpectedVersion, comment } });
    private static byte[] NextDraftCommand(string token, Guid source, string id = "next")
        => JsonSerializer.SerializeToUtf8Bytes(new { commandId = id, expectedVersion = token, body = new { sourceVersionId = source } });
    private static Task<FlowPublicationPreviewV2> Preview(FlowAuthoringOwnerV2 owner, string key, WorkflowCommandResultV1 saved)
        => owner.PreviewPublicationAsync(key, new(saved.Result!.DraftId, saved.ResourceVersion!));

    [CurrentPermissionPostgresFact]
    public async Task Publication_exact_preview_publishes_once_and_next_draft_copies_named_immutable_source()
    {
        await using var h = await Seed(); var owner = await PreparePublication(h);
        var saved = await owner.SaveAsync("versioned", AuthoringCommand()); var preview = await Preview(owner, "versioned", saved);
        Assert.True(preview.CanPublish); Assert.Empty(preview.Dependencies);
        var command = PublicationCommand(preview); var first = await owner.PublishAsync("versioned", command);
        var p = first.Result!.Publication!; Assert.Equal(saved.Result!.DraftId, p.VersionId); Assert.Equal(1, p.Version);
        Assert.False(p.ActivationChanged); Assert.Equal(AuthoringSchema, p.Fields["SchemaJson"]);
        Assert.True((await owner.ReadHeadAsync("versioned")).Published); Assert.False((await owner.ReadHeadAsync("versioned")).Enabled);
        Assert.Equal("DRAFT_REQUIRED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.ReadAsync("versioned"))).Message);
        Assert.Equal(first.ResourceVersion, (await owner.PublishAsync("versioned", command)).ResourceVersion);
        Assert.Equal("COMMAND_BODY_CONFLICT", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.PublishAsync("versioned", PublicationCommand(preview, comment: "changed")))).Message);
        Assert.Equal("DRAFT_REQUIRED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("versioned", AuthoringCommand("bad", first.ResourceVersion)))).Message);
        var next = await owner.CreateNextDraftAsync("versioned", NextDraftCommand(first.ResourceVersion!, p.VersionId));
        Assert.Equal(2, next.Result!.Draft!.VersionNumber); Assert.NotEqual(p.VersionId, next.Result.Draft.DraftId);
        Assert.Equal(AuthoringSchema, next.Result.Draft.Fields["SchemaJson"]);
        Assert.Equal("DRAFT_EXISTS", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.CreateNextDraftAsync("versioned", NextDraftCommand(next.ResourceVersion!, p.VersionId, "second")))).Message);
        var edit = await owner.SaveAsync("versioned", AuthoringCommand("edit2", next.ResourceVersion, "second version"));
        var two = await owner.PublishAsync("versioned", PublicationCommand(await Preview(owner, "versioned", edit), "publish2"));
        Assert.Equal(2, two.Result!.Publication!.Version);
        var oldSource = await owner.CreateNextDraftAsync("versioned", NextDraftCommand(two.ResourceVersion!, p.VersionId, "from-old"));
        Assert.Equal("draft", oldSource.Result!.Draft!.Fields["Name"]); Assert.Equal(3, oldSource.Result.Draft.VersionNumber);
        var versions = await owner.VersionsAsync("versioned"); Assert.Equal(2, versions.Length); Assert.Equal("draft", versions[1].Fields["Name"]);
        Assert.Empty(await h.Db.Wf_FlowDefs.ToArrayAsync());
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_workflow_authoring_v2.versions SET snapshot_json='{{}}' WHERE tenant_id={h.Tenant}"));
    }
    [CurrentPermissionPostgresFact]
    public async Task Publication_policy_registration_does_not_grant_publish_and_distinct_author_is_current()
    {
        await using var h = await Seed(); var owner = await PrepareAuthoring(h); var saved = await owner.SaveAsync("permissions", AuthoringCommand());
        var configured = await owner.ConfigurePublicationAsync(new(Guid.NewGuid(), "absent", true, true, [], "four eyes"));
        Assert.Equal("ACTION_FORBIDDEN", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => Preview(owner, "permissions", saved))).Message);
        await EngineeringAction(h, "oa-designer", "publish");
        Assert.Equal("PUBLICATION_SOD_FORBIDDEN", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => Preview(owner, "permissions", saved))).Message);
        await owner.ConfigurePublicationAsync(new(Guid.NewGuid(), configured.Policy.Version.ToString("D"), true, false, [], "approved direct policy"));
        Assert.True((await Preview(owner, "permissions", saved)).CanPublish);
    }
    [CurrentPermissionPostgresFact]
    public async Task Publication_stale_preview_policy_and_incomplete_graph_never_write_a_version()
    {
        await using var h = await Seed(); var owner = await PreparePublication(h); var saved = await owner.SaveAsync("stale", AuthoringCommand());
        var preview = await Preview(owner, "stale", saved);
        var policy = (await owner.PublicationPolicyAsync())!;
        await owner.ConfigurePublicationAsync(new(Guid.NewGuid(), policy.Version.ToString("D"), true, false, [], "new policy revision"));
        Assert.Equal("PREVIEW_STALE", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.PublishAsync("stale", PublicationCommand(preview)))).Message);
        var changed = await owner.SaveAsync("stale", AuthoringCommand("edit", saved.ResourceVersion, schema: "{\"start\":\"s\",\"nodes\":[{\"id\":\"s\",\"type\":\"start\"}],\"edges\":[]}"));
        Assert.Equal("DEFINITION_CHANGED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.PublishAsync("stale", PublicationCommand(preview)))).Message);
        var invalid = await Preview(owner, "stale", changed); Assert.False(invalid.CanPublish); Assert.NotEmpty(invalid.Diagnostics);
        Assert.Equal("PUBLICATION_VALIDATION_FAILED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.PublishAsync("stale", PublicationCommand(invalid, "invalid")))).Message);
        Assert.Empty(await owner.VersionsAsync("stale")); Assert.False((await owner.ReadAsync("stale")).Published);
    }
    [CurrentPermissionPostgresFact]
    public async Task Publication_unknown_commit_replays_original_after_write_revoke_and_projects_current_fields()
    {
        var fault = new CommitFault(); await using var h = await Seed(fault); var owner = await PreparePublication(h);
        var saved = await owner.SaveAsync("lost-publish", AuthoringCommand()); var preview = await Preview(owner, "lost-publish", saved); var command = PublicationCommand(preview);
        fault.Armed = true;
        Assert.Equal("COMMAND_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.PublishAsync("lost-publish", command))).Message);
        await h.Db.Sys_RoleActions.Where(a => a.ActionCode == "edit" || a.ActionCode == "publish").ExecuteDeleteAsync();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_scope_authorization_v1.fences SET enabled=false WHERE tenant_id={h.Tenant}");
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = FlowAuthoringOwnerV2.Resource, FieldName = "SchemaJson", Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var original = await owner.LifecycleOriginalAsync("lost-publish", FlowAuthoringOwnerV2.PublishOperation, "publish");
        Assert.Equal("SUCCEEDED", original.Outcome); Assert.DoesNotContain("SchemaJson", original.Result!.Publication!.Fields.Keys); Assert.Null(original.Result.Publication.SchemaDigest);
        Assert.Equal(original.ResourceVersion, (await owner.PublishAsync("lost-publish", command)).ResourceVersion);
        Assert.Single(await owner.VersionsAsync("lost-publish")); Assert.Null((await owner.VersionsAsync("lost-publish"))[0].SchemaDigest);
        Assert.Equal("NOT_OBSERVED", (await owner.LifecycleOriginalAsync("lost-publish", FlowAuthoringOwnerV2.NextDraftOperation, "publish")).Outcome);
    }
    [CurrentPermissionPostgresFact]
    public async Task Publication_final_revoke_rolls_back_version_draft_transition_and_receipt()
    {
        await using var h = await Seed(); var normal = await PreparePublication(h); var saved = await normal.SaveAsync("rollback-publish", AuthoringCommand());
        var preview = await Preview(normal, "rollback-publish", saved); var calls = 0;
        var owner = Authoring(h, async _ =>
        {
            if (++calls == 2) await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_workflow_authoring_v2.publication_policy SET enabled=false WHERE tenant_id={h.Tenant}");
            return h.User;
        });
        Assert.Equal("PUBLICATION_POLICY_DISABLED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.PublishAsync("rollback-publish", PublicationCommand(preview)))).Message);
        Assert.Empty(await normal.VersionsAsync("rollback-publish")); Assert.Equal(saved.ResourceVersion, (await normal.ReadAsync("rollback-publish")).Version);
        Assert.Equal("NOT_OBSERVED", (await normal.LifecycleOriginalAsync("rollback-publish", FlowAuthoringOwnerV2.PublishOperation, "publish")).Outcome);
        Assert.True((await normal.PublicationPolicyAsync())!.Enabled);
    }
    [CurrentPermissionPostgresFact]
    public async Task Publication_missing_protection_blocks_new_publication_but_keeps_original_read()
    {
        await using var h = await Seed(); var owner = await PreparePublication(h); var saved = await owner.SaveAsync("coverage", AuthoringCommand());
        var preview = await Preview(owner, "coverage", saved);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_workflow_authoring_v2.versions DISABLE TRIGGER publication_versions_immutable");
        try
        {
            Assert.Equal("PUBLICATION_WRITER_COVERAGE_UNAVAILABLE", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.PublishAsync("coverage", PublicationCommand(preview)))).Message);
            Assert.Equal("SUCCEEDED", (await owner.OriginalAsync("coverage", "first")).Outcome);
        }
        finally { await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_workflow_authoring_v2.versions ENABLE TRIGGER publication_versions_immutable"); }
    }
    [CurrentPermissionPostgresFact]
    public async Task Publication_unqualified_form_reference_remains_draft_and_does_not_infer_current_head_evidence()
    {
        await using var h = await Seed(); var owner = await PreparePublication(h);
        var body = JsonSerializer.SerializeToUtf8Bytes(new { commandId = "form", expectedVersion = (string?)null,
            body = new { name = "with form", schemaJson = AuthoringSchema, formKey = "legacy-form", functionId = (string?)null, flowCode = (string?)null } });
        var saved = await owner.SaveAsync("form-source", body); var preview = await Preview(owner, "form-source", saved);
        Assert.False(preview.CanPublish); Assert.Contains("FORM_PUBLICATION_SOURCE_UNPROVEN", preview.Diagnostics);
        Assert.Equal("UNPROVEN", Assert.Single(preview.Dependencies).EvidenceState);
        Assert.Equal("PUBLICATION_VALIDATION_FAILED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.PublishAsync("form-source", PublicationCommand(preview)))).Message);
        Assert.Empty(await owner.VersionsAsync("form-source"));
    }
    [CurrentPermissionPostgresFact]
    public async Task Publication_actual_HTTP_preview_publish_versions_and_original_use_bound_session()
    {
        await using var h = await Seed(); var owner = await PreparePublication(h); var saved = await owner.SaveAsync("http-publication", AuthoringCommand());
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,
            new Dictionary<string, string> { ["ScopeAuthorization__EnvironmentId"] = ScopeEnvironment.ToString("D") });
        var cookies = await Login(host, h); const string root = "/api/oa/v2/definitions/FLOW/http-publication";
        using var response = await host.Send(BoundRequest(cookies, root + "/publish-preview", new FlowPublicationPreviewInputV2(saved.Result!.DraftId, saved.ResourceVersion!)));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var preview = json.RootElement.GetProperty("data").Deserialize<FlowPublicationPreviewV2>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        using var command = JsonDocument.Parse(PublicationCommand(preview));
        using var published = await host.Send(BoundRequest(cookies, root + "/publish", command.RootElement));
        Assert.True(published.IsSuccessStatusCode, await published.Content.ReadAsStringAsync()); Assert.True(published.Headers.CacheControl?.NoStore);
        using var versions = await host.Send(BoundRequest(cookies, root + "/versions")); Assert.Equal(HttpStatusCode.OK, versions.StatusCode);
        using var original = await host.Send(BoundRequest(cookies, "/api/oa/v2/commands/publish?operation=FLOW_PUBLISH&resource=http-publication"));
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        using var draft = await host.Send(BoundRequest(cookies, root + "/draft")); Assert.Equal(HttpStatusCode.NotFound, draft.StatusCode);
    }
    [CurrentPermissionPostgresFact]
    public async Task Publication_preview_expiry_at_completion_rolls_back_all_publication_effects()
    {
        await using var h = await Seed(); var normal = await PreparePublication(h); var saved = await normal.SaveAsync("expiry", AuthoringCommand());
        var preview = await Preview(normal, "expiry", saved); var calls = 0; var time = DateTimeOffset.UtcNow;
        var owner = Authoring(h, _ => { if (++calls == 2) time = preview.ExpiresAtUtc.AddSeconds(1); return Task.FromResult(h.User); });
        owner.PublicationClock = () => time;
        Assert.Equal("PREVIEW_STALE", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.PublishAsync("expiry", PublicationCommand(preview)))).Message);
        Assert.Empty(await normal.VersionsAsync("expiry")); Assert.Equal(saved.ResourceVersion, (await normal.ReadAsync("expiry")).Version);
        Assert.Equal("NOT_OBSERVED", (await normal.LifecycleOriginalAsync("expiry", FlowAuthoringOwnerV2.PublishOperation, "publish")).Outcome);
    }
    [CurrentPermissionPostgresFact]
    public async Task Publication_policy_revoke_waits_until_commit_and_later_publish_is_denied()
    {
        await using var h = await Seed(); var normal = await PreparePublication(h); var saved = await normal.SaveAsync("race", AuthoringCommand());
        var preview = await Preview(normal, "race", saved);
        await using var writer = server.Context(new CP6.Core.Services.Common.TenantContext { CurrentTenantId = h.Tenant });
        var app = "publication-revoke-" + Guid.NewGuid().ToString("N"); writer.Database.SetConnectionString(writer.Database.GetConnectionString() + ";Application Name=" + app);
        await using var monitor = server.Context(new CP6.Core.Services.Common.TenantContext { CurrentTenantId = h.Tenant });
        Task<int>? revoke = null; var calls = 0;
        var owner = Authoring(h, async _ =>
        {
            if (++calls == 2)
            {
                revoke = writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_workflow_authoring_v2.publication_policy SET enabled=false WHERE tenant_id={h.Tenant}");
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
        var result = await owner.PublishAsync("race", PublicationCommand(preview)); Assert.NotNull(revoke); await revoke!;
        Assert.Equal("SUCCEEDED", result.Outcome); Assert.Single(await normal.VersionsAsync("race"));
        var next = await normal.CreateNextDraftAsync("race", NextDraftCommand(result.ResourceVersion!, result.Result!.Publication!.VersionId));
        Assert.Equal("PUBLICATION_POLICY_DISABLED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => normal.PreviewPublicationAsync("race", new(next.Result!.Draft!.DraftId, next.ResourceVersion!)))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Publication_server_state_in_draft_body_is_rejected_and_hidden_publication_state_is_not_disclosed()
    {
        await using var h = await Seed(); var owner = await PreparePublication(h);
        var forged = JsonSerializer.SerializeToUtf8Bytes(new { commandId = "forged", expectedVersion = (string?)null,
            body = new { name = "forged", schemaJson = AuthoringSchema, formKey = (string?)null, functionId = (string?)null, flowCode = (string?)null,
                savedBy = Guid.NewGuid(), status = 1, number = 999 } });
        Assert.Equal("COMMAND_SHAPE_INVALID", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("forged", forged))).Message);
        Assert.Empty(await owner.ListAsync());
        var saved = await owner.SaveAsync("private-published", AuthoringCommand());
        var published = await owner.PublishAsync("private-published", PublicationCommand(await Preview(owner, "private-published", saved)));
        var other = new Sys_User { UserName = "reader-" + h.Tenant.ToString("N"), Password = "unused", RoleId = 72 };
        h.Db.Sys_Users.Add(other); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        h.Http.User = CurrentPermissionContextTests.Principal(other.Id, h.Tenant); var hidden = Authoring(h, _ => Task.FromResult(other.Id));
        Assert.Equal("DEFINITION_NOT_FOUND", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => hidden.ReadAsync("private-published"))).Message);
        Assert.Equal("DEFINITION_NOT_FOUND", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => hidden.SaveAsync("private-published", AuthoringCommand("attack", published.ResourceVersion)))).Message);
        Assert.Equal("DEFINITION_NOT_FOUND", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => hidden.VersionsAsync("private-published"))).Message);
    }

}
