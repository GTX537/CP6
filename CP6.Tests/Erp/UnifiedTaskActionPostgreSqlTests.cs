using System.Net;
using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static byte[] AppWfc((ApprovalTaskDecisionInputV2 Input,Guid Task) c,string? correlation=null)
        =>System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new{commandId=c.Input.CommandId.ToString("D"),expectedVersion=c.Input.TaskRowVersion,
            body=new{kind="APP",taskId=c.Task,decision=c.Input.Decision.ToUpperInvariant(),comment=c.Input.Comment,expectedTaskVersion=c.Input.TaskRowVersion,expectedInstanceVersion=c.Input.InstanceRowVersion},clientCorrelationId=correlation},PersonalFormOwnerV2.Json);
    [CurrentPermissionPostgresFact]
    public async Task UnifiedTask_HTTP_SFS_context_has_exact_assignment_versions_and_server_selected_input()
    {
        await using var h=await Seed();var f=await NativeFormSetup(h);var c=await f.Owner.TaskContextAsync(f.Task);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});
        var cookies=await Login(host,h);var root="/api/oa/v2/tasks/"+f.Task;
        using var response=await host.Send(BoundRequest(cookies,root+"/action-context"));var context=await PersonalHttpResult<JsonElement>(response);
        Assert.Equal("SFS",context.GetProperty("contextKind").GetString());Assert.Equal(c.Task.TaskRowVersion,context.GetProperty("taskRowVersion").GetString());
        Assert.Equal(c.FormDataVersion,context.GetProperty("formDataVersion").GetString());Assert.Equal(h.User,context.GetProperty("actualHandlerId").GetGuid());
        Assert.Equal(h.User,context.GetProperty("createdPrincipalId").GetGuid());Assert.Equal(c.TokenId,context.GetProperty("tokenId").GetGuid());
        using var spoof=await host.Send(PersonalHttp(cookies,root+"/decisions",SfsCommand(SfsInput(c) with{Kind="APP"})));Assert.Equal(HttpStatusCode.BadRequest,spoof.StatusCode);
        using var decision=await host.Send(PersonalHttp(cookies,root+"/decisions",SfsCommand(SfsInput(c,"{\"a\":\"unified SFS edit\"}"),"opaque.SFS-intent_1")));
        Assert.Equal("SUCCEEDED",(await PersonalHttpResult<PersonalFormResultV2>(decision)).Outcome);
        Assert.Contains("unified SFS edit",(await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync()).DataJson);
    }
    [CurrentPermissionPostgresFact]
    public async Task UnifiedTask_HTTP_APP_keeps_accepted_command_identity_and_frozen_subject_without_patch()
    {
        await using var h=await Seed();var(_,first,_)=await DecisionSetup(h);var actor=DecisionActor(h,first);var input=await DecisionInput(actor,"reject");
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});
        var cookies=await DocumentReviewerLogin(host,h,first);var root="/api/oa/v2/tasks/"+input.Task;
        using var response=await host.Send(BoundRequest(cookies,root+"/action-context"));var context=await PersonalHttpResult<JsonElement>(response);
        Assert.Equal("APP",context.GetProperty("contextKind").GetString());Assert.Equal(first,context.GetProperty("currentPrincipalId").GetGuid());
        Assert.Equal(input.Input.TaskRowVersion,context.GetProperty("taskRowVersion").GetString());Assert.Equal(JsonValueKind.Null,context.GetProperty("formDataVersion").ValueKind);
        var body=new{kind="APP",taskId=input.Task,decision="REJECT",comment=input.Input.Comment,expectedTaskVersion=input.Input.TaskRowVersion,expectedInstanceVersion=input.Input.InstanceRowVersion};
        var raw=PersonalCommand(body,input.Input.CommandId.ToString("D"),body.expectedTaskVersion);
        async Task Check(int status,Action<System.Text.Json.Nodes.JsonObject> change)
        {
            var node=System.Text.Json.Nodes.JsonNode.Parse(raw)!.AsObject();change(node);
            using var failed=await host.Send(PersonalHttp(cookies,root+"/decisions",System.Text.Encoding.UTF8.GetBytes(node.ToJsonString())));
            Assert.Equal(status,(int)failed.StatusCode);
            if(status==428){using var error=JsonDocument.Parse(await failed.Content.ReadAsStringAsync());Assert.Equal("/api/oa/tasks/decision-commands/"+input.Input.CommandId,error.RootElement.GetProperty("resultQueryRef").GetString());}
        }
        await Check(400,n=>n["body"]!["dataPatch"]=new System.Text.Json.Nodes.JsonObject());
        await Check(400,n=>n["body"]!["expectedFormDataVersion"]=body.expectedTaskVersion);
        await Check(400,n=>n["body"]!["kind"]="SFS");
        await Check(400,n=>n["body"]!["actualHandlerId"]=first.ToString("D"));
        await Check(428,n=>n["body"]!.AsObject().Remove("expectedInstanceVersion"));
        await Check(428,n=>n.Remove("expectedVersion"));
        await Check(400,n=>n["expectedVersion"]=Convert.ToBase64String(new byte[8]));
        await Check(422,n=>n["body"]!["comment"]=new string('a',501));
        await Check(422,n=>n["body"]!["comment"]=" \t\r\n");
        await Check(422,n=>n["commandId"]="opaque.APP-is-not-a-Guid");
        await Check(400,n=>n["commandId"]=123);
        await Check(412,n=>n["body"]!["expectedInstanceVersion"]=Convert.ToBase64String(new byte[8]));
        using(var noCsrf=await host.Send(PersonalHttp(cookies,root+"/decisions",raw,false)))Assert.Equal(HttpStatusCode.Forbidden,noCsrf.StatusCode);
        using var decided=await host.Send(PersonalHttp(cookies,root+"/decisions",raw));var result=await PersonalHttpResult<ApprovalTaskDecisionResultV2>(decided);
        Assert.Equal(input.Input.CommandId,result.CommandId);Assert.NotNull(result.Result.DecisionId);
        Assert.Equal(result,await Decisions(actor).OriginalAsync(input.Input.CommandId));
        Assert.Equal(result,await Vote(actor,input));
        var stored=await h.Db.Database.SqlQuery<string>($"SELECT request_body AS \"Value\" FROM cp6_approval_decision_v2.commands WHERE id={input.Input.CommandId}").SingleAsync();
        Assert.DoesNotContain("dataPatch",stored);Assert.DoesNotContain("expectedFormDataVersion",stored);Assert.Contains(input.Input.Comment,stored);
        Assert.Equal(raw,await h.Db.Database.SqlQuery<byte[]>($"SELECT wfc_raw_body AS \"Value\" FROM cp6_approval_decision_v2.commands WHERE id={input.Input.CommandId}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task UnifiedTask_APP_alias_is_immutable_and_original_accepted_canonical_remains_authoritative()
    {
        await using var h=await Seed();var(_,first,_)=await DecisionSetup(h);var actor=DecisionActor(h,first);var c=await DecisionInput(actor,"reject");var owner=Decisions(actor);
        c=(c.Input with{Comment=string.Concat(Enumerable.Repeat("🙂",500))},c.Task);var raw=AppWfc(c,"first diagnostic");
        var result=await owner.DecideWfcAsync(c.Task,raw);
        Assert.Equal(result,await owner.DecideWfcAsync(c.Task,AppWfc(c,"changed diagnostic")));Assert.Equal(result,await Vote(actor,c));
        var stored=await h.Db.Database.SqlQuery<string>($"SELECT request_body AS \"Value\" FROM cp6_approval_decision_v2.commands WHERE id={c.Input.CommandId}").SingleAsync();
        using var request=JsonDocument.Parse(stored);Assert.Equal(new[]{"commandId","comment","decision","instanceRowVersion","taskRowVersion"},request.RootElement.GetProperty("input").EnumerateObject().Select(p=>p.Name).Order().ToArray());
        Assert.Equal(c.Input.Comment,request.RootElement.GetProperty("input").GetProperty("comment").GetString());
        var alternative=System.Text.Json.Nodes.JsonNode.Parse(raw)!.AsObject();alternative["body"]!["taskId"]=c.Task.ToString("D").ToUpperInvariant();
        Assert.Equal("COMMAND_BODY_CONFLICT",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.DecideWfcAsync(c.Task,System.Text.Encoding.UTF8.GetBytes(alternative.ToJsonString())))).Message);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_approval_decision_v2.commands SET wfc_digest={new string('a',64)} WHERE id={c.Input.CommandId}"));
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_decision_v2.commands WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_native_tasks_v2.commands WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task UnifiedTask_APP_alias_vote_recorder_and_outbox_rollback_together_and_unknown_commit_recovers_after_revocation()
    {
        var hook=new StartWriteHook();var fault=new CommitFault();await using var h=await Seed(hook,fault);var(_,first,_)=await DecisionSetup(h);var actor=DecisionActor(h,first);
        var c=await DecisionInput(actor,"reject");var raw=AppWfc(c);var owner=Decisions(actor);
        hook.Before=sql=>{if(sql.CommandText.Contains("INSERT INTO cp6_approval_decision_v2.decisions")){hook.Before=null;throw new InvalidOperationException("injected recorder failure");}return Task.CompletedTask;};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>owner.DecideWfcAsync(c.Task,raw));
        Assert.Equal(0,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Id==c.Task)).Status);
        Assert.Equal("NOT_OBSERVED",(await owner.DecisionOperationAsync(c.Task,c.Input.CommandId)).State);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_decision_v2.commands WHERE tenant_id={h.Tenant}").SingleAsync());
        fault.Armed=true;Assert.Equal("OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.DecideWfcAsync(c.Task,raw))).Message);
        await h.Db.Sys_RoleActions.Where(a=>a.ActionCode=="approve").ExecuteDeleteAsync();
        var original=await owner.DecisionOperationAsync(c.Task,c.Input.CommandId);Assert.Equal("COMPLETED",original.State);Assert.Equal(2,original.Result!.Result.Task.InstanceStatus);
        Assert.Equal(original.Result,await owner.DecideWfcAsync(c.Task,raw));Assert.Equal(original,await owner.CloseAsync(c.Task,c.Input.CommandId));
        Assert.Equal(raw,await h.Db.Database.SqlQuery<byte[]>($"SELECT wfc_raw_body AS \"Value\" FROM cp6_approval_decision_v2.commands WHERE id={c.Input.CommandId}").SingleAsync());
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_decision_v2.outbox WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task UnifiedTask_APP_context_projects_actual_principal_pins_and_only_current_delegated_decisions()
    {
        await using var h=await Seed();var(_,first,_)=await DecisionSetup(h);var actual=await ActualReviewer(h);
        var grant=await CreateTaskGrant(h,first,actual.User,"binding-flow",new(["TASK_READ"],["binding-flow"]));
        var task=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.AssigneeId==first);var owner=Decisions(actual);var c=await owner.ActionContextAsync(task.Id,first);
        Assert.Empty(c.AllowedActions);Assert.Equal(first,c.CreatedPrincipalId);Assert.Equal(first,c.ExpectedAssigneeId);Assert.Equal(first,c.CurrentPrincipalId);Assert.Equal(actual.User,c.ActualHandlerId);
        Assert.Equal(grant.GrantId,c.GrantId);Assert.Equal(grant.Revision,c.GrantRevision);Assert.Null(c.FormDataVersion);Assert.Null(c.Form);Assert.Empty(c.FieldMask);
        var actor=DecisionActor(h,first);var revised=(await TaskGrants(actor).WriteAsync("REVISE",grant.GrantId,PersonalCommand(GrantInput(actual.User,"binding-flow","TASK_READ","TASK_REJECT"),"reject-only",grant.Version.ToString("D")))).Result!;
        c=await owner.ActionContextAsync(task.Id,first);Assert.Equal(new[]{"REJECT"},c.AllowedActions);Assert.Equal(revised.Revision,c.GrantRevision);
        var accepted=new ApprovalTaskDecisionInputV2(Guid.NewGuid(),"reject","current delegated decision",c.TaskRowVersion,c.InstanceRowVersion);
        var result=await owner.DecideWfcAsync(task.Id,AppWfc((accepted,task.Id)),first);Assert.NotNull(result.Result.DecisionId);
        await TaskGrants(actor).WriteAsync("REVOKE",grant.GrantId,PersonalCommand(new TaskDelegationRevokeV2("finished"),"revoke",revised.Version.ToString("D")));
        Assert.Equal(result,(await owner.DecisionOperationAsync(task.Id,accepted.CommandId)).Result);
        var vote=JsonSerializer.Deserialize<ApprovalVoteV2>(await h.Db.Database.SqlQuery<string>($"SELECT body AS \"Value\" FROM cp6_approval_decision_v2.votes WHERE command_id={accepted.CommandId}").SingleAsync(),ApprovalBindingOwnerV2.Json)!;
        Assert.Equal(actual.User,vote.Authority.ActualHandlerId);Assert.Equal(first,vote.Authority.PrincipalId);Assert.Equal(revised.Revision,vote.Authority.GrantRevision);
    }
    [CurrentPermissionPostgresFact]
    public async Task UnifiedTask_APP_close_reserves_the_accepted_slot_and_context_does_not_authorize_another_tenant()
    {
        await using var h=await Seed();var(_,first,_)=await DecisionSetup(h);var actor=DecisionActor(h,first);var c=await DecisionInput(actor);var owner=Decisions(actor);
        Assert.Equal("NOT_OBSERVED",(await owner.DecisionOperationAsync(c.Task,c.Input.CommandId)).State);
        Assert.Equal("CLOSED",(await owner.CloseAsync(c.Task,c.Input.CommandId)).State);Assert.Equal("CLOSED",(await owner.DecisionOperationAsync(c.Task,c.Input.CommandId)).State);
        Assert.Equal("COMMAND_CLOSED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.DecideWfcAsync(c.Task,AppWfc(c)))).Message);
        Assert.Equal(0,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Id==c.Task)).Status);
        await using var other=await Seed();await DecisionSetup(other);
        Assert.Equal(404,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>PersonalForm(other).ActionContextAsync(c.Task))).Status);
        Assert.Equal(404,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>Decisions(other).DecisionOperationAsync(c.Task,c.Input.CommandId))).Status);
    }
}
