using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_requirement_set_BLOCKED_reauthorization_and_lost_response_recovery_preserve_original_facts()
    {
        await using var h=await Seed();var f=await SetDecisionSetup(h);var service=await GrantSetWithdrawRelay(h,f);
        await EngineeringAction(h,"plm-requirement-set","review-delegate");
        var actor=DecisionActor(h,await GrantDelegate(h,"document-business-delegate"));var owner=SetReviews(h);var actual=SetReviews(actor);
        var input=SetGrantInput(f.Submission,actor.User);var grant=(await owner.GrantBusinessAsync(f.Source.Set.Reference.Id,input)).Grant;
        var context=await actual.WithdrawalContextAsync(f.Source.Set.Reference.Id,f.Submission);
        var withdrawn=await actual.WithdrawAsync(f.Source.Set.Reference.Id,f.Submission,"\""+context.ControlRowVersion+"\"",new(Guid.NewGuid(),"Initial exact-round delegated withdrawal"),behalf:h.User);
        var raw=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_requirement_set_review_control_v1.withdrawals WHERE command_id={withdrawn.TransportCommandId}").SingleAsync();
        await owner.GrantBusinessAsync(f.Source.Set.Reference.Id,input with{OperationId=Guid.NewGuid(),GrantId=grant.GrantId,ExpectedVersion=grant.Version,Enabled=false});
        var workflow=SetWithdrawals(h,f,service);var current=await workflow.ContextAsync(f.Submission);
        Assert.Equal("BLOCKED",(await workflow.WithdrawAsync(f.Submission,"\""+current.SubmissionRowVersion+"\"",raw)).Ack.State);
        var password=new BCryptPasswordHasher(4).Hash("Price1!Password");
        await h.Db.Sys_Users.Where(x=>x.Id==actor.User).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.Password,password));
        await h.Db.Sys_Menus.Where(x=>x.MenuKey=="plm-requirement-set").ExecuteUpdateAsync(x=>x.SetProperty(m=>m.RoutePath,"/plm/requirement-set"));
        var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="plm-requirement-set");
        if(!await h.Db.Sys_RoleMenus.AnyAsync(x=>x.MenuId==menu.MenuId&&x.RoleId==72))h.Db.Sys_RoleMenus.Add(new(){MenuId=menu.MenuId,RoleId=72});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        using var browser=await BrowserScenario(h,actor.User,"requirement-set-withdrawal-browser.mjs","requirement-set-withdrawal-browser",SetHostSettings(),new(){
            ["CP6_OA_BROWSER_REQUIREMENT_SET"]=f.Source.Set.Reference.Id.ToString("D"),["CP6_OA_BROWSER_SUBMISSION"]=f.Submission.ToString("D"),
            ["CP6_OA_BROWSER_SET_NAME"]=f.Source.Set.Body.Name,
            ["CP6_OA_BROWSER_ENVIRONMENT"]=ScopeEnvironment.ToString("D"),["CP6_OA_BROWSER_PREVIOUS"]=withdrawn.TransportCommandId!.Value.ToString("D")});
        var next=browser.RootElement.GetProperty("accepted").Deserialize<RequirementSetWithdrawalResultV1>(PersonalFormOwnerV2.Json)!;
        Assert.Equal(actor.User,next.Fact!.Actor.ActualHandlerId);Assert.Equal(h.User,next.Fact.Actor.RequesterId);Assert.NotEqual(grant.GrantId,next.Fact.Actor.GrantId);
        Assert.Equal(withdrawn.TransportCommandId,next.PreviousCommandId);Assert.Equal(withdrawn.Fact!.BusinessAfterRowVersion,next.Fact.BusinessBeforeRowVersion);
        Assert.Equal(raw,await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_requirement_set_review_control_v1.withdrawals WHERE command_id={withdrawn.TransportCommandId}").SingleAsync());
        var nextRaw=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_requirement_set_review_control_v1.withdrawals WHERE command_id={next.TransportCommandId}").SingleAsync();
        Assert.Equal("WITHDRAWN",(await workflow.WithdrawAsync(f.Submission,"\""+current.SubmissionRowVersion+"\"",nextRaw)).Ack.State);
        Assert.Equal("BLOCKED",(await workflow.OriginalAsync(withdrawn.TransportCommandId.Value)).Ack.State);
        Assert.Equal(next,(await actual.WithdrawalOperationAsync(next.OperationId,false)).Result);
        Assert.Equal(2,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_requirement_set_review_control_v1.withdrawals WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal("DRAFT",(await Sets(h).ReadAsync(f.Source.Set.Reference.Id)).State);
    }
}
