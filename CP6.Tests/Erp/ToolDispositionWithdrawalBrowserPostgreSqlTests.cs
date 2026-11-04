using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wms;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_tool_BLOCKED_reauthorization_and_lost_response_recovery_preserve_original_facts()
    {
        await using var h=await Seed();var f=await ToolWithdrawalSetup(h);var service=await GrantToolWithdrawRelay(h,f);
        await EngineeringAction(h,"wms-plate-mold","dispose-delegate");
        var actor=DecisionActor(h,await GrantDelegate(h,"tool-business-delegate"));var owner=ToolApproval(h);var actual=ToolApproval(actor);
        var input=ToolGrantInput(f.Submission,actor.User);var grant=(await owner.GrantBusinessAsync(f.Tool,input)).Grant;
        var context=await actual.WithdrawalContextAsync(f.Tool,f.Submission);
        var withdrawn=await actual.WithdrawAsync(f.Tool,f.Submission,"\""+context.ControlRowVersion+"\"",new(Guid.NewGuid(),"Initial exact-round delegated withdrawal"),behalf:h.User);
        var raw=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_tool_approval_control_v1.withdrawals WHERE command_id={withdrawn.TransportCommandId}").SingleAsync();
        await owner.GrantBusinessAsync(f.Tool,input with{OperationId=Guid.NewGuid(),GrantId=grant.GrantId,ExpectedVersion=grant.Version,Enabled=false});
        var workflow=ToolWithdrawals(h,f,service);var current=await workflow.ContextAsync(f.Submission);
        Assert.Equal("BLOCKED",(await workflow.WithdrawAsync(f.Submission,"\""+current.SubmissionRowVersion+"\"",raw)).Ack.State);
        var password=new BCryptPasswordHasher(4).Hash("Price1!Password");
        await h.Db.Sys_Users.Where(x=>x.Id==actor.User).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.Password,password));
        await h.Db.Sys_Menus.Where(x=>x.MenuKey=="wms-plate-mold").ExecuteUpdateAsync(x=>x.SetProperty(m=>m.RoutePath,"/wms/plate-mold"));
        var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="wms-plate-mold");
        if(!await h.Db.Sys_RoleMenus.AnyAsync(x=>x.MenuId==menu.MenuId&&x.RoleId==72))h.Db.Sys_RoleMenus.Add(new(){MenuId=menu.MenuId,RoleId=72});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        using var browser=await BrowserScenario(h,actor.User,"tool-withdrawal-browser.mjs","tool-withdrawal-browser",new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString()},new(){
            ["CP6_OA_BROWSER_TOOL"]=f.Tool.ToString("D"),["CP6_OA_BROWSER_SUBMISSION"]=f.Submission.ToString("D"),
            ["CP6_OA_BROWSER_ENVIRONMENT"]=ScopeEnvironment.ToString("D"),["CP6_OA_BROWSER_PREVIOUS"]=withdrawn.TransportCommandId!.Value.ToString("D")});
        var next=browser.RootElement.GetProperty("accepted").Deserialize<ToolWithdrawalResultV1>(PersonalFormOwnerV2.Json)!;
        Assert.Equal(actor.User,next.Fact!.Actor.ActualHandlerId);Assert.Equal(h.User,next.Fact.Actor.RequesterId);Assert.NotEqual(grant.GrantId,next.Fact.Actor.GrantId);
        Assert.Equal(withdrawn.TransportCommandId,next.PreviousCommandId);Assert.Equal(withdrawn.Fact!.BusinessAfterRowVersion,next.Fact.BusinessBeforeRowVersion);
        Assert.Equal(raw,await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_tool_approval_control_v1.withdrawals WHERE command_id={withdrawn.TransportCommandId}").SingleAsync());
        var nextRaw=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_tool_approval_control_v1.withdrawals WHERE command_id={next.TransportCommandId}").SingleAsync();
        Assert.Equal("WITHDRAWN",(await workflow.WithdrawAsync(f.Submission,"\""+current.SubmissionRowVersion+"\"",nextRaw)).Ack.State);
        Assert.Equal("BLOCKED",(await workflow.OriginalAsync(withdrawn.TransportCommandId.Value)).Ack.State);
        Assert.Equal(next,(await actual.WithdrawalOperationAsync(next.OperationId,false)).Result);
        Assert.Equal(2,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_tool_approval_control_v1.withdrawals WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal("BLOCKED_PENDING_DISPOSITION",(await Disposition(h).ContextAsync(f.Tool)).State);Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());
    }
}
