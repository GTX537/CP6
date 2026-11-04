using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Plm;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_requirement_impact_request_assessment_and_lost_response_recover_original_without_input_replacement()
    {
        await using var h=await Seed();var f=await IterationSetup(h);await GrantImpact(h);var candidate=await ImpactSuccessor(h,f);
        foreach(var resource in new[]{"plm-requirement-set","plm-engineering-iteration"})
        {
            var route=resource=="plm-requirement-set"?"/plm/requirement-set":"/plm/engineering-iteration";
            await h.Db.Sys_Menus.Where(x=>x.MenuKey==resource).ExecuteUpdateAsync(x=>x.SetProperty(m=>m.RoutePath,route));
            var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey==resource);
            if(!await h.Db.Sys_RoleMenus.AnyAsync(x=>x.MenuId==menu.MenuId&&x.RoleId==72))h.Db.Sys_RoleMenus.Add(new(){MenuId=menu.MenuId,RoleId=72});
        }
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        using var browser=await BrowserScenario(h,h.User,"requirement-impact-browser.mjs","requirement-impact-browser",SetHostSettings(),
            new(){["CP6_OA_BROWSER_REQUIREMENT_SET"]=candidate.Reference.Id.ToString("D")});
        var result=browser.RootElement.GetProperty("accepted").Deserialize<EngineeringIterationResultV1>(PersonalFormOwnerV2.Json)!;
        Assert.Equal(result,(await Iterations(h).OperationAsync(result.OperationId,false)).Result);
        Assert.Equal("KEEP_CURRENT",Assert.Single((await Iterations(h).ImpactAsync(f.Iteration.Reference.Id)).Cases).State);
        Assert.Equal(f.Iteration.OriginalJson,(await Iterations(h).ReadAsync(f.Iteration.Reference.Id)).OriginalJson);
    }
}
