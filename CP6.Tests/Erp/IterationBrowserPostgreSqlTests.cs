using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_iteration_withdrawal_reauthorization_keeps_old_fact_and_reaches_Workflow()
    {
        await using var h=await Seed();var f=await IterationDecisionSetup(h);await GrantIterationWithdrawRelay(h,f);await EngineeringAction(h,"plm-engineering-iteration","review-delegate");
        var id=f.Source.Context.IterationId;var actor=await IterationDelegateActor(h,f);var owner=IterationReviews(h,f.Source.Files);var actual=IterationReviews(actor,f.Source.Files);
        var input=IterationGrantInput(f.Submission,actor.User);var grant=(await owner.GrantBusinessAsync(id,input)).Grant;var context=await actual.ApplicationAsync(id,f.Submission);
        var withdrawn=await actual.WithdrawAsync(id,f.Submission,"\""+context.ControlRowVersion+"\"",new(Guid.NewGuid(),"Initial explicitly delegated withdrawal"),behalf:h.User);
        var original=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_iteration_review_control_v1.withdrawals WHERE command_id={withdrawn.TransportCommandId}").SingleAsync();
        await owner.GrantBusinessAsync(id,input with{OperationId=Guid.NewGuid(),GrantId=grant.GrantId,ExpectedVersion=grant.Version,Enabled=false});
        var workflow=IterationWithdrawals(h,f);var workflowContext=await workflow.ContextAsync(f.Submission);
        Assert.Equal("BLOCKED",(await workflow.WithdrawAsync(f.Submission,"\""+workflowContext.SubmissionRowVersion+"\"",original)).Ack.State);
        var password=new BCryptPasswordHasher(4).Hash("Price1!Password");
        await h.Db.Sys_Users.Where(x=>x.Id==actor.User).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.Password,password));
        await h.Db.Sys_Menus.Where(x=>x.MenuKey=="plm-engineering-iteration").ExecuteUpdateAsync(x=>x.SetProperty(m=>m.RoutePath,"/plm/engineering-iteration"));
        var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="plm-engineering-iteration");
        if(!await h.Db.Sys_RoleMenus.AnyAsync(x=>x.MenuId==menu.MenuId&&x.RoleId==72))h.Db.Sys_RoleMenus.Add(new(){MenuId=menu.MenuId,RoleId=72});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var settings=await DocumentHostSettings(f.Source.Files);settings["RequirementEvaluation__EvidenceMode"]="DEMO";settings["QualitySpecification__EvidenceMode"]="DEMO";
        using var browser=await BrowserScenario(h,actor.User,"oa-iteration-browser.mjs","iteration-withdrawal-browser",settings,new(){
            ["CP6_OA_BROWSER_ITERATION"]=id.ToString("D"),["CP6_OA_BROWSER_SUBMISSION"]=f.Submission.ToString("D"),["CP6_OA_BROWSER_MODE"]="withdrawal",
            ["CP6_OA_BROWSER_PROJECT_NUMBER"]=f.Source.Iteration.Project.Body.Number,["CP6_OA_BROWSER_ITERATION_NUMBER"]=f.Source.Iteration.Iteration.Body.Number,
            ["CP6_OA_BROWSER_PREVIOUS"]=withdrawn.TransportCommandId!.Value.ToString("D")});
        var next=browser.RootElement.GetProperty("accepted").Deserialize<IterationWithdrawalResultV1>(PersonalFormOwnerV2.Json)!;
        Assert.Equal(withdrawn.TransportCommandId,next.PreviousCommandId);Assert.Equal(actor.User,next.Fact!.Actor.ActualHandlerId);Assert.Equal(h.User,next.Fact.Actor.RequesterId);Assert.NotEqual(grant.GrantId,next.Fact.Actor.GrantId);
        Assert.Equal(withdrawn.Fact!.BusinessAfterRowVersion,next.Fact.BusinessBeforeRowVersion);Assert.NotEqual(next.Fact.BusinessBeforeRowVersion,next.Fact.BusinessAfterRowVersion);
        Assert.Equal(original,await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_iteration_review_control_v1.withdrawals WHERE command_id={withdrawn.TransportCommandId}").SingleAsync());
        var bytes=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_iteration_review_control_v1.withdrawals WHERE command_id={next.TransportCommandId}").SingleAsync();
        Assert.Equal("WITHDRAWN",(await workflow.WithdrawAsync(f.Submission,"\""+workflowContext.SubmissionRowVersion+"\"",bytes)).Ack.State);
        Assert.Equal("BLOCKED",(await workflow.OriginalAsync(withdrawn.TransportCommandId.Value)).Ack.State);
        Assert.Equal("IN_DESIGN",(await Iterations(h,f.Source.Files).ReadAsync(id)).Body.Iteration!.Stage);
    }
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_iteration_application_reauthorization_has_continuous_PostgreSQL_receipts()
    {
        await using var h=await Seed();var f=await IterationDecisionSetup(h);await EngineeringAction(h,"plm-engineering-iteration","review-delegate");
        var id=f.Source.Context.IterationId;var owner=IterationReviews(h,f.Source.Files);var issues=await owner.IssuesAsync(id);var issueId=Guid.NewGuid();
        await owner.IssueAsync(id,issues.Etag,new(Guid.NewGuid(),f.Submission,issueId,"CREATE",null,"Browser clarification",new("PROCESS",null,null),h.User,new(2026,11,3),null,"Blocking clarification"));
        await IterationVote(h,f);var receiver=await IterationReceiver(h,f);FactAck(await receiver.Receiver.ReceiveAsync(receiver.Payload));
        var context=await owner.ApplicationAsync(id,f.Submission);var original=new IterationApplicationCommandV1(Guid.NewGuid(),f.Submission,null,"Original blocked application");
        var blocked=await owner.ApplyAsync(id,context.Etag,original);Assert.Equal("BLOCKED",blocked.Receipt.Application);
        issues=await owner.IssuesAsync(id);await owner.IssueAsync(id,issues.Etag,new(Guid.NewGuid(),f.Submission,issueId,"RESPOND",1,"Clarified before browser action",null,null,null,[],"Resolve blocker"));
        var verifier=IterationReviews(DecisionActor(h,f.Source.Iteration.Reviewer),f.Source.Files);issues=await verifier.IssuesAsync(id);
        await verifier.IssueAsync(id,issues.Etag,new(Guid.NewGuid(),f.Submission,issueId,"VERIFY",2,"Independent verification",null,null,null,null,"Verified exact response"));
        var actor=await IterationDelegateActor(h,f);var password=new BCryptPasswordHasher(4).Hash("Price1!Password");
        await h.Db.Sys_Users.Where(x=>x.Id==actor.User).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.Password,password));
        await h.Db.Sys_Menus.Where(x=>x.MenuKey=="plm-engineering-iteration").ExecuteUpdateAsync(x=>x.SetProperty(m=>m.RoutePath,"/plm/engineering-iteration"));
        var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="plm-engineering-iteration");
        if(!await h.Db.Sys_RoleMenus.AnyAsync(x=>x.MenuId==menu.MenuId&&x.RoleId==72))h.Db.Sys_RoleMenus.Add(new(){MenuId=menu.MenuId,RoleId=72});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var settings=await DocumentHostSettings(f.Source.Files);settings["RequirementEvaluation__EvidenceMode"]="DEMO";settings["QualitySpecification__EvidenceMode"]="DEMO";
        using var browser=await BrowserScenario(h,actor.User,"oa-iteration-browser.mjs","iteration-application-browser",settings,new(){
            ["CP6_OA_BROWSER_ITERATION"]=id.ToString("D"),["CP6_OA_BROWSER_SUBMISSION"]=f.Submission.ToString("D"),["CP6_OA_BROWSER_MODE"]="application",
            ["CP6_OA_BROWSER_PROJECT_NUMBER"]=f.Source.Iteration.Project.Body.Number,["CP6_OA_BROWSER_ITERATION_NUMBER"]=f.Source.Iteration.Iteration.Body.Number,
            ["CP6_OA_BROWSER_PREVIOUS"]=blocked.Receipt.ReceiptId.ToString("D")});
        var applied=(await owner.ApplicationAsync(id,f.Submission)).Receipt!;
        Assert.Equal("APPLIED",applied.Application);Assert.Equal("2",applied.ReceiptRevision);Assert.Equal(blocked.Receipt.ReceiptId,applied.PreviousReceiptId);Assert.Equal(actor.User,applied.ActionActorId);
        Assert.Equal(blocked,(await owner.ApplicationOperationAsync(original.OperationId,false)).Result);
        var audit=await h.Db.Database.SqlQuery<string>($"SELECT audit_json AS \"Value\" FROM cp6_iteration_review_application_v1.receipts WHERE id={applied.ReceiptId}").SingleAsync();
        using var doc=JsonDocument.Parse(audit);Assert.True(doc.RootElement.GetProperty("reauthorize").GetBoolean());Assert.Equal(h.User,doc.RootElement.GetProperty("behalf").GetGuid());
        var grant=JsonSerializer.Deserialize<IterationBusinessGrantV1>(doc.RootElement.GetProperty("grantBody").GetString()!,PersonalFormOwnerV2.Json)!;
        Assert.Equal(actor.User,grant.DelegateId);Assert.Equal(f.Submission,grant.SubmissionId);
    }
}
