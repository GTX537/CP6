using System.Text.Json;
using CP6.Core.Services.Oa;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_draft_unknown_delete_and_submit_recover_after_refresh_without_active_rows()
    {
        await using var h=await Seed();var(owner,_,_)=await PreparePersonal(h);var context=await owner.ContextAsync("personal");
        var drafts=new Dictionary<string,Guid>();
        foreach(var name in new[]{"delete","submit","unobserved"})
            drafts[name]=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData("{\"a\":\"private draft value\"}"),"Recovery "+name),"prepare-"+name))).DraftId;
        foreach(var(menu,path)in new[]{("oa-form-catalog","/oa/form-catalog"),("oa-inbox","/oa/inbox")})
            await h.Db.Sys_Menus.Where(x=>x.MenuKey==menu).ExecuteUpdateAsync(x=>x.SetProperty(m=>m.RoutePath,path));
        using var result=await BrowserScenario(h,h.User,"oa-draft-recovery-browser.mjs","draft-recovery-browser",new(){["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")},new(){["CP6_RECOVERY_DRAFTS"]=JsonSerializer.Serialize(drafts)});
        Assert.Equal(2,(await owner.DraftAsync(drafts["delete"])).Status);
        Assert.Equal(1,(await owner.DraftAsync(drafts["submit"])).Status);
        Assert.Equal(0,(await owner.DraftAsync(drafts["unobserved"])).Status);
        Assert.Equal(1,await h.Db.Wf_FormDatas.CountAsync());
        foreach(var name in drafts.Keys)
        {
            var command=result.RootElement.GetProperty("commands").GetProperty(name).GetString()!;
            var original=await owner.OriginalAsync(name=="submit"?PersonalFormOwnerV2.Submit:PersonalFormOwnerV2.Delete,drafts[name].ToString("D"),command);
            Assert.Equal(name=="unobserved"?"CLOSED":"SUCCEEDED",original.Outcome);
        }
    }
}
