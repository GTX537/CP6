using System.Net;
using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static FlowSchema ParallelRecoveryGraph(FlowNode left, FlowNode right) => new()
    {
        Start = "s",
        Nodes = [new() { Id = "s", Type = "start" }, new() { Id = "split", Type = "parallelSplit" },
            left, right, new() { Id = "join", Type = "parallelJoin" }, new() { Id = "e", Type = "end" }],
        Edges = [new() { From = "s", To = "split" }, new() { From = "split", To = left.Id },
            new() { From = "split", To = right.Id }, new() { From = left.Id, To = "join" },
            new() { From = right.Id, To = "join" }, new() { From = "join", To = "e" }]
    };

    private static FlowNode EmptyRecoveryNode(string id) => new()
    {
        Id = id,
        ApproverStrategy = "Specified",
        ApproverUserId = Guid.NewGuid(),
        ApproverRecoveryPolicy = new("MANUAL_ASSIGNMENT", [72])
    };

    [CurrentPermissionPostgresFact]
    public async Task NativeRecovery_HTTP_returned_terminal_original_and_submitted_draft_use_separate_commands()
    {
        await using var h = await Seed();
        var graph = LinearNative(h.User);
        graph.Edges.Insert(0, new() { From = "s", To = "e", Condition = "a == 'finish'" });
        var f = await NativeTaskGraph(h, graph, fromDraft: true);
        var before = await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync();
        var task = await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync();
        var draft = await h.Db.Database.SqlQuery<Guid>($"SELECT draft_id AS \"Value\" FROM cp6_personal_form_v2.submissions WHERE root_id={f.Instance}").SingleAsync();

        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,
            new Dictionary<string, string> { ["ScopeAuthorization__EnvironmentId"] = ScopeEnvironment.ToString("D") });
        var cookies = await Login(host, h);
        var instancePath = $"/api/oa/v2/instances/{f.Instance:D}";
        var taskPath = $"/api/oa/v2/tasks/{task.Id:D}";
        using var previewResponse = await host.Send(BoundRequest(cookies, taskPath + "/sendback-preview", new NativeSendBackTargetV2("STARTER", null)));
        var preview = await PersonalHttpResult<NativeSendBackPreviewV2>(previewResponse);
        using var returned = await host.Send(PersonalHttp(cookies, taskPath + "/sendback", SendBackCommand(preview, "terminal-return")));
        var returnedResult = await PersonalHttpResult<PersonalFormResultV2>(returned);
        using var read = await host.Send(BoundRequest(cookies, instancePath + "/returned-form"));
        var context = await PersonalHttpResult<ReturnedFormContextV2>(read);
        Assert.Equal(before.FormDataId, context.FormDataId);
        Assert.Equal(before.FormDefVersionId, context.FormDefVersionId);
        Assert.Equal(before.FlowDefVersionId, context.FlowDefVersionId);
        Assert.Equal(returnedResult.Result!.Value.GetProperty("returnRevision").GetInt64(), context.ReturnRevision);

        var oldDraft = await f.Owner.DraftAsync(draft);
        using var ordinary = await host.Send(PersonalHttp(cookies, $"/api/oa/v2/drafts/{draft:D}/submit",
            PersonalCommand(PersonalSubmit(await f.Owner.ContextAsync("personal")), "ordinary-after-return", oldDraft.RowVersion)));
        Assert.Equal(HttpStatusCode.Conflict, ordinary.StatusCode);

        async Task<HttpResponseMessage> Edit(string key, string instanceVersion, string dataVersion)
        {
            using var request = PersonalHttp(cookies, instancePath + "/returned-form", PersonalCommand(
                new ReturnedFormEditV2(context.ReturnRevision, dataVersion, PersonalData("""{"a":"finish"}"""), null),
                key, instanceVersion));
            request.Method = HttpMethod.Put;
            return await host.Send(request);
        }

        var staleVersion = Convert.ToBase64String(new byte[8]);
        using var staleInstance = await Edit("stale-instance", staleVersion, context.FormDataVersion);
        using var staleForm = await Edit("stale-form", context.InstanceVersion, staleVersion);
        Assert.Equal(HttpStatusCode.Conflict, staleInstance.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, staleForm.StatusCode);
        using var saved = await Edit("terminal-edit", context.InstanceVersion, context.FormDataVersion);
        var editReceipt = await PersonalHttpResult<PersonalFormResultV2>(saved);
        var edited = PersonalResult<ReturnedFormResultV2>(editReceipt);
        Assert.Equal("DRAFT", editReceipt.Result!.Value.GetProperty("instanceStatus").GetString());
        Assert.Equal(context.ExecutionCycleId, edited.ExecutionCycleId);
        Assert.Empty(edited.TaskIds);

        var body = new ReturnedFormResubmitV2(context.ReturnRevision, edited.FormDataVersion, "沿原流程直接完成");
        var terminals = await Task.WhenAll(
            host.Send(PersonalHttp(cookies, instancePath + "/resubmit", PersonalCommand(body, "terminal-1", edited.InstanceVersion))),
            host.Send(PersonalHttp(cookies, instancePath + "/resubmit", PersonalCommand(body, "terminal-2", edited.InstanceVersion))));
        using var won = Assert.Single(terminals, r => r.StatusCode == HttpStatusCode.OK);
        using var lost = Assert.Single(terminals, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Contains("ALREADY_RESUBMITTED", await lost.Content.ReadAsStringAsync());
        var result = await PersonalHttpResult<PersonalFormResultV2>(won);
        Assert.Equal("APPROVED", result.Result!.Value.GetProperty("instanceStatus").GetString());
        var resubmitted = PersonalResult<ReturnedFormResultV2>(result);
        Assert.NotEqual(context.ExecutionCycleId, resubmitted.ExecutionCycleId);
        Assert.Empty(resubmitted.TaskIds);
        Assert.Empty(resubmitted.PendingResolutionIds);

        using var original = await host.Send(BoundRequest(cookies,
            $"/api/oa/v2/personal-forms/commands/SFS_RETURNED_RESUBMIT/{f.Instance:D}/{result.CommandId}"));
        Assert.Equal(ApprovalCanonicalV2.Bytes(result), ApprovalCanonicalV2.Bytes(await PersonalHttpResult<PersonalFormResultV2>(original)));
        var after = await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync();
        Assert.Equal((before.Id, before.FormDataId, before.FormDefVersionId, before.FlowDefVersionId),
            (after.Id, after.FormDataId, after.FormDefVersionId, after.FlowDefVersionId));
        Assert.Equal(FlowInstanceStatus.Approved, after.Status);
        Assert.Equal(FlowTaskStatus.Cancelled, (await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync()).Status);
        Assert.Empty((await NativeInbox(h).DetailAsync(f.Instance)).Votes);
        Assert.Equal(oldDraft.RowVersion, (await f.Owner.DraftAsync(draft)).RowVersion);
        Assert.Equal(2, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.execution_cycles WHERE root_id={f.Instance}").SingleAsync());
    }


    [CurrentPermissionPostgresFact]
    public async Task NativeRecovery_HTTP_transfer_and_after_addsign_keep_distinct_originals_and_current_duties()
    {
        await using var h = await Seed();
        await using var foreign = await Seed();
        var f = await NativeFormSetup(h);
        await NativeOperationPermissions(h);
        await NativeTaskGraph(foreign, LinearNative(foreign.User));
        var recipient = await GrantDelegate(h, "current-reviewer");
        var countersigner = await GrantDelegate(h, "additional-reviewer");
        var disabled = await GrantDelegate(h, "disabled-reviewer");
        var unqualified = await GrantDelegate(h, "unqualified-reviewer");
        h.Db.Sys_Roles.Add(new() { RoleId = 74, RoleName = "未授权审批角色" });
        await h.Db.SaveChangesAsync();
        await h.Db.Sys_Users.Where(u => u.Id == disabled).ExecuteUpdateAsync(s => s.SetProperty(u => u.Enable, false));
        await h.Db.Sys_Users.Where(u => u.Id == unqualified).ExecuteUpdateAsync(s => s.SetProperty(u => u.RoleId, 74));
        var hash = new CP6.Core.Services.Sys.BCryptPasswordHasher(4).Hash("Price1!Password");
        await h.Db.Sys_Users.Where(u => u.Id == recipient).ExecuteUpdateAsync(s => s.SetProperty(u => u.Password, hash));

        var initial = await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync();
        var originalFormTo = await h.Db.Wf_FlowFormTos.AsNoTracking().SingleAsync();
        var formData = (await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync()).DataJson;
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,
            new Dictionary<string, string> { ["ScopeAuthorization__EnvironmentId"] = ScopeEnvironment.ToString("D") });
        var owner = await Login(host, h);
        var outsider = await Login(host, foreign);
        var taskPath = $"/api/oa/v2/tasks/{f.Task:D}";
        using var initialContext = await host.Send(BoundRequest(owner, taskPath + "/operations-context"));
        var before = (await PersonalHttpResult<NativeTaskOperationContextV2>(initialContext)).Task;

        byte[] Transfer(Guid target, string command) => PersonalCommand(
            new NativeTransferInputV2(f.Task, target, "转交给有资格的职责人", before.TaskRowVersion),
            command, before.InstanceRowVersion);

        using var foreignActor = await host.Send(PersonalHttp(outsider, taskPath + "/transfer", Transfer(recipient, "wrong-tenant")));
        using var foreignTarget = await host.Send(PersonalHttp(owner, taskPath + "/transfer", Transfer(foreign.User, "wrong-tenant-target")));
        using var disabledTarget = await host.Send(PersonalHttp(owner, taskPath + "/transfer", Transfer(disabled, "disabled-target")));
        using var unqualifiedTarget = await host.Send(PersonalHttp(owner, taskPath + "/transfer", Transfer(unqualified, "unqualified-target")));
        Assert.Equal(HttpStatusCode.NotFound, foreignActor.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, foreignTarget.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, disabledTarget.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unqualifiedTarget.StatusCode);

        var menu = await h.Db.Sys_Menus.Where(m => m.MenuKey == "oa-inbox").Select(m => m.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(r => r.MenuId == menu && r.ActionCode == "transfer" && r.RoleId == 72).ExecuteDeleteAsync();
        using var revoked = await host.Send(PersonalHttp(owner, taskPath + "/transfer", Transfer(recipient, "revoked-transfer")));
        Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.assignment_changes WHERE task_id={f.Task}").SingleAsync());
        Assert.Equal(originalFormTo.Id, (await h.Db.Wf_FlowFormTos.AsNoTracking().SingleAsync()).Id);
        await EngineeringAction(h, "oa-inbox", "transfer");

        using var transferred = await host.Send(PersonalHttp(owner, taskPath + "/transfer", Transfer(recipient, "original-transfer")));
        var transfer = await PersonalHttpResult<PersonalFormResultV2>(transferred);
        var current = await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync();
        Assert.Equal((initial.Id, initial.TokenId, initial.StageIndex, initial.StageRound, initial.DueAt),
            (current.Id, current.TokenId, current.StageIndex, current.StageRound, current.DueAt));
        Assert.Equal(recipient, current.AssigneeId);
        var oldDuty = await h.Db.Wf_FlowFormTos.AsNoTracking().SingleAsync(row => row.Id == originalFormTo.Id);
        Assert.Equal((f.Task, h.User, h.User, FlowFormToStatus.Transferred),
            (oldDuty.NativeTaskId!.Value, oldDuty.ExpectedHandlerId, oldDuty.ActualHandlerId!.Value, oldDuty.Status));
        var detail = await NativeInbox(h).DetailAsync(f.Instance);
        Assert.Equal(h.User, Assert.Single(detail.Tasks).CreatedPrincipalId);

        var currentActor = await Login(host, "current-reviewer", "Price1!Password", h.Tenant.ToString("N"));
        using var changed = await host.Send(BoundRequest(currentActor, taskPath + "/operations-context"));
        var view = (await PersonalHttpResult<NativeTaskOperationContextV2>(changed)).Task;
        var raw = PersonalCommand(new NativeAddSignInputV2(f.Task, countersigner, "after", "追加后续审核", view.TaskRowVersion),
            "original-addsign", view.InstanceRowVersion);
        using var signed = await host.Send(PersonalHttp(currentActor, taskPath + "/addsign", raw));
        var addsign = await PersonalHttpResult<PersonalFormResultV2>(signed);
        var created = await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t => t.Id != f.Task);
        Assert.NotEqual(f.Task, created.Id);
        Assert.Equal((countersigner, initial.TokenId, initial.StageIndex, initial.StageRound),
            (created.AssigneeId, created.TokenId, created.StageIndex, created.StageRound));
        Assert.Equal(countersigner,
            (await h.Db.Wf_FlowFormTos.AsNoTracking().SingleAsync(row => row.NativeTaskId == created.Id)).ExpectedHandlerId);

        using var originalTransfer = await host.Send(BoundRequest(owner, "/api/oa/v2/tasks/operations/OA_TASK_TRANSFER/" + f.Task + "/original-transfer"));
        using var originalAddsign = await host.Send(BoundRequest(currentActor, "/api/oa/v2/tasks/operations/OA_TASK_ADDSIGN/" + f.Task + "/original-addsign"));
        Assert.Equal(ApprovalCanonicalV2.Bytes(transfer), ApprovalCanonicalV2.Bytes(await PersonalHttpResult<PersonalFormResultV2>(originalTransfer)));
        Assert.Equal(ApprovalCanonicalV2.Bytes(addsign), ApprovalCanonicalV2.Bytes(await PersonalHttpResult<PersonalFormResultV2>(originalAddsign)));
        Assert.Equal(formData, (await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync()).DataJson);
        Assert.Empty((await NativeInbox(h).DetailAsync(f.Instance)).Votes);
        Assert.Equal(2, await h.Db.Wf_FlowTasks.CountAsync());
        Assert.Equal(2, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.commands WHERE root_id={f.Instance} AND operation IN ('OA_TASK_TRANSFER','OA_TASK_ADDSIGN') AND completed").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task NativeRecovery_HTTP_checks_organization_and_both_versions_then_preserves_unresolved_slots_on_replay()
    {
        await using var h = await Seed();
        var department = Guid.NewGuid();
        h.Db.Sys_Depts.Add(new()
        {
            Id = department, DeptCode = "recovery-org", DeptName = "暂不可用的组织来源",
            Path = $"/{department:D}/", Enable = false
        });
        await h.Db.SaveChangesAsync();
        await h.Db.Sys_Users.Where(u => u.Id == h.User).ExecuteUpdateAsync(s => s.SetProperty(u => u.DeptId, department));
        h.Db.ChangeTracker.Clear();
        var left = EmptyRecoveryNode("org");
        left.ApproverStrategy = "DeptLeader";
        left.ApproverUserId = null;
        var f = await NativeTaskGraph(h, ParallelRecoveryGraph(left, EmptyRecoveryNode("vacant")));
        var target = await GrantDelegate(h);
        Assert.Empty(await h.Db.Wf_FlowTasks.ToArrayAsync());
        Assert.Equal(FlowInstanceStatus.Suspended, (await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync()).Status);

        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,
            new Dictionary<string, string> { ["ScopeAuthorization__EnvironmentId"] = ScopeEnvironment.ToString("D") });
        var cookies = await Login(host, h);
        using var listed = await host.Send(BoundRequest(cookies, $"/api/oa/v2/instances/{f.Instance:D}/pending-resolutions"));
        var slots = await PersonalHttpResult<PendingResolutionContextV2[]>(listed);
        Assert.Equal(2, slots.Length);
        var org = slots.Single(s => s.NodeId == "org");
        var vacant = slots.Single(s => s.NodeId == "vacant");
        Assert.False(org.CanAssign);
        Assert.Empty(org.CandidatePrincipalIds);
        Assert.Contains("AUTHORITY_UNAVAILABLE", org.BlockingReasons);
        Assert.True(vacant.CanAssign);
        Assert.Equal(f.Instance, vacant.ExecutionCycleId);

        Task<HttpResponseMessage> Assign(PendingResolutionContextV2 slot, Guid person, string key) =>
            host.Send(PersonalHttp(cookies, $"/api/oa/v2/pending-resolutions/{slot.ResolutionId:D}/assign",
                ResolveCommand(slot, person, key)));

        using var unavailable = await Assign(org, target, "organization-unavailable");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        using var staleInstance = await Assign(vacant with { InstanceVersion = Convert.ToBase64String(new byte[8]) }, target, "wrong-instance");
        using var staleSlot = await Assign(vacant with { ResolutionVersion = Guid.NewGuid().ToString("D") }, target, "wrong-slot");
        using var wrongPolicy = await Assign(vacant with { RoutePolicyRef = org.RoutePolicyRef }, target, "wrong-policy");
        Assert.Equal(HttpStatusCode.Conflict, staleInstance.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, staleSlot.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, wrongPolicy.StatusCode);
        using var self = await Assign(vacant, h.User, "starter-sod");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, self.StatusCode);
        var menu = await h.Db.Sys_Menus.Where(m => m.MenuKey == "oa-inbox").Select(m => m.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(r => r.MenuId == menu && r.ActionCode == "resolve-approver" && r.RoleId == 72).ExecuteDeleteAsync();
        using var revoked = await Assign(vacant, target, "resolver-revoked");
        Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        await EngineeringAction(h, "oa-inbox", "resolve-approver");
        Assert.Empty(await h.Db.Wf_FlowTasks.ToArrayAsync());

        var attempts = await Task.WhenAll(Assign(vacant, target, "resolve-1"), Assign(vacant, target, "resolve-2"));
        using var winner = Assert.Single(attempts, r => r.StatusCode == HttpStatusCode.OK);
        using var conflict = Assert.Single(attempts, r => r.StatusCode == HttpStatusCode.Conflict);
        var first = await PersonalHttpResult<PersonalFormResultV2>(winner);
        using var differentIntent = await Assign(vacant, h.User, first.CommandId);
        Assert.Equal(HttpStatusCode.Conflict, differentIntent.StatusCode);
        Assert.Contains("COMMAND_BODY_CONFLICT", await differentIntent.Content.ReadAsStringAsync());
        var result = first.Result!.Value;
        Assert.Equal(org.ResolutionId, Assert.Single(result.GetProperty("remainingResolutionIds").EnumerateArray()).GetGuid());
        Assert.Equal(FlowInstanceStatus.Suspended, result.GetProperty("instanceStatus").GetInt32());
        var firstTaskId = Assert.Single(result.GetProperty("taskIds").EnumerateArray()).GetGuid();
        var task = await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync();
        Assert.Equal((firstTaskId, target, vacant.TokenId, vacant.NodeId, vacant.StageIndex, vacant.StageRound),
            (task.Id, task.AssigneeId, task.TokenId!.Value, task.NodeId, task.StageIndex, task.StageRound));
        var formTo = await h.Db.Wf_FlowFormTos.AsNoTracking().SingleAsync();
        Assert.Equal((firstTaskId, target, FlowFormToStatus.Pending),
            (formTo.NativeTaskId!.Value, formTo.ExpectedHandlerId, formTo.Status));
        Assert.Empty((await NativeInbox(h).DetailAsync(f.Instance)).Votes);

        await h.Db.Sys_Depts.Where(d => d.Id == department).ExecuteUpdateAsync(s => s.SetProperty(d => d.Enable, true));
        using var current = await host.Send(BoundRequest(cookies, $"/api/oa/v2/pending-resolutions/{org.ResolutionId:D}/context"));
        var refreshed = await PersonalHttpResult<PendingResolutionContextV2>(current);
        Assert.True(refreshed.CanAssign);
        Assert.Equal(org.RoutePolicyRef, refreshed.RoutePolicyRef);
        using var completed = await Assign(refreshed, target, "resolve-organization");
        var last = (await PersonalHttpResult<PersonalFormResultV2>(completed)).Result!.Value;
        Assert.Equal(FlowInstanceStatus.Running, last.GetProperty("instanceStatus").GetInt32());
        Assert.Empty(last.GetProperty("remainingResolutionIds").EnumerateArray());
        using var original = await host.Send(BoundRequest(cookies,
            $"/api/oa/v2/tasks/operations/OA_APPROVER_RESOLVE/{vacant.ResolutionId:D}/{first.CommandId}"));
        Assert.Equal(ApprovalCanonicalV2.Bytes(first), ApprovalCanonicalV2.Bytes(await PersonalHttpResult<PersonalFormResultV2>(original)));
        Assert.Equal(2, await h.Db.Wf_FlowTasks.CountAsync());
        Assert.Equal(2, await h.Db.Wf_FlowFormTos.CountAsync());
        Assert.Empty((await NativeInbox(h).DetailAsync(f.Instance)).Votes);
        Assert.Equal(2, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.commands WHERE root_id={f.Instance} AND operation='OA_APPROVER_RESOLVE' AND completed").SingleAsync());
    }
}
