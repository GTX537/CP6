using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_privacy_durable_generation_recovery_download_and_revocation_show_partial_scope()
    {
        await using var h=await Seed();await PrivacyAuthority(h);
        var home=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="erp-order-price-correction");
        home.RoutePath="/order-price-correction";h.Db.Sys_RoleMenus.Add(new(){RoleId=72,MenuId=home.MenuId});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await h.Db.Sys_Users.Where(x=>x.Id==h.User).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsPlatformAdmin,true));
        h.Db.Sys_SecurityLogs.Add(new(){UserId=h.User,UserName="historic-browser-name",CreatedAt=DateTime.UtcNow,
            ContextJson="{\"unclassified\":\"browser-excluded-content\"}"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        using var result=await BrowserScenario(h,h.User,"privacy-subject-browser.mjs","privacy-subject-browser",SetHostSettings());
        Assert.Equal(h.User,result.RootElement.GetProperty("subjectId").GetGuid());
    }
}
