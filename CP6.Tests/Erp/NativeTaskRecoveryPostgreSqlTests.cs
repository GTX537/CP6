using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private async Task<(PersonalFormOwnerV2 Owner,FlowAuthoringOwnerV2 Flow,Guid Instance)> NativeTaskGraph(Harness h,FlowSchema graph,string formSchema=FormAuthoringSchema)
    {
        var(owner,_,flow)=await PreparePersonal(h,formSchema);await NativeOperationPermissions(h);
        var saved=await flow.SaveAsync("operations",DependentCommand("personal",schema:JsonSerializer.Serialize(graph,PersonalFormOwnerV2.Json)));
        var published=(await flow.PublishAsync("operations",PublicationCommand(await Preview(flow,"operations",saved)))).Result!.Publication!;
        await flow.SetActivationAsync("operations",ActivationCommand((await flow.ActivationPreviewAsync("operations")).Current,published.VersionId));
        var started=PersonalResult<PersonalFormSubmissionV2>(await owner.SubmitDirectAsync("personal",PersonalCommand(PersonalDirect(await owner.ContextAsync("personal")),"start")));
        return(owner,flow,started.FlowInstanceId!.Value);
    }
    private static FlowSchema LinearNative(Guid user)=>new(){Start="s",Nodes=[new(){Id="s",Type="start"},new(){Id="a",ApproverStrategy="Specified",ApproverUserId=user},new(){Id="b",ApproverStrategy="Specified",ApproverUserId=user},new(){Id="e",Type="end"}],Edges=[new(){From="s",To="a"},new(){From="a",To="b"},new(){From="b",To="e"}]};
    private static async Task NativeApprove(PersonalFormOwnerV2 owner,Guid task)
    {
        var c=await owner.TaskContextAsync(task);var input=NativeVote(c,"actual task vote","approve");
        await DecideNative(owner,task,input);
    }
    private static byte[] ResolveCommand(PendingResolutionContextV2 c,Guid target,string id)=>PersonalCommand(new ResolveApproverInputV2(c.InstanceVersion,c.ExecutionCycleId,c.NodeId,c.TokenId,c.StageIndex,c.StageRound,[target],c.RoutePolicyRef,"manual assignment under published policy"),id,c.ResolutionVersion);

    [CurrentPermissionPostgresFact]
    public async Task NativeTask_pending_resolution_requires_pinned_manual_policy_and_keeps_each_parallel_slot()
    {
        await using var h=await Seed();var graph=LinearNative(Guid.NewGuid());
        var policy=new ApproverRecoveryPolicyV2("MANUAL_ASSIGNMENT",[72]);graph.Nodes.Single(n=>n.Id=="a").ApproverRecoveryPolicy=policy;graph.Nodes.Single(n=>n.Id=="b").ApproverRecoveryPolicy=policy;
        graph.Nodes.AddRange([new(){Id="split",Type="parallelSplit"},new(){Id="join",Type="parallelJoin"}]);
        graph.Edges=[new(){From="s",To="split"},new(){From="split",To="a"},new(){From="split",To="b"},new(){From="a",To="join"},new(){From="b",To="join"},new(){From="join",To="e"}];
        var f=await NativeTaskGraph(h,graph);var target=await GrantDelegate(h);var slots=await f.Owner.PendingResolutionsAsync(f.Instance);
        Assert.Equal(2,slots.Length);Assert.All(slots,s=>Assert.Equal("PENDING",s.State));Assert.All(slots,s=>Assert.Equal(target,Assert.Single(s.CandidatePrincipalIds)));
        Assert.Empty(await h.Db.Wf_FlowTasks.ToArrayAsync());Assert.Equal(FlowInstanceStatus.Suspended,(await h.Db.Wf_FlowInstances.SingleAsync()).Status);
        var first=slots[0];Assert.Equal("TARGET_INELIGIBLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.ResolveApproverAsync(first.ResolutionId,ResolveCommand(first,h.User,"self")))).Message);
        var raw=ResolveCommand(first,target,"resolve-1");var resolved=await f.Owner.ResolveApproverAsync(first.ResolutionId,raw);
        Assert.Equal(FlowInstanceStatus.Suspended,resolved.Result!.Value.GetProperty("instanceStatus").GetInt32());
        Assert.Equal("RESOLUTION_CONTEXT_CHANGED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.ResolveApproverAsync(slots[1].ResolutionId,ResolveCommand(slots[1],target,"stale-slot")))).Message);
        var second=await f.Owner.ResolutionContextAsync(slots[1].ResolutionId);var completed=await f.Owner.ResolveApproverAsync(second.ResolutionId,ResolveCommand(second,target,"resolve-2"));
        Assert.Equal(FlowInstanceStatus.Running,completed.Result!.Value.GetProperty("instanceStatus").GetInt32());
        Assert.Equal(JsonSerializer.Serialize(resolved),JsonSerializer.Serialize(await f.Owner.ResolveApproverAsync(first.ResolutionId,raw)));
        Assert.Equal(2,await h.Db.Wf_FlowTasks.CountAsync());Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.votes v JOIN cp6_personal_form_v2.assignments a ON a.task_id=v.task_id WHERE a.instance_id={f.Instance}").SingleAsync());
        TaskActor(h,target);var owner=PersonalForm(h with{User=target});foreach(var task in await h.Db.Wf_FlowTasks.AsNoTracking().Select(t=>t.Id).ToArrayAsync())await NativeApprove(owner,task);
        Assert.Equal(FlowInstanceStatus.Approved,(await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync()).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task NativeTask_unresolved_without_manual_policy_is_explicit_and_admin_cannot_assign()
    {
        await using var h=await Seed();var f=await NativeTaskGraph(h,LinearNative(Guid.NewGuid()));var target=await GrantDelegate(h);
        var c=Assert.Single(await f.Owner.PendingResolutionsAsync(f.Instance));Assert.False(c.CanAssign);Assert.Contains("MANUAL_ASSIGNMENT_NOT_ALLOWED",c.BlockingReasons);
        Assert.Equal("MANUAL_ASSIGNMENT_NOT_ALLOWED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.ResolveApproverAsync(c.ResolutionId,ResolveCommand(c,target,"forbidden")))).Message);
        Assert.Empty(await h.Db.Wf_FlowTasks.ToArrayAsync());Assert.Equal("PENDING",(await f.Owner.ResolutionContextAsync(c.ResolutionId)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task NativeTask_node_return_preserves_old_votes_and_prevstage_reenters_a_new_round()
    {
        await using var h=await Seed();var f=await NativeTaskGraph(h,LinearNative(h.User));var first=(await h.Db.Wf_FlowTasks.SingleAsync()).Id;
        await NativeApprove(f.Owner,first);var b=(await h.Db.Wf_FlowTasks.SingleAsync(t=>t.Status==FlowTaskStatus.Pending)).Id;
        var preview=await f.Owner.SendBackPreviewAsync(b,new("NODE","a"));await f.Owner.SendBackAsync(b,SendBackCommand(preview));
        Assert.Equal(FlowTaskStatus.Approved,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Id==first)).Status);
        var again=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Status==FlowTaskStatus.Pending);Assert.NotEqual(first,again.Id);
        await NativeApprove(f.Owner,again.Id);await NativeApprove(f.Owner,(await h.Db.Wf_FlowTasks.SingleAsync(t=>t.Status==FlowTaskStatus.Pending)).Id);
        Assert.Equal(FlowInstanceStatus.Approved,(await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync()).Status);

        await using var staged=await Seed();var graph=LinearNative(staged.User);graph.Nodes.Single(n=>n.Id=="a").Stages=[new(){ApproverStrategy="Specified",ApproverUserId=staged.User},new(){ApproverStrategy="Specified",ApproverUserId=staged.User}];
        var s=await NativeTaskGraph(staged,graph);var stage0=await staged.Db.Wf_FlowTasks.SingleAsync();
        Assert.Equal("E-WF-012",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>s.Owner.SendBackPreviewAsync(stage0.Id,new("PREV_STAGE",null)))).Message);
        await NativeApprove(s.Owner,stage0.Id);var stage1=await staged.Db.Wf_FlowTasks.SingleAsync(t=>t.Status==FlowTaskStatus.Pending);
        await s.Owner.SendBackAsync(stage1.Id,SendBackCommand(await s.Owner.SendBackPreviewAsync(stage1.Id,new("PREV_STAGE",null))));
        var renewed=await staged.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Status==FlowTaskStatus.Pending);
        Assert.Equal(0,renewed.StageIndex);Assert.Equal(stage0.StageRound+1,renewed.StageRound);Assert.Equal(stage0.TokenId,renewed.TokenId);
        Assert.Equal(FlowTaskStatus.Cancelled,(await staged.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Id==stage1.Id)).Status);
    }
}
