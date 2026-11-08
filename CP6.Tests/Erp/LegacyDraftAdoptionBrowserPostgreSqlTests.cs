using CP6.Core.Services.Oa;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_legacy_source_mapping_and_forward_adoption_recover_original_without_recopying()
    {
        const string schema="""{"fields":[{"name":"amount","type":"number"},{"name":"private","type":"input"}]}""";
        const string data="""{"amount":9007199254740993.0001,"private":"must-never-appear-in-browser"}""";
        await using var h=await Seed();var(owner,_,_)=await PreparePersonal(h,schema);await LegacyAdoptionAuthority(h);var context=await owner.ContextAsync("personal");var source=await LegacyAdoptionSource(h,schema,data);
        var policy=(await owner.PolicyAsync("personal"))!;await owner.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),true,true,[new(72,new(){["private"]="hidden"})],policy.RetentionPolicyRef,30,true,"browser current source projection"));
        foreach(var(menu,path)in new[]{("oa-designer","/oa/designer"),("oa-inbox","/oa/inbox"),("oa-form-catalog","/oa/form-catalog")})await h.Db.Sys_Menus.Where(x=>x.MenuKey==menu).ExecuteUpdateAsync(x=>x.SetProperty(m=>m.RoutePath,path));
        var legacyKey=await h.Db.Wf_FormDefs.Where(x=>x.Id==source.Form).Select(x=>x.FormKey).SingleAsync();
        using var result=await BrowserScenario(h,h.User,"oa-legacy-adoption-browser.mjs","legacy-adoption-browser",new(){["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")},new(){["CP6_LEGACY_SOURCE_KEY"]=legacyKey,["CP6_LEGACY_SOURCE_DRAFT"]=source.Draft.ToString("D"),["CP6_LEGACY_SOURCE_VERSION"]=source.Version.ToString("D"),["CP6_LEGACY_TARGET_VERSION"]=context.FormVersionId.ToString("D")});
        var copied=result.RootElement.GetProperty("draftId").GetGuid();Assert.Equal(data,await h.Db.Database.SqlQuery<string>($"SELECT data_json AS \"Value\" FROM cp6_personal_form_v2.drafts WHERE id={copied}").SingleAsync());
        Assert.Equal(data,await h.Db.Wf_FormDrafts.Where(x=>x.Id==source.Draft).Select(x=>x.DataJson).SingleAsync());
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_personal_form_v2.legacy_adoptions WHERE source_draft_id={source.Draft}").SingleAsync());Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());
    }
}
