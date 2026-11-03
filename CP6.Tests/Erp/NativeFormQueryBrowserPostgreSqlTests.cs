using CP6.Core.Services.Oa;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_favorite_lost_response_recovery_and_bound_unbound_pinned_history()
    {
        await using var h = await Seed(); var (owner, form, flow) = await PreparePersonal(h); await QueryPermission(h);
        await EngineeringAction(h, "oa-form-catalog", "favorite");
        await h.Db.Sys_Tenants.Where(t => t.Id == h.Tenant).ExecuteUpdateAsync(s => s.SetProperty(t => t.TimeZoneId, "America/New_York"));
        var original = await owner.ContextAsync("personal");
        var unbound = PersonalResult<PersonalFormSubmissionV2>(await owner.SubmitDirectAsync("personal", PersonalCommand(PersonalDirect(original, "{\"a\":\"Original unbound value\"}"), "unbound-history")));
        var next = await form.CreateNextDraftAsync("personal", FormNextDraftCommand((await form.ReadHeadAsync("personal")).Version, original.FormVersionId));
        var edited = await form.SaveAsync("personal", FormAuthoringCommand("renamed", next.ResourceVersion, "Current catalog name"));
        var published = (await form.PublishAsync("personal", FormPublicationCommand(await FormPreview(form, "personal", edited), "second-publication"))).Result!.Publication!;
        await form.SetActivationAsync("personal", FormActivationCommand((await form.ActivationPreviewAsync("personal")).Current, published.VersionId, id: "second-active"));
        await ActiveDependencyFlow(flow, "history-flow", "personal");
        var bound = PersonalResult<PersonalFormSubmissionV2>(await owner.SubmitDirectAsync("personal", PersonalCommand(PersonalDirect(await owner.ContextAsync("personal"), "{\"a\":\"Bound current value\"}"), "bound-history")));
        foreach (var (menu, path) in new[] { ("oa-form-catalog", "/oa/form-catalog"), ("oa-form-search", "/oa/form-search"), ("oa-inbox", "/oa/inbox") })
            await h.Db.Sys_Menus.Where(m => m.MenuKey == menu).ExecuteUpdateAsync(s => s.SetProperty(m => m.RoutePath, path));
        using var result = await BrowserScenario(h, h.User, "oa-catalog-history-browser.mjs", "catalog-history-browser",
            new() { ["ScopeAuthorization__EnvironmentId"] = ScopeEnvironment.ToString("D") },
            new() { ["CP6_HISTORY_UNBOUND"] = unbound.FormDataId.ToString("D"), ["CP6_HISTORY_BOUND"] = bound.FlowInstanceId!.Value.ToString("D"), ["CP6_HISTORY_FORM_PIN"] = original.FormVersionId.ToString("D") });
        Assert.Equal(2, await h.Db.Wf_FormDatas.CountAsync()); Assert.Equal(1, await h.Db.Wf_FlowInstances.CountAsync());
        Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_personal_form_v2.favorites WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(2, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_personal_form_v2.commands WHERE tenant_id={h.Tenant} AND operation='FORM_FAVORITE_SET' AND completed").SingleAsync());
        Assert.Equal(original.FormVersionId, (await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync(f => f.Id == unbound.FormDataId)).FormDefVersionId);
    }
}
