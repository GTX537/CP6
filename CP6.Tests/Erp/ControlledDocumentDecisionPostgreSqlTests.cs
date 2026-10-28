using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Plm;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed record DocumentDecisionFixture(ContentStore Files,Guid Document,Guid Submission,Guid First,Guid Second);
    private async Task<DocumentDecisionFixture> DocumentDecisionSetup(Harness h,bool receive=true)
    {
        var (binding,flows,initialVersion)=await PrepareBinding(h);
        var f=await DocumentReviewSetup(h,false);var first=Guid.NewGuid();var second=Guid.NewGuid();
        h.Db.Sys_Roles.AddRange(new(){RoleId=73,RoleName="Workflow assignment"},new(){RoleId=74,RoleName="Document professional qualification"});
        h.Db.Sys_Users.AddRange(new(){Id=first,UserName="document-reviewer-one",Password="fixture",RoleId=73},
            new(){Id=second,UserName="document-reviewer-two",Password="fixture",RoleId=73});
        h.Db.Sys_UserRoles.AddRange(new(){UserId=first,RoleId=74},new(){UserId=second,RoleId=74});
        await h.Db.SaveChangesAsync();await TaskPermission(h,first);await TaskPermission(h,second);await EngineeringAction(h,"oa-inbox","query");
        var head=await flows.ReadHeadAsync("binding-flow");var draft=await flows.CreateNextDraftAsync("binding-flow",NextDraftCommand(head.Version,initialVersion));
        var edit=await flows.SaveAsync("binding-flow",AuthoringCommand("document-review-flow",draft.ResourceVersion,"document review",StartSchema));
        var publication=await flows.PublishAsync("binding-flow",PublicationCommand(await Preview(flows,"binding-flow",edit),"document-review-publish"));
        var version=publication.Result!.Publication!.VersionId;var principal=Guid.NewGuid();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_approval_binding_v2.registrations(tenant_id,environment_id,biz_type,subject_kind,owner,revision,snapshot_schema,snapshot_fields,enabled,service_principal,audience,fixed_route,evidence_mode)
            VALUES({h.Tenant},{ScopeEnvironment},'PLM_DOCUMENT_REVIEW','DOCUMENT_REVISION','Main.Plm.ControlledDocument.v1',{Guid.NewGuid()},
              'Main.Plm.DocumentReview.snapshot/1','[]',true,{principal},'CP6.Services','/internal/plm/document-review-results/v1','DEMO')
            """);
        var at=BindingTime();await binding.SaveAsync(null,null,BindingBytes(BindingInput(version,at) with{BizType="PLM_DOCUMENT_REVIEW",SubjectKind="DOCUMENT_REVISION",DetailRoute="/plm/controlled-document/{bizId}"}));
        var context=await f.Owner.ContextAsync(f.Id);var policy=DocumentPolicy() with{
            Requirements=[new("TECHNICAL_CONTENT","Original, derivations and authority language",74,2)],SeparateAuthorAndReviewer=true};
        await f.Owner.PolicyAsync(f.Id,context.Etag,policy);context=await f.Owner.ContextAsync(f.Id);
        var submission=await f.Owner.SubmitAsync(f.Id,context.Etag,DocumentSubmit(context));
        var payload=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_document_review_v1.outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        var service=new IntakeAuthority(new(ScopeEnvironment,h.Tenant,principal,"fixture-service","CP6.Services","test-registration",true,"DEMO"));
        var intake=new ApprovalIntakeOwnerV2(h.Db,ScopeFence(h),service,ToolApproval(h),flows,new(ToolApproval(h),f.Owner)){ReceiveClock=()=>at.AddSeconds(1)};
        if(receive){await intake.SubmitAsync(payload);await Starter(h).RunAsync(h.Tenant);}h.Db.ChangeTracker.Clear();
        return new(f.Files,f.Id,submission.Submission!.SubmissionId,first,second);
    }
    private static ApprovalTaskDecisionOwnerV2 DocumentDecisions(Harness h,ContentStore files)
        =>new(h.Db,ScopeFence(h),TaskAuthority(h),ToolApproval(h),new(ToolApproval(h),DocumentReviews(h,files)));
    private static async Task<(Guid Task,ApprovalTaskDecisionInputV2 Input)> DocumentDecisionInput(Harness h,ContentStore files,string decision="approve")
    {
        var task=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(x=>x.AssigneeId==h.User&&x.Status==0);
        var context=await DocumentDecisions(h,files).ContextAsync(task.Id);
        return(task.Id,new(Guid.NewGuid(),decision,"Reviewed exact frozen source, differences, external requirements and authority language",context.TaskRowVersion,context.InstanceRowVersion));
    }
    private static Task<ApprovalTaskDecisionResultV2> DocumentVote(Harness h,ContentStore files,(Guid Task,ApprovalTaskDecisionInputV2 Input) command)
        =>DocumentDecisions(h,files).DecideAsync(command.Task,command.Input,null,"\""+command.Input.TaskRowVersion+"\"");
    [CurrentPermissionPostgresFact]
    public async Task DocumentReview_native_votes_preserve_historical_qualification_and_emit_exact_document_decision()
    {
        await using var h=await Seed();var f=await DocumentDecisionSetup(h);
        var actor1=DecisionActor(h,f.First);var first=await DocumentDecisionInput(actor1,f.Files);
        var one=await DocumentVote(actor1,f.Files,first);Assert.Null(one.Result.DecisionId);
        await h.Db.Sys_UserRoles.Where(x=>x.UserId==f.First&&x.RoleId==74).ExecuteDeleteAsync();
        var actor2=DecisionActor(h,f.Second);var second=await DocumentDecisionInput(actor2,f.Files);
        var two=await DocumentVote(actor2,f.Files,second);Assert.NotNull(two.Result.DecisionId);
        var payload=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_approval_decision_v2.outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        var decision=ApprovalDecisionWireV2.Parse(payload).Value;
        Assert.Equal("Main.Plm.ControlledDocument.v1",decision.ConsumerOwner);Assert.Equal(f.Submission,decision.SubmissionId);
        Assert.Equal("APPROVED",decision.Decision);Assert.Contains(decision.SatisfiedRequirementIds,x=>x.StartsWith("document.requirement/",StringComparison.Ordinal));
        Assert.Equal(two,await DocumentVote(actor2,f.Files,second));
        Assert.Equal(one,await DocumentDecisions(actor1,f.Files).OriginalAsync(first.Input.CommandId));
        // Workflow's terminal decision is not yet the document Owner's applied approval.
        Assert.False((await DocumentReviews(h,f.Files).ContextAsync(f.Document)).Submissions.Single().IsApproved);
    }
    [CurrentPermissionPostgresFact]
    public async Task DocumentReview_professional_role_revocation_and_missing_comment_leave_tasks_and_vote_log_unchanged()
    {
        await using var h=await Seed();var f=await DocumentDecisionSetup(h);var actor=DecisionActor(h,f.First);
        var command=await DocumentDecisionInput(actor,f.Files);
        await h.Db.Sys_UserRoles.Where(x=>x.UserId==f.First&&x.RoleId==74).ExecuteDeleteAsync();
        Assert.Equal("DOCUMENT_PROFESSIONAL_ROLE_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>DocumentVote(actor,f.Files,command))).Message);
        Assert.Equal(0,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(x=>x.Id==command.Task)).Status);
        h.Db.Sys_UserRoles.Add(new(){UserId=f.First,RoleId=74});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("DOCUMENT_REVIEW_COMMENT_REQUIRED",(await Assert.ThrowsAsync<CP6.Core.Services.Sys.ScopeAuthorizationExceptionV1>(()=>DocumentVote(actor,f.Files,(command.Task,command.Input with{Comment=""})))).Message);
        Assert.Empty(await h.Db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM cp6_approval_decision_v2.votes WHERE tenant_id={h.Tenant}").ToArrayAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task DocumentReview_native_task_entry_and_closed_command_prevent_late_vote_without_losing_original_signature()
    {
        await using var h=await Seed();var f=await DocumentDecisionSetup(h);var actor=DecisionActor(h,f.First);
        var owner=DocumentDecisions(actor,f.Files);var tasks=await owner.DocumentTasksAsync(f.Document);
        var task=Assert.Single(tasks);Assert.Equal(f.Submission,task.SubmissionId);Assert.Equal("1",task.DocumentVersion);
        var inbox=new InboxService(actor.Db,Moq.Mock.Of<CP6.Core.Services.Wf.IFlowEngine>(Moq.MockBehavior.Strict),
            Moq.Mock.Of<IForecastService>(Moq.MockBehavior.Strict));
        var item=Assert.Single(await inbox.PendingAsync(actor.User));
        Assert.Equal($"/plm/controlled-document/{f.Document}?submission={f.Submission}",item.DetailRoute);
        var input=await DocumentDecisionInput(actor,f.Files);var closed=await owner.CloseAsync(input.Task,input.Input.CommandId);
        Assert.Equal("CLOSED",closed.State);
        Assert.Equal("COMMAND_CLOSED",(await Assert.ThrowsAsync<CP6.Core.Services.Sys.ScopeAuthorizationExceptionV1>(()=>DocumentVote(actor,f.Files,input))).Message);
        Assert.Equal(0,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(x=>x.Id==input.Task)).Status);
        var real=(Task:input.Task,Input:input.Input with{CommandId=Guid.NewGuid()});
        var result=await DocumentVote(actor,f.Files,real);Assert.Equal(result,(await owner.CloseAsync(real.Task,real.Input.CommandId)).Result);
        Assert.Equal(result,await owner.OriginalAsync(real.Input.CommandId));Assert.Empty(await owner.DocumentTasksAsync(f.Document));
    }
}
