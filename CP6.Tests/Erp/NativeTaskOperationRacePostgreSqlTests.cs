using CP6.Core.Services.Common;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task NativeRecovery_sendback_and_approval_share_a_gate_and_replay_has_one_notice()
    {
        var hook = new StartWriteHook();
        await using var h = await Seed(hook);
        var f = await NativeFormSetup(h);
        await NativeOperationPermissions(h);
        var context = await f.Owner.TaskContextAsync(f.Task);
        var vote = NativeVote(context, "与退回竞争的原审批", "approve");
        var raw = SendBackCommand(await f.Owner.SendBackPreviewAsync(f.Task, new("STARTER", null)), "race-return");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hook.Before = async command =>
        {
            if (!command.CommandText.Contains("INSERT INTO cp6_personal_form_v2.returns", StringComparison.Ordinal)) return;
            entered.TrySetResult();
            await release.Task;
        };
        var returning = f.Owner.SendBackAsync(f.Task, raw);
        await using var peer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var other = PersonalForm(h with { Db = peer });
        Task<Exception?> losing;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            losing = Record.ExceptionAsync(() => DecideNative(other, f.Task, vote));
            await Task.Delay(100);
            Assert.False(losing.IsCompleted);
        }
        finally { release.TrySetResult(); }
        var accepted = await returning;
        Assert.IsType<ScopeAuthorizationExceptionV1>(await losing);
        Assert.Equal(ApprovalCanonicalV2.Bytes(accepted), ApprovalCanonicalV2.Bytes(await f.Owner.SendBackAsync(f.Task, raw)));
        Assert.Equal(FlowInstanceStatus.Draft, (await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(FlowTaskStatus.Cancelled, (await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync()).Status);
        Assert.Empty((await NativeInbox(h).DetailAsync(f.Instance)).Votes);
        Assert.Equal(1, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.returns WHERE instance_id={f.Instance}").SingleAsync());
        Assert.Equal(1, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.commands WHERE root_id={f.Instance} AND operation='OA_TASK_SENDBACK' AND completed").SingleAsync());
        Assert.Equal(1, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.outbox WHERE task_id={f.Task} AND kind='TASK_SENT_BACK'").SingleAsync());
        Assert.Equal(1, await h.Db.Wf_FlowHistories.CountAsync(x => x.InstanceId == f.Instance && x.Action == "sendback"));
    }

    [CurrentPermissionPostgresFact]
    public async Task NativeRecovery_before_and_after_addsign_each_fence_competing_approval()
    {
        foreach (var source in new[] { "before", "after" })
        {
            var hook = new StartWriteHook();
            await using var h = await Seed(hook);
            var f = await NativeFormSetup(h);
            await NativeOperationPermissions(h);
            var target = await GrantDelegate(h);
            var context = await f.Owner.TaskContextAsync(f.Task);
            var vote = NativeVote(context, "基于加签前版本的审批", "approve");
            var raw = PersonalCommand(new NativeAddSignInputV2(f.Task, target, source, "固定加签位置", context.Task.TaskRowVersion),
                "race-addsign-" + source, context.Task.InstanceRowVersion);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            hook.Before = async command =>
            {
                if (!command.CommandText.Contains("INSERT INTO cp6_personal_form_v2.add_sign_links", StringComparison.Ordinal)) return;
                entered.TrySetResult();
                await release.Task;
            };
            var signing = f.Owner.AddSignTaskAsync(f.Task, raw);
            await using var peer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
            Task<Exception?> losing;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                losing = Record.ExceptionAsync(() => DecideNative(PersonalForm(h with { Db = peer }), f.Task, vote));
                await Task.Delay(100);
                Assert.False(losing.IsCompleted);
            }
            finally { release.TrySetResult(); }
            var original = await signing;
            Assert.IsType<ScopeAuthorizationExceptionV1>(await losing);
            Assert.Equal(ApprovalCanonicalV2.Bytes(original), ApprovalCanonicalV2.Bytes(await f.Owner.AddSignTaskAsync(f.Task, raw)));
            var tasks = await h.Db.Wf_FlowTasks.AsNoTracking().ToArrayAsync();
            Assert.Equal(2, tasks.Length);
            var added = Assert.Single(tasks, t => t.Id != f.Task);
            Assert.Equal((target, source, FlowTaskStatus.Pending), (added.AssigneeId, added.AddSignSource, added.Status));
            Assert.Equal(source == "before" ? FlowTaskStatus.Suspended : FlowTaskStatus.Pending, tasks.Single(t => t.Id == f.Task).Status);
            Assert.Empty((await NativeInbox(h).DetailAsync(f.Instance)).Votes);
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task NativeRecovery_principal_and_explicit_delegate_cannot_both_vote_on_the_same_duty()
    {
        var hook = new StartWriteHook();
        await using var h = await Seed(hook);
        var (_, principal, _) = await DecisionSetup(h);
        var proxy = await GrantDelegate(h, "concurrent-delegate");
        await CreateTaskGrant(h, principal, proxy, "binding-flow");
        var original = DecisionActor(h, principal);
        var originalVote = await DecisionInput(original);
        await using var peer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var acting = DecisionActor(h with { Db = peer }, proxy);
        var proxyVote = await DecisionInput(acting, behalf: principal);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hook.Before = async command =>
        {
            if (!command.CommandText.Contains("INSERT INTO cp6_approval_decision_v2.votes", StringComparison.Ordinal)) return;
            entered.TrySetResult();
            await release.Task;
        };
        var voting = Vote(original, originalVote);
        Task<Exception?> losing;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            losing = Record.ExceptionAsync(() => Vote(acting, proxyVote, principal));
            await Task.Delay(100);
            Assert.False(losing.IsCompleted);
        }
        finally { release.TrySetResult(); }
        var accepted = await voting;
        Assert.IsType<ScopeAuthorizationExceptionV1>(await losing);
        Assert.Null(accepted.Result.DecisionId);
        Assert.Equal(principal, await h.Db.Database.SqlQuery<Guid>($"SELECT actual_id AS \"Value\" FROM cp6_approval_decision_v2.votes WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Null(await Decisions(acting).OriginalAsync(proxyVote.Input.CommandId));
    }

    [CurrentPermissionPostgresFact]
    public async Task NativeTask_transfer_and_approval_two_connections_have_one_winner_and_keep_each_read_principal()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await NativeFormSetup(h);await NativeOperationPermissions(h);var target=await GrantDelegate(h);
        var inbox=NativeInbox(h);var oldRead=await inbox.MarkReadAsync("TASK",f.Task,PersonalCommand(new{},"read-before-transfer"));
        var context=await f.Owner.TaskContextAsync(f.Task);var vote=NativeVote(context,"stale competing approval","approve");
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hook.Before=async c=>{if(c.CommandText.Contains("INSERT INTO cp6_personal_form_v2.assignment_changes")){entered.SetResult();await release.Task;}};
        var transfer=f.Owner.TransferTaskAsync(f.Task,PersonalCommand(new NativeTransferInputV2(f.Task,target,"explicit handoff",context.Task.TaskRowVersion),"race-transfer",context.Task.InstanceRowVersion));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var peer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var peerOwner=PersonalForm(h with{Db=peer});
        var losing=Record.ExceptionAsync(()=>DecideNative(peerOwner,f.Task,vote));
        await Task.Delay(100);Assert.False(losing.IsCompleted);release.SetResult();await transfer;Assert.IsType<ScopeAuthorizationExceptionV1>(await losing);
        Assert.Equal(target,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Id==f.Task)).AssigneeId);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.votes WHERE task_id={f.Task}").SingleAsync());
        Assert.Equal(oldRead,await inbox.ReadOriginalAsync("TASK",f.Task,"read-before-transfer"));
        TaskActor(h,target);var targetInbox=NativeInbox(h with{User=target});Assert.Null(Assert.Single((await targetInbox.DetailAsync(f.Instance)).Tasks).Read);
        await targetInbox.MarkReadAsync("TASK",f.Task,PersonalCommand(new{},"read-transferred"));
        Assert.Equal(2,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_workflow_read_v2.facts WHERE target_id={f.Task}").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task NativeTask_lost_transfer_commit_recovers_original_and_closed_slot_never_transfers()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var f=await NativeFormSetup(h);await NativeOperationPermissions(h);var target=await GrantDelegate(h);
        var context=await f.Owner.TaskContextAsync(f.Task);
        var closed=await f.Owner.CloseAsync("OA_TASK_TRANSFER",f.Task.ToString("D"),"closed-transfer");Assert.Equal("CLOSED",closed.Outcome);
        var body=new NativeTransferInputV2(f.Task,target,"transfer with lost acknowledgement",context.Task.TaskRowVersion);
        Assert.Equal("COMMAND_CLOSED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.TransferTaskAsync(f.Task,PersonalCommand(body,"closed-transfer",context.Task.InstanceRowVersion)))).Message);
        var raw=PersonalCommand(body,"lost-transfer",context.Task.InstanceRowVersion);fault.Armed=true;
        Assert.Equal("COMMAND_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.TransferTaskAsync(f.Task,raw))).Message);
        var original=await f.Owner.OriginalAsync("OA_TASK_TRANSFER",f.Task.ToString("D"),"lost-transfer");Assert.NotNull(original);
        Assert.Equal(ApprovalCanonicalV2.Bytes(original),ApprovalCanonicalV2.Bytes(await f.Owner.TransferTaskAsync(f.Task,raw)));
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.assignment_changes WHERE task_id={f.Task}").SingleAsync());
        Assert.Equal(target,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Id==f.Task)).AssigneeId);
    }

    [CurrentPermissionPostgresFact]
    public async Task NativeApp_addsign_to_existing_reviewer_updates_exact_task_formto_then_real_terminal_votes()
    {
        await using var h=await Seed();var(_,first,second)=await DecisionSetup(h);await NativeOperationPermissions(h);
        var a=DecisionActor(h,first);var owner=Decisions(a);var command=await DecisionInput(a);var context=await owner.ContextAsync(command.Task);
        var existing=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.AssigneeId==second);
        await owner.AddSignTaskAsync(command.Task,PersonalCommand(new NativeAddSignInputV2(command.Task,second,"before","second duty for same reviewer",context.TaskRowVersion),"same-reviewer",context.InstanceRowVersion));
        var extra=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.AssigneeId==second&&t.Id!=existing.Id);var b=DecisionActor(h,second);
        async Task Decide(Guid id)
        {var c=await Decisions(b).ContextAsync(id);await Decisions(b).DecideAsync(id,new(Guid.NewGuid(),"approve","review exact duty",c.TaskRowVersion,c.InstanceRowVersion),ifMatch:"\""+c.TaskRowVersion+"\"");}
        await Decide(existing.Id);
        Assert.Equal(1,(await h.Db.Wf_FlowFormTos.AsNoTracking().SingleAsync(x=>x.NativeTaskId==existing.Id)).Status);
        Assert.Equal(0,(await h.Db.Wf_FlowFormTos.AsNoTracking().SingleAsync(x=>x.NativeTaskId==extra.Id)).Status);
        await Decide(extra.Id);Assert.NotNull((await Vote(a,await DecisionInput(a))).Result.DecisionId);
        Assert.All(await h.Db.Wf_FlowFormTos.AsNoTracking().ToArrayAsync(),x=>Assert.Equal(1,x.Status));
    }
}
