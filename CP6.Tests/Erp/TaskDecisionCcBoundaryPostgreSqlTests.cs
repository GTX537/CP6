using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task TaskCard_HTTP_SFS_copy_recipient_can_read_but_cannot_decide_even_with_menu_approve_permission()
    {
        await using var h=await Seed();var cc=await GrantDelegate(h,"copy-reader");
        var f=await NativeFormSetup(h,configure:g=>g.Nodes.Single(n=>n.Id=="a").CcUsers=[cc]);
        var c=await f.Owner.TaskContextAsync(f.Task);var raw=SfsCommand(SfsInput(c));
        await CheckCcCannotDecide(h,cc,f.Instance,f.Task,raw);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.votes WHERE task_id={f.Task}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task TaskCard_HTTP_APP_copy_recipient_can_read_frozen_business_but_cannot_borrow_another_duty()
    {
        await using var h=await Seed();var cc=await GrantDelegate(h,"app-copy-reader");
        var graph=JsonNode.Parse(StartSchema)!;graph["nodes"]!.AsArray().Single(n=>n!["id"]!.GetValue<string>()=="a")!["ccUsers"]=new JsonArray(JsonValue.Create(cc));
        var(_,first,_)=await DecisionSetup(h,graph.ToJsonString());await TaskPermission(h,cc);
        var c=await DecisionInput(DecisionActor(h,first));var task=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Id==c.Task);
        await CheckCcCannotDecide(h,cc,task.InstanceId,c.Task,AppWfc(c));
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_decision_v2.commands WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    private static async Task CheckCcCannotDecide(Harness h,Guid actor,Guid instance,Guid task,byte[] raw)
    {
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});
        var cookies=await DocumentReviewerLogin(host,h,actor);
        using var list=await host.Send(BoundRequest(cookies,"/api/oa/v2/inbox/cc"));var items=(await PersonalHttpResult<JsonElement>(list)).GetProperty("items");Assert.NotEqual(0,items.GetArrayLength());
        using var detail=await host.Send(BoundRequest(cookies,"/api/oa/v2/inbox/instances/"+instance));var data=await PersonalHttpResult<JsonElement>(detail);
        Assert.All(data.GetProperty("tasks").EnumerateArray(),t=>Assert.False(t.GetProperty("canRequestDecisionContext").GetBoolean()));
        using var context=await host.Send(BoundRequest(cookies,"/api/oa/v2/tasks/"+task+"/action-context"));Assert.Equal(HttpStatusCode.NotFound,context.StatusCode);
        using var decision=await host.Send(PersonalHttp(cookies,"/api/oa/v2/tasks/"+task+"/decisions",raw));Assert.Equal(HttpStatusCode.NotFound,decision.StatusCode);
        Assert.Equal(0,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Id==task)).Status);
    }
}
