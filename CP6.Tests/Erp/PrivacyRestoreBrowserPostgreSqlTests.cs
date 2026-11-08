using System.Net;
using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_privacy_restore_independent_approval_cancel_and_lost_execute_reply_recover_without_reexecution()
    {
        await using var h=await Seed();var setup=await PrivacyDispositionSetup(h);await EngineeringAction(h,"privacy-disposition","execute");await RestorePermissions(h);
        var home=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="erp-order-price-correction");home.RoutePath="/order-price-correction";
        h.Db.Sys_RoleMenus.AddRange(new(){RoleId=72,MenuId=home.MenuId},new(){RoleId=73,MenuId=home.MenuId});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);
        var runtime=await RestoreRuntime(h);await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant},runtime);var actor=h with{Db=db};var approver=setup.Approver with{Db=db};
        var privacy=await PrivacyApprovedPlan(actor,setup.Target,setup.Policy,approver);await PrivacyDispositionOwner(actor).ExecuteAsync(privacy.Plan,PrivacyExecuteCommand(privacy),PrivacySession(actor));
        await using var targetHost=await AuthenticationApiProcess.Start(candidate.Connection,await RestoreHostSettings(actor,new(){ControlConnectionString=db.Database.GetConnectionString()!}));
        using(var closed=await targetHost.Send(new HttpRequestMessage(HttpMethod.Get,"api/auth/profile")))Assert.Equal(HttpStatusCode.ServiceUnavailable,closed.StatusCode);
        var options=RestoreRegistration(candidate);
        using var result=await BrowserScenario(actor,approver.User,"privacy-restore-browser.mjs","privacy-restore-browser",await RestoreHostSettings(actor,options),new(){["CP6_PRIVACY_RESTORE_CANDIDATE"]=candidate.Id.ToString("D")});
        var plan=await PrivacyRestorer(actor,options,runtime).RestorePlanAsync(result.RootElement.GetProperty("planId").GetGuid(),PrivacySession(actor));
        Assert.Equal("APPLIED",plan.Outcome!.State);Assert.Equal("PARTIAL",plan.Outcome.AggregateState);
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);Assert.Null(await target.Sys_Users.Where(x=>x.Id==setup.Target).Select(x=>x.Email).SingleAsync());
        var cookies=await Login(targetHost,h);using var admitted=await targetHost.Send(OperationsRequest(cookies,HttpMethod.Get,"/api/auth/profile"));Assert.Equal(HttpStatusCode.OK,admitted.StatusCode);
    }
}
