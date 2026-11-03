using CP6.Core.Services.Common;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
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
