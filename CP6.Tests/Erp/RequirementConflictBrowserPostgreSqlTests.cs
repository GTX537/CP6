using System.Text.Json;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Oa;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_requirement_conflict_decision_cancel_and_lost_response_recovery_preserve_both_originals()
    {
        await using var h=await Seed();var f=await ConflictSetup(h);
        foreach(var resource in new[]{"plm-requirement-set","plm-engineering-requirement"})
        {
            var route=resource=="plm-requirement-set"?"/plm/requirement-set":"/plm/engineering-requirement";
            await h.Db.Sys_Menus.Where(x=>x.MenuKey==resource).ExecuteUpdateAsync(x=>x.SetProperty(m=>m.RoutePath,route));
            var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey==resource);
            if(!await h.Db.Sys_RoleMenus.AnyAsync(x=>x.MenuId==menu.MenuId&&x.RoleId==72))h.Db.Sys_RoleMenus.Add(new(){MenuId=menu.MenuId,RoleId=72});
        }
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        using var browser=await BrowserScenario(h,h.User,"requirement-conflict-browser.mjs","requirement-conflict-browser",SetHostSettings(),new(){["CP6_OA_BROWSER_REQUIREMENT_SET"]=f.Set.Reference.Id.ToString("D")});
        var accepted=browser.RootElement.GetProperty("accepted").Deserialize<RequirementSetResultV1>(PersonalFormOwnerV2.Json)!;
        Assert.Equal(accepted,(await Sets(h).OperationAsync(accepted.OperationId,false)).Result);
        var saved=await Sets(h).ReadAsync(f.Set.Reference.Id);Assert.Equal("RESOLVED",Assert.Single(saved.ConflictStatus!).State);
        Assert.Equal(f.Set.OriginalJson,(await Sets(h).ReadAsync(f.Set.Reference.Id,"1")).OriginalJson);
        Assert.Equal(f.Left.Source.OriginalJson,saved.MemberStatus![0].Source.OriginalJson);Assert.Equal(f.RightSource.OriginalJson,saved.MemberStatus[1].Source.OriginalJson);
    }
}
