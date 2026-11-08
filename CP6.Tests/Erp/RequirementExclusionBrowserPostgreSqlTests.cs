using System.Text.Json;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Oa;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_requirement_optional_exclusion_cancel_and_lost_reply_preserve_source_and_original()
    {
        await using var h=await Seed();var f=await ExclusionSetup(h);
        foreach(var resource in new[]{"plm-requirement-set","plm-engineering-requirement"})
        {
            var route=resource=="plm-requirement-set"?"/plm/requirement-set":"/plm/engineering-requirement";
            await h.Db.Sys_Menus.Where(x=>x.MenuKey==resource).ExecuteUpdateAsync(x=>x.SetProperty(m=>m.RoutePath,route));
            var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey==resource);
            if(!await h.Db.Sys_RoleMenus.AnyAsync(x=>x.MenuId==menu.MenuId&&x.RoleId==72))h.Db.Sys_RoleMenus.Add(new(){MenuId=menu.MenuId,RoleId=72});
        }
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        using var browser=await BrowserScenario(h,h.User,"requirement-exclusion-browser.mjs","requirement-exclusion-browser",SetHostSettings(),new(){["CP6_OA_BROWSER_REQUIREMENT_SET"]=f.Set.Reference.Id.ToString("D")});
        var accepted=browser.RootElement.GetProperty("accepted").Deserialize<RequirementSetResultV1>(PersonalFormOwnerV2.Json)!;
        Assert.Equal(accepted,(await Sets(h).OperationAsync(accepted.OperationId,false)).Result);
        var saved=await Sets(h).ReadAsync(f.Set.Reference.Id);Assert.Equal("ACCEPTED",Assert.Single(saved.ExclusionStatus!).State);
        Assert.Equal(f.Set.OriginalJson,(await Sets(h).ReadAsync(f.Set.Reference.Id,"1")).OriginalJson);
        Assert.Equal(f.Member.Source.OriginalJson,Assert.Single(saved.MemberStatus!).Source.OriginalJson);
    }
}
