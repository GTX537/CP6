using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_privacy_disposition_independent_approval_hold_and_original_recovery()
    {
        await using var h=await Seed();var setup=await PrivacyDispositionSetup(h);
        var home=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="erp-order-price-correction");home.RoutePath="/order-price-correction";
        h.Db.Sys_RoleMenus.AddRange(new(){RoleId=72,MenuId=home.MenuId},new(){RoleId=73,MenuId=home.MenuId});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        using var result=await BrowserScenario(h,setup.Approver.User,"privacy-disposition-browser.mjs","privacy-disposition-browser",SetHostSettings(),
            new(){["CP6_PRIVACY_TARGET"]=setup.Target.ToString("D"),["CP6_PRIVACY_POLICY"]=setup.Policy.ToString("D")});
        var plan=await PrivacyDispositionOwner(h).PlanAsync(result.RootElement.GetProperty("planId").GetGuid(),PrivacySession(h));
        Assert.Equal("APPROVED",plan.State);Assert.False(plan.ApprovalCurrent);Assert.Contains("ACTIVE_HOLD",plan.CurrentBlockers);
        var target=await h.Db.Sys_Users.AsNoTracking().SingleAsync(x=>x.Id==setup.Target);
        Assert.Equal("simulated@example.invalid",target.Email);Assert.Equal("original profile remains",target.NickName);Assert.True(target.Enable);
    }
}
