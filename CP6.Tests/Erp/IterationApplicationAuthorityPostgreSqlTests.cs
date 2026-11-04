using System.Text.Json;
using System.Net;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Plm;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public Task IterationApplicationAuthority_replaced_grant_requires_explicit_reauthorization()=>ChangedApplicationAuthority("replacement");
    [CurrentPermissionPostgresFact]
    public Task IterationApplicationAuthority_revised_grant_requires_explicit_reauthorization()=>ChangedApplicationAuthority("revision");
    [CurrentPermissionPostgresFact]
    public Task IterationApplicationAuthority_delegated_to_self_requires_explicit_reauthorization()=>ChangedApplicationAuthority("to-self");
    [CurrentPermissionPostgresFact]
    public Task IterationApplicationAuthority_self_to_delegated_requires_explicit_reauthorization()=>ChangedApplicationAuthority("to-delegated");

    private async Task ChangedApplicationAuthority(string change)
    {
        await using var h=await Seed();var f=await IterationDecisionSetup(h);
        await EngineeringAction(h,"plm-engineering-iteration","review-delegate");
        var id=f.Source.Context.IterationId;var owner=IterationReviews(h,f.Source.Files);
        var issues=await owner.IssuesAsync(id);var issueId=Guid.NewGuid();
        await owner.IssueAsync(id,issues.Etag,new(Guid.NewGuid(),f.Submission,issueId,"CREATE",null,"Current round needs clarification",new("PROCESS",null,null),h.User,new(2026,11,3),null,"Blocking clarification"));
        await IterationVote(h,f);var receiver=await IterationReceiver(h,f);FactAck(await receiver.Receiver.ReceiveAsync(receiver.Payload));
        var actor=await IterationDelegateActor(h,f);var actual=IterationReviews(actor,f.Source.Files);
        var input=IterationGrantInput(f.Submission,actor.User);var grant=(await owner.GrantBusinessAsync(id,input)).Grant;
        var context=await actual.ApplicationAsync(id,f.Submission);
        var original=new IterationApplicationCommandV1(Guid.NewGuid(),f.Submission,null,"Original authority must survive BLOCKED retry");
        var blocked=await actual.ApplyAsync(id,context.Etag,original,behalf:change=="to-delegated"?null:h.User);
        Assert.Equal("BLOCKED",blocked.Receipt.Application);
        var originalAudit=await h.Db.Database.SqlQuery<string>($"SELECT audit_json AS \"Value\" FROM cp6_iteration_review_application_v1.receipts WHERE id={blocked.Receipt.ReceiptId}").SingleAsync();
        if(change=="replacement")
        {
            await owner.GrantBusinessAsync(id,input with{OperationId=Guid.NewGuid(),GrantId=grant.GrantId,ExpectedVersion=grant.Version,Enabled=false});
            grant=(await owner.GrantBusinessAsync(id,input with{OperationId=Guid.NewGuid()})).Grant;
        }
        else if(change=="revision")grant=(await owner.GrantBusinessAsync(id,input with{OperationId=Guid.NewGuid(),GrantId=grant.GrantId,ExpectedVersion=grant.Version,Reason="Explicit revised grant"})).Grant;
        Guid? behalf=change=="to-self"?null:h.User;
        context=await actual.ApplicationAsync(id,f.Submission);
        var command=new IterationApplicationCommandV1(Guid.NewGuid(),f.Submission,blocked.Receipt.ReceiptId,"Explicit changed authority");
        var error=await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>actual.ApplyAsync(id,context.Etag,command,behalf:behalf));
        Assert.Equal(403,error.Status);
        Assert.Equal(change is "replacement" or "revision"?"ITERATION_BUSINESS_GRANT_DENIED":"ITERATION_REVIEW_ORIGINAL_AUTHORITY_REQUIRED",error.Message);
        Assert.Equal(blocked.Receipt,(await actual.ApplicationAsync(id,f.Submission)).Receipt);
        Assert.Equal("UNKNOWN",(await actual.ApplicationOperationAsync(command.OperationId,false)).State);
        IterationApplicationResultV1 next;
        if(change=="replacement")
        {
            var settings=await DocumentHostSettings(f.Source.Files);settings["RequirementEvaluation__EvidenceMode"]="DEMO";settings["QualitySpecification__EvidenceMode"]="DEMO";
            await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,settings);
            var cookies=await DocumentReviewerLogin(host,h,actor.User);var route="/api/engineering-iterations/owner-v1/"+id+"/review-application";
            using(var request=OperationsRequest(cookies,HttpMethod.Post,route,command))
            {
                request.Headers.TryAddWithoutValidation("If-Match",context.Etag);request.Headers.Add("X-Acting-As",h.User.ToString("D"));
                using var response=await host.Send(request);Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
                Assert.Contains("ITERATION_BUSINESS_GRANT_DENIED",await response.Content.ReadAsStringAsync());
            }
            using(var request=OperationsRequest(cookies,HttpMethod.Post,route+"/reauthorize",command))
            {
                request.Headers.TryAddWithoutValidation("If-Match",context.Etag);request.Headers.Add("X-Acting-As",h.User.ToString("D"));
                using var response=await host.Send(request);next=await DocumentHttpData<IterationApplicationResultV1>(response);
            }
        }
        else next=await actual.ReauthorizeApplicationAsync(id,context.Etag,command,behalf:behalf);
        Assert.Equal("BLOCKED",next.Receipt.Application);Assert.Equal("2",next.Receipt.ReceiptRevision);
        Assert.Equal(blocked.Receipt.ReceiptId,next.Receipt.PreviousReceiptId);Assert.Equal(actor.User,next.Receipt.ActionActorId);
        var nextAudit=await h.Db.Database.SqlQuery<string>($"SELECT audit_json AS \"Value\" FROM cp6_iteration_review_application_v1.receipts WHERE id={next.Receipt.ReceiptId}").SingleAsync();
        using(var audit=JsonDocument.Parse(nextAudit))
        {
            Assert.True(audit.RootElement.GetProperty("reauthorize").GetBoolean());
            if(behalf==null)Assert.Equal(JsonValueKind.Null,audit.RootElement.GetProperty("grantBody").ValueKind);
            else Assert.Equal(grant.Version,JsonSerializer.Deserialize<IterationBusinessGrantV1>(audit.RootElement.GetProperty("grantBody").GetString()!,PersonalFormOwnerV2.Json)!.Version);
        }
        issues=await owner.IssuesAsync(id);await owner.IssueAsync(id,issues.Etag,new(Guid.NewGuid(),f.Submission,issueId,"RESPOND",1,"Clarification",null,null,null,[],"Resolve blocker"));
        var verifier=IterationReviews(DecisionActor(h,f.Source.Iteration.Reviewer),f.Source.Files);issues=await verifier.IssuesAsync(id);
        await verifier.IssueAsync(id,issues.Etag,new(Guid.NewGuid(),f.Submission,issueId,"VERIFY",2,"Independent verification",null,null,null,null,"Verified response"));
        context=await actual.ApplicationAsync(id,f.Submission);
        var applied=await actual.ApplyAsync(id,context.Etag,new(Guid.NewGuid(),f.Submission,next.Receipt.ReceiptId,"Continue with unchanged exact authority"),behalf:behalf);
        Assert.Equal("APPLIED",applied.Receipt.Application);Assert.Equal("3",applied.Receipt.ReceiptRevision);Assert.Equal(next.Receipt.ReceiptId,applied.Receipt.PreviousReceiptId);
        Assert.Equal(blocked,(await actual.ApplicationOperationAsync(original.OperationId,false)).Result);
        Assert.Equal(originalAudit,await h.Db.Database.SqlQuery<string>($"SELECT audit_json AS \"Value\" FROM cp6_iteration_review_application_v1.receipts WHERE id={blocked.Receipt.ReceiptId}").SingleAsync());
    }
}
