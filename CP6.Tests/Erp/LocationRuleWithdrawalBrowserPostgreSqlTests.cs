using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wms;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_location_rule_BLOCKED_reauthorization_and_lost_response_recovery_preserve_original_facts()
    {
        await using var h=await Seed();var f=await RuleDecisionSetup(h);var service=await GrantRuleWithdrawRelay(h,f);
        await EngineeringAction(h,"wms-location-rule","review-delegate");
        var actor=DecisionActor(h,await GrantDelegate(h,"rule-business-delegate"));var owner=RuleReviews(h);var actual=RuleReviews(actor);
        var input=RuleGrantInput(f.Submission,actor.User);var grant=(await owner.GrantBusinessAsync(f.Source.Candidate.Reference.Id,input)).Grant;
        var context=await actual.WithdrawalContextAsync(f.Source.Candidate.Reference.Id,f.Submission);
        var withdrawn=await actual.WithdrawAsync(f.Source.Candidate.Reference.Id,f.Submission,"\""+context.ControlRowVersion+"\"",new(Guid.NewGuid(),"Initial exact-round delegated withdrawal"),behalf:h.User);
        var raw=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_location_rule_review_control_v1.withdrawals WHERE command_id={withdrawn.TransportCommandId}").SingleAsync();
        await owner.GrantBusinessAsync(f.Source.Candidate.Reference.Id,input with{OperationId=Guid.NewGuid(),GrantId=grant.GrantId,ExpectedVersion=grant.Version,Enabled=false});
        var workflow=RuleWithdrawals(h,service);var current=await workflow.ContextAsync(f.Submission);
        Assert.Equal("BLOCKED",(await workflow.WithdrawAsync(f.Submission,"\""+current.SubmissionRowVersion+"\"",raw)).Ack.State);
        var password=new BCryptPasswordHasher(4).Hash("Price1!Password");
        await h.Db.Sys_Users.Where(x=>x.Id==actor.User).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.Password,password));
        // Rule review is an internal detail route reached from the existing location menu.
        await h.Db.Sys_Menus.Where(x=>x.MenuKey=="wms-location").ExecuteUpdateAsync(x=>x.SetProperty(m=>m.RoutePath,"/wms/location"));
        var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="wms-location");
        if(!await h.Db.Sys_RoleMenus.AnyAsync(x=>x.MenuId==menu.MenuId&&x.RoleId==72))h.Db.Sys_RoleMenus.Add(new(){MenuId=menu.MenuId,RoleId=72});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        using var browser=await BrowserScenario(h,actor.User,"location-rule-withdrawal-browser.mjs","location-rule-withdrawal-browser",RuleWithdrawalHostSettings(),new(){
            ["CP6_OA_BROWSER_RULE"]=f.Source.Candidate.Reference.Id.ToString("D"),["CP6_OA_BROWSER_SUBMISSION"]=f.Submission.ToString("D"),
            ["CP6_OA_BROWSER_ENVIRONMENT"]=ScopeEnvironment.ToString("D"),["CP6_OA_BROWSER_PREVIOUS"]=withdrawn.TransportCommandId!.Value.ToString("D")});
        var next=browser.RootElement.GetProperty("accepted").Deserialize<LocationRuleWithdrawalResultV1>(PersonalFormOwnerV2.Json)!;
        Assert.Equal(actor.User,next.Fact!.Actor.ActualHandlerId);Assert.Equal(h.User,next.Fact.Actor.RequesterId);Assert.NotEqual(grant.GrantId,next.Fact.Actor.GrantId);
        Assert.Equal(withdrawn.TransportCommandId,next.PreviousCommandId);Assert.Equal(withdrawn.Fact!.BusinessAfterRowVersion,next.Fact.BusinessBeforeRowVersion);
        Assert.Equal(raw,await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_location_rule_review_control_v1.withdrawals WHERE command_id={withdrawn.TransportCommandId}").SingleAsync());
        var nextRaw=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_location_rule_review_control_v1.withdrawals WHERE command_id={next.TransportCommandId}").SingleAsync();
        Assert.Equal("WITHDRAWN",(await workflow.WithdrawAsync(f.Submission,"\""+current.SubmissionRowVersion+"\"",nextRaw)).Ack.State);
        Assert.Equal("BLOCKED",(await workflow.OriginalAsync(withdrawn.TransportCommandId.Value)).Ack.State);
        Assert.Equal(next,(await actual.WithdrawalOperationAsync(next.OperationId,false)).Result);
        Assert.Equal(2,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_location_rule_review_control_v1.withdrawals WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal("WITHDRAWN",(await ReferenceRules(h).ReadAsync(f.Source.Location,f.Source.Candidate.Reference.Id)).State);Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());
    }
}
