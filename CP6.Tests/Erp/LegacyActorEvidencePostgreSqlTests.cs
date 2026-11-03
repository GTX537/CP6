using System.Net;
using System.Text.Json;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task LegacyActor_HTTP_replaced_assignee_and_overwritten_onbehalf_remain_unverified_without_rewriting_history()
    {
        await using var h=await Seed();await RegisterScope(h);var recipient=await GrantDelegate(h,"legacy-recipient");
        var grant=new Wf_FlowDelegate{GrantorId=h.User,DelegateId=recipient,Scope="legacy-identity",ValidFrom=DateTime.Now.AddMinutes(-5),ValidTo=DateTime.Now.AddHours(1)};
        h.Db.Wf_FlowDelegates.Add(grant);
        var schema=new FlowSchema{Start="s",Nodes=[new(){Id="s",Type="start"},new(){Id="a",Type="approval",ApproverStrategy="Specified",ApproverUserId=h.User},new(){Id="e",Type="end"}],Edges=[new(){From="s",To="a"},new(){From="a",To="e"}]};
        h.Db.Wf_FlowDefs.Add(new(){FlowKey="legacy-identity",FlowName="Historical identity",SchemaJson=JsonSerializer.Serialize(schema),Version=1,Enable=true});
        await h.Db.SaveChangesAsync();await CP6.Tests.Wf.LegacyDefinitionFixture.ArrangePublishedHistory(h.Db);
        var engine=new FlowEngine(h.Db,new ApproverResolver(h.Db));var instance=await engine.SubmitAsync("legacy-identity",h.User,"{}");
        var task=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.InstanceId==instance);
        Assert.Equal(recipient,task.AssigneeId);
        var sent=await h.Db.Wf_FlowFormTos.AsNoTracking().SingleAsync(t=>t.InstanceId==instance);
        Assert.Equal(h.User,sent.OnBehalfOfId);Assert.Null(sent.ActualHandlerId);Assert.Null(sent.NativeTaskId);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});
        var cookies=await DocumentReviewerLogin(host,h,recipient);
        async Task<JsonElement> Read()
        {
            using var response=await host.Send(BoundRequest(cookies,"/api/oa/inbox/detail/"+instance));
            var detail=await PersonalHttpResult<JsonElement>(response);var row=Assert.Single(detail.GetProperty("timeline").EnumerateArray());
            Assert.Equal("LEGACY_ACTOR_INCOMPLETE",row.GetProperty("actorEvidenceStatus").GetString());
            Assert.Equal(sent.Id,row.GetProperty("formToId").GetGuid());
            Assert.Equal(recipient,row.GetProperty("expectedHandlerId").GetGuid());return row.Clone();
        }
        var before=JsonSerializer.Serialize(sent);var pending=await Read();Assert.Equal(h.User,pending.GetProperty("onBehalfOfId").GetGuid());
        Assert.Equal(before,JsonSerializer.Serialize(await h.Db.Wf_FlowFormTos.AsNoTracking().SingleAsync(t=>t.Id==sent.Id)));
        // Today's grant cannot attest the original delivery; the old chain remains separately usable.
        await h.Db.Wf_FlowDelegates.Where(g=>g.Id==grant.Id).ExecuteUpdateAsync(u=>u.SetProperty(g=>g.Enable,false));
        await Read();await engine.ActAsync(task.Id,recipient,true,"old chain result");
        var handled=await Read();Assert.Equal(recipient,handled.GetProperty("actualHandlerId").GetGuid());Assert.Equal(JsonValueKind.Null,handled.GetProperty("onBehalfOfId").ValueKind);
        var stored=await h.Db.Wf_FlowFormTos.AsNoTracking().SingleAsync(t=>t.Id==sent.Id);
        Assert.Equal(recipient,stored.ActualHandlerId);Assert.Null(stored.OnBehalfOfId);
        using var native=await host.Send(BoundRequest(cookies,"/api/oa/v2/tasks/"+task.Id+"/action-context"));
        Assert.True(native.StatusCode==HttpStatusCode.NotFound,await native.Content.ReadAsStringAsync());
        var outsider=await GrantDelegate(h,"unrelated-legacy-reader");var stranger=await DocumentReviewerLogin(host,h,outsider);
        using var forbidden=await host.Send(BoundRequest(stranger,"/api/oa/inbox/detail/"+instance));Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode);
        Assert.Equal(JsonSerializer.Serialize(stored),JsonSerializer.Serialize(await h.Db.Wf_FlowFormTos.AsNoTracking().SingleAsync(t=>t.Id==sent.Id)));
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_decision_v2.votes WHERE tenant_id={h.Tenant}").SingleAsync());
    }
}
