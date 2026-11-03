using System.Net;
using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task FormOptions_actual_HTTP_exact_candidates_direct_submit_and_current_field_revocation()
    {
        await using var h=await Seed();var type=await OptionDictionary(h);var(owner,forms,_)=await PreparePersonal(h,OptionFormSchema(type));var context=await owner.ContextAsync("personal");
        await forms.SaveAsync("option-sources",FormAuthoringCommand(id:"ordinary-form-key"));
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});var cookies=await Login(host,h);
        var query=new FormOptionQueryV1(new("FORM",context.FormVersionId,"personal"),["choice"]);const string route="/api/oa/v2/form-options/query";
        using var csrf=await host.Send(BoundRequest(cookies,route,query,false));Assert.Equal(HttpStatusCode.Forbidden,csrf.StatusCode);
        using var response=await host.Send(BoundRequest(cookies,route,query));var options=await PersonalHttpResult<FormOptionPageV1>(response);
        Assert.Equal(new[]{"A","a","1","1.0"},options.Items.Select(o=>o.Value));Assert.True(response.Headers.CacheControl?.NoStore);
        using var types=await host.Send(BoundRequest(cookies,"/api/oa/v2/form-option-sources"));Assert.Contains(await PersonalHttpResult<FormOptionTypeV1[]>(types),x=>x.Id==type);
        using var namedHead=await host.Send(BoundRequest(cookies,"/api/oa/v2/definitions/FORM/option-sources"));Assert.Equal(HttpStatusCode.OK,namedHead.StatusCode);Assert.Contains("\"key\":\"option-sources\"",await namedHead.Content.ReadAsStringAsync());
        using var forged=await host.Send(PersonalHttp(cookies,"/api/oa/v2/forms/personal/submissions",PersonalCommand(PersonalDirect(context,"{\"choice\":\"forged\"}"),"http-forged")));
        Assert.Equal(HttpStatusCode.UnprocessableEntity,forged.StatusCode);Assert.Contains("FORM_OPTION_VALUE_UNAVAILABLE",await forged.Content.ReadAsStringAsync());
        using var submit=await host.Send(PersonalHttp(cookies,"/api/oa/v2/forms/personal/submissions",PersonalCommand(PersonalDirect(context,"{\"choice\":\"1.0\"}"),"http-exact")));
        Assert.Equal("SUCCEEDED",(await PersonalHttpResult<PersonalFormResultV2>(submit)).Outcome);
        var policy=(await owner.PolicyAsync("personal"))!;await owner.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),true,true,[new(72,new(){{"choice","hidden"}})],policy.RetentionPolicyRef,30,true,"revoke current field"));
        using var hidden=await host.Send(BoundRequest(cookies,route,query));Assert.Equal(HttpStatusCode.Forbidden,hidden.StatusCode);Assert.DoesNotContain("Choice ",await hidden.Content.ReadAsStringAsync());
        Assert.Equal("1.0",JsonDocument.Parse((await h.Db.Wf_FormDatas.SingleAsync()).DataJson).RootElement.GetProperty("choice").GetString());
    }
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_FORM_shared_source_designer_publication_selection_submission_and_original_history()
    {
        await using var h=await Seed();var type=await OptionDictionary(h);await PreparePersonal(h,"""{"fields":[{"name":"choice","type":"select","options":[{"label":"Original static","value":"original"}]},{"name":"checks","type":"checkbox","default":["A"],"options":[{"label":"Upper","value":"A"},{"label":"Lower","value":"a"}]}]}""");await QueryPermission(h);
        foreach(var(menu,path)in new[]{("oa-form-catalog","/oa/form-catalog"),("oa-form-search","/oa/form-search"),("oa-designer","/oa/designer"),("oa-flow-admin","/oa/flow-admin")})
            await h.Db.Sys_Menus.Where(m=>m.MenuKey==menu).ExecuteUpdateAsync(s=>s.SetProperty(m=>m.RoutePath,path));
        using var result=await BrowserScenario(h,h.User,"oa-form-options-browser.mjs","form-options-browser",new(){["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")},new(){["CP6_OPTION_TYPE_ID"]=type.ToString()});
        var data=await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync();Assert.Equal("1.0",JsonDocument.Parse(data.DataJson).RootElement.GetProperty("choice").GetString());
        Assert.Equal(new[]{"A","a"},JsonDocument.Parse(data.DataJson).RootElement.GetProperty("checks").EnumerateArray().Select(v=>v.GetString()));
        Assert.Equal(2,data.FormVersion);Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());
        var original=await FormAuthoring(h).VersionsAsync("personal");Assert.Contains("optionSourceV1",original[0].Fields["SchemaJson"]);Assert.Contains("Original static",original[1].Fields["SchemaJson"]);
    }
}
