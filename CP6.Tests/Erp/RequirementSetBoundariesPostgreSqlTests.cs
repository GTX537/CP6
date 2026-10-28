using System.Net;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Plm;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{

    private sealed record SetApplicationFixture(SetDecisionFixture Set,RequirementSetReviewFactReceiverV1 Receiver,
        IntakeAuthority Authority,byte[] Payload,ApprovalDecisionEnvelopeV2 Decision);
    private async Task<SetApplicationFixture> SetApplicationSetup(Harness h,bool approve=true,bool reuseBinding=false)
    {
        var f=await SetDecisionSetup(h,reuseBinding:reuseBinding);await SetVote(DecisionActor(h,f.Source.Member.First),approve?"approve":"reject");
        if(approve)await SetVote(DecisionActor(h,f.Source.Member.Second));
        var receiver=await SetReceiver(h,f);return new(f,receiver.Receiver,receiver.Authority,receiver.Payload,ApprovalDecisionWireV2.Parse(receiver.Payload).Value);
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementSet_current_professional_roles_and_withdrawn_member_guard_new_votes_but_allow_rejection()
    {
        await using var h=await Seed();var f=await SetDecisionSetup(h);var first=DecisionActor(h,f.Source.Member.First);
        await h.Db.Sys_UserRoles.Where(x=>x.UserId==f.Source.Member.First&&x.RoleId==74).ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>SetVote(first))).Status);
        var evaluation=Evaluations(h);await evaluation.CommandAsync(EvaluationCommand(await evaluation.ReadAsync(f.Source.Member.Definition.Reference.Id),"WITHDRAW"));
        var second=DecisionActor(h,f.Source.Member.Second);Assert.Equal(422,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>SetVote(second))).Status);
        Assert.Empty(await h.Db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM cp6_approval_decision_v2.votes WHERE tenant_id={h.Tenant}").ToArrayAsync());
        Assert.NotNull((await SetVote(second,"reject")).Result.DecisionId);
        var receiver=await SetReceiver(h,f);FactAck(await receiver.Receiver.ReceiveAsync(receiver.Payload));
        var owner=SetReviews(h);var ready=await owner.SubmissionAsync(f.Source.Set.Reference.Id,f.Submission);
        var result=await owner.ApplyAsync(f.Source.Set.Reference.Id,ready.Application.Etag,new(Guid.NewGuid(),f.Submission,null,"Record rejection without restoring withdrawn approval"));
        Assert.Equal("REQUIREMENT_SET_REJECTED",result.Receipt.OutcomeCode);Assert.Equal("UNAVAILABLE",(await Sets(h).SourceAsync(f.Source.Set.Reference)).Availability);
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementSet_dropped_application_effect_rolls_back_receipt_outbox_original_then_exact_key_recovers()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await SetApplicationSetup(h);var id=f.Set.Source.Set.Reference.Id;
        FactAck(await f.Receiver.ReceiveAsync(f.Payload));var owner=SetReviews(h);var ready=await owner.SubmissionAsync(id,f.Set.Submission);
        var command=new RequirementSetApplicationCommandV1(Guid.NewGuid(),f.Set.Submission,null,"Atomic exact requirement-set approval");
        hook.Before=c=>{if(c.CommandText.Contains("INSERT INTO cp6_requirement_set_review_application_v1.applications"))c.CommandText="SELECT 1";return Task.CompletedTask;};
        var failure=await Record.ExceptionAsync(()=>owner.ApplyAsync(id,ready.Application.Etag,command));Assert.NotNull(failure);
        Assert.Contains("REQUIREMENT_SET_APPLICATION_EFFECT_REQUIRED",failure.ToString());hook.Before=null;
        Assert.Equal("UNKNOWN",(await owner.ApplicationOperationAsync(command.OperationId,false)).State);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_requirement_set_review_application_v1.receipts WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_requirement_set_review_application_v1.outbox WHERE tenant_id={h.Tenant}").SingleAsync());
        var applied=await owner.ApplyAsync(id,ready.Application.Etag,command);Assert.Equal(applied,(await owner.ApplicationOperationAsync(command.OperationId,false)).Result);
        Assert.Equal("AVAILABLE",(await Sets(h).SourceAsync(f.Set.Source.Set.Reference)).Availability);
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementSet_concurrent_revisions_preserve_one_frontier_and_original_operation_close_prevents_late_write()
    {
        await using var h=await Seed();var f=await SetSetup(h);await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var peer=h with{Db=db};
        var command=SetRevise(f.Set,f.Set.Body with{Name="Concurrent successor"});
        async Task<string> Write(Harness current,RequirementSetCommandV1 c){try{return(await Sets(current).CommandAsync(c)).Version;}catch(OrderPriceCorrectionException e){return e.Message;}}
        var outcomes=await Task.WhenAll(Write(h,command),Write(peer,command with{OperationId=Guid.NewGuid()}));
        Assert.Single(outcomes,x=>x=="2");Assert.Single(outcomes,x=>x=="REQUIREMENT_SET_VERSION_CHANGED");
        var latest=await Sets(h).ReadAsync(f.Set.Reference.Id);var delayed=SetRevise(latest,latest.Body);
        Assert.Equal("CLOSED",(await Sets(h).OperationAsync(delayed.OperationId,true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>Sets(h).CommandAsync(delayed))).Message);
        var reviewer=Sets(DecisionActor(h,f.Member.First));var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="plm-requirement-set");
        h.Db.Sys_RoleDataScopes.Add(new(){RoleId=72,ResourceKey="plm-requirement-set",ScopeType=1});await h.Db.SaveChangesAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>reviewer.ReadAsync(f.Set.Reference.Id,"1"))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementSet_changed_original_source_keeps_historical_text_and_forbids_current_adoption()
    {
        await using var h=await Seed();var f=await SetSetup(h);var source=f.Member.Source;
        await EvaluationDrafts(h).CommandAsync(ReviseRequirement(source,source.Body with{Source=source.Body.Source! with{SourceLabel="Changed proposal"}}));
        var old=await Sets(h).ReadAsync(f.Set.Reference.Id,"1");Assert.Equal(f.Set.OriginalJson,old.OriginalJson);
        Assert.Equal(source.OriginalJson,old.MemberStatus!.Single().Source.OriginalJson);Assert.Contains("MEMBER_UNAVAILABLE:"+f.Member.Definition.Reference.Id,old.Missing);
        Assert.Equal("UNAVAILABLE",(await Sets(h).SourceAsync(f.Set.Reference)).Availability);
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementSetReview_actual_Program_HTTP_professional_votes_exact_approval_and_read_only_recovery()
    {
        await using var h=await Seed();var f=await SetDecisionSetup(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{{"ScopeAuthorization__EnvironmentId",ScopeEnvironment.ToString()},{"RequirementEvaluation__EvidenceMode","DEMO"}});
        var author=await Login(host,h);var url=$"/api/requirement-set/owner-v1/{f.Source.Set.Reference.Id}/review-submissions/{f.Submission}";
        async Task<RequirementSetReviewDetailV1> Detail(){using var r=await host.Send(OperationsRequest(author,HttpMethod.Get,url));return await DocumentHttpData<RequirementSetReviewDetailV1>(r);}
        var before=await Detail();Assert.False(before.Submission.IsApproved);Assert.Null(before.Application.Decision);
        foreach(var actor in new[]{f.Source.Member.First,f.Source.Member.Second})
        {
            var cookies=await DocumentReviewerLogin(host,h,actor);
            using var list=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"/api/oa/tasks/requirement-set-review/"+f.Source.Set.Reference.Id));
            var task=Assert.Single(await DocumentHttpData<RequirementSetReviewTaskV1[]>(list));Assert.Equal(f.Submission,task.SubmissionId);
            using var get=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"/api/oa/tasks/{task.Task.TaskId}/decision-context"));
            var current=await DocumentHttpData<ApprovalTaskDecisionViewV2>(get);
            var command=new ApprovalTaskDecisionInputV2(Guid.NewGuid(),"approve","Reviewed exact original, derivations and language authority",current.TaskRowVersion,current.InstanceRowVersion);
            using var request=OperationsRequest(cookies,HttpMethod.Post,$"/api/oa/tasks/{task.Task.TaskId}/decision",command);
            request.Headers.TryAddWithoutValidation("If-Match",get.Headers.ETag!.ToString());
            using var decision=await host.Send(request);var saved=await DocumentHttpData<ApprovalTaskDecisionResultV2>(decision);
            Assert.Equal("COMPLETED",saved.State);
            using var original=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"/api/oa/tasks/decision-commands/"+command.CommandId));
            Assert.Equal(saved,await DocumentHttpData<ApprovalTaskDecisionResultV2>(original));
        }
        Assert.Null((await Detail()).Application.Decision);
        var receiver=await SetReceiver(h,f);FactAck(await receiver.Receiver.ReceiveAsync(receiver.Payload));
        var ready=await Detail();Assert.Equal("APPROVED",ready.Application.Decision);Assert.False(ready.Submission.IsApproved);
        var apply=new RequirementSetApplicationCommandV1(Guid.NewGuid(),f.Submission,null,"Apply actual professional signatures to this exact requirementSet version");
        using var applicationRequest=OperationsRequest(author,HttpMethod.Post,$"/api/requirement-set/owner-v1/{f.Source.Set.Reference.Id}/review-application",apply);
        applicationRequest.Headers.TryAddWithoutValidation("If-Match",ready.Application.Etag);
        using var applicationResponse=await host.Send(applicationRequest);var applied=await DocumentHttpData<RequirementSetApplicationResultV1>(applicationResponse);
        Assert.Equal("APPLIED",applied.Receipt.Application);Assert.True((await Detail()).Submission.IsApproved);
        await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="review-apply").ExecuteDeleteAsync();
        using var recovered=await host.Send(OperationsRequest(author,HttpMethod.Get,"/api/requirement-set/owner-v1/review-application-operations/"+apply.OperationId));
        Assert.Equal(applied,(await DocumentHttpData<RequirementSetApplicationOperationV1>(recovered)).Result);
    }
}
