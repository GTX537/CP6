using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private NativeTaskBatchOwnerV2 NativeBatches(Harness h)=>new(h.Db,ScopeFence(h),PersonalForm(h),Decisions(h));
    [CurrentPermissionPostgresFact]
    public async Task NativeBatch_limit_counts_authorized_resources_and_never_hidden_tasks()
    {
        await using var h=await Seed();var f=await NativeFormSetup(h);await EngineeringAction(h,"oa-inbox","batch-transfer");
        var target=await GrantDelegate(h,"batch-limit-target");var context=await f.Owner.ContextAsync("personal");
        for(var index=0;index<500;index++)
            await f.Owner.SubmitDirectAsync("personal",PersonalCommand(PersonalDirect(context),"limit-"+index));
        var owner=NativeBatches(h);var filter=new NativeTransferBatchFilterV2(h.User,target,"visible limit",null,null,null);
        Assert.Equal("BATCH_LIMIT_EXCEEDED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.PreviewAsync(PersonalCommand(filter,"visible")))).Message);
        var policy=(await f.Owner.PolicyAsync("personal"))!;
        await f.Owner.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),true,true,[],policy.RetentionPolicyRef,30,true,"revoke all selected resources"));
        Assert.Empty((await owner.PreviewAsync(PersonalCommand(filter,"hidden"))).Items);
    }
    [CurrentPermissionPostgresFact]
    public async Task NativeBatch_same_instance_uses_own_prior_result_but_never_accepts_an_intervening_vote()
    {
        foreach(var intervene in new[]{false,true})
        {
            await using var h=await Seed();var f=await NativeFormSetup(h,true);await EngineeringAction(h,"oa-inbox","batch-transfer");
            var target=await GrantDelegate(h,"same-instance-target");var owner=NativeBatches(h);
            var preview=await owner.PreviewAsync(PersonalCommand(new NativeTransferBatchFilterV2(h.User,target,"same instance",null,null,null),"same-instance"));Assert.Equal(2,preview.Items.Length);
            var first=preview.Items[0];
            await f.Owner.AdministrativeTransferTaskAsync(first.TaskId,h.User,preview.BatchId,
                PersonalCommand(new NativeTransferInputV2(first.TaskId,target,preview.Filter.Reason,first.TaskVersion),first.ItemCommandId,first.InstanceVersion),default);
            if(intervene)
            {
                var to=PersonalForm(DecisionActor(h,target));var context=await to.TaskContextAsync(first.TaskId);
                var vote=new ApprovalTaskDecisionInputV2(Guid.NewGuid(),"approve","intervening independent decision",context.Task.TaskRowVersion,context.Task.InstanceRowVersion);
                await to.DecideTaskAsync(first.TaskId,vote,ifMatch:"\""+vote.TaskRowVersion+"\"");
            }
            var report=await owner.ExecuteAsync(new(preview.BatchId,preview.PreviewDigest));
            Assert.Equal("SUCCEEDED",report.Items[0].State);Assert.Equal(intervene?"REJECTED":"SUCCEEDED",report.Items[1].State);
            if(intervene)Assert.Equal("TASK_CONTEXT_CHANGED",report.Items[1].Error);
            Assert.Equal(intervene?h.User:target,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Id==preview.Items[1].TaskId)).AssigneeId);
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task NativeBatch_freezes_selection_reports_partial_results_and_does_not_include_later_tasks()
    {
        await using var h=await Seed();var f=await NativeFormSetup(h);await EngineeringAction(h,"oa-inbox","batch-transfer");
        var target=await GrantDelegate(h,"batch-target");var manager=await GrantDelegate(h,"batch-manager");
        var context=await f.Owner.ContextAsync("personal");
        var second=PersonalResult<PersonalFormSubmissionV2>(await f.Owner.SubmitDirectAsync("personal",PersonalCommand(PersonalDirect(context),"second")));
        var secondTask=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.InstanceId==second.FlowInstanceId);
        var owner=NativeBatches(DecisionActor(h,manager));
        var input=new NativeTransferBatchFilterV2(h.User,target,"handover two selected tasks","native-inbox",DateTimeOffset.UtcNow,[f.Task,secondTask.Id]);
        var raw=PersonalCommand(input,"preview-two");var preview=await owner.PreviewAsync(raw);Assert.Equal(2,preview.Items.Length);
        Assert.Equal(ApprovalCanonicalV2.Bytes(preview),ApprovalCanonicalV2.Bytes(await owner.OriginalPreviewAsync("preview-two")));
        Assert.Equal(ApprovalCanonicalV2.Bytes(preview),ApprovalCanonicalV2.Bytes(await owner.PreviewAsync(raw)));
        var later=PersonalResult<PersonalFormSubmissionV2>(await f.Owner.SubmitDirectAsync("personal",PersonalCommand(PersonalDirect(context),"later")));
        var vote=await f.Owner.TaskContextAsync(f.Task);var command=new ApprovalTaskDecisionInputV2(Guid.NewGuid(),"approve","completed after preview",vote.Task.TaskRowVersion,vote.Task.InstanceRowVersion);
        await f.Owner.DecideTaskAsync(f.Task,command,ifMatch:"\""+command.TaskRowVersion+"\"");
        var report=await owner.ExecuteAsync(new(preview.BatchId,preview.PreviewDigest));
        Assert.Equal("REJECTED",report.Items.Single(x=>x.TaskId==f.Task).State);Assert.Equal("TASK_NOT_PENDING",report.Items.Single(x=>x.TaskId==f.Task).Error);
        Assert.Equal("SUCCEEDED",report.Items.Single(x=>x.TaskId==secondTask.Id).State);
        Assert.Equal(h.User,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.InstanceId==later.FlowInstanceId)).AssigneeId);
        Assert.Equal(target,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Id==secondTask.Id)).AssigneeId);
        var audit=await h.Db.Wf_FlowFormTos.AsNoTracking().SingleAsync(x=>x.NativeTaskId==secondTask.Id&&x.Status==CP6.Core.Services.Wf.FlowFormToStatus.Transferred);
        Assert.Equal(manager,audit.ActualHandlerId);Assert.Equal(h.User,audit.ExpectedHandlerId);Assert.Null(audit.OnBehalfOfId);
        Assert.Equal(ApprovalCanonicalV2.Bytes(report),ApprovalCanonicalV2.Bytes(await owner.ReportAsync(preview.BatchId)));
        var retried=await owner.ExecuteAsync(new(preview.BatchId,preview.PreviewDigest));Assert.Equal(ApprovalCanonicalV2.Bytes(report),ApprovalCanonicalV2.Bytes(retried));
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.assignment_changes WHERE task_id={secondTask.Id}").SingleAsync());
        Assert.Equal("BATCH_PREVIEW_CHANGED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.ExecuteAsync(new(preview.BatchId,"different")))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task NativeBatch_lost_item_commit_recovers_original_and_target_revocation_never_becomes_success()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var f=await NativeFormSetup(h);await EngineeringAction(h,"oa-inbox","batch-transfer");
        var target=await GrantDelegate(h,"batch-lost-target");var owner=NativeBatches(h);
        var preview=await owner.PreviewAsync(PersonalCommand(new NativeTransferBatchFilterV2(h.User,target,"lost response",null,null,[f.Task]),"lost-preview"));
        fault.Armed=true;var unknown=await owner.ExecuteAsync(new(preview.BatchId,preview.PreviewDigest));Assert.Equal("UNKNOWN",Assert.Single(unknown.Items).State);
        Assert.Equal("SUCCEEDED",Assert.Single((await owner.ReportAsync(preview.BatchId)).Items).State);
        Assert.Equal("SUCCEEDED",Assert.Single((await owner.ExecuteAsync(new(preview.BatchId,preview.PreviewDigest))).Items).State);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.assignment_changes WHERE task_id={f.Task}").SingleAsync());
        var context=await f.Owner.ContextAsync("personal");var later=PersonalResult<PersonalFormSubmissionV2>(await f.Owner.SubmitDirectAsync("personal",PersonalCommand(PersonalDirect(context),"next")));
        var nextTask=(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.InstanceId==later.FlowInstanceId)).Id;
        var rejected=await owner.PreviewAsync(PersonalCommand(new NativeTransferBatchFilterV2(h.User,target,"disabled target",null,null,[nextTask]),"disabled-preview"));
        await h.Db.Sys_Users.Where(u=>u.Id==target).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.Enable,false));
        Assert.Equal("TARGET_INELIGIBLE",Assert.Single((await owner.ExecuteAsync(new(rejected.BatchId,rejected.PreviewDigest))).Items).Error);
        Assert.Equal(h.User,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Id==nextTask)).AssigneeId);
    }
    [CurrentPermissionPostgresFact]
    public async Task NativeBatch_management_permission_resource_authority_and_500_limit_are_enforced()
    {
        await using var h=await Seed();var f=await NativeFormSetup(h);var target=await GrantDelegate(h);var owner=NativeBatches(h);
        var filter=new NativeTransferBatchFilterV2(h.User,target,"management",null,null,[f.Task]);
        Assert.Equal(403,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.PreviewAsync(PersonalCommand(filter)))).Status);
        await EngineeringAction(h,"oa-inbox","batch-transfer");
        Assert.Equal("BATCH_FILTER_INVALID",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.PreviewAsync(PersonalCommand(filter with{ExplicitTaskIds=Enumerable.Range(0,501).Select(_=>Guid.NewGuid()).ToArray()})))).Message);
        var preview=await owner.PreviewAsync(PersonalCommand(filter,"resource-preview"));Assert.Single(preview.Items);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_native_tasks_v2.transfer_batches DISABLE TRIGGER transfer_batch_immutable");
        try
        {
            Assert.Equal("BATCH_STORE_UNAVAILABLE",Assert.Single((await owner.ExecuteAsync(new(preview.BatchId,preview.PreviewDigest))).Items).Error);
            Assert.NotNull(await owner.OriginalPreviewAsync("resource-preview"));
            Assert.Equal(h.User,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Id==f.Task)).AssigneeId);
        }
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_native_tasks_v2.transfer_batches ENABLE TRIGGER transfer_batch_immutable");}
        var p=(await f.Owner.PolicyAsync("personal"))!;await f.Owner.ConfigureAsync("personal",new(Guid.NewGuid(),p.Version.ToString("D"),true,true,[],p.RetentionPolicyRef,30,true,"revoke resource"));
        Assert.Empty((await owner.PreviewAsync(PersonalCommand(filter,"new-preview"))).Items);
        Assert.Equal(404,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.OriginalPreviewAsync("resource-preview"))).Status);
        Assert.Equal("REJECTED",Assert.Single((await owner.ExecuteAsync(new(preview.BatchId,preview.PreviewDigest))).Items).State);
        Assert.Equal(h.User,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Id==f.Task)).AssigneeId);
    }
    [CurrentPermissionPostgresFact]
    public async Task NativeBatch_app_manager_is_not_the_assignee_and_never_supplies_a_human_vote()
    {
        await using var h=await Seed();var(_,first,second)=await DecisionSetup(h);await EngineeringAction(h,"oa-inbox","batch-transfer");
        var target=Guid.NewGuid();h.Db.Sys_Users.Add(new(){Id=target,UserName="batch-app-target",Password="fixture",RoleId=73});await h.Db.SaveChangesAsync();await TaskPermission(h,target);h.Db.ChangeTracker.Clear();
        var task=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.AssigneeId==first);
        var manager=DecisionActor(h,second);var owner=NativeBatches(manager);
        Assert.Equal(404,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>Decisions(manager).ContextAsync(task.Id))).Status);
        var preview=await owner.PreviewAsync(PersonalCommand(new NativeTransferBatchFilterV2(first,target,"manager reassignment",null,null,[task.Id]),"app-batch"));
        Assert.Single(preview.Items);var report=await owner.ExecuteAsync(new(preview.BatchId,preview.PreviewDigest));Assert.Equal("SUCCEEDED",Assert.Single(report.Items).State);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_decision_v2.votes WHERE tenant_id={h.Tenant}").SingleAsync());
        var to=DecisionActor(h,target);Assert.Null((await Vote(to,await DecisionInput(to))).Result.DecisionId);
        Assert.NotNull((await Vote(manager,await DecisionInput(manager))).Result.DecisionId);
        Assert.Equal(ApprovalCanonicalV2.Bytes(report),ApprovalCanonicalV2.Bytes(await owner.ReportAsync(preview.BatchId)));
    }
}
