using CP6.Core.Services.Oa;
using CP6.Tests.Platform;
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
        var tenantProfile = await ConnRegistration(h, "CONNECTOR", ConnFixture());
        var appProfile = await ConnRegistration(h, "CONNECTOR", ConnFixture("APP"));
        var dispatchProfile = await ConnRegistration(h, "CONNECTOR", DispatchProfile());
        Guid jobId;
        await using (var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, ConnSettings()))
        {
            var maker = new ConnApi(host, await Login(host, h));
            var checker = new ConnApi(host, await Login(host, "conn-reviewer", "Reviewer1!Test", h.Tenant.ToString("N")));
            var connector = await ConnActivate(maker, checker,
                await ConnCreate(maker, "CONNECTOR", new("browser-dispatch-history", "TENANT", dispatchProfile), "浏览器派发历史"));
            var job = await DispatchJob(h, connector, "browser-dispatch-history");
            jobId = job.Id;
            await using var wire = await OaConnectorDispatchWireFixture.StartAsync();
            using var http = DispatchHttp();
            Assert.Equal(1, await DispatchWorker(h.Db, [DispatchParticipant(h, connector, wire, http)])
                .ScanOnceAsync(DateTime.UtcNow, "browser-dispatch"));
            Assert.Equal("HTTP_ACK", await DispatchLastError(h, job.Id));
            await SetDispatchScope(maker, false);
            await SetDispatchScope(maker, true);
        }
        using var evidence=await BrowserScenario(h,reviewer,"oa-connector-control-browser.mjs","control-browser",ConnSettings(),
            new(){["CP6_CONN_PROFILE"]=profile.ToString(),["CP6_CONN_FLOW"]=flowVersion.ToString(),
                ["CP6_CONN_TENANT_PROFILE"]=tenantProfile.ToString(),["CP6_CONN_APP_PROFILE"]=appProfile.ToString(),
                ["CP6_CONN_DISPATCH_JOB"]=jobId.ToString("D")});
        var result=evidence.RootElement;
        var root=result.GetProperty("root").GetGuid();
        var version=result.GetProperty("version").GetGuid();
        var count=await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_oa_conn_v1.version_control
            WHERE tenant_id={h.Tenant} AND root_id={root} AND version_id={version} AND published AND state='ENABLED'
            """).SingleAsync();
        Assert.Equal(1,count);
        var connectors = result.GetProperty("connectors");
        Assert.Equal(jobId, result.GetProperty("dispatch").GetGuid());
        var tenantRoot = connectors.GetProperty("TENANT").GetProperty("rootId").GetGuid();
        var appRoot = connectors.GetProperty("APP").GetProperty("rootId").GetGuid();
        Assert.NotEqual(tenantRoot, appRoot);
        foreach (var (source, expected) in new[] { ("TENANT", "DISABLED"), ("APP", "ENABLED") })
        {
            var configuration = connectors.GetProperty(source);
            var rootId = configuration.GetProperty("rootId").GetGuid();
            var versionId = configuration.GetProperty("versionId").GetGuid();
            var state = await h.Db.Database.SqlQuery<string>($"""
                SELECT c.state AS "Value" FROM cp6_oa_conn_v1.version_control c
                JOIN cp6_oa_conn_v1.roots r ON (r.tenant_id,r.environment_id,r.id)=(c.tenant_id,c.environment_id,c.root_id)
                WHERE r.tenant_id={h.Tenant} AND r.environment_id={ScopeEnvironment} AND r.id={rootId}
                  AND r.name='browser-same-connector' AND r.source_kind={source} AND c.version_id={versionId}
                """).SingleAsync();
            Assert.Equal(expected, state);
        }
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
