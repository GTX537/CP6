using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static Harness DecisionActor(Harness h, Guid actor)
    {
        return h with { User = actor, Http = new Microsoft.AspNetCore.Http.DefaultHttpContext {
            User = CurrentPermissionContextTests.Principal(actor, h.Tenant) } };
    }
    private static ApprovalTaskDecisionOwnerV2 Decisions(Harness h)
        => new(h.Db, ScopeFence(h), TaskAuthority(h), ToolApproval(h));
    private async Task<(IntakeFixture Source, Guid First, Guid Second)> DecisionSetup(Harness h, string schema = StartSchema, bool two = true)
    {
        var (source, first) = await StartSetup(h, schema);
        await TaskPermission(h, first); await EngineeringAction(h, "oa-inbox", "query");
        var second = Guid.NewGuid();
        if (two)
        {
            h.Db.Sys_Users.Add(new() { Id = second, UserName = "second-duty", Password = "fixture", RoleId = 73 });
            await h.Db.SaveChangesAsync(); await TaskPermission(h, second);
        }
        h.Db.ChangeTracker.Clear(); await Starter(h).RunAsync(h.Tenant); h.Db.ChangeTracker.Clear();
        return (source, first, second);
    }
    private async Task<(ApprovalTaskDecisionInputV2 Input, Guid Task)> DecisionInput(Harness actor, string decision = "approve", Guid? behalf = null)
    {
        var task = await actor.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(x => x.AssigneeId == (behalf ?? actor.User) && x.Status == 0);
        var view = await Decisions(actor).ContextAsync(task.Id, behalf);
        return (new(Guid.NewGuid(), decision, decision == "approve" ? "Reviewed original frozen request" : "Original request rejected",
            view.TaskRowVersion, view.InstanceRowVersion), task.Id);
    }
    private static Task<ApprovalTaskDecisionResultV2> Vote(Harness actor, (ApprovalTaskDecisionInputV2 Input, Guid Task) command, Guid? behalf = null)
        => Decisions(actor).DecideAsync(command.Task, command.Input, behalf, "\"" + command.Input.TaskRowVersion + "\"");

    [CurrentPermissionPostgresFact]
    public async Task App_decision_actual_all_votes_terminal_evidence_outbox_and_original_command_commit_together()
    {
        await using var h = await Seed(); var (source, first, second) = await DecisionSetup(h);
        var actor1 = DecisionActor(h, first); var c1 = await DecisionInput(actor1); var r1 = await Vote(actor1, c1);
        Assert.Null(r1.Result.DecisionId); Assert.Equal(0, r1.Result.Task.InstanceStatus); Assert.Equal(1, r1.Result.Task.TaskStatus);
        Assert.Empty(await h.Db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM cp6_approval_decision_v2.outbox WHERE tenant_id={h.Tenant}").ToArrayAsync());
        var actor2 = DecisionActor(h, second); var c2 = await DecisionInput(actor2); var r2 = await Vote(actor2, c2);
        Assert.NotNull(r2.Result.DecisionId); Assert.Equal(1, r2.Result.Task.InstanceStatus);
        Assert.NotEqual(c2.Input.InstanceRowVersion, r2.Result.Task.InstanceRowVersion); Assert.NotEqual(c2.Input.TaskRowVersion, r2.Result.Task.TaskRowVersion);
        var bytes = await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_approval_decision_v2.outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        using var doc = JsonDocument.Parse(bytes); var e = doc.RootElement;
        Assert.Equal("APPROVED", e.GetProperty("decision").GetString()); Assert.Equal("HUMAN", e.GetProperty("decisionMode").GetString());
        Assert.Equal(second, e.GetProperty("terminalActor").GetProperty("decisionPrincipalId").GetGuid());
        Assert.Equal(source.Version, e.GetProperty("flowDefVersionId").GetGuid());
        var proof = await h.Db.Database.SqlQuery<string>($"SELECT evidence_json AS \"Value\" FROM cp6_approval_decision_v2.decisions WHERE tenant_id={h.Tenant}").SingleAsync();
        using var facts = JsonDocument.Parse(proof);
        Assert.Equal(2, facts.RootElement.GetProperty("votes").GetArrayLength());
        Assert.Equal(2, facts.RootElement.GetProperty("votes").EnumerateArray().Select(x => x.GetProperty("authority").GetProperty("principalId").GetGuid()).Distinct().Count());
        Assert.Equal(ApprovalCanonicalV2.Hash(System.Text.Encoding.UTF8.GetBytes(proof)), e.GetProperty("decisionEvidenceDigest").GetProperty("value").GetString());
        var node = System.Text.Json.Nodes.JsonNode.Parse(bytes)!.AsObject(); var digest = node["payloadDigest"]!["value"]!.GetValue<string>(); node.Remove("payloadDigest");
        Assert.Equal(digest, ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(node)));
        Assert.Equal(r2, await Decisions(actor2).OriginalAsync(c2.Input.CommandId)); Assert.Equal(r2, await Vote(actor2, c2));
        Assert.Equal("DISPATCH_PENDING", (await source.Owner.OriginalAsync(IntakeBody(source).CommandId)).State); // Immutable intake ACK.
        Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Wf_FlowTask\" SET \"Status\"=0 WHERE \"Id\"={c2.Task}"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_approval_decision_v2.votes WHERE tenant_id={h.Tenant}"));
    }

    [CurrentPermissionPostgresFact]
    public async Task App_decision_policy_missing_human_vote_and_mid_transaction_fault_leave_original_tasks_unchanged()
    {
        await using (var h = await Seed())
        {
            var (_, first, _) = await DecisionSetup(h, two: false); var actor = DecisionActor(h, first); var input = await DecisionInput(actor);
            Assert.Equal("DECISION_EVIDENCE_INCOMPLETE", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => Vote(actor, input))).Message);
            Assert.Equal(0, (await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync()).Status);
            Assert.Empty(await h.Db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM cp6_approval_decision_v2.commands WHERE tenant_id={h.Tenant}").ToArrayAsync());
        }
        var hook = new StartWriteHook(); await using var failed = await Seed(hook); var (_, a, b) = await DecisionSetup(failed);
        var firstActor = DecisionActor(failed, a); await Vote(firstActor, await DecisionInput(firstActor));
        var secondActor = DecisionActor(failed, b); var last = await DecisionInput(secondActor);
        hook.Before = c => { if (c.CommandText.Contains("INSERT INTO cp6_approval_decision_v2.decisions")) throw new InvalidOperationException("decision outbox fault"); return Task.CompletedTask; };
        Assert.Equal("decision outbox fault", (await Assert.ThrowsAsync<InvalidOperationException>(() => Vote(secondActor, last))).Message);
        Assert.Equal(0, (await failed.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(x => x.Id == last.Task)).Status);
        Assert.Single(await failed.Db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM cp6_approval_decision_v2.votes WHERE tenant_id={failed.Tenant}").ToArrayAsync());
        // A missing message must be rejected by the database's deferred completeness
        // check, even when the application reached its commit call.
        hook.Before = c => {
            var outbox = c.CommandText.IndexOf("INSERT INTO cp6_approval_decision_v2.outbox", StringComparison.Ordinal);
            if (outbox >= 0) c.CommandText = c.CommandText[..outbox];
            return Task.CompletedTask;
        };
        var omitted = await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => Vote(secondActor, last));
        Assert.Equal("OUTCOME_UNKNOWN", omitted.Message);
        Assert.Contains("APP_DECISION_OUTBOX_REQUIRED", omitted.ToString());
        Assert.Null(await Decisions(secondActor).OriginalAsync(last.Input.CommandId));
        Assert.Equal(0, (await failed.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(x => x.Id == last.Task)).Status);
        Assert.Single(await failed.Db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM cp6_approval_decision_v2.votes WHERE tenant_id={failed.Tenant}").ToArrayAsync());
        // A real permission mutation late in this same transaction invalidates the
        // authority lease and rolls back both the mutation and the current vote.
        hook.Before = async c => {
            if (!c.CommandText.Contains("INSERT INTO cp6_approval_decision_v2.votes")) return;
            hook.Before = null;
            await failed.Db.Sys_RoleActions.Where(x => x.ActionCode == "approve").ExecuteDeleteAsync();
        };
        Assert.Equal("SCOPE_AUTHORITY_CHANGED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => Vote(secondActor, last))).Message);
        Assert.True(await failed.Db.Sys_RoleActions.AnyAsync(x => x.ActionCode == "approve"));
        Assert.Null(await Decisions(secondActor).OriginalAsync(last.Input.CommandId));
        Assert.Equal(0, (await failed.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(x => x.Id == last.Task)).Status);
        hook.Before = null; Assert.NotNull((await Vote(secondActor, last)).Result.DecisionId);
    }

    [CurrentPermissionPostgresFact]
    public async Task App_decision_rejection_and_lost_commit_ack_recover_exact_result_after_write_revocation()
    {
        var fault = new CommitFault(); await using var h = await Seed(fault); var (_, first, _) = await DecisionSetup(h);
        var actor = DecisionActor(h, first); var c = await DecisionInput(actor, "reject"); fault.Armed = true;
        Assert.Equal("OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => Vote(actor, c))).Message);
        await h.Db.Sys_RoleActions.Where(x => x.ActionCode == "approve").ExecuteDeleteAsync();
        var original = await Decisions(actor).OriginalAsync(c.Input.CommandId); Assert.NotNull(original!.Result.DecisionId); Assert.Equal(2, original.Result.Task.InstanceStatus);
        Assert.Equal(original, await Vote(actor, c));
        Assert.Equal("COMMAND_PAYLOAD_CONFLICT", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => Vote(actor, (c.Input with { Comment = "changed" }, c.Task)))).Message);
        Assert.Equal(1, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_decision_v2.outbox WHERE tenant_id={h.Tenant}").SingleAsync());
        await h.Db.Sys_RoleActions.Where(x => x.ActionCode == "query" && x.MenuId == 443).ExecuteDeleteAsync();
        await Assert.ThrowsAsync<CP6.Core.Services.Erp.OrderPriceCorrectionException>(() => Decisions(actor).OriginalAsync(c.Input.CommandId));
    }

    [CurrentPermissionPostgresFact]
    public async Task App_decision_task_and_instance_versions_prevent_stale_vote_and_two_connections_resume_safely()
    {
        await using var h = await Seed(); var (_, first, second) = await DecisionSetup(h);
        var a = DecisionActor(h, first); var c1 = await DecisionInput(a);
        var b = DecisionActor(h, second); var c2 = await DecisionInput(b);
        var firstContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = CurrentPermissionContextTests.Principal(first, h.Tenant) };
        await using var peerDb = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var peer = h with { Db = peerDb, User = first, Http = firstContext };
        var firstVote = Vote(peer, c1); var secondVote = Vote(b, c2);
        var outcomes = await Task.WhenAll(new[] { firstVote, secondVote }.Select(async task => {
            try { return (Result: await task, Error: (Exception?)null); } catch (Exception ex) { return (Result: (ApprovalTaskDecisionResultV2?)null, Error: ex); }
        }));
        Assert.Single(outcomes.Where(x => x.Result != null)); var loser = Assert.Single(outcomes.Where(x => x.Error != null));
        Assert.True(loser.Error is ScopeAuthorizationExceptionV1 { Message: "PRECONDITION_FAILED", Status: 412 }, loser.Error!.ToString());
        var remaining = await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(x => x.Status == 0);
        var current = DecisionActor(h, remaining.AssigneeId); Assert.NotNull((await Vote(current, await DecisionInput(current))).Result.DecisionId);
        Assert.Equal(2, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_decision_v2.votes WHERE tenant_id={h.Tenant}").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task App_decision_delegated_vote_retains_original_principal_and_rejects_expired_grant()
    {
        await using var h = await Seed(); var (_, first, second) = await DecisionSetup(h);
        var delegateUser = await ActualReviewer(h); await EngineeringAction(h, "oa-inbox", "query");
        var grant = new CP6.Entity.DomainModels.Wf.Wf_FlowDelegate { GrantorId = first, DelegateId = delegateUser.User,
            Scope = "binding-flow", ValidFrom = DateTime.Now.AddMinutes(-1), ValidTo = DateTime.Now.AddHours(1) };
        h.Db.Wf_FlowDelegates.Add(grant); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var cmd = await DecisionInput(delegateUser, behalf: first); var result = await Vote(delegateUser, cmd, first);
        var body = await h.Db.Database.SqlQuery<string>($"SELECT body AS \"Value\" FROM cp6_approval_decision_v2.votes WHERE tenant_id={h.Tenant}").SingleAsync();
        var proof = JsonSerializer.Deserialize<ApprovalVoteV2>(body, ApprovalBindingOwnerV2.Json)!;
        Assert.Equal(first, proof.Authority.PrincipalId); Assert.Equal(delegateUser.User, proof.Authority.ActualHandlerId);
        Assert.Equal(grant.Id, proof.Authority.GrantId); Assert.NotNull(proof.Authority.GrantRevision);
        Assert.Equal(first, (await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(x => x.Id == cmd.Task)).AssigneeId);
        await h.Db.Wf_FlowDelegates.Where(x => x.Id == grant.Id).ExecuteUpdateAsync(x => x.SetProperty(g => g.Enable, false));
        Assert.Equal(result, await Vote(delegateUser, cmd, first)); // Historical read needs no live grant.
        var secondActor = DecisionActor(h, second); Assert.NotNull((await Vote(secondActor, await DecisionInput(secondActor))).Result.DecisionId);
    }

    [CurrentPermissionPostgresFact]
    public async Task App_decision_serial_and_parallel_use_real_kernel_and_preserve_every_original_duty()
    {
        var schemas = new[] {
            """{"start":"s","nodes":[{"id":"s","type":"start"},{"id":"a","type":"approval","stages":[{"kind":"fixed","approverStrategy":"Role","approverRoleId":73,"countersign":"all"},{"kind":"fixed","approverStrategy":"Role","approverRoleId":73,"countersign":"veto"}]},{"id":"e","type":"end"}],"edges":[{"from":"s","to":"a"},{"from":"a","to":"e"}]}""",
            """{"start":"s","nodes":[{"id":"s","type":"start"},{"id":"p","type":"parallelSplit"},{"id":"a","type":"approval","approverStrategy":"Role","approverRoleId":73},{"id":"b","type":"approval","approverStrategy":"Role","approverRoleId":73},{"id":"j","type":"parallelJoin"},{"id":"e","type":"end"}],"edges":[{"from":"s","to":"p"},{"from":"p","to":"a"},{"from":"p","to":"b"},{"from":"a","to":"j"},{"from":"b","to":"j"},{"from":"j","to":"e"}]}"""
        };
        foreach (var schema in schemas)
        {
            await using var h = await Seed(); await DecisionSetup(h, schema);
            var votes = 0;
            while (await h.Db.Wf_FlowTasks.AnyAsync(x => x.Status == 0))
            {
                Assert.True(votes < 5);
                var task = await h.Db.Wf_FlowTasks.AsNoTracking().Where(x => x.Status == 0).OrderBy(x => x.Id).FirstAsync();
                var actor = DecisionActor(h, task.AssigneeId); var context = await Decisions(actor).ContextAsync(task.Id);
                var input = new ApprovalTaskDecisionInputV2(Guid.NewGuid(), "approve", "Actual stage vote", context.TaskRowVersion, context.InstanceRowVersion);
                var result = await Vote(actor, (input, task.Id)); votes++;
                Assert.Equal(votes == 4, result.Result.DecisionId != null);
            }
            Assert.Equal(4, votes); Assert.Equal(1, (await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync()).Status);
            Assert.Equal(4, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_tasks_v2.assignments WHERE tenant_id={h.Tenant}").SingleAsync());
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task App_decision_actual_http_original_login_csrf_dual_versions_strict_fields_and_recovery()
    {
        await using var h = await Seed(); var (_, first, _) = await DecisionSetup(h); var actor = DecisionActor(h, first);
        var hash = new BCryptPasswordHasher(4).Hash("Price1!Password");
        await h.Db.Sys_Users.Where(x => x.Id == first).ExecuteUpdateAsync(x => x.SetProperty(u => u.Password, hash));
        var name = await h.Db.Sys_Users.Where(x => x.Id == first).Select(x => x.UserName).SingleAsync();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,
            new Dictionary<string, string> { ["ScopeAuthorization__EnvironmentId"] = ScopeEnvironment.ToString() });
        using var login = await host.Send(new HttpRequestMessage(HttpMethod.Post, "api/auth/login") {
            Content = JsonContent.Create(new { UserName = name, Password = "Price1!Password", TenantCode = h.Tenant.ToString("N") }) });
        Assert.True(login.StatusCode == HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
        var cookies = login.Headers.GetValues("Set-Cookie").Select(x => x.Split(';')[0].Split('=', 2)).Where(x => x[1].Length > 0).ToDictionary(x => x[0], x => x[1]);
        var task = await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(x => x.AssigneeId == first);
        using var get = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"/api/oa/tasks/{task.Id}/decision-context"));
        Assert.True(get.StatusCode == HttpStatusCode.OK, await get.Content.ReadAsStringAsync()); Assert.True(get.Headers.CacheControl!.NoStore);
        var view = JsonSerializer.Deserialize<ApprovalTaskDecisionViewV2>(await get.Content.ReadAsStringAsync(), ApprovalBindingOwnerV2.Json)!;
        var input = new ApprovalTaskDecisionInputV2(Guid.NewGuid(), "reject", "Business request rejected", view.TaskRowVersion, view.InstanceRowVersion);
        async Task<HttpResponseMessage> Post(object body, bool etag = true, bool csrf = true)
        {
            using var request = OperationsRequest(cookies, HttpMethod.Post, $"/api/oa/tasks/{task.Id}/decision");
            if (!csrf) request.Headers.Remove("X-CSRF-Token");
            if (etag) request.Headers.TryAddWithoutValidation("If-Match", get.Headers.ETag!.ToString());
            request.Content = JsonContent.Create(body); return await host.Send(request);
        }
        using (var noCsrf = await Post(input, csrf: false)) Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        using (var noVersion = await Post(input, etag: false)) {
            Assert.Equal((HttpStatusCode)428, noVersion.StatusCode);
            using var error = JsonDocument.Parse(await noVersion.Content.ReadAsStringAsync());
            Assert.Equal("/api/oa/tasks/decision-commands/" + input.CommandId, error.RootElement.GetProperty("resultQueryRef").GetString());
            Assert.Equal("PRECONDITION_REQUIRED", error.RootElement.GetProperty("messageKey").GetString());
        }
        using (var spoof = await Post(new { input.CommandId, input.Decision, input.Comment, input.TaskRowVersion, input.InstanceRowVersion, decidedBy = h.User }))
            Assert.Equal(HttpStatusCode.BadRequest, spoof.StatusCode);
        using var accepted = await Post(input); Assert.True(accepted.StatusCode == HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        var originalBody = await accepted.Content.ReadAsStringAsync();
        using var acceptedJson = JsonDocument.Parse(originalBody);
        Assert.Equal(new[] { "commandId", "queryRef", "result", "state" }, acceptedJson.RootElement.EnumerateObject().Select(x => x.Name).Order().ToArray());
        var result = JsonSerializer.Deserialize<ApprovalTaskDecisionResultV2>(originalBody, ApprovalBindingOwnerV2.Json)!; Assert.NotNull(result.Result.DecisionId);
        await h.Db.Sys_RoleActions.Where(x => x.ActionCode == "approve").ExecuteDeleteAsync();
        using var read = await host.Send(OperationsRequest(cookies, HttpMethod.Get, result.QueryRef));
        Assert.True(read.StatusCode == HttpStatusCode.OK, await read.Content.ReadAsStringAsync());
        Assert.Equal(result, JsonSerializer.Deserialize<ApprovalTaskDecisionResultV2>(await read.Content.ReadAsStringAsync(), ApprovalBindingOwnerV2.Json));
        using var replay = await Post(input); Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
    }
}
