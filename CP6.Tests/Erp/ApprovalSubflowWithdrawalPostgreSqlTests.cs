using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task App_subflow_real_iteration_owner_withdrawal_checks_tree_revision_and_cancels_atomically()
    {
        const string serial="""
            {"start":"s","nodes":[{"id":"s","type":"start"},{"id":"a","type":"approval","approverStrategy":"Role","approverRoleId":76},{"id":"b","type":"approval","approverStrategy":"Role","approverRoleId":76},{"id":"e","type":"end"}],"edges":[{"from":"s","to":"a"},{"from":"a","to":"b"},{"from":"b","to":"e"}]}
            """;
        var hook=new StartWriteHook();await using var h=await Seed(hook);
        var f=await IterationDecisionSetup(h,subflow:true,subflowSchema:serial);await GrantIterationWithdrawRelay(h,f);
        var workflow=IterationWithdrawals(h,f);var beforeVote=await workflow.ContextAsync(f.Submission);
        var members=await h.Db.Database.SqlQuery<Guid>($"SELECT instance_id AS \"Value\" FROM cp6_approval_runtime_v2.instances WHERE tenant_id={h.Tenant} AND root_instance_id={beforeVote.InstanceId}").ToArrayAsync();
        var unrelated=await h.Db.Wf_FlowInstances.AsNoTracking().Where(x=>!members.Contains(x.Id)).Select(x=>new{x.Id,x.Status}).OrderBy(x=>x.Id).ToArrayAsync();
        var tasksOwner=new ApprovalTaskDecisionOwnerV2(h.Db,ScopeFence(h),TaskAuthority(DecisionActor(h,f.Source.Iteration.Reviewer)),ToolApproval(h),
            new(ToolApproval(h),null,null,IterationReviews(DecisionActor(h,f.Source.Iteration.Reviewer),f.Source.Files)));
        var listed=await tasksOwner.IterationTasksAsync(f.Submission);Assert.Single(listed);Assert.NotEqual(beforeVote.InstanceId,listed[0].Task.InstanceId);
        Assert.Null((await IterationVote(h,f)).Result.DecisionId);
        var afterVote=await workflow.ContextAsync(f.Submission);Assert.NotEqual(beforeVote.SubmissionRowVersion,afterVote.SubmissionRowVersion);
        var owner=IterationReviews(h,f.Source.Files);var id=f.Source.Context.IterationId;var c=await owner.ApplicationAsync(id,f.Submission);
        var withdrawn=await owner.WithdrawAsync(id,f.Submission,"\""+c.ControlRowVersion+"\"",new(Guid.NewGuid(),"Rework the exact child review"));
        var bytes=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_iteration_review_control_v1.withdraw_outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        Assert.Equal(412,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>workflow.WithdrawAsync(f.Submission,"\""+beforeVote.SubmissionRowVersion+"\"",bytes))).Status);
        hook.Before=cmd=>{if(cmd.CommandText.Contains("INSERT INTO cp6_approval_decision_v2.outbox"))cmd.CommandText="SELECT 1";return Task.CompletedTask;};
        Assert.NotNull(await Record.ExceptionAsync(()=>workflow.WithdrawAsync(f.Submission,"\""+afterVote.SubmissionRowVersion+"\"",bytes)));
        Assert.All(await h.Db.Wf_FlowInstances.AsNoTracking().Where(x=>members.Contains(x.Id)).ToArrayAsync(),x=>Assert.Equal(0,x.Status));
        Assert.Single(await h.Db.Wf_FlowTasks.Where(x=>x.Status==0).ToArrayAsync());
        var child=members.Single(x=>x!=beforeVote.InstanceId);
        hook.Before=async cmd=>
        {
            if(!cmd.CommandText.Contains("INSERT INTO cp6_approval_decision_v2.decisions"))return;
            hook.Before=null;
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Wf_FlowInstance\" SET \"Status\"=0 WHERE \"Id\"={child}");
        };
        Assert.NotNull(await Record.ExceptionAsync(()=>workflow.WithdrawAsync(f.Submission,"\""+afterVote.SubmissionRowVersion+"\"",bytes)));
        Assert.All(await h.Db.Wf_FlowInstances.AsNoTracking().Where(x=>members.Contains(x.Id)).ToArrayAsync(),x=>Assert.Equal(0,x.Status));
        hook.Before=null;var result=await workflow.WithdrawAsync(f.Submission,"\""+afterVote.SubmissionRowVersion+"\"",bytes);Assert.Equal("WITHDRAWN",result.Ack.State);
        Assert.Equal(result,await workflow.OriginalAsync(withdrawn.TransportCommandId!.Value));
        Assert.All(await h.Db.Wf_FlowInstances.AsNoTracking().Where(x=>members.Contains(x.Id)).ToArrayAsync(),x=>Assert.Equal(3,x.Status));
        Assert.Equal(unrelated,await h.Db.Wf_FlowInstances.AsNoTracking().Where(x=>!members.Contains(x.Id)).Select(x=>new{x.Id,x.Status}).OrderBy(x=>x.Id).ToArrayAsync());
        Assert.False(await h.Db.Wf_FlowTasks.AnyAsync(x=>x.Status==0||x.Status==4));Assert.False(await h.Db.Wf_FlowTokens.AnyAsync(x=>x.Status==0));
        Assert.False(await h.Db.Wf_FlowFormTos.AnyAsync(x=>x.Status==0));
        var payload=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_approval_decision_v2.outbox WHERE tenant_id={h.Tenant} AND submission_id={f.Submission}").SingleAsync();
        Assert.Equal(beforeVote.InstanceId,ApprovalDecisionWireV2.Parse(payload).Value.InstanceId);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_decision_v2.votes WHERE tenant_id={h.Tenant} AND instance_id=ANY({members})").SingleAsync());
    }
}
