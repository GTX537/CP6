using System.Net;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static HttpRequestMessage PersonalHttp(Dictionary<string,string> cookies,string path,byte[] raw,bool csrf=true)
    {
        var request=BoundRequest(cookies,path,new{},csrf);request.Content=new StringContent(Encoding.UTF8.GetString(raw),Encoding.UTF8,"application/json");return request;
    }
    private static async Task<T> PersonalHttpResult<T>(HttpResponseMessage response)
    {
        var text=await response.Content.ReadAsStringAsync();Assert.True(response.IsSuccessStatusCode,text);
        using var doc=JsonDocument.Parse(text);return doc.RootElement.GetProperty("data").Deserialize<T>(PersonalFormOwnerV2.Json)!;
    }
    [CurrentPermissionPostgresFact]
    public async Task PersonalForm_HTTP_actual_session_CSRSF_exact_draft_bound_submit_task_and_legacy_fences()
    {
        await using var h=await Seed();var(owner,_,flow)=await PreparePersonal(h,"""{"fields":[{"name":"a","type":"input"},{"name":"person","type":"user"}]}""");
        var schema=JsonSerializer.Serialize(new{start="s",nodes=new object[]{new{id="s",type="start"},new{id="a",type="approval",approverStrategy="Specified",approverUserId=h.User},new{id="e",type="end"}},edges=new[]{new{from="s",to="a"},new{from="a",to="e"}}});
        var f=await flow.SaveAsync("http-personal",DependentCommand("personal",schema:schema));
        var p=(await flow.PublishAsync("http-personal",PublicationCommand(await Preview(flow,"http-personal",f)))).Result!.Publication!;
        await flow.SetActivationAsync("http-personal",ActivationCommand((await flow.ActivationPreviewAsync("http-personal")).Current,p.VersionId));
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});
        var cookies=await Login(host,h);const string root="/api/oa/v2";
        using var contextResponse=await host.Send(BoundRequest(cookies,root+"/forms/personal/initiation-context"));
        var context=await PersonalHttpResult<PersonalFormContextV2>(contextResponse);Assert.True(contextResponse.Headers.CacheControl?.NoStore);
        using var referenceResponse=await host.Send(BoundRequest(cookies,root+"/forms/personal/references/person"));
        Assert.Equal(h.User,Assert.Single(await PersonalHttpResult<PersonalFormReferenceV2[]>(referenceResponse)).Id);
        var raw=PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData("{\"a\":\"HTTP draft\"}"),"HTTP title"));
        using var csrf=await host.Send(PersonalHttp(cookies,root+"/forms/personal/drafts",raw,false));Assert.Equal(HttpStatusCode.Forbidden,csrf.StatusCode);
        using var created=await host.Send(PersonalHttp(cookies,root+"/forms/personal/drafts",raw));var draft=PersonalResult<PersonalFormDraftV2>(await PersonalHttpResult<PersonalFormResultV2>(created));
        using var submitted=await host.Send(PersonalHttp(cookies,root+"/drafts/"+draft.DraftId+"/submit",PersonalCommand(PersonalSubmit(context),"submit",draft.RowVersion)));
        var form=PersonalResult<PersonalFormSubmissionV2>(await PersonalHttpResult<PersonalFormResultV2>(submitted));Assert.Equal(context.FlowVersionId,form.FlowDefVersionId);
        var task=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync();
        using var detail=await host.Send(BoundRequest(cookies,"/api/oa/inbox/detail/"+form.FlowInstanceId));Assert.Equal(HttpStatusCode.Forbidden,detail.StatusCode);
        Assert.DoesNotContain("HTTP draft",await detail.Content.ReadAsStringAsync());
        using var pending=await host.Send(BoundRequest(cookies,"/api/oa/inbox/pending"));Assert.Empty(await PersonalHttpResult<JsonElement[]>(pending));
        using var running=await host.Send(BoundRequest(cookies,"/api/oa/inbox/running"));Assert.Empty(await PersonalHttpResult<JsonElement[]>(running));
        using var stats=await host.Send(BoundRequest(cookies,"/api/oa/inbox/stats"));Assert.Equal(0,(await PersonalHttpResult<JsonElement>(stats)).GetProperty("pendingCount").GetInt32());
        using var oldTodos=await host.Send(BoundRequest(cookies,"/api/wf/my-todos"));Assert.Empty(await PersonalHttpResult<JsonElement[]>(oldTodos));
        using var oldApplications=await host.Send(BoundRequest(cookies,"/api/wf/my-applications"));Assert.Empty(await PersonalHttpResult<JsonElement[]>(oldApplications));
        using var oldDecision=await host.Send(BoundRequest(cookies,"/api/oa/tasks/"+task.Id+"/decision",new{decision="approve"}));Assert.Equal(HttpStatusCode.UnprocessableEntity,oldDecision.StatusCode);
        using var taskResponse=await host.Send(BoundRequest(cookies,root+"/personal-forms/tasks/"+task.Id+"/context"));
        var current=await PersonalHttpResult<PersonalFormTaskContextV2>(taskResponse);Assert.Contains("HTTP draft",current.DataJson);
        var input=new ApprovalTaskDecisionInputV2(Guid.NewGuid(),"approve","HTTP verified",current.Task.TaskRowVersion,current.Task.InstanceRowVersion);
        using var missing=await host.Send(BoundRequest(cookies,root+"/personal-forms/tasks/"+task.Id+"/decision",input));Assert.Equal((HttpStatusCode)428,missing.StatusCode);
        using var decision=BoundRequest(cookies,root+"/personal-forms/tasks/"+task.Id+"/decision",input);decision.Headers.TryAddWithoutValidation("If-Match","\""+input.TaskRowVersion+"\"");
        using var decided=await host.Send(decision);var accepted=await PersonalHttpResult<PersonalFormTaskResultV2>(decided);Assert.Equal(1,accepted.Task!.InstanceStatus);
        using var original=await host.Send(BoundRequest(cookies,root+"/personal-forms/tasks/"+task.Id+"/commands/"+input.CommandId));
        Assert.Equal(accepted,await PersonalHttpResult<PersonalFormTaskResultV2>(original));
        Assert.Equal("FORM_DECISION_OWNER_REQUIRED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>new FlowEngine(h.Db,new ApproverResolver(h.Db)).ActAsync(task.Id,h.User,true))).Message);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Wf_FormDatas.Where(x=>x.Id==form.FormDataId).ExecuteUpdateAsync(x=>x.SetProperty(f=>f.DataJson,"{}")));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Wf_FlowTasks.Where(x=>x.Id==task.Id).ExecuteUpdateAsync(x=>x.SetProperty(t=>t.Status,0)));
    }
    [CurrentPermissionPostgresFact]
    public async Task PersonalForm_HTTP_original_closed_command_and_current_field_projection_survive_write_revoke()
    {
        const string schema="""{"fields":[{"name":"visible","type":"number"},{"name":"secret","type":"input"}]}""";
        await using var h=await Seed();var(owner,_,_)=await PreparePersonal(h,schema);var context=await owner.ContextAsync("personal");
        var draft=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData("{\"visible\":9007199254740993.01,\"secret\":\"do not leak\"}"),null))));
        var policy=(await owner.PolicyAsync("personal"))!;await owner.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),true,true,[new(72,new(){["secret"]="hidden"})],policy.RetentionPolicyRef,30,true,"current disclosure"));
        await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="add"||x.ActionCode=="submit").ExecuteDeleteAsync();
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});
        var cookies=await Login(host,h);const string original="/api/oa/v2/personal-forms/commands/FORM_DRAFT_CREATE/personal/create";
        using var response=await host.Send(BoundRequest(cookies,original));var saved=PersonalResult<PersonalFormDraftV2>(await PersonalHttpResult<PersonalFormResultV2>(response));
        Assert.DoesNotContain("do not leak",saved.DataJson);Assert.DoesNotContain("secret",saved.SchemaJson);Assert.Contains("9007199254740993.01",saved.DataJson);
        var close=BoundRequest(cookies,"/api/oa/v2/personal-forms/commands/FORM_DIRECT_SUBMIT/personal/not-sent/close",new{});
        using var closed=await host.Send(close);Assert.Equal("CLOSED",(await PersonalHttpResult<PersonalFormResultV2>(closed)).Outcome);
        using var originalClosed=await host.Send(BoundRequest(cookies,"/api/oa/v2/personal-forms/commands/FORM_DIRECT_SUBMIT/personal/not-sent"));Assert.Equal("CLOSED",(await PersonalHttpResult<PersonalFormResultV2>(originalClosed)).Outcome);
        using var malformed=await host.Send(PersonalHttp(cookies,"/api/oa/v2/forms/personal/submissions",Encoding.UTF8.GetBytes("{\"commandId\":\"a\",\"commandId\":\"b\"}")));
        Assert.Equal(HttpStatusCode.BadRequest,malformed.StatusCode);Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());
    }
}
