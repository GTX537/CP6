using System.Text.Json;
using System.Net;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Erp;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private async Task<Harness> IterationDelegateActor(Harness h,IterationDecisionFixture f)
    {
        var id=await GrantDelegate(h,"iteration-business-delegate");var iterations=Iterations(h,f.Source.Files);
        var project=await iterations.ReadAsync(f.Source.Iteration.Project.Reference.Id);
        await IterationWrite(iterations,IterationChange(project,"EDIT_PROJECT") with{Project=project.Body.Project!.Definition with{Members=[..project.Body.Project.Definition.Members,id]}});
        return DecisionActor(h,id);
    }
    [CurrentPermissionPostgresFact]
    public async Task IterationBusinessDelegation_withdrawal_requires_Owner_grant_and_explicit_new_fact_after_BLOCKED()
    {
        await using var h=await Seed();var f=await IterationDecisionSetup(h);await GrantIterationWithdrawRelay(h,f);await EngineeringAction(h,"plm-engineering-iteration","review-delegate");
        var id=f.Source.Context.IterationId;var actor=await IterationDelegateActor(h,f);var owner=IterationReviews(h,f.Source.Files);var actual=IterationReviews(actor,f.Source.Files);
        var settings=await DocumentHostSettings(f.Source.Files);settings["RequirementEvaluation__EvidenceMode"]="DEMO";settings["QualitySpecification__EvidenceMode"]="DEMO";
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,settings);
        var ownerCookies=await Login(host,h);var actorCookies=await DocumentReviewerLogin(host,h,actor.User);var root="/api/engineering-iterations/owner-v1/"+id;
        Assert.False(await h.Db.Wf_FlowTasks.AnyAsync(t=>t.AssigneeId==actor.User));
        await CreateTaskGrant(h,h.User,actor.User,"binding-flow");var context=await actual.ApplicationAsync(id,f.Submission);var command=new ApprovalUserWithdrawV2(Guid.NewGuid(),"Explicit delegated business withdrawal");
        Assert.Equal("ITERATION_BUSINESS_GRANT_DENIED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>actual.WithdrawAsync(id,f.Submission,"\""+context.ControlRowVersion+"\"",command,behalf:h.User))).Message);
        var input=IterationGrantInput(f.Submission,actor.User);IterationBusinessGrantV1 grant;
        using(var missing=OperationsRequest(ownerCookies,HttpMethod.Post,root+"/review-business-grants",input))
        {missing.Headers.Remove("X-CSRF-Token");using var denied=await host.Send(missing);Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);}
        var invalid=JsonSerializer.SerializeToNode(input,new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();invalid["fromUtc"]="2026-11-02T10:00:00";
        using(var bad=await host.Send(OperationsRequest(ownerCookies,HttpMethod.Post,root+"/review-business-grants",invalid)))Assert.Equal(HttpStatusCode.BadRequest,bad.StatusCode);
        using(var accepted=await host.Send(OperationsRequest(ownerCookies,HttpMethod.Post,root+"/review-business-grants",input)))
        {grant=(await DocumentHttpData<IterationBusinessGrantResultV1>(accepted)).Grant;Assert.True(accepted.Headers.CacheControl?.NoStore);}
        var withdrawn=await actual.WithdrawAsync(id,f.Submission,"\""+context.ControlRowVersion+"\"",command,behalf:h.User);
        Assert.Equal(h.User,withdrawn.Fact!.Actor.RequesterId);Assert.Equal(actor.User,withdrawn.Fact.Actor.ActualHandlerId);Assert.Equal(grant.GrantId,withdrawn.Fact.Actor.GrantId);
        var bytes=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_iteration_review_control_v1.withdrawals WHERE command_id={withdrawn.TransportCommandId}").SingleAsync();
        var revoke=input with{OperationId=Guid.NewGuid(),GrantId=grant.GrantId,ExpectedVersion=grant.Version,Enabled=false};var revoked=(await owner.GrantBusinessAsync(id,revoke)).Grant;
        var workflow=IterationWithdrawals(h,f);var workflowContext=await workflow.ContextAsync(f.Submission);
        var blocked=await workflow.WithdrawAsync(f.Submission,"\""+workflowContext.SubmissionRowVersion+"\"",bytes);Assert.Equal("BLOCKED",blocked.Ack.State);
        Assert.Equal("IN_DESIGN",(await Iterations(h,f.Source.Files).ReadAsync(id)).Body.Iteration!.Stage);
        var renewed=(await owner.GrantBusinessAsync(id,input with{OperationId=Guid.NewGuid(),GrantId=grant.GrantId,ExpectedVersion=revoked.Version})).Grant;
        Assert.Equal("BLOCKED",(await workflow.WithdrawAsync(f.Submission,"\""+workflowContext.SubmissionRowVersion+"\"",bytes)).Ack.State);
        context=await actual.ApplicationAsync(id,f.Submission);
        IterationWithdrawalResultV1 next;
        using(var request=OperationsRequest(actorCookies,HttpMethod.Post,root+"/review-submissions/"+f.Submission+"/withdrawals/"+withdrawn.TransportCommandId+"/reauthorize",new ApprovalUserWithdrawV2(Guid.NewGuid(),"Use newly explicit business authorization")))
        {
            request.Headers.TryAddWithoutValidation("If-Match","\""+context.ControlRowVersion+"\"");request.Headers.Add("X-Acting-As",h.User.ToString("D"));
            using var response=await host.Send(request);next=await DocumentHttpData<IterationWithdrawalResultV1>(response);
        }
        Assert.Equal(withdrawn.TransportCommandId,next.PreviousCommandId);Assert.Equal(renewed.Revision,next.Fact!.Actor.GrantRevision);
        Assert.Equal(withdrawn.Fact.BusinessAfterRowVersion,next.Fact.BusinessBeforeRowVersion);Assert.NotEqual(next.Fact.BusinessBeforeRowVersion,next.Fact.BusinessAfterRowVersion);
        Assert.Equal(bytes,await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_iteration_review_control_v1.withdrawals WHERE command_id={withdrawn.TransportCommandId}").SingleAsync());
        var nextBytes=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_iteration_review_control_v1.withdrawals WHERE command_id={next.TransportCommandId}").SingleAsync();
        Assert.Equal("WITHDRAWN",(await workflow.WithdrawAsync(f.Submission,"\""+workflowContext.SubmissionRowVersion+"\"",nextBytes)).Ack.State);
        Assert.Equal("BLOCKED",(await workflow.OriginalAsync(withdrawn.TransportCommandId.Value)).Ack.State);
        Assert.Equal(next, (await actual.WithdrawalOperationAsync(next.OperationId,false)).Result);
        Assert.Equal(2,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_iteration_review_control_v1.withdrawals WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    private static IterationBusinessGrantCommandV1 IterationGrantInput(Guid submission,Guid actual)
        =>new(Guid.NewGuid(),submission,null,null,actual,DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddHours(1),["review-withdraw","review-apply"],true,"Explicit exact-round business delegation");
    [CurrentPermissionPostgresFact]
    public async Task IterationBusinessDelegation_new_actor_requires_explicit_application_authorization_and_keeps_receipt_chain()
    {
        await using var h=await Seed();var f=await IterationDecisionSetup(h);await EngineeringAction(h,"plm-engineering-iteration","review-delegate");
        var id=f.Source.Context.IterationId;var owner=IterationReviews(h,f.Source.Files);
        var issues=await owner.IssuesAsync(id);var issueId=Guid.NewGuid();
        await owner.IssueAsync(id,issues.Etag,new(Guid.NewGuid(),f.Submission,issueId,"CREATE",null,"Clarify current round",new("PROCESS",null,null),h.User,new(2026,11,3),null,"Blocking clarification"));
        await IterationVote(h,f);var receiver=await IterationReceiver(h,f);FactAck(await receiver.Receiver.ReceiveAsync(receiver.Payload));
        var context=await owner.ApplicationAsync(id,f.Submission);var original=new IterationApplicationCommandV1(Guid.NewGuid(),f.Submission,null,"Original current application request");
        var blocked=await owner.ApplyAsync(id,context.Etag,original);Assert.Equal("BLOCKED",blocked.Receipt.Application);
        issues=await owner.IssuesAsync(id);await owner.IssueAsync(id,issues.Etag,new(Guid.NewGuid(),f.Submission,issueId,"RESPOND",1,"Exact clarification",null,null,null,[],"Resolve blocker"));
        var verifier=IterationReviews(DecisionActor(h,f.Source.Iteration.Reviewer),f.Source.Files);issues=await verifier.IssuesAsync(id);
        await verifier.IssueAsync(id,issues.Etag,new(Guid.NewGuid(),f.Submission,issueId,"VERIFY",2,"Independent verification",null,null,null,null,"Verified exact response"));
        var actor=await IterationDelegateActor(h,f);var actual=IterationReviews(actor,f.Source.Files);var input=IterationGrantInput(f.Submission,actor.User);
        var grant=(await owner.GrantBusinessAsync(id,input)).Grant;context=await actual.ApplicationAsync(id,f.Submission);
        var command=new IterationApplicationCommandV1(Guid.NewGuid(),f.Submission,blocked.Receipt.ReceiptId,"Explicit new acting identity after blocked result");
        Assert.Equal("ITERATION_REVIEW_ORIGINAL_APPLIER_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>actual.ApplyAsync(id,context.Etag,command,behalf:h.User))).Message);
        var applied=await actual.ReauthorizeApplicationAsync(id,context.Etag,command,behalf:h.User);
        Assert.Equal("APPLIED",applied.Receipt.Application);Assert.Equal(blocked.Receipt.ReceiptId,applied.Receipt.PreviousReceiptId);Assert.Equal("2",applied.Receipt.ReceiptRevision);
        Assert.Equal(actor.User,applied.Receipt.ActionActorId);Assert.Equal(blocked.Receipt.DecisionId,applied.Receipt.DecisionId);
        var audit=await h.Db.Database.SqlQuery<string>($"SELECT audit_json AS \"Value\" FROM cp6_iteration_review_application_v1.receipts WHERE id={applied.Receipt.ReceiptId}").SingleAsync();
        using(var doc=JsonDocument.Parse(audit))
        {
            Assert.True(doc.RootElement.GetProperty("reauthorize").GetBoolean());Assert.Equal(h.User,doc.RootElement.GetProperty("behalf").GetGuid());
            var authority=JsonSerializer.Deserialize<IterationBusinessGrantV1>(doc.RootElement.GetProperty("grantBody").GetString()!,PersonalFormOwnerV2.Json)!;Assert.Equal(grant.Version,authority.Version);
        }
        Assert.Equal(blocked,(await owner.ApplicationOperationAsync(original.OperationId,false)).Result);
        await owner.GrantBusinessAsync(id,input with{OperationId=Guid.NewGuid(),GrantId=grant.GrantId,ExpectedVersion=grant.Version,Enabled=false});
        Assert.Equal(applied,(await actual.ApplicationOperationAsync(command.OperationId,false)).Result);
    }
    [CurrentPermissionPostgresFact]
    public async Task IterationBusinessDelegation_actual_Owner_grants_are_exact_round_revisioned_and_original_commands_survive_revocation()
    {
        await using var h=await Seed();var f=await IterationDecisionSetup(h);await EngineeringAction(h,"plm-engineering-iteration","review-delegate");
        var id=f.Source.Context.IterationId;var owner=IterationReviews(h,f.Source.Files);var input=IterationGrantInput(f.Submission,f.Source.Iteration.Reviewer);
        var created=await owner.GrantBusinessAsync(id,input);var grant=created.Grant;Assert.Equal(h.User,grant.GrantorId);Assert.Equal(f.Submission,grant.SubmissionId);
        Assert.Equal(JsonSerializer.Serialize(created),JsonSerializer.Serialize(await owner.GrantBusinessAsync(id,input)));
        Assert.Equal("1",Assert.Single(await owner.BusinessGrantsAsync(id,f.Submission)).Revision);
        var revised=await owner.GrantBusinessAsync(id,input with{OperationId=Guid.NewGuid(),GrantId=grant.GrantId,ExpectedVersion=grant.Version,Actions=["review-withdraw"]});
        var stale=await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.GrantBusinessAsync(id,input with{OperationId=Guid.NewGuid(),GrantId=grant.GrantId,ExpectedVersion=grant.Version,Enabled=false}));
        Assert.Equal("ITERATION_BUSINESS_GRANT_CHANGED",stale.Message);
        var revoked=await owner.GrantBusinessAsync(id,input with{OperationId=Guid.NewGuid(),GrantId=grant.GrantId,ExpectedVersion=revised.Grant.Version,Actions=["review-withdraw"],Enabled=false});
        Assert.False(revoked.Grant.Enabled);Assert.Equal("3",revoked.Grant.Revision);
        Assert.Equal(JsonSerializer.Serialize(created),JsonSerializer.Serialize((await owner.BusinessGrantOperationAsync(input.OperationId,false)).Result));
        Assert.Equal(3,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_iteration_review_control_v1.business_grants WHERE tenant_id={h.Tenant}").SingleAsync());
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_iteration_review_control_v1.business_grants SET body='{{}}' WHERE tenant_id={h.Tenant}"));
        var closed=Guid.NewGuid();Assert.Equal("CLOSED",(await owner.BusinessGrantOperationAsync(closed,true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.GrantBusinessAsync(id,input with{OperationId=closed}))).Message);
    }
}
