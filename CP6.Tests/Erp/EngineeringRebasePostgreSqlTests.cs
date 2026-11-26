using System.Net;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static EngineeringRevisionCommandV1 RebaseCommand(EngineeringRevisionSnapshotV1 current, EngineeringRebaseSourcesV1 sources,
        params EngineeringRebaseResolutionV1[] resolutions) => new()
    {
        OperationId = Guid.NewGuid(), RevisionId = current.Reference.Id, Action = "REBASE", ExpectedVersion = current.Reference.Version,
        ExpectedEtag = current.Etag, Reason = "以指定来源追加工作版", Rebase = new(sources, resolutions)
    };

    private static EngineeringDraftPatchV1 Upsert(EngineeringComponentDraftV1 value) => new() { Op = "UPSERT_COMPONENT", Component = value };
    private static EngineeringRevisionRefV1 BaselineRef(EngineeringSourceRefV1 value) => new(value.Owner, value.Id, value.Version, value.Digest);

    [CurrentPermissionPostgresFact]
    public async Task EngineeringRebase_HTTP_previews_exact_saved_candidates_and_recovers_the_same_operation()
    {
        await using var h = await Seed();
        await GrantEngineeringRevision(h);
        await GrantEngineering(h);
        await RevisionRule(h);
        var product = await MakeEngineering(h);
        var owner = Revisions(h);
        var basis = await MakeRevision(owner, product.Reference);
        var incoming = await MakeRevision(owner, product.Reference);
        await owner.CommandAsync(PatchRevision(incoming, new EngineeringDraftPatchV1 { Op = "SET_GOAL", Goal = "传入的工程目标" }));
        incoming = await owner.ReadAsync(incoming.Reference.Id, null);
        await owner.CommandAsync(PatchRevision(basis, new EngineeringDraftPatchV1 { Op = "SET_GOAL", Goal = "当前的工程目标" }));
        var current = await owner.ReadAsync(basis.Reference.Id, null);

        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        const string path = "api/engineering-revisions/owner-v1";
        var sources = new { @base = basis.CandidateRef, incoming = incoming.CandidateRef };
        async Task<HttpResponseMessage> Post(string endpoint, object value)
        {
            var request = OperationsRequest(cookies, HttpMethod.Post, endpoint);
            request.Content = new StringContent(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json");
            return await host.Send(request);
        }
        using var preview = await Post($"{path}/{current.Reference.Id}/rebase-preview", sources);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        using var body = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
        var change = Assert.Single(body.RootElement.GetProperty("data").GetProperty("changes").EnumerateArray());
        Assert.Equal("goal", change.GetProperty("path").GetString());
        Assert.Equal(basis.Draft.Goal, change.GetProperty("baseValue").GetString());
        Assert.Equal(current.Draft.Goal, change.GetProperty("currentValue").GetString());
        Assert.Equal(incoming.Draft.Goal, change.GetProperty("incomingValue").GetString());
        Assert.True(change.GetProperty("conflict").GetBoolean());

        var operationId = Guid.NewGuid();
        var command = new { operationId, revisionId = current.Reference.Id, action = "REBASE", expectedVersion = current.Reference.Version,
            expectedEtag = current.Etag, reason = "采用准确的传入候选", rebase = new { sources, resolutions = new[] {
                new { path = "goal", decision = "ACCEPT_SOURCE", reason = "沿用已核对的目标" } } } };
        using var saved = await Post($"{path}/commands", command);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var retry = await Post($"{path}/commands", command);
        Assert.Equal(await saved.Content.ReadAsStringAsync(), await retry.Content.ReadAsStringAsync());
        var next = await owner.ReadAsync(current.Reference.Id, null);
        Assert.Equal("3", next.Reference.Version);
        Assert.Equal(incoming.Draft.Goal, next.Draft.Goal);
        Assert.Equal(current.OriginalJson, (await owner.ReadAsync(current.Reference.Id, "2")).OriginalJson);
        var operation = await owner.OperationAsync(operationId, false);
        Assert.Equal("COMMITTED", operation.State);
        Assert.Equal(next.Reference.Digest, operation.Result!.Digest);
    }

    [CurrentPermissionPostgresFact]
    public async Task EngineeringRebase_stale_http_recovers_immutable_occurrence_conflicts_by_original_key()
    {
        await using var h = await Seed();
        await GrantEngineeringRevision(h);
        await GrantEngineering(h);
        await RevisionRule(h);
        var owner = Revisions(h);
        var product = await MakeEngineering(h);
        var source = new EngineeringSourceRefV1(product.Reference.Owner, product.Reference.Id, product.Reference.Version, product.Reference.Digest);
        var member = new EngineeringComponentDraftV1(Guid.NewGuid(), "PRODUCT", "机架/左", 20, "APPLIES", "", "原工作值", source);
        var a = await MakeRevision(owner, product.Reference);
        var b = await MakeRevision(owner, product.Reference);
        await owner.CommandAsync(PatchRevision(a, Upsert(member)));
        await owner.CommandAsync(PatchRevision(b, Upsert(member with { Note = "并行写入值" })));
        var expected = await owner.ReadAsync(a.Reference.Id, null);
        var incoming = await owner.ReadAsync(b.Reference.Id, null);
        var field = $"components/{member.OccurrenceId}/note";
        var sources = new EngineeringRebaseSourcesV1(a.CandidateRef, incoming.CandidateRef);
        var resolution = new EngineeringRebaseResolutionV1($"components/{member.OccurrenceId}", "ACCEPT_SOURCE", "按装配记录采用传入注释");
        var rejected = RebaseCommand(expected, sources, resolution);
        Assert.Equal(resolution.Path, Assert.Single((await owner.RebasePreviewAsync(a.Reference.Id, sources)).Changes).Path);

        var colleague = new Sys_User { UserName = "competing-engineer", RoleId = 72 };
        h.Db.Sys_Users.Add(colleague);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        await Revisions(DecisionActor(h, colleague.Id)).CommandAsync(PatchRevision(expected, Upsert(member with { Note = "先保存值" })));
        var winner = await owner.ReadAsync(a.Reference.Id, null);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        var operationPath = $"api/engineering-revisions/owner-v1/operations/{rejected.OperationId}";
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        async Task<HttpResponseMessage> Post(EngineeringRevisionCommandV1 command)
        {
            var request = OperationsRequest(cookies, HttpMethod.Post, "api/engineering-revisions/owner-v1/commands");
            request.Content = new StringContent(JsonSerializer.Serialize(command, json), Encoding.UTF8, "application/json");
            return await host.Send(request);
        }
        using var stale = await Post(rejected);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Contains("ENGINEERING_WORKING_VERSION_CHANGED", await stale.Content.ReadAsStringAsync());
        using var read = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"{operationPath}/conflict"));
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.True(read.Headers.CacheControl?.NoStore);
        var original = await read.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(original);
        var data = body.RootElement.GetProperty("data");
        Assert.Equal(rejected.OperationId, data.GetProperty("operationId").GetGuid());
        Assert.Equal(rejected.Reason, data.GetProperty("reason").GetString());
        Assert.Equal(expected.CandidateRef, data.GetProperty("expected").GetProperty("candidate").Deserialize<EngineeringRevisionRefV1>(json));
        Assert.Equal(winner.CandidateRef, data.GetProperty("current").GetProperty("candidate").Deserialize<EngineeringRevisionRefV1>(json));
        Assert.Equal(sources, data.GetProperty("sources").Deserialize<EngineeringRebaseSourcesV1>(json));
        var item = Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal(field, item.GetProperty("path").GetString());
        Assert.Equal(member.OccurrenceId, item.GetProperty("occurrenceId").GetGuid());
        Assert.Equal("原工作值", item.GetProperty("expectedValue").GetString());
        Assert.Equal("先保存值", item.GetProperty("currentValue").GetString());
        Assert.Equal("并行写入值", item.GetProperty("attemptedValue").GetString());
        Assert.Equal(resolution.Reason, item.GetProperty("reason").GetString());
        foreach (var property in new[] { "expectedSource", "currentSource", "attemptedSource" })
            Assert.Equal(source, item.GetProperty(property).Deserialize<EngineeringSourceRefV1>(json));
        Assert.True(item.GetProperty("conflict").GetBoolean());

        using var repeat = await Post(rejected);
        Assert.Equal(HttpStatusCode.PreconditionFailed, repeat.StatusCode);
        using var altered = await Post(rejected with { Reason = "改写原操作" });
        Assert.Equal(HttpStatusCode.Conflict, altered.StatusCode);
        foreach (var (method, endpoint) in new[] { (HttpMethod.Get, operationPath), (HttpMethod.Post, $"{operationPath}/close") })
        {
            using var status = await host.Send(OperationsRequest(cookies, method, endpoint));
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            using var statusBody = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
            var operation = statusBody.RootElement.GetProperty("data");
            Assert.Equal("CONFLICT", operation.GetProperty("state").GetString());
            Assert.True(!operation.TryGetProperty("result", out var result) || result.ValueKind == JsonValueKind.Null);
        }
        Assert.Equal(winner.Reference, (await owner.ReadAsync(a.Reference.Id, null)).Reference);
        Assert.Equal(3, await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_engineering_revision_v1.working_versions
            WHERE tenant_id={h.Tenant} AND revision_id={a.Reference.Id}
            """).SingleAsync());
        Assert.Equal(1, await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_engineering_revision_v1.revision_conflicts
            WHERE tenant_id={h.Tenant} AND operation_id={rejected.OperationId} AND actor_id={h.User}
              AND expected_version=2 AND current_version=3 AND body_digest=encode(sha256(body),'hex')
            """).SingleAsync());
        Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_main_owner_v1.operations
            WHERE tenant_id={h.Tenant} AND owner={EngineeringRevisionValuesV1.RevisionOwner} AND operation_id={rejected.OperationId}
            """).SingleAsync());

        await owner.CommandAsync(PatchRevision(winner, Upsert(member with { Note = "更晚的工作值" })));
        await owner.CommandAsync(PatchRevision(incoming, Upsert(member with { Note = "更晚的来源值" })));
        using var historical = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"{operationPath}/conflict"));
        Assert.Equal(HttpStatusCode.OK, historical.StatusCode);
        Assert.Equal(original, await historical.Content.ReadAsStringAsync());
        var colleagueCookies = await DocumentReviewerLogin(host, h, colleague.Id);
        using var anotherActor = await host.Send(OperationsRequest(colleagueCookies, HttpMethod.Get, $"{operationPath}/conflict"));
        Assert.Equal(HttpStatusCode.Forbidden, anotherActor.StatusCode);
        Assert.DoesNotContain("并行写入值", await anotherActor.Content.ReadAsStringAsync());

        await using var foreign = await Seed();
        await GrantEngineeringRevision(foreign);
        var foreignCookies = await Login(host, foreign);
        using var isolated = await host.Send(OperationsRequest(foreignCookies, HttpMethod.Get, $"{operationPath}/conflict"));
        Assert.Equal(HttpStatusCode.NotFound, isolated.StatusCode);
        Assert.DoesNotContain("先保存值", await isolated.Content.ReadAsStringAsync());
        var menu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "plm-engineering-product").Select(x => x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == menu && x.ActionCode == "query" && x.RoleId == 72).ExecuteDeleteAsync();
        using var revoked = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"{operationPath}/conflict"));
        Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        Assert.DoesNotContain("并行写入值", await revoked.Content.ReadAsStringAsync());
        using var revokedReplay = await Post(rejected);
        Assert.Equal(HttpStatusCode.Forbidden, revokedReplay.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task EngineeringRebase_occurrence_fields_keep_accept_manual_and_retain_original_refs()
    {
        await using var h = await Seed();
        await GrantEngineeringRevision(h);
        await GrantEngineering(h);
        await RevisionRule(h);
        var product = await MakeEngineering(h);
        var owner = Revisions(h);
        var a = await MakeRevision(owner, product.Reference);
        var source = new EngineeringSourceRefV1(product.Reference.Owner, product.Reference.Id, product.Reference.Version, product.Reference.Digest);
        var left = new EngineeringComponentDraftV1(Guid.NewGuid(), "PRODUCT", "机架/左", 20, "APPLIES", "", "基准左", source);
        var right = left with { OccurrenceId = Guid.NewGuid(), Path = "机架/右", SemanticOrder = 21, Note = "基准右" };
        await owner.CommandAsync(PatchRevision(a, Upsert(left), Upsert(right)));
        var basis = await owner.ReadAsync(a.Reference.Id, null);
        var editedLeft = left with { Path = "当前/左", SemanticOrder = 30 };
        await owner.CommandAsync(PatchRevision(basis, Upsert(editedLeft), Upsert(right with { Note = "当前右" })));
        var current = await owner.ReadAsync(a.Reference.Id, null);
        var b = await MakeRevision(owner, product.Reference);
        await owner.CommandAsync(PatchRevision(b, Upsert(left with { Path = "传入/左", SemanticOrder = 40 }), Upsert(right with { Note = "传入右" })));
        var incoming = await owner.ReadAsync(b.Reference.Id, null);
        var sources = new EngineeringRebaseSourcesV1(basis.CandidateRef, incoming.CandidateRef);
        var keep = new EngineeringRebaseResolutionV1($"components/{left.OccurrenceId}/path", "KEEP_BASE", "保留原安装位置");
        var accept = new EngineeringRebaseResolutionV1($"components/{right.OccurrenceId}/note", "ACCEPT_SOURCE", "采用传入注释");
        var manual = new EngineeringRebaseResolutionV1($"components/{left.OccurrenceId}/semanticOrder", "MANUAL", "人工定序", JsonSerializer.SerializeToElement(50));
        var preview = await owner.RebasePreviewAsync(a.Reference.Id, sources);
        Assert.Equal(3, preview.Changes.Length);
        Assert.All(preview.Changes, x => Assert.True(x.Conflict));
        var command = RebaseCommand(current, sources, keep, accept, manual);
        Assert.Equal("ENGINEERING_REBASE_RESOLUTION_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            owner.CommandAsync(command with { Rebase = new(sources, [keep, manual]) }))).Message);
        Assert.Equal("ENGINEERING_REBASE_MANUAL_INVALID", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            owner.CommandAsync(command with { Rebase = new(sources, [keep, accept, manual with { Manual = JsonSerializer.SerializeToElement("50") }]) }))).Message);
        Assert.Equal("UNKNOWN", (await owner.OperationAsync(command.OperationId, false)).State);

        var result = await owner.CommandAsync(command);
        var next = await owner.ReadAsync(a.Reference.Id, null);
        Assert.Equal(new[] { left with { SemanticOrder = 50 }, right with { Note = "传入右" } }, next.Draft.Components);
        Assert.All(next.Draft.Components, x => Assert.Equal(source, x.SourceRef));
        Assert.Equal(current.Reference.Version, next.ParentWorkingVersion);
        Assert.Equal(current.CandidateRef, next.Rebase!.PreviousCandidate);
        Assert.Equal(sources, next.Rebase.Input.Sources);
        Assert.Equal(incoming.CandidateRef, next.CompositionBase);
        Assert.Equal(command.OperationId, next.Rebase.OperationId);
        Assert.Equal(new[] { keep.Reason, accept.Reason, manual.Reason }, next.Rebase.Input.Resolutions.Select(x => x.Reason));
        Assert.Equal(result, await owner.CommandAsync(command));
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            owner.CommandAsync(command with { Reason = "更改原体" }))).Message);
        await owner.CommandAsync(PatchRevision(next, new EngineeringDraftPatchV1 { Op = "SET_GOAL", Goal = "独立后继草稿" }));
        Assert.Equal(result, (await owner.OperationAsync(command.OperationId, false)).Result);
        Assert.Equal(incoming.CandidateRef, (await owner.ReadAsync(a.Reference.Id, null)).CompositionBase);
        Assert.Equal(current.OriginalJson, (await owner.ReadAsync(a.Reference.Id, current.Reference.Version)).OriginalJson);
    }

    [CurrentPermissionPostgresFact]
    public async Task EngineeringRebase_rejects_unresolved_exact_refs_stale_frontiers_and_cross_tenant_disclosure()
    {
        await using var h = await Seed();
        await GrantEngineeringRevision(h);
        await GrantEngineering(h);
        await RevisionRule(h);
        var owner = Revisions(h);
        var product = await MakeEngineering(h);
        var basis = await MakeRevision(owner, product.Reference);
        await owner.CommandAsync(PatchRevision(basis, new EngineeringDraftPatchV1 { Op = "SET_GOAL", Goal = "当前稿" }));
        var current = await owner.ReadAsync(basis.Reference.Id, null);
        var incoming = await MakeRevision(owner, product.Reference);
        var sources = new EngineeringRebaseSourcesV1(basis.CandidateRef, incoming.CandidateRef);
        var choice = new EngineeringRebaseResolutionV1("goal", "ACCEPT_SOURCE", "采用传入稿");
        var valid = RebaseCommand(current, sources, choice);
        foreach (var invalid in new[] { incoming.CandidateRef with { Version = "latest" }, incoming.CandidateRef with { Owner = "OTHER" } })
            Assert.Equal(400, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.RebasePreviewAsync(current.Reference.Id, sources with { Incoming = invalid }))).Status);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.RebasePreviewAsync(current.Reference.Id,
            sources with { Incoming = incoming.CandidateRef with { Digest = new string('e', 64) } }))).Status);
        await using var foreign = await Seed();
        await GrantEngineeringRevision(foreign);
        await RevisionRule(foreign);
        var foreignRow = await MakeRevision(Revisions(foreign));
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.RebasePreviewAsync(current.Reference.Id,
            sources with { Incoming = foreignRow.CandidateRef }))).Status);
        await owner.CommandAsync(PatchRevision(incoming, new EngineeringDraftPatchV1 { Op = "SET_GOAL", Goal = "传入稿新头" }));
        Assert.Equal("ENGINEERING_REBASE_SOURCE_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(valid))).Message);
        incoming = await owner.ReadAsync(incoming.Reference.Id, null);
        valid = RebaseCommand(current, sources with { Incoming = incoming.CandidateRef }, choice);
        var committed = await owner.CommandAsync(valid);
        Assert.Equal("3", committed.Version);
        var menu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "plm-engineering-revision").Select(x => x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == menu && x.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.OperationAsync(valid.OperationId, false))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(valid))).Status);
        Assert.Equal(0, (await Revisions(foreign).ListAsync(null)).Count(x => x.Reference.Id == current.Reference.Id));
    }

    [CurrentPermissionPostgresFact]
    public async Task EngineeringRebase_concurrent_CAS_writes_create_one_snapshot_and_one_receipt()
    {
        await using var h = await Seed();
        await GrantEngineeringRevision(h);
        await GrantEngineering(h);
        await RevisionRule(h);
        var owner = Revisions(h);
        var product = await MakeEngineering(h);
        var basis = await MakeRevision(owner, product.Reference);
        await owner.CommandAsync(PatchRevision(basis, new EngineeringDraftPatchV1 { Op = "SET_GOAL", Goal = "当前目标" }));
        var current = await owner.ReadAsync(basis.Reference.Id, null);
        var incoming = await MakeRevision(owner, product.Reference);
        var sources = new EngineeringRebaseSourcesV1(basis.CandidateRef, incoming.CandidateRef);
        var first = RebaseCommand(current, sources, new EngineeringRebaseResolutionV1("goal", "KEEP_BASE", "保留基准"));
        var second = RebaseCommand(current, sources, new EngineeringRebaseResolutionV1("goal", "ACCEPT_SOURCE", "采用新来源"));
        await using var peer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        async Task<string> Save(EngineeringRevisionOwnerV1 writer, EngineeringRevisionCommandV1 command)
        {
            try { await writer.CommandAsync(command); return "COMMITTED"; }
            catch (OrderPriceCorrectionException ex) { Assert.Equal(412, ex.Status); return ex.Message; }
        }
        var states = await Task.WhenAll(Save(owner, first), Save(Revisions(h, peer), second));
        Assert.Single(states, x => x == "COMMITTED");
        Assert.Single(states, x => x == "ENGINEERING_WORKING_VERSION_CHANGED");
        Assert.Equal("3", (await owner.ReadAsync(current.Reference.Id, null)).Reference.Version);
        Assert.Equal(1, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_engineering_revision_v1.rebases WHERE tenant_id={h.Tenant}").SingleAsync());
        var rejected = states[0] == "COMMITTED" ? second.OperationId : first.OperationId;
        Assert.Equal("CONFLICT", (await owner.OperationAsync(rejected, false)).State);
        Assert.Equal(current.CandidateRef, (await owner.ConflictAsync(rejected)).Expected.Candidate);
        Assert.Equal(1, await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_main_owner_v1.operations
            WHERE tenant_id={h.Tenant} AND operation_id IN ({first.OperationId},{second.OperationId})
            """).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task EngineeringRebase_stale_patch_and_etag_only_preserve_typed_values_and_the_actual_head()
    {
        await using var h = await Seed();
        await GrantEngineeringRevision(h);
        await RevisionRule(h);
        var owner = Revisions(h);
        var basis = await MakeRevision(owner);
        var order = new EngineeringComponentDraftV1(Guid.NewGuid(), "PRODUCT", "工位/总成", 10, "UNKNOWN", "", "");
        await owner.CommandAsync(PatchRevision(basis, Upsert(order)));
        var expected = await owner.ReadAsync(basis.Reference.Id, null);
        await owner.CommandAsync(PatchRevision(expected, Upsert(order with { SemanticOrder = 20 })));
        var actual = await owner.ReadAsync(basis.Reference.Id, null);
        var late = PatchRevision(expected, Upsert(order with { SemanticOrder = 30 }));
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(late))).Status);
        var original = await owner.ConflictAsync(late.OperationId);
        var item = Assert.Single(original.Items);
        Assert.Equal($"components/{order.OccurrenceId}/semanticOrder", item.Path);
        Assert.Equal(10, item.ExpectedValue.GetInt32());
        Assert.Equal(20, item.CurrentValue.GetInt32());
        Assert.Equal(30, item.AttemptedValue.GetInt32());
        Assert.True(item.Conflict);
        Assert.Equal(late.Reason, original.Reason);

        var badTag = PatchRevision(actual, Upsert(order with { SemanticOrder = 20 })) with { ExpectedEtag = expected.Etag };
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(badTag))).Status);
        var etagOnly = await owner.ConflictAsync(badTag.OperationId);
        Assert.Equal(expected.Etag, etagOnly.RequestedEtag);
        Assert.Equal(actual.Etag, etagOnly.Current.Etag);
        Assert.Equal(etagOnly.Current, etagOnly.Expected);
        Assert.Empty(etagOnly.Items);
        Assert.Equal(actual.Reference, (await owner.ReadAsync(basis.Reference.Id, null)).Reference);
    }

    [CurrentPermissionPostgresFact]
    public async Task EngineeringRebase_manual_member_requires_an_exact_known_source_or_reports_a_draft_gap()
    {
        await using var h = await Seed();
        await GrantEngineeringRevision(h);
        await GrantEngineering(h);
        await RevisionRule(h);
        var owner = Revisions(h);
        var product = await MakeEngineering(h);
        var source = new EngineeringSourceRefV1(product.Reference.Owner, product.Reference.Id, product.Reference.Version, product.Reference.Digest);
        var basis = await MakeRevision(owner, product.Reference);
        var member = new EngineeringComponentDraftV1(Guid.NewGuid(), "PRODUCT", "装配/左", 1, "APPLIES", "", "", source);
        await owner.CommandAsync(PatchRevision(basis, Upsert(member)));
        var current = await owner.ReadAsync(basis.Reference.Id, null);
        var incoming = await MakeRevision(owner, product.Reference);
        var sources = new EngineeringRebaseSourcesV1(basis.CandidateRef, incoming.CandidateRef);
        var path = "components/" + member.OccurrenceId;
        EngineeringRevisionCommandV1 Manual(EngineeringSourceRefV1? value) => RebaseCommand(current, sources,
            new EngineeringRebaseResolutionV1(path, "MANUAL", "保留位置并核对来源",
                JsonSerializer.SerializeToElement(member with { SourceRef = value }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        Assert.Equal("ENGINEERING_COMPONENT_SOURCE_OWNER_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            owner.CommandAsync(Manual(source with { Owner = "OTHER" })))).Message);
        Assert.Equal(400, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(Manual(source with { Version = "latest" })))).Status);
        Assert.Equal(current.CandidateRef, (await owner.ReadAsync(basis.Reference.Id, null)).CandidateRef);
        await owner.CommandAsync(Manual(null));
        var revised = await owner.ReadAsync(basis.Reference.Id, null);
        Assert.Null(Assert.Single(revised.Draft.Components).SourceRef);
        Assert.Contains("SOURCE_REQUIRED:" + member.OccurrenceId, revised.Missing!);
        Assert.Equal("DRAFT_ONLY", revised.Readiness);
        Assert.Equal("NOT_REQUESTED", revised.Freeze);
    }

    [CurrentPermissionPostgresFact]
    public async Task EngineeringRebase_frozen_parent_and_review_are_exact_and_old_check_loses_current_use()
    {
        await using var h = await Seed();
        var f = await BaselineSetup(h, apply: false);
        var files = f.Review.Source.Files;
        var baselines = Baselines(h, files);
        var owner = new EngineeringRevisionOwnerV1(h.Db, CandidateAuthority(h), Documents(h, files), Specifications(h, files), baselines);
        var awaiting = await owner.ReadAsync(f.Command.Revision.Id, null);
        Assert.Equal("IN_REVIEW", awaiting.State);
        Assert.Equal("ENGINEERING_WORKING_NOT_DRAFT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(
            PatchRevision(awaiting, new EngineeringDraftPatchV1 { Op = "SET_GOAL", Goal = "在审改写" })))).Message);
        var incoming = await MakeRevision(owner, awaiting.Draft.ProductRef);

        var review = IterationReviews(h, files);
        var context = await review.ApplicationAsync(f.Command.IterationId, f.Command.ReviewSubmissionId);
        var applied = await review.ApplyAsync(f.Command.IterationId, context.Etag,
            new(Guid.NewGuid(), f.Command.ReviewSubmissionId, null, "应用准确原决定"));
        var frozen = await baselines.FreezeAsync(f.Command with { ApplicationReceiptId = applied.Receipt.ReceiptId });
        var original = await baselines.ReadAsync(frozen.Baseline.Id);
        var locked = await owner.ReadAsync(f.Command.Revision.Id, null);
        await owner.CommandAsync(PatchRevision(incoming, new EngineeringDraftPatchV1 { Op = "SET_GOAL", Goal = "冻结后另存的并行候选目标" }));
        incoming = await owner.ReadAsync(incoming.Reference.Id, null);
        var sources = new EngineeringRebaseSourcesV1(BaselineRef(frozen.Baseline), incoming.CandidateRef);
        Assert.Equal("ENGINEERING_WORKING_NOT_DRAFT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            owner.CommandAsync(RebaseCommand(locked, sources)))).Message);

        var successor = await baselines.SuccessorAsync(new(Guid.NewGuid(), frozen.Baseline, locked.Reference, locked.Etag, "批准从冻结版另起工作版"));
        var working = await owner.ReadAsync(locked.Reference.Id, null);
        Assert.Equal(successor.Revision, working.Reference);
        Assert.Equal(sources.Base, working.CompositionBase);
        Assert.Equal("ENGINEERING_REBASE_BASE_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            owner.RebasePreviewAsync(working.Reference.Id, sources with { Base = locked.CandidateRef }))).Message);

        var checks = Checks(h, files);
        var check = await checks.CheckAsync(original.Original.Check.Command with { OperationId = Guid.NewGuid(),
            Revision = working.Reference, Candidate = working.CandidateRef, ExpectedEtag = working.Etag, Reason = "后继工作稿的完整检查" });
        var before = await checks.ReadAsync(check.CheckRunId);
        Assert.Equal("CURRENT", before.Freshness);
        Assert.Equal("PASS", before.Original.State);
        var preview = await owner.RebasePreviewAsync(working.Reference.Id, sources);
        var choices = preview.Changes.Select(x => new EngineeringRebaseResolutionV1(x.Path, "KEEP_BASE", "保留已核验的父基线内容")).ToArray();
        var document = working.Draft.Components.Single(x => x.Kind == "DOCUMENT");
        var occurrence = $"components/{document.OccurrenceId}";
        var lateChoices = choices.Select(choice => choice.Path == occurrence
            ? new EngineeringRebaseResolutionV1(occurrence, "MANUAL", "补入后到的文档备注",
                JsonSerializer.SerializeToElement(document with { Note = "后到的文档备注" }, EngineeringProductStoreV1.Json))
            : choice).ToArray();
        Assert.Contains(lateChoices, choice => choice.Decision == "MANUAL");
        var late = RebaseCommand(working, sources, lateChoices);
        await owner.CommandAsync(RebaseCommand(working, sources, choices));
        var next = await owner.ReadAsync(working.Reference.Id, null);
        Assert.Equal(working.CandidateRef.Digest, next.CandidateRef.Digest);
        Assert.NotEqual(working.CandidateRef.Version, next.CandidateRef.Version);
        var after = await checks.ReadAsync(check.CheckRunId);
        Assert.Equal("STALE", after.Freshness);
        Assert.Contains("CANDIDATE_CHANGED", after.Changes);
        Assert.Equal(before.OriginalJson, after.OriginalJson);
        Assert.Equal("ENGINEERING_CHECK_NOT_CURRENT_PASS", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            CandidateAuthority(h).ExecuteAsync(OperationsOwnerV1.EngineeringRevision, "query", false,
                (a, t) => checks.HoldExact(a, before.Reference, t)))).Message);
        await owner.CommandAsync(PatchRevision(next, Upsert(document with { Note = "先保存的文档备注" })));
        var winner = await owner.ReadAsync(working.Reference.Id, null);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(late))).Status);
        var conflict = await owner.ConflictAsync(late.OperationId);
        Assert.Equal(BaselineRef(frozen.Baseline), conflict.Sources!.Base);
        Assert.Equal(incoming.CandidateRef, conflict.Sources.Incoming);
        Assert.Equal(working.Reference, conflict.Expected.Revision);
        Assert.Equal(winner.Reference, conflict.Current.Revision);
        var note = Assert.Single(conflict.Items);
        Assert.True(note.Conflict);
        Assert.Equal($"{occurrence}/note", note.Path);
        Assert.Equal(document.SourceRef, note.AttemptedSource);
        Assert.Equal("先保存的文档备注", note.CurrentValue.GetString());
        Assert.Equal("后到的文档备注", note.AttemptedValue.GetString());
        Assert.Equal("补入后到的文档备注", note.Reason);
        await owner.CommandAsync(PatchRevision(incoming, new EngineeringDraftPatchV1 { Op = "SET_GOAL", Goal = "并行修订工作目标" }));
        var variant = await owner.ReadAsync(incoming.Reference.Id, null);
        var frozenSource = new EngineeringRebaseSourcesV1(incoming.CandidateRef, BaselineRef(frozen.Baseline));
        var frozenPreview = await owner.RebasePreviewAsync(variant.Reference.Id, frozenSource);
        await owner.CommandAsync(RebaseCommand(variant, frozenSource, frozenPreview.Changes.Select(x =>
            new EngineeringRebaseResolutionV1(x.Path, "KEEP_BASE", "先保留并行版本原值")).ToArray()));
        Assert.Equal(frozenSource.Incoming, (await owner.ReadAsync(variant.Reference.Id, null)).CompositionBase);
        Assert.Equal(incoming.CandidateRef, (await owner.ReadAsync(next.Reference.Id, null)).CompositionBase);
        Assert.Equal(original.OriginalJson, (await baselines.ReadAsync(frozen.Baseline.Id)).OriginalJson);
        await VerifyApprovedChangeConsumption(h, files, original, owner, baselines);
    }

    private static async Task VerifyApprovedChangeConsumption(Harness h, ContentStore files, EngineeringBaselineViewV1 parent,
        EngineeringRevisionOwnerV1 ownerWithoutChange, EngineeringBaselineOwnerV1 baselines)
    {
        var product = parent.Original.Revision.Draft.ProductRef!;
        var change = new EngineeringSourceRefV1("Demo.Plm.ApprovedChange", Guid.NewGuid(), "r-07", new string('c', 64));
        var create = await NewRevision(ownerWithoutChange, product: product);
        Assert.Equal("ENGINEERING_PARENT_CHANGE_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            ownerWithoutChange.CommandAsync(create))).Message);
        create = create with { Origin = new("CHANGE", ParentBaselineRef: parent.Reference, ChangeRef: change) };
        Assert.Equal("ENGINEERING_PARENT_CHANGE_OWNER_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            ownerWithoutChange.CommandAsync(create))).Message);
        Assert.Equal("UNKNOWN", (await ownerWithoutChange.OperationAsync(create.OperationId, false)).State);

        // 明确标记的合成变更提供方，仅在隔离 PostgreSQL 夹具中存在。
        await h.Db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS wp0503_demo_change (tenant_id uuid,id uuid,version text,digest text,
              parent_digest text,product_id uuid,approved bool NOT NULL,PRIMARY KEY(tenant_id,id));
            """);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO wp0503_demo_change VALUES ({h.Tenant},{change.Id},{change.Version},{change.Digest},{parent.Reference.Digest},{product.Id},true)
            """);
        var owner = new EngineeringRevisionOwnerV1(h.Db, CandidateAuthority(h), Documents(h, files), Specifications(h, files), baselines,
            new DemoEngineeringRevisionChange(h.Db));
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(create with
        {
            Origin = create.Origin! with { ParentBaselineRef = parent.Reference with { Digest = new string('a', 64) } }
        }))).Status);
        var first = await owner.CommandAsync(create);
        Assert.Equal(first, await owner.CommandAsync(create));
        var second = await NewRevision(owner, product: product);
        await owner.CommandAsync(second with { Origin = create.Origin });
        var a = await owner.ReadAsync(create.RevisionId, null);
        var b = await owner.ReadAsync(second.RevisionId, null);
        Assert.NotEqual(a.Reference.Id, b.Reference.Id);
        Assert.NotEqual(a.RevisionCode, b.RevisionCode);
        Assert.Equal(product, a.Draft.ProductRef);
        Assert.Equal(product, b.Draft.ProductRef);
        Assert.Equal(create.Origin, a.Origin);
        Assert.Equal(create.Origin, b.Origin);
        Assert.Equal(BaselineRef(parent.Reference), b.CompositionBase);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE wp0503_demo_change SET approved=false WHERE tenant_id={h.Tenant} AND id={change.Id}");
        Assert.Equal("DEMO_CHANGE_NOT_APPROVED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            owner.CommandAsync(PatchRevision(a, new EngineeringDraftPatchV1 { Op = "SET_GOAL", Goal = "采用已撤销变更" })))).Message);
        Assert.Equal(first, (await owner.OperationAsync(create.OperationId, false)).Result);
        Assert.Equal(a.OriginalJson, (await owner.ReadAsync(a.Reference.Id, null)).OriginalJson);
    }

    private sealed class DemoEngineeringRevisionChange(CP6Context db) : EngineeringRevisionChangeAuthorityV1
    {
        internal override bool Owns(CP6Context context) => ReferenceEquals(db, context);

        internal override async Task RequireAsync(OperationsAccessV1 access, EngineeringSourceRefV1 change,
            EngineeringSourceRefV1 parent, EngineeringProductRefV1 product, bool requireApproval, CancellationToken ct)
        {
            Assert.NotNull(db.Database.CurrentTransaction);
            var approved = await db.Database.SqlQuery<bool?>($"""
                SELECT approved AS "Value" FROM wp0503_demo_change WHERE tenant_id={db.CurrentTenantId} AND id={change.Id}
                AND version={change.Version} AND digest={change.Digest} AND parent_digest={parent.Digest} AND product_id={product.Id} FOR SHARE
                """).SingleOrDefaultAsync(ct);
            if (change.Owner != "Demo.Plm.ApprovedChange" || approved == null || requireApproval && approved != true)
                throw new OrderPriceCorrectionException("DEMO_CHANGE_NOT_APPROVED", 412);
        }
    }
}
