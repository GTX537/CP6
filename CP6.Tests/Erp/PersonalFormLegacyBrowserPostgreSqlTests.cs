using CP6.Core.Services.Oa;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_legacy_draft_adapters_keep_delete_cancel_failure_and_refresh_consistent()
    {
        await using var h=await Seed();var(owner,_,_)=await PreparePersonal(h);var context=await owner.ContextAsync("personal");
        var draft=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData("{\"a\":\"browser original\"}"),"Browser current draft"))));
        foreach(var(menu,path)in new[]{("oa-form-catalog","/oa/form-catalog"),("oa-inbox","/oa/inbox")})
            await h.Db.Sys_Menus.Where(x=>x.MenuKey==menu).ExecuteUpdateAsync(x=>x.SetProperty(m=>m.RoutePath,path));
        var legacy=new Wf_FormDef{Id=Guid.NewGuid(),FormKey="historical-browser",FormName="historical",SchemaJson=FormAuthoringSchema};h.Db.Wf_FormDefs.Add(legacy);await h.Db.SaveChangesAsync();
        var version=new Wf_FormDefVersion{Id=Guid.NewGuid(),FormDefId=legacy.Id,Version=1,Status=1,FormNameSnapshot="historical",SchemaJson=FormAuthoringSchema,Creator="oa-p0-backfill"};h.Db.Wf_FormDefVersions.Add(version);await h.Db.SaveChangesAsync();
        h.Db.Wf_FormDrafts.Add(new(){Id=Guid.NewGuid(),OwnerUserId=h.User,FormDefId=legacy.Id,FormDefVersionId=version.Id,Title="Browser historical source",DataJson="{\"a\":\"private historical value\"}"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        using var result=await BrowserScenario(h,h.User,"oa-legacy-draft-browser.mjs","legacy-draft-browser",new(){["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")},new(){["CP6_LEGACY_DRAFT_BROWSER"]=draft.DraftId.ToString("D")});
        var retained=await owner.DraftAsync(draft.DraftId);Assert.Equal(2,retained.Status);Assert.Contains("changed by actual legacy request",retained.DataJson);
        Assert.Equal("NOT_OBSERVED",(await owner.OriginalAsync(PersonalFormOwnerV2.Delete,draft.DraftId.ToString("D"),result.RootElement.GetProperty("rejectedCommand").GetString()!)).Outcome);
        Assert.Equal("SUCCEEDED",(await owner.OriginalAsync(PersonalFormOwnerV2.Delete,draft.DraftId.ToString("D"),result.RootElement.GetProperty("deleteCommand").GetString()!)).Outcome);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.legacy_commands WHERE tenant_id={h.Tenant} AND legacy_key='browser-second-request'").SingleAsync());
        Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());Assert.Contains("private historical value",(await h.Db.Wf_FormDrafts.AsNoTracking().SingleAsync()).DataJson);
    }
}
