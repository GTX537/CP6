using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_privacy_execution_confirmation_original_receipt_and_subject_session_revocation()
    {
        await using var h=await Seed();var setup=await PrivacyDispositionSetup(h);await EngineeringAction(h,"privacy-disposition","execute");
        var home=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="erp-order-price-correction");home.RoutePath="/order-price-correction";
        h.Db.Sys_RoleMenus.AddRange(new(){RoleId=72,MenuId=home.MenuId},new(){RoleId=73,MenuId=home.MenuId});
        await h.Db.Sys_Users.Where(x=>x.Id==setup.Target).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Password,new CP6.Core.Services.Sys.BCryptPasswordHasher(4).Hash("Price1!Password")));
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        using var result=await BrowserScenario(h,setup.Approver.User,"privacy-execution-browser.mjs","privacy-execution-browser",PrivacyExecutionEnvironment(h.Tenant),
            new(){["CP6_PRIVACY_TARGET"]=setup.Target.ToString("D"),["CP6_PRIVACY_POLICY"]=setup.Policy.ToString("D")});
        var user=await h.Db.Sys_Users.AsNoTracking().SingleAsync(x=>x.Id==setup.Target);Assert.Null(user.Email);Assert.False(user.Enable);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_disposition_v1.executions WHERE target_tenant={h.Tenant}").SingleAsync());
    }
}
