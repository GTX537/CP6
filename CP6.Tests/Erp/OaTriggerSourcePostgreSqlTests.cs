using System.Net;
using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("TIMER")]
    [InlineData("EVENT")]
    public async Task OaConn_timer_and_event_versions_preserve_source_contract_and_deny_reserved_variables(string kind)
    {
        await using var h=await Seed();
        var (flow,published)=await PrepareActivation(h,"typed-trigger");
        var flowId=published.Result!.Publication!.VersionId;
        var activation=(await flow.ActivationPreviewAsync("typed-trigger")).Current;
        await flow.SetActivationAsync("typed-trigger",ActivationCommand(activation,flowId));
        await ConnPeople(h);
        var spec=kind=="TIMER" ? new ConnProfileV1(TimeZone:"UTC",TimeRules:"tzdb-owned-v1",StarterAuthority:"local",StarterId:h.User) :
            new ConnProfileV1(Registration:"event-owned-v1",Schema:"event-v1",SourceModule:"CRM",HookName:"accepted",StarterAuthority:"local",StarterId:h.User);
        var p=await ConnRegistration(h,kind,spec);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,ConnSettings());
        var maker=new ConnApi(host,await Login(host,h));
        var checker=new ConnApi(host,await Login(host,"conn-reviewer","Reviewer1!Test",h.Tenant.ToString("N")));
        var selected=new ConnSelectionV1("typed-"+kind,kind,p,"typed-trigger",flowId);
        var expected=(await maker.Data("creation-contexts?kind=TRIGGER",selected)).GetProperty("expectedVersion").GetString()!;
        var bad=kind=="TIMER" ? new ConnConfigV1("0 12 * * *",JsonSerializer.SerializeToElement(new{starterId="other"})) :
            new ConnConfigV1(VarsMap:new(){["scope"]="event.field"});
        await maker.Reject("triggers",ConnCmd(expected,new ConnDraftV1(selected,"来源",bad)),HttpStatusCode.UnprocessableEntity,kind+"_CONFIG_INVALID");
        var good=kind=="TIMER" ? new ConnConfigV1("0 12 * * *",JsonSerializer.SerializeToElement(new{amount=2})) :
            new ConnConfigV1(VarsMap:new(){["amount"]="event.amount"});
        var saved=await maker.Data("triggers",ConnCmd(expected,new ConnDraftV1(selected,"来源",good)));
        var target=new ConnResource("triggers",saved.GetProperty("rootId").GetGuid(),saved.GetProperty("versionId").GetGuid(),
            saved.GetProperty("result").GetProperty("revision").GetString()!);
        var vote=await ConnReview(maker,checker,target,"PUBLISH");
        var publication=await maker.Data(target.Route+"/publish",ConnCmd(target.Head,new ConnControlInputV1(target.Version,DecisionRef:vote)));
        target=target with{Head=publication.GetProperty("result").GetProperty("revision").GetString()!};
        await ConnActivate(maker,checker,target);
        Assert.Equal(flowId,(await maker.Data(target.Route+"/context")).GetProperty("content").GetProperty("selection").GetProperty("flowVersionId").GetGuid());
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_oa_conn_v1.versions SET display_name='修改历史' WHERE tenant_id={h.Tenant} AND id={target.Version}
            """));
    }
}
