using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task TaskRoundCard_actual_two_actor_all_and_any_new_rounds_ignore_old_votes_and_original_replays()
    {
        foreach(var mode in new[]{"all","any"})
        {
            await using var h=await Seed();var second=await GrantDelegate(h,"round-second-"+mode);var graph=LinearNative(h.User);
            graph.Nodes.Single(n=>n.Id=="a").Stages=[new(){ApproverStrategy="Role",ApproverRoleId=72,Countersign=mode},new(){ApproverStrategy="Specified",ApproverUserId=h.User}];
            var f=await NativeTaskGraph(h,graph);var old=await h.Db.Wf_FlowTasks.AsNoTracking().Where(t=>t.InstanceId==f.Instance).ToArrayAsync();Assert.Equal(2,old.Length);
            var first=old.Single(t=>t.AssigneeId==h.User);var peer=old.Single(t=>t.AssigneeId==second);
            var oldVote=NativeVote(await f.Owner.TaskContextAsync(first.Id),"first round actual vote");var original=await DecideNative(f.Owner,first.Id,oldVote);
            var other=PersonalForm(h with{User=second});
            if(mode=="all") {TaskActor(h,second);await NativeApprove(other,peer.Id);TaskActor(h,h.User);}
            var oldNext=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.InstanceId==f.Instance&&t.Status==FlowTaskStatus.Pending);Assert.Equal(1,oldNext.StageIndex);
            var preview=await f.Owner.SendBackPreviewAsync(oldNext.Id,new("PREV_STAGE",null));await f.Owner.SendBackAsync(oldNext.Id,SendBackCommand(preview,"return-"+mode));
            var renewed=await h.Db.Wf_FlowTasks.AsNoTracking().Where(t=>t.InstanceId==f.Instance&&t.Status==FlowTaskStatus.Pending).ToArrayAsync();Assert.Equal(2,renewed.Length);
            Assert.All(renewed,t=>{Assert.Equal(first.TokenId,t.TokenId);Assert.Equal(first.NodeId,t.NodeId);Assert.Equal(0,t.StageIndex);Assert.Equal(first.StageRound+1,t.StageRound);});
            var replay=await DecideNative(f.Owner,first.Id,oldVote);Assert.Equal(original.VoteId,replay.VoteId);
            Assert.Equal(renewed.Select(t=>t.Id).Order().ToArray(),await h.Db.Wf_FlowTasks.AsNoTracking().Where(t=>t.InstanceId==f.Instance&&t.Status==FlowTaskStatus.Pending).OrderBy(t=>t.Id).Select(t=>t.Id).ToArrayAsync());
            TaskActor(h,second);var secondTask=renewed.Single(t=>t.AssigneeId==second);var current=await other.TaskContextAsync(secondTask.Id);
            // `any`: an old approval must not rescue this new rejection. `all`: old approvals
            // must not supply the missing current principal's vote after this new approval.
            var result=await DecideNative(other,secondTask.Id,NativeVote(current,"current round actual vote",mode=="any"?"reject":"approve"));Assert.Equal(FlowInstanceStatus.Running,result.Task!.InstanceStatus);
            var waiting=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.InstanceId==f.Instance&&t.Status==FlowTaskStatus.Pending);Assert.Equal(h.User,waiting.AssigneeId);Assert.Equal(0,waiting.StageIndex);Assert.Equal(first.StageRound+1,waiting.StageRound);
            Assert.False(await h.Db.Wf_FlowTasks.AnyAsync(t=>t.InstanceId==f.Instance&&t.NodeId=="b"));
            TaskActor(h,h.User);await NativeApprove(f.Owner,waiting.Id);var next=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.InstanceId==f.Instance&&t.Status==FlowTaskStatus.Pending);Assert.Equal("a",next.NodeId);Assert.Equal(1,next.StageIndex);Assert.Equal(oldNext.StageRound+1,next.StageRound);
            await NativeApprove(f.Owner,next.Id);var last=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.InstanceId==f.Instance&&t.Status==FlowTaskStatus.Pending);Assert.Equal("b",last.NodeId);await NativeApprove(f.Owner,last.Id);
            Assert.Equal(FlowInstanceStatus.Approved,await h.Db.Wf_FlowInstances.Where(i=>i.Id==f.Instance).Select(i=>i.Status).SingleAsync());Assert.Equal(FlowTaskStatus.Approved,await h.Db.Wf_FlowTasks.Where(t=>t.Id==first.Id).Select(t=>t.Status).SingleAsync());
            Assert.Equal(mode=="all"?FlowTaskStatus.Approved:FlowTaskStatus.Cancelled,await h.Db.Wf_FlowTasks.Where(t=>t.Id==peer.Id).Select(t=>t.Status).SingleAsync());
        }
    }
}
