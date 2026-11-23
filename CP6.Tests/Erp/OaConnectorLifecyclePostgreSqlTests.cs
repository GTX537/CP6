using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static readonly string[] ConnActions=["conn.read","conn.trigger.write","conn.trigger.review","conn.trigger.publish",
        "conn.trigger.control","conn.connector.write","conn.connector.review","conn.connector.activate","conn.connector.control"];
    private static Dictionary<string,string> ConnSettings() => new() {
        ["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D"),["OaConnectorControl__AllowSimulatedParticipants"]="true"};
    private static async Task<Guid> ConnPeople(Harness h)
    {
        foreach(var action in ConnActions) await EngineeringAction(h,"oa-flow-admin",action);
        var menu=await h.Db.Sys_Menus.SingleAsync(m=>m.MenuKey=="oa-flow-admin");
        if(!await h.Db.Sys_RoleMenus.AnyAsync(r=>r.RoleId==72 && r.MenuId==menu.MenuId))
            h.Db.Sys_RoleMenus.Add(new() {RoleId=72,MenuId=menu.MenuId});
        if(!await h.Db.Sys_RoleDataScopes.AnyAsync(s=>s.RoleId==72 && s.ResourceKey==FlowAuthoringOwnerV2.Resource))
            h.Db.Sys_RoleDataScopes.Add(new(){ RoleId=72,ResourceKey=FlowAuthoringOwnerV2.Resource,ScopeType=5 });
        var reviewer=new Sys_User {UserName="conn-reviewer",RoleId=72,Password=new BCryptPasswordHasher(4).Hash("Reviewer1!Test")};
        h.Db.Sys_Users.Add(reviewer);
        h.Db.Sys_Roles.Add(new() {RoleId=81,RoleName="配置员"});
        h.Db.Sys_Users.Add(new() {UserName="conn-editor",RoleId=81,Password=new BCryptPasswordHasher(4).Hash("Editor1!Test")});
        h.Db.Sys_RoleMenus.Add(new() {RoleId=81,MenuId=menu.MenuId});
        foreach(var action in new[]{"conn.read","conn.trigger.write","conn.connector.write"})
            h.Db.Sys_RoleActions.Add(new() {RoleId=81,MenuId=menu.MenuId,ActionCode=action});
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        return reviewer.Id;
    }
    private static ConnProfileV1 ConnFixture(string source="TENANT",int? duration=20) => new(SourceKind:source,
        Implementation:"fixture-"+source,ImplementationVersion:"1.0",DestinationPolicy:"local-read-policy-v1",EndpointProfile:"isolated-test-v1",
        Operation:"lookup-v1",InputSchema:"request-v1",OutputSchema:"response-v1",ParameterMapping:"params-v1",ResponseMapping:"result-v1",
        EffectClass:"READ_ONLY",MaxCallDurationSec:duration,LeasePolicy:"main-job-300s",RetryPolicy:"bounded-v1",DisclosurePolicy:"scope-v1",
        CredentialLocator:"opaque-binding",CredentialGeneration:"generation-1");
    private static async Task<Guid> ConnRegistration(Harness h,string kind,ConnProfileV1 spec,string adoption="SIMULATED")
    {
        var id=Guid.NewGuid(); var json=JsonSerializer.Serialize(spec,new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_oa_conn_v1.profiles(tenant_id,environment_id,id,kind,label,content,adoption,evidence_ref)
            VALUES({h.Tenant},{ScopeEnvironment},{id},{kind},{kind+" "+(spec.SourceKind??"")} ,CAST({json} AS jsonb),{adoption},'isolated-contract-fixture');
            INSERT INTO cp6_oa_conn_v1.profile_control(tenant_id,environment_id,profile_id,enabled)
            VALUES({h.Tenant},{ScopeEnvironment},{id},true)
            """);
        return id;
    }
    private sealed record ConnApi(AuthenticationApiProcess Host,Dictionary<string,string> Cookies)
    {
        internal Task<HttpResponseMessage> Send(string path,object? body=null) =>
            Host.Send(OperationsRequest(Cookies,body==null ? HttpMethod.Get:HttpMethod.Post,"api/oa/conn/v1/"+path,body));
        internal async Task<JsonElement> Data(string path,object? body=null)
        {
            using var response=await Send(path,body);
            Assert.True(response.IsSuccessStatusCode, path+" "+response.StatusCode+" "+await OperationsFailure(response));
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        }
        internal async Task Reject(string path,object? body,HttpStatusCode status,string code)
        {
            using var response=await Send(path,body);
            Assert.Equal(status,response.StatusCode);
            Assert.Equal(code,(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
    }
    private static object ConnCmd(string expected,object body,string? id=null) =>
        new { commandId=id??Guid.NewGuid().ToString("D"),expectedVersion=expected,body };
    private sealed record ConnResource(string Family,Guid Id,Guid Version,string Head)
    {
        internal string Route=>Family+"/"+Id;
        internal string Kind=>Family=="triggers"?"TRIGGER":"CONNECTOR";
    }
    private static async Task<ConnResource> ConnCreate(ConnApi maker,string kind,ConnSelectionV1 selection,string display)
    {
        var family=kind=="TRIGGER"?"triggers":"connectors";
        var context=await maker.Data("creation-contexts?kind="+kind,selection);
        Assert.Equal("ABSENT",context.GetProperty("absence").GetString());
        var receipt=await maker.Data(family,ConnCmd(context.GetProperty("expectedVersion").GetString()!,new ConnDraftV1(selection,display)));
        return new(family,receipt.GetProperty("rootId").GetGuid(),receipt.GetProperty("versionId").GetGuid(),
            receipt.GetProperty("result").GetProperty("revision").GetString()!);
    }
    private static async Task<JsonElement> ConnImpact(ConnApi actor,ConnResource target,string action)
    {
        var receipt=await actor.Data(target.Route+"/control-impact-previews",ConnCmd(target.Head,new ConnPreviewInputV1(target.Version,action)));
        return receipt.GetProperty("result");
    }
    private static async Task<Guid> ConnReview(ConnApi maker,ConnApi checker,ConnResource target,string action,Guid? impact=null)
    {
        var subject=await maker.Data(target.Route+"/submit-review",ConnCmd(target.Head,new ConnReviewInputV1(target.Version,target.Kind+"_"+action,impact)));
        var id=subject.GetProperty("result").GetProperty("subjectRef").GetGuid();
        var inbox=await checker.Data("review-subjects?kind="+target.Kind);
        Assert.Contains(inbox.EnumerateArray(),item=>item.GetProperty("subjectRef").GetGuid()==id);
        var explicitContext=await checker.Data("review-subjects/"+id+"/context?kind="+target.Kind);
        Assert.Equal(target.Version,explicitContext[0].GetProperty("versionId").GetGuid());
        var vote=ConnCmd(target.Head,new ConnDecideInputV1(id,"APPROVE"));
        await maker.Reject(target.Route+"/decisions",vote,HttpStatusCode.Forbidden,"SOD_VIOLATION");
        var approved=await checker.Data(target.Route+"/decisions",vote);
        return approved.GetProperty("result").GetProperty("decisionRef").GetGuid();
    }
    private static async Task<ConnResource> ConnActivate(ConnApi maker,ConnApi checker,ConnResource target)
    {
        string action=target.Kind=="TRIGGER"?"ENABLE":"ACTIVATE";
        var preview=await ConnImpact(maker,target,action);
        Assert.Equal("COMPLETE",preview.GetProperty("coverage").GetString());
        Assert.Equal(0,preview.GetProperty("manifest").GetProperty("jobs").GetInt32());
        var impact=preview.GetProperty("previewId").GetGuid();
        var approval=await ConnReview(maker,checker,target,action,impact);
        var result=await maker.Data(target.Route+"/"+action.ToLowerInvariant(),
            ConnCmd(target.Head,new ConnControlInputV1(target.Version,impact,approval,"首次启用")));
        return target with {Head=result.GetProperty("result").GetProperty("revision").GetString()!};
    }

    [CurrentPermissionPostgresFact]
    public async Task OaConn_public_trigger_reviews_zero_manifest_original_replay_and_independent_disable()
    {
        await using var h=await Seed();
        var (flow,published)=await PrepareActivation(h,"conn-flow");
        var activation=(await flow.ActivationPreviewAsync("conn-flow")).Current;
        var flowId=published.Result!.Publication!.VersionId;
        await flow.SetActivationAsync("conn-flow",ActivationCommand(activation,flowId));
        await ConnPeople(h);
        var profile=await ConnRegistration(h,"MESSAGE",new(Registration:"message-registration-v1",Schema:"message-v1",VarsSchema:"vars-v1",
            RateLimitPolicy:"rate-v1",StarterAuthority:"local-establishment",StarterId:h.User));
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,ConnSettings());
        var maker=new ConnApi(host,await Login(host,h));
        var checker=new ConnApi(host,await Login(host,"conn-reviewer","Reviewer1!Test",h.Tenant.ToString("N")));
        var editor=new ConnApi(host,await Login(host,"conn-editor","Editor1!Test",h.Tenant.ToString("N")));
        var options=await maker.Data("options?kind=TRIGGER");
        Assert.Contains(options.GetProperty("profiles").EnumerateArray(),p=>p.GetProperty("id").GetGuid()==profile);
        var selection=new ConnSelectionV1("conn-message","MESSAGE",profile,"conn-flow",flowId);
        var target=await ConnCreate(maker,"TRIGGER",selection,"消息触发");
        Assert.False(await h.Db.Wf_FlowTriggers.AnyAsync(t=>t.Id==target.Id && t.ApiKeyHash!=null));
        await editor.Reject(target.Route+"/decisions",ConnCmd(target.Head,new ConnDecideInputV1(Guid.NewGuid(),"APPROVE")),
            HttpStatusCode.Forbidden,"ACTION_FORBIDDEN");
        var publishVote=await ConnReview(maker,checker,target,"PUBLISH");
        var command=ConnCmd(target.Head,new ConnControlInputV1(target.Version,DecisionRef:publishVote),"original-publish");
        var receipt=await maker.Data(target.Route+"/publish",command);
        target=target with {Head=receipt.GetProperty("result").GetProperty("revision").GetString()!};
        var first=await ConnImpact(maker,target,"ENABLE");
        Assert.Equal(0,first.GetProperty("manifest").GetProperty("slots").GetInt32());
        Assert.Empty(first.GetProperty("manifest").GetProperty("states").EnumerateArray());
        await maker.Reject(target.Route+"/enable",ConnCmd(target.Head,new ConnControlInputV1(target.Version,first.GetProperty("previewId").GetGuid(),publishVote,"验证决定类型")),
            HttpStatusCode.Conflict,"REVIEW_SUBJECT_MISMATCH");
        target=await ConnActivate(maker,checker,target);
        var beforeDispatch=await ConnImpact(checker,target,"DISABLE");
        var expiredId=Guid.NewGuid();
        var liveId=beforeDispatch.GetProperty("previewId").GetGuid();
        var ttl=beforeDispatch.GetProperty("expires").GetDateTimeOffset()-DateTimeOffset.UtcNow;
        Assert.InRange(ttl.TotalSeconds,570,601);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_oa_conn_v1.previews(tenant_id,environment_id,id,root_id,version_id,action,heads,manifest,coverage,expires_at)
            SELECT tenant_id,environment_id,{expiredId},root_id,version_id,action,heads,manifest,coverage,clock_timestamp()-interval '1 second'
            FROM cp6_oa_conn_v1.previews WHERE tenant_id={h.Tenant} AND id={liveId}
            """);
        await checker.Reject(target.Route+"/disable",ConnCmd(target.Head,new ConnControlInputV1(target.Version,expiredId,null,"过期预览")),
            HttpStatusCode.PreconditionFailed,"IMPACT_CHANGED");
        await using(var other=server.Context(new CP6.Core.Services.Common.TenantContext{CurrentTenantId=h.Tenant}))
        {
            await using var tx=await other.Database.BeginTransactionAsync();
            await other.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_oa_conn_v1.runtime_members(tenant_id,environment_id,root_id,version_id,kind,id,state)
                VALUES({h.Tenant},{ScopeEnvironment},{target.Id},{target.Version},'DISPATCH',{Guid.NewGuid()},'DISPATCH_ADMITTED')
                """);
            await tx.CommitAsync();
        }
        Assert.Equal(target.Head,(await maker.Data(target.Route+"/context")).GetProperty("revision").GetString());
        await checker.Reject(target.Route+"/disable",ConnCmd(target.Head,new ConnControlInputV1(target.Version,liveId,null,"独立事务已推进")),
            HttpStatusCode.PreconditionFailed,"IMPACT_CHANGED");
        var disable=await ConnImpact(checker,target,"DISABLE");
        var result=await checker.Data(target.Route+"/disable",ConnCmd(target.Head,new ConnControlInputV1(target.Version,
            disable.GetProperty("previewId").GetGuid(),null,"独立管理员停用")));
        Assert.Equal("DISABLED",result.GetProperty("result").GetProperty("state").GetString());
        target=target with {Head=result.GetProperty("result").GetProperty("revision").GetString()!};
        var retire=await ConnImpact(maker,target,"RETIRE");
        await maker.Reject(target.Route+"/retire",ConnCmd(target.Head,new ConnControlInputV1(target.Version,retire.GetProperty("previewId").GetGuid(),publishVote,"复核退役")),
            HttpStatusCode.Conflict,"REVIEW_SUBJECT_MISMATCH");
        var oldTrigger=new Wf_FlowTrigger{Id=target.Id,FlowKey="conn-flow",TriggerType=WfTriggerType.Message,
            Enabled=true,StarterUserId=h.User};
        var legacyRun=new FlowTriggerService(h.Db,new FlowEngine(h.Db,new ApproverResolver(h.Db)));
        int instancesBefore=await h.Db.Wf_FlowInstances.CountAsync();
        await using var legacyTx=await h.Db.Database.BeginTransactionAsync();
        await AuthenticationCompletionGateV1.AcquireAsync(h.Db,default);
        var attempt=await legacyRun.FireManualAsync(oldTrigger,"{}",default);
        await legacyTx.CommitAsync();
        Assert.False(attempt.Success);
        Assert.Equal("TRUSTED_CONN_V1_REQUIRED",attempt.Error);
        Assert.Equal(instancesBefore,await h.Db.Wf_FlowInstances.CountAsync());
        var enableVote=(await maker.Data(target.Route+"/context")).GetProperty("approvals").EnumerateArray()
            .Single(x=>x.GetProperty("kind").GetString()=="TRIGGER_ENABLE").GetProperty("decisionRef").GetGuid();
        var retireId=retire.GetProperty("previewId").GetGuid();
        await maker.Reject(target.Route+"/retire",ConnCmd(target.Head,new ConnControlInputV1(target.Version,retireId,enableVote,"异动作")),
            HttpStatusCode.Conflict,"REVIEW_SUBJECT_MISMATCH");
        var retirement=await ConnReview(maker,checker,target,"RETIRE",retireId);
        var final=await maker.Data(target.Route+"/retire",ConnCmd(target.Head,new ConnControlInputV1(target.Version,retireId,retirement,"独立退役")));
        Assert.Equal("RETIRED",final.GetProperty("result").GetProperty("state").GetString());
        var direct=await maker.Data("commands/original-publish");
        Assert.Equal(receipt.GetProperty("result").GetProperty("revision").GetGuid(),direct.GetProperty("result").GetProperty("revision").GetGuid());
        var replay=await maker.Data(target.Route+"/publish",command);
        Assert.Equal(direct.GetRawText(),replay.GetRawText());
        var original=await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_oa_conn_v1.audit WHERE tenant_id={h.Tenant} AND command_id='original-publish'
            """).SingleAsync();
        Assert.Equal(1,original);
    }
}
