using System.Text.Json;
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
    public async Task NativeSendBack_sibling_is_rejected_same_branch_preserves_sibling_and_before_split_cancels_exact_live_set()
    {
        await using var h=await Seed();var graph=LinearNative(h.User);
        graph.Nodes.AddRange([new(){Id="pre",ApproverStrategy="Specified",ApproverUserId=h.User},new(){Id="a2",ApproverStrategy="Specified",ApproverUserId=h.User},new(){Id="split",Type="parallelSplit"},new(){Id="join",Type="parallelJoin"}]);
        graph.Edges=[new(){From="s",To="pre"},new(){From="pre",To="split"},new(){From="split",To="a"},new(){From="split",To="b"},new(){From="a",To="a2"},new(){From="a2",To="join"},new(){From="b",To="join"},new(){From="join",To="e"}];
        var f=await NativeTaskGraph(h,graph);await NativeApprove(f.Owner,(await h.Db.Wf_FlowTasks.SingleAsync()).Id);
        await NativeApprove(f.Owner,(await h.Db.Wf_FlowTasks.SingleAsync(t=>t.NodeId=="a"&&t.Status==0)).Id);
        var current=(await h.Db.Wf_FlowTasks.SingleAsync(t=>t.NodeId=="a2"&&t.Status==0)).Id;
        var sibling=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.NodeId=="b"&&t.Status==0);
        var siblingVersion=(await f.Owner.TaskContextAsync(sibling.Id)).Task.TaskRowVersion;var histories=await h.Db.Wf_FlowHistories.CountAsync();
        Assert.Equal("E-WF-019",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.SendBackPreviewAsync(current,new("NODE","b")))).Message);
        Assert.Equal(histories,await h.Db.Wf_FlowHistories.CountAsync());
        var local=await f.Owner.SendBackPreviewAsync(current,new("NODE","a"));Assert.Equal("TOKEN_SUBTREE",local.Scope);Assert.Equal(current,Assert.Single(local.AffectedTaskIds));
        await f.Owner.SendBackAsync(current,SendBackCommand(local,"local-return"));
        Assert.Equal(siblingVersion,(await f.Owner.TaskContextAsync(sibling.Id)).Task.TaskRowVersion);
        Assert.Equal(0,(await h.Db.Wf_FlowFormTos.AsNoTracking().SingleAsync(x=>x.NativeTaskId==sibling.Id)).Status);
        await NativeApprove(f.Owner,(await h.Db.Wf_FlowTasks.SingleAsync(t=>t.NodeId=="a"&&t.Status==0)).Id);
        current=(await h.Db.Wf_FlowTasks.SingleAsync(t=>t.NodeId=="a2"&&t.Status==0)).Id;
        var whole=await f.Owner.SendBackPreviewAsync(current,new("NODE","pre"));Assert.Equal(new[]{current,sibling.Id}.Order(),whole.AffectedTaskIds.Order());
        await f.Owner.SendBackAsync(current,SendBackCommand(whole,"before-split"));
        Assert.Equal(FlowTaskStatus.Cancelled,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Id==sibling.Id)).Status);
        Assert.Equal("pre",(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Status==0)).NodeId);
        for(var i=0;i<6;i++){var next=await h.Db.Wf_FlowTasks.AsNoTracking().Where(t=>t.Status==0).OrderBy(t=>t.Id).FirstOrDefaultAsync();if(next==null)break;await NativeApprove(f.Owner,next.Id);}
        Assert.Equal(FlowInstanceStatus.Approved,(await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync()).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task NativeSendBack_from_child_cancels_the_root_tree_and_resubmits_new_children_with_original_pins()
    {
        const string formSchema="""{"fields":[{"name":"rows","type":"table","columns":[{"name":"item","type":"input"}]}]}""";
        await using var h=await Seed();var(owner,_,flow)=await PreparePersonal(h,formSchema);await NativeOperationPermissions(h);
        var child=await flow.SaveAsync("return-child",DependentCommand(schema:JsonSerializer.Serialize(LinearNative(h.User),PersonalFormOwnerV2.Json)));
        var childPin=(await flow.PublishAsync("return-child",PublicationCommand(await Preview(flow,"return-child",child)))).Result!.Publication!;
        await flow.SetActivationAsync("return-child",ActivationCommand((await flow.ActivationPreviewAsync("return-child")).Current,childPin.VersionId));
        var schema="""{"start":"s","nodes":[{"id":"s","type":"start"},{"id":"call","type":"subFlow","subFlowKey":"return-child","subCollectionVar":"rows","subCompletionPolicy":"all"},{"id":"e","type":"end"}],"edges":[{"from":"s","to":"call"},{"from":"call","to":"e"}]}""";
        var root=await flow.SaveAsync("return-root",DependentCommand("personal",schema:schema));
        var pin=(await flow.PublishAsync("return-root",PublicationCommand(await Preview(flow,"return-root",root)))).Result!.Publication!;
        await flow.SetActivationAsync("return-root",ActivationCommand((await flow.ActivationPreviewAsync("return-root")).Current,pin.VersionId));
        var started=PersonalResult<PersonalFormSubmissionV2>(await owner.SubmitDirectAsync("personal",PersonalCommand(PersonalDirect(await owner.ContextAsync("personal"),"{\"rows\":[{\"item\":\"one\"},{\"item\":\"two\"}]}"),"tree")));
        var oldChildren=await h.Db.Wf_FlowInstances.AsNoTracking().Where(i=>i.ParentInstanceId==started.FlowInstanceId).Select(i=>i.Id).ToArrayAsync();
        var tasks=await h.Db.Wf_FlowTasks.AsNoTracking().ToArrayAsync();var preview=await owner.SendBackPreviewAsync(tasks[0].Id,new("STARTER",null));
        Assert.Equal(tasks.Select(t=>t.Id).Order(),preview.AffectedTaskIds.Order());await owner.SendBackAsync(tasks[0].Id,SendBackCommand(preview));
        Assert.False(await h.Db.Wf_FlowTasks.AnyAsync(t=>t.Status==0||t.Status==4));Assert.False(await h.Db.Wf_FlowTokens.AnyAsync(t=>t.Status==0));
        Assert.All(await h.Db.Wf_FlowInstances.AsNoTracking().Where(i=>oldChildren.Contains(i.Id)).ToArrayAsync(),i=>Assert.Equal(FlowInstanceStatus.Withdrawn,i.Status));
        var returned=await owner.ReturnedFormAsync(started.FlowInstanceId!.Value);
        var resumed=PersonalResult<ReturnedFormResultV2>(await owner.ResubmitReturnedAsync(returned.InstanceId,PersonalCommand(new ReturnedFormResubmitV2(returned.ReturnRevision,returned.FormDataVersion,"restart original pins"),"resubmit-tree",returned.InstanceVersion)));
        Assert.Equal(returned.InstanceId,resumed.InstanceId);Assert.Equal(pin.VersionId,resumed.FlowDefVersionId);
        var children=await h.Db.Wf_FlowInstances.AsNoTracking().Where(i=>i.ParentInstanceId==returned.InstanceId&&!oldChildren.Contains(i.Id)).ToArrayAsync();
        Assert.Equal(2,children.Length);Assert.All(children,i=>Assert.Equal(childPin.VersionId,i.FlowDefVersionId));
        for(var i=0;i<4;i++)await NativeApprove(owner,(await h.Db.Wf_FlowTasks.AsNoTracking().Where(t=>t.Status==0).OrderBy(t=>t.Id).FirstAsync()).Id);
        Assert.Equal(FlowInstanceStatus.Approved,(await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync(i=>i.Id==returned.InstanceId)).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task NativeSendBack_resubmit_two_connections_commit_one_cycle_and_keep_the_winning_original()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await NativeFormSetup(h);await NativeOperationPermissions(h);
        await f.Owner.SendBackAsync(f.Task,SendBackCommand(await f.Owner.SendBackPreviewAsync(f.Task,new("STARTER",null))));
        var c=await f.Owner.ReturnedFormAsync(f.Instance);var body=new ReturnedFormResubmitV2(c.ReturnRevision,c.FormDataVersion,"competing resubmit");
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hook.Before=async sql=>{if(sql.CommandText.Contains("INSERT INTO cp6_personal_form_v2.execution_cycles")){entered.SetResult();await release.Task;}};
        var winner=f.Owner.ResubmitReturnedAsync(f.Instance,PersonalCommand(body,"first-resubmit",c.InstanceVersion));await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var peer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var other=PersonalForm(h with{Db=peer});
        var loser=Record.ExceptionAsync(()=>other.ResubmitReturnedAsync(f.Instance,PersonalCommand(body,"second-resubmit",c.InstanceVersion)));
        await Task.Delay(100);Assert.False(loser.IsCompleted);release.SetResult();var result=await winner;
        Assert.Equal("ALREADY_RESUBMITTED",Assert.IsType<ScopeAuthorizationExceptionV1>(await loser).Message);
        Assert.Equal(2,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.execution_cycles WHERE root_id={f.Instance}").SingleAsync());
        Assert.Equal(ApprovalCanonicalV2.Bytes(result),ApprovalCanonicalV2.Bytes(await f.Owner.OriginalAsync("SFS_RETURNED_RESUBMIT",f.Instance.ToString("D"),"first-resubmit")));
    }
}
