using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task NativeApp_operation_original_survives_missing_write_guard_while_new_writes_fail_fast()
    {
        await using var h=await Seed();var(_,first,second)=await DecisionSetup(h);await NativeOperationPermissions(h);
        var actor=DecisionActor(h,first);var owner=Decisions(actor);var c=await DecisionInput(actor);var view=await owner.ContextAsync(c.Task);
        var raw=PersonalCommand(new NativeTransferInputV2(c.Task,second,"original must remain readable",view.TaskRowVersion),"guard-original",view.InstanceRowVersion);
        var result=await owner.TransferTaskAsync(c.Task,raw);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_native_tasks_v2.commands DISABLE TRIGGER operation_complete");
        try
        {
            Assert.Equal(ApprovalCanonicalV2.Bytes(result),ApprovalCanonicalV2.Bytes(await owner.OriginalOperationAsync("OA_TASK_TRANSFER",c.Task,"guard-original")));
            Assert.Equal(ApprovalCanonicalV2.Bytes(result),ApprovalCanonicalV2.Bytes(await owner.TransferTaskAsync(c.Task,raw)));
            var newer=PersonalCommand(new NativeTransferInputV2(c.Task,first,"new write blocked",view.TaskRowVersion),"guard-new",view.InstanceRowVersion);
            Assert.Equal("APP_DECISION_STORE_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>Decisions(DecisionActor(h,second)).TransferTaskAsync(c.Task,newer))).Message);
        }
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_native_tasks_v2.commands ENABLE TRIGGER operation_complete");}
        Assert.Equal(second,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Id==c.Task)).AssigneeId);
    }
    [CurrentPermissionPostgresFact]
    public async Task NativeApp_transfer_preserves_created_assignment_and_actual_two_votes_reach_terminal()
    {
        await using var h=await Seed();var(_,first,second)=await DecisionSetup(h);
        await NativeOperationPermissions(h);var target=Guid.NewGuid();
        h.Db.Sys_Users.Add(new(){Id=target,UserName="transferred-reviewer",Password="fixture",RoleId=73});
        await h.Db.SaveChangesAsync();await TaskPermission(h,target);h.Db.ChangeTracker.Clear();
        var actor=DecisionActor(h,first);var owner=Decisions(actor);var input=await DecisionInput(actor);var before=await owner.ContextAsync(input.Task);
        var original=await ApprovalTaskAssignmentsV2.Read(h.Db,ScopeEnvironment,input.Task,default);
        var raw=PersonalCommand(new NativeTransferInputV2(input.Task,target,"change current responsible reviewer",before.TaskRowVersion),"app-transfer",before.InstanceRowVersion);
        var result=await owner.TransferTaskAsync(input.Task,raw);
        var current=await ApprovalTaskAssignmentsV2.Read(h.Db,ScopeEnvironment,input.Task,default);
        Assert.Equal(first,current.CreatedPrincipalId);Assert.Equal(target,current.PrincipalId);Assert.Equal(original.TokenId,current.TokenId);
        Assert.Equal(ApprovalCanonicalV2.Bytes(result),ApprovalCanonicalV2.Bytes(await owner.TransferTaskAsync(input.Task,raw)));
        Assert.Equal(first,await h.Db.Database.SqlQuery<Guid>($"SELECT principal_id AS \"Value\" FROM cp6_approval_tasks_v2.assignments WHERE task_id={input.Task}").SingleAsync());
        var transferred=DecisionActor(h,target);Assert.Null((await Vote(transferred,await DecisionInput(transferred))).Result.DecisionId);
        var remaining=DecisionActor(h,second);Assert.NotNull((await Vote(remaining,await DecisionInput(remaining))).Result.DecisionId);
        Assert.Equal(2,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_decision_v2.votes WHERE tenant_id={h.Tenant}").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task NativeApp_before_addsign_restores_parent_and_does_not_substitute_a_vote()
    {
        await using var h=await Seed();var(_,first,second)=await DecisionSetup(h);await NativeOperationPermissions(h);
        var target=Guid.NewGuid();h.Db.Sys_Users.Add(new(){Id=target,UserName="added-reviewer",Password="fixture",RoleId=73});
        await h.Db.SaveChangesAsync();await TaskPermission(h,target);h.Db.ChangeTracker.Clear();
        var actor=DecisionActor(h,first);var owner=Decisions(actor);var input=await DecisionInput(actor);var context=await owner.ContextAsync(input.Task);
        await owner.AddSignTaskAsync(input.Task,PersonalCommand(new NativeAddSignInputV2(input.Task,target,"before","extra review",context.TaskRowVersion),"app-before",context.InstanceRowVersion));
        Assert.Equal(FlowTaskStatus.Suspended,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Id==input.Task)).Status);
        var added=DecisionActor(h,target);Assert.Null((await Vote(added,await DecisionInput(added))).Result.DecisionId);
        Assert.Equal(FlowTaskStatus.Pending,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Id==input.Task)).Status);
        Assert.Null((await Vote(actor,await DecisionInput(actor))).Result.DecisionId);
        var last=DecisionActor(h,second);Assert.NotNull((await Vote(last,await DecisionInput(last))).Result.DecisionId);
    }

    [CurrentPermissionPostgresFact]
    public async Task NativeApp_pending_recovery_creates_human_tasks_and_original_resolution_then_terminal_evidence()
    {
        await using var h=await Seed();
        var schema=StartSchema.Replace("\"approverRoleId\":73","\"approverRoleId\":999,\"approverRecoveryPolicy\":{\"mode\":\"MANUAL_ASSIGNMENT\",\"eligibleRoleIds\":[73]}");
        var(_,first,second)=await DecisionSetup(h,schema);await NativeOperationPermissions(h);
        var instance=await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync();Assert.Equal(FlowInstanceStatus.Suspended,instance.Status);Assert.Empty(await h.Db.Wf_FlowTasks.ToArrayAsync());
        var owner=Decisions(h);var context=Assert.Single(await owner.PendingResolutionsAsync(instance.Id));Assert.True(context.CanAssign);
        var input=new ResolveApproverInputV2(context.InstanceVersion,context.ExecutionCycleId,context.NodeId,context.TokenId,context.StageIndex,context.StageRound,
            new[]{first,second}.Order().ToArray(),context.RoutePolicyRef,"recover exact frozen slot");
        var raw=PersonalCommand(input,"app-resolve",context.ResolutionVersion);var result=await owner.ResolveApproverAsync(context.ResolutionId,raw);
        Assert.Equal(ApprovalCanonicalV2.Bytes(result),ApprovalCanonicalV2.Bytes(await owner.ResolveApproverAsync(context.ResolutionId,raw)));
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_decision_v2.votes WHERE tenant_id={h.Tenant}").SingleAsync());
        var a=DecisionActor(h,first);Assert.Null((await Vote(a,await DecisionInput(a))).Result.DecisionId);
        var b=DecisionActor(h,second);Assert.NotNull((await Vote(b,await DecisionInput(b))).Result.DecisionId);
        Assert.Equal("HUMAN",ApprovalDecisionWireV2.Parse(await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_approval_decision_v2.outbox WHERE tenant_id={h.Tenant}").SingleAsync()).Value.DecisionMode);
    }

    [CurrentPermissionPostgresFact]
    public async Task NativeApp_return_to_starter_preserves_frozen_subject_and_original_owner_can_withdraw()
    {
        await using var h=await Seed();var f=await IterationDecisionSetup(h);await GrantIterationWithdrawRelay(h,f);await NativeOperationPermissions(h);
        var reviewer=DecisionActor(h,f.Source.Iteration.Reviewer);
        var tasks=new ApprovalTaskDecisionOwnerV2(h.Db,ScopeFence(h),TaskAuthority(reviewer),ToolApproval(h),
            new(ToolApproval(h),null,null,IterationReviews(reviewer,f.Source.Files)));
        var task=Assert.Single(await tasks.IterationTasksAsync(f.Submission));
        var before=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_approval_intake_v2.submissions WHERE id={f.Submission} AND tenant_id={h.Tenant}").SingleAsync();
        var preview=await tasks.SendBackPreviewAsync(task.Task.TaskId,new("STARTER",null));
        var result=await tasks.SendBackTaskAsync(task.Task.TaskId,SendBackCommand(preview,"app-return"));
        Assert.Equal(FlowInstanceStatus.Draft,(await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync(i=>i.Id==preview.InstanceId)).Status);
        Assert.Equal(before,await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_approval_intake_v2.submissions WHERE id={f.Submission} AND tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(ApprovalCanonicalV2.Bytes(result),ApprovalCanonicalV2.Bytes(await tasks.OriginalOperationAsync("OA_TASK_SENDBACK",task.Task.TaskId,"app-return")));
        var workflow=IterationWithdrawals(h,f);var context=await workflow.ContextAsync(f.Submission);
        var owner=IterationReviews(h,f.Source.Files);var id=f.Source.Context.IterationId;var application=await owner.ApplicationAsync(id,f.Submission);
        var withdrawn=await owner.WithdrawAsync(id,f.Submission,"\""+application.ControlRowVersion+"\"",new(Guid.NewGuid(),"Owner withdraws returned frozen revision"));
        var raw=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_iteration_review_control_v1.withdraw_outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        Assert.Equal("WITHDRAWN",(await workflow.WithdrawAsync(f.Submission,"\""+context.SubmissionRowVersion+"\"",raw)).Ack.State);
        Assert.Equal("WITHDRAWN",(await workflow.OriginalAsync(withdrawn.TransportCommandId!.Value))!.Ack.State);
    }
}
