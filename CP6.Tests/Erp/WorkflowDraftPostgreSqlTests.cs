using CP6.Core.Services.Wf;
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
    [CurrentPermissionPostgresFact]
    public async Task Workflow_draft_read_does_not_create_version_or_track_mutable_entities()
    {
        await using var h = await Seed(); var source = await PublishedDraftSource(h);
        var service = new FlowDefService(h.Db);
        Assert.Null(await service.GetDraftAsync(source.Key));
        Assert.Null(await service.GetDraftAsync(source.Key, true)); // Legacy argument cannot turn a read into a write.
        Assert.Equal(1, await h.Db.Wf_FlowDefVersions.CountAsync());
        Assert.Empty(h.Db.ChangeTracker.Entries());
    }
    private static FlowDraftOwnerV1 DraftOwner(Harness h) => new(h.Db, new HttpContextAccessor { HttpContext = h.Http });
    private static async Task<FlowDraftCreateV1> DraftCommand(FlowDraftOwnerV1 owner, string key, Guid source)
        => new(Guid.NewGuid(), source, (await owner.ContextAsync(key)).HeadVersion);
    [CurrentPermissionPostgresFact]
    public async Task Workflow_draft_explicit_copy_keeps_exact_source_raw_schema_and_published_head()
    {
        await using var h = await Seed(); var s = await PublishedDraftSource(h); await EngineeringAction(h, "oa-designer", "edit");
        var owner = DraftOwner(h); var command = await DraftCommand(owner, s.Key, s.Source);
        var created = await owner.CreateAsync(s.Key, command);
        Assert.Equal(2, created.Draft.Version); Assert.Equal(DraftFlowSchema, created.Draft.SchemaJson);
        Assert.Equal(s.Source, created.SourceVersionId); Assert.Equal(0, created.Draft.Status);
        var head = await h.Db.Wf_FlowDefs.AsNoTracking().SingleAsync(); Assert.Equal(1, head.Version); Assert.False(head.Enable);
        var source = await h.Db.Wf_FlowDefVersions.AsNoTracking().SingleAsync(x => x.Id == s.Source);
        Assert.Equal(1, source.Status); Assert.Equal(DraftFlowSchema, source.SchemaJson);
        Assert.Equal(0, await h.Db.Wf_FlowInstances.CountAsync());
        Assert.Equal(created.Draft.VersionId, (await owner.OperationAsync(s.Key, command.OperationId)).Result!.Draft.VersionId);
        var legacy = new FlowDefService(h.Db); var update = await legacy.SaveDraftAsync(s.Key, "Edited", null, DraftFlowSchema, created.Draft.RowVersion);
        Assert.Equal(created.Draft.VersionId, update.VersionId); Assert.Equal(2, update.Version);
    }
    [CurrentPermissionPostgresFact]
    public async Task Workflow_draft_existing_head_requires_explicit_source_and_existing_draft_requires_token()
    {
        await using var h = await Seed(); var s = await PublishedDraftSource(h); await EngineeringAction(h, "oa-designer", "edit");
        var service = new FlowDefService(h.Db);
        Assert.Equal("DRAFT_REQUIRED", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveDraftAsync(s.Key, "Unchosen", null, "{}", null))).Message);
        Assert.False(h.Db.ChangeTracker.HasChanges()); h.Db.ChangeTracker.Clear();
        var owner = DraftOwner(h); var created = await owner.CreateAsync(s.Key, await DraftCommand(owner, s.Key, s.Source));
        Assert.Equal("E-WF-045", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveDraftAsync(s.Key, "No token", null, "{}", null))).Message);
        Assert.False(h.Db.ChangeTracker.HasChanges()); h.Db.ChangeTracker.Clear();
        var changed = await service.SaveDraftAsync(s.Key, "New", null, DraftFlowSchema, created.Draft.RowVersion); h.Db.ChangeTracker.Clear();
        Assert.Equal("E-WF-045", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveDraftAsync(s.Key, "Stale", null, "{}", created.Draft.RowVersion))).Message);
        Assert.False(h.Db.ChangeTracker.HasChanges()); Assert.NotEqual(created.Draft.RowVersion, changed.RowVersion);
    }
    [CurrentPermissionPostgresFact]
    public async Task Workflow_draft_wrong_source_stale_head_and_existing_draft_do_not_write()
    {
        await using var h = await Seed(); var s = await PublishedDraftSource(h); await EngineeringAction(h, "oa-designer", "edit"); var owner = DraftOwner(h);
        var cmd = await DraftCommand(owner, s.Key, Guid.NewGuid());
        Assert.Equal(422, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CreateAsync(s.Key, cmd))).Status);
        cmd = cmd with { SourceVersionId = s.Source };
        await h.Db.Wf_FlowDefs.ExecuteUpdateAsync(x => x.SetProperty(h => h.FlowName, "Renamed"));
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CreateAsync(s.Key, cmd))).Status);
        Assert.Equal("NOT_OBSERVED", (await owner.OperationAsync(s.Key, cmd.OperationId)).State);
        await owner.CreateAsync(s.Key, await DraftCommand(owner, s.Key, s.Source));
        var next = await DraftCommand(owner, s.Key, s.Source);
        Assert.Equal("DRAFT_EXISTS", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CreateAsync(s.Key, next))).Message);
        Assert.Equal(2, await h.Db.Wf_FlowDefVersions.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Workflow_draft_lost_commit_ACK_recovers_original_after_edit_revocation_and_conflicts_on_changed_input()
    {
        var fault = new CommitFault(); await using var h = await Seed(fault); var s = await PublishedDraftSource(h); await EngineeringAction(h, "oa-designer", "edit");
        var owner = DraftOwner(h); var cmd = await DraftCommand(owner, s.Key, s.Source); fault.Armed = true;
        Assert.Equal("DRAFT_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CreateAsync(s.Key, cmd))).Message);
        await h.Db.Sys_RoleActions.Where(x => x.ActionCode == "edit").ExecuteDeleteAsync();
        var result = await owner.CreateAsync(s.Key, cmd); Assert.Equal(result.Draft.VersionId, (await owner.OperationAsync(s.Key, cmd.OperationId)).Result!.Draft.VersionId);
        Assert.Equal("COMMAND_BODY_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CreateAsync(s.Key, cmd with { SourceVersionId = Guid.NewGuid() }))).Message);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CreateAsync(s.Key, cmd with { OperationId = Guid.NewGuid() }))).Status);
        Assert.Equal(2, await h.Db.Wf_FlowDefVersions.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Workflow_draft_parallel_creations_have_one_winner_and_one_version_number()
    {
        await using var h = await Seed(); var s = await PublishedDraftSource(h); await EngineeringAction(h, "oa-designer", "edit");
        var cmd = await DraftCommand(DraftOwner(h), s.Key, s.Source);
        async Task<bool> Attempt(Guid op)
        {
            await using var db = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
            var owner = new FlowDraftOwnerV1(db, new HttpContextAccessor { HttpContext = h.Http });
            try { await owner.CreateAsync(s.Key, cmd with { OperationId = op }); return true; }
            catch (OrderPriceCorrectionException e) when (e.Status == 412) { return false; }
        }
        Assert.Equal(1, (await Task.WhenAll(Attempt(Guid.NewGuid()), Attempt(Guid.NewGuid()))).Count(x => x));
        Assert.Equal(new[] { 1, 2 }, await h.Db.Wf_FlowDefVersions.OrderBy(x => x.Version).Select(x => x.Version).ToArrayAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Workflow_draft_final_source_change_rolls_back_new_draft_and_original_result()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); var s = await PublishedDraftSource(h); await EngineeringAction(h, "oa-designer", "edit");
        var owner = DraftOwner(h); var cmd = await DraftCommand(owner, s.Key, s.Source);
        var injected = false;
        hook.Before = async command =>
        {
            if (!command.CommandText.Contains("Wf_FlowDef")) return;
            await using var probe = command.Connection!.CreateCommand(); probe.Transaction = command.Transaction;
            probe.CommandText = "SELECT EXISTS(SELECT FROM public.\"Wf_FlowDefVersion\" WHERE \"TenantId\"=@tenant AND \"Status\"=0)";
            probe.Parameters.Add(new Npgsql.NpgsqlParameter("tenant", h.Tenant));
            if (!(bool)(await probe.ExecuteScalarAsync())!) return;
            hook.Before = null; injected = true;
            await using var mutate = command.Connection!.CreateCommand(); mutate.Transaction = command.Transaction;
            mutate.CommandText = "UPDATE public.\"Wf_FlowDef\" SET \"FlowName\"='changed in same UoW' WHERE \"FlowKey\"=@key";
            mutate.Parameters.Add(new Npgsql.NpgsqlParameter("key", s.Key)); await mutate.ExecuteNonQueryAsync();
        };
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CreateAsync(s.Key, cmd))).Status);
        Assert.True(injected);
        Assert.Equal(1, await h.Db.Wf_FlowDefVersions.CountAsync()); Assert.Equal("Original", (await h.Db.Wf_FlowDefs.AsNoTracking().SingleAsync()).FlowName);
        Assert.Equal("NOT_OBSERVED", (await owner.OperationAsync(s.Key, cmd.OperationId)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task Workflow_draft_actual_HTTP_reads_are_safe_and_explicit_creation_requires_current_edit_csrf_closed_shape()
    {
        await using var h = await Seed(); var s = await PublishedDraftSource(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        var path = $"/api/oa/flow-defs/{s.Key}";
        using (var get = await host.Send(BoundRequest(cookies, path + "/draft"))) Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        using (var load = await host.Send(BoundRequest(cookies, "/api/oa/designer/load/" + s.Key)))
        {
            Assert.Equal(HttpStatusCode.OK, load.StatusCode);
            var data = JsonDocument.Parse(await load.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
            Assert.Equal(DraftFlowSchema, data.GetProperty("schemaJson").GetString()); Assert.Equal(JsonValueKind.Null, data.GetProperty("draft").ValueKind);
        }
        Assert.Equal(1, await h.Db.Wf_FlowDefVersions.CountAsync());
        var cmd = await DraftCommand(DraftOwner(h), s.Key, s.Source);
        using (var denied = await host.Send(BoundRequest(cookies, path + "/drafts", cmd))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await EngineeringAction(h, "oa-designer", "edit");
        using (var csrf = await host.Send(BoundRequest(cookies, path + "/drafts", cmd, false))) Assert.Equal(HttpStatusCode.Forbidden, csrf.StatusCode);
        using (var extra = await host.Send(BoundRequest(cookies, path + "/drafts", new { cmd.OperationId, cmd.SourceVersionId, cmd.ExpectedHeadVersion, approved = true }))) Assert.Equal(HttpStatusCode.BadRequest, extra.StatusCode);
        using (var created = await host.Send(BoundRequest(cookies, path + "/drafts", cmd))) Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        using (var original = await host.Send(BoundRequest(cookies, path + "/draft-operations/" + cmd.OperationId))) Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        Assert.Equal(2, await h.Db.Wf_FlowDefVersions.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Workflow_draft_older_published_source_is_explicit_and_does_not_rewind_head()
    {
        await using var h = await Seed(); var s = await PublishedDraftSource(h); await EngineeringAction(h, "oa-designer", "edit");
        var head = await h.Db.Wf_FlowDefs.SingleAsync(); head.Version = 2; head.FlowName = "Later";
        h.Db.Wf_FlowDefVersions.Add(new Wf_FlowDefVersion { Id = Guid.NewGuid(), FlowDefId = head.Id, Version = 2, Status = 1,
            FlowNameSnapshot = "Later", SchemaJson = DraftFlowSchema.Replace("1.00", "2.00"), PublishedAtUtc = DateTime.UtcNow, PublishedBy = h.User });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var owner = DraftOwner(h); var c = await owner.ContextAsync(s.Key); Assert.Equal(new[] { 2, 1 }, c.Sources.Select(x => x.Version));
        var made = await owner.CreateAsync(s.Key, new(Guid.NewGuid(), s.Source, c.HeadVersion));
        Assert.Equal(3, made.Draft.Version); Assert.Equal("Original", made.Draft.Name); Assert.Equal(DraftFlowSchema, made.Draft.SchemaJson);
        Assert.Equal(2, (await h.Db.Wf_FlowDefs.AsNoTracking().SingleAsync()).Version);
    }
    [CurrentPermissionPostgresFact]
    public async Task Workflow_draft_foreign_tenant_source_and_locked_actor_cannot_create_or_disclose_original()
    {
        await using var h = await Seed(); var s = await PublishedDraftSource(h); await EngineeringAction(h, "oa-designer", "edit");
        await using var foreign = await Seed(); var other = await PublishedDraftSource(foreign); await EngineeringAction(foreign, "oa-designer", "edit");
        var owner = DraftOwner(h); var cmd = await DraftCommand(owner, s.Key, other.Source);
        Assert.Equal(422, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CreateAsync(s.Key, cmd))).Status);
        var created = await owner.CreateAsync(s.Key, cmd with { SourceVersionId = s.Source });
        Assert.Equal("NOT_OBSERVED", (await DraftOwner(foreign).OperationAsync(other.Key, created.OperationId)).State);
        // IHttpContextAccessor is AsyncLocal: restore this request after exercising the other tenant.
        owner = DraftOwner(h);
        await h.Db.Sys_Users.Where(x => x.Id == h.User).ExecuteUpdateAsync(x => x.SetProperty(u => u.LockedUntil, DateTime.Now.AddHours(1)));
        Assert.Equal(401, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.OperationAsync(s.Key, created.OperationId))).Status);
        Assert.Equal(401, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CreateAsync(s.Key, cmd with { OperationId = Guid.NewGuid() }))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Workflow_draft_final_edit_revocation_rolls_back_both_creation_and_original_journal()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); var s = await PublishedDraftSource(h); await EngineeringAction(h, "oa-designer", "edit");
        var owner = DraftOwner(h); var cmd = await DraftCommand(owner, s.Key, s.Source); var injected = false;
        var menu = await h.Db.Sys_Menus.AsNoTracking().SingleAsync(x => x.MenuKey == "oa-designer");
        hook.Before = async command =>
        {
            if (!command.CommandText.Contains("Wf_FlowDef")) return;
            await using var probe = command.Connection!.CreateCommand(); probe.Transaction = command.Transaction;
            probe.CommandText = "SELECT EXISTS(SELECT FROM public.\"Wf_FlowDefVersion\" WHERE \"TenantId\"=@tenant AND \"Status\"=0)";
            probe.Parameters.Add(new Npgsql.NpgsqlParameter("tenant", h.Tenant));
            if (!(bool)(await probe.ExecuteScalarAsync())!) return; hook.Before = null; injected = true;
            await h.Db.Sys_RoleActions.Where(x => x.MenuId == menu.MenuId && x.ActionCode == "edit").ExecuteDeleteAsync();
        };
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CreateAsync(s.Key, cmd))).Status);
        Assert.True(injected); Assert.Equal(1, await h.Db.Wf_FlowDefVersions.CountAsync());
        Assert.Equal("NOT_OBSERVED", (await owner.OperationAsync(s.Key, cmd.OperationId)).State);
        Assert.True(await h.Db.Sys_RoleActions.AnyAsync(x => x.MenuId == menu.MenuId && x.ActionCode == "edit"));
    }
    [CurrentPermissionPostgresFact]
    public async Task Workflow_draft_publication_requires_the_draft_token_and_does_not_recreate_after_publish()
    {
        await using var h = await Seed(); var s = await PublishedDraftSource(h); await EngineeringAction(h, "oa-designer", "edit");
        var owner = DraftOwner(h); var cmd = await DraftCommand(owner, s.Key, s.Source); var created = await owner.CreateAsync(s.Key, cmd);
        var service = new FlowDefService(h.Db);
        Assert.Equal("E-WF-045", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.PublishAsync(s.Key, null, h.User))).Message);
        Assert.False(h.Db.ChangeTracker.HasChanges());
        var published = await service.PublishAsync(s.Key, created.Draft.RowVersion, h.User); h.Db.ChangeTracker.Clear();
        Assert.Equal(created.Draft.VersionId, published.VersionId); Assert.Null(await service.GetDraftAsync(s.Key));
        Assert.Equal(2, await h.Db.Wf_FlowDefVersions.CountAsync());
        var replay = await owner.CreateAsync(s.Key, cmd); Assert.Equal(created.Draft.VersionId, replay.Draft.VersionId);
        Assert.Equal(0, replay.Draft.Status); // Original creation result is immutable, not rewritten as a later publication.
        Assert.False((await h.Db.Wf_FlowDefs.AsNoTracking().SingleAsync()).Enable);
    }
}
