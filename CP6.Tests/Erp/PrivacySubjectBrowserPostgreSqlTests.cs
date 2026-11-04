using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_privacy_subject_file_and_manifest_show_partial_coverage_without_raw_nested_content()
    {
        await using var h=await Seed();
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
