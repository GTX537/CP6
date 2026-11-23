using CP6.Core.Services.Oa;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaNativeBrowserPostgresFact]
    public async Task OaConn_browser_actual_OA_tab_two_people_and_lost_publish_reply()
    {
        await using var h=await Seed();
        var (owner,published)=await PrepareActivation(h,"browser-conn-flow");
        var current=(await owner.ActivationPreviewAsync("browser-conn-flow")).Current;
        var flowVersion=published.Result!.Publication!.VersionId;
        await owner.SetActivationAsync("browser-conn-flow",ActivationCommand(current,flowVersion));
        var reviewer=await ConnPeople(h);
        await h.Db.Sys_Menus.Where(m=>m.MenuKey=="oa-flow-admin").ExecuteUpdateAsync(s=>s.SetProperty(m=>m.RoutePath,"/oa/flow-admin"));
        var profile=await ConnRegistration(h,"MESSAGE",new(Registration:"browser-message",Schema:"message-v1",VarsSchema:"vars-v1",
            RateLimitPolicy:"rate-v1",StarterAuthority:"isolated",StarterId:h.User));
        using var evidence=await BrowserScenario(h,reviewer,"oa-connector-control-browser.mjs","control-browser",ConnSettings(),
            new(){["CP6_CONN_PROFILE"]=profile.ToString(),["CP6_CONN_FLOW"]=flowVersion.ToString()});
        var result=evidence.RootElement;
        var root=result.GetProperty("root").GetGuid();
        var version=result.GetProperty("version").GetGuid();
        var count=await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_oa_conn_v1.version_control
            WHERE tenant_id={h.Tenant} AND root_id={root} AND version_id={version} AND published AND state='ENABLED'
            """).SingleAsync();
        Assert.Equal(1,count);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_oa_conn_v1.audit WHERE tenant_id={h.Tenant} AND root_id={root} AND operation='TRIGGER_PUBLISH'
            """).SingleAsync());
        Assert.Equal(2,await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_oa_conn_v1.decisions d JOIN cp6_oa_conn_v1.subjects s
            ON (d.tenant_id,d.environment_id,d.subject_id)=(s.tenant_id,s.environment_id,s.id)
            WHERE d.tenant_id={h.Tenant} AND s.root_id={root} AND d.reviewer={reviewer} AND d.decision='APPROVE' AND s.maker={h.User}
            """).SingleAsync());
    }
}
