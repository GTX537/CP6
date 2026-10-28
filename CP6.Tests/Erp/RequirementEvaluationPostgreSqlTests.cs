using System.Net;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static OperationsOwnerV1 EvaluationAuthority(Harness h)
    {
        var catalog=new AuthorizationCatalogStoreV1();
        foreach(var key in new[]{"plm-engineering-requirement","uom-profile"})
        {catalog.RegisterScope(key,key,[1,2,3,4,5],5);catalog.RegisterFields(key,key=="plm-engineering-requirement"?[new("Definition","Definition"),new("Source","Source")]:[]);}
        catalog.Seal();return new(h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),catalog.Capture);
    }
    private static EngineeringRequirementService EvaluationDrafts(Harness h)=>new(h.Db,EvaluationAuthority(h));
    private static RequirementEvaluationService Evaluations(Harness h,string mode="DEMO",Guid? environment=null)
        =>new(h.Db,EvaluationAuthority(h),ScopeFence(h,environment),new(mode),EvaluationDrafts(h));
    private sealed record EvaluationFixture(Guid First,Guid Second,EngineeringRequirementSnapshotV1 Source,EngineeringRequirementSnapshotV1 Definition);
    private async Task<EvaluationFixture> EvaluationSetup(Harness h)
    {
        await GrantRequirement(h);await GrantUomProfile(h);await RegisterScope(h);
        foreach(var action in new[]{"review-policy","review-submit","review-decide","withdraw"})await EngineeringAction(h,"plm-engineering-requirement",action);
        var first=Guid.NewGuid();var second=Guid.NewGuid();h.Db.Sys_Roles.Add(new(){RoleId=74,RoleName="Requirement professional"});
        h.Db.Sys_Users.AddRange(new(){Id=first,UserName="requirement-reviewer-one",Password="fixture",RoleId=72},new(){Id=second,UserName="requirement-reviewer-two",Password="fixture",RoleId=72});
        h.Db.Sys_UserRoles.AddRange(new(){UserId=first,RoleId=74},new(){UserId=second,RoleId=74});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var o=EvaluationDrafts(h);var source=await CreateRequirement(o,SourceBody());var b=DefinitionBody(source.Reference);
        var definition=await CreateRequirement(o,b with{Requirement=b.Requirement! with{ApplicableObject="Product label",Necessity="Required source clause",
            Criteria=[new("appearance","QUALITATIVE","Label","Daylight","Professional comparison",QualitativeBasis:"Text and symbols match the referenced original")]}});
        return new(first,second,source,definition);
    }
    private static RequirementEvaluationPolicyInputV1 EvaluationPolicy()=>new("ENABLED","Two independent domain professionals",[new("PROFESSIONAL",74,2)],DateTimeOffset.UtcNow.AddHours(-1),DateTimeOffset.UtcNow.AddDays(1));
    private static RequirementEvaluationCommandV1 EvaluationCommand(RequirementEvaluationViewV1 v,string action)=>new(Guid.NewGuid(),v.Requirement.Reference.Id,action,v.Etag,"Explicit professional decision");
    private static async Task<RequirementEvaluationViewV1> EvaluationReady(Harness h,EvaluationFixture f)
    {
        var o=Evaluations(h);var v=await o.ReadAsync(f.Definition.Reference.Id);await o.CommandAsync(EvaluationCommand(v,"POLICY") with{Policy=EvaluationPolicy()});
        v=await o.ReadAsync(f.Definition.Reference.Id);await o.CommandAsync(EvaluationCommand(v,"SUBMIT") with{Requirement=v.Requirement.Reference});return await o.ReadAsync(f.Definition.Reference.Id);
    }
    private static async Task<RequirementEvaluationViewV1> EvaluationApprove(Harness h,EvaluationFixture f)
    {
        await EvaluationReady(h,f);
        foreach(var id in new[]{f.First,f.Second}){var o=Evaluations(DecisionActor(h,id));await o.CommandAsync(EvaluationCommand(await o.ReadAsync(f.Definition.Reference.Id),"APPROVE"));}
        return await Evaluations(h).ReadAsync(f.Definition.Reference.Id);
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementEvaluation_independent_fixed_approval_original_replay_and_new_draft_preserve_prior_approval()
    {
        await using var h=await Seed();var f=await EvaluationSetup(h);var o=Evaluations(h);var ready=await EvaluationReady(h,f);
        Assert.Equal("IN_REVIEW",ready.Control!.State);Assert.Equal(f.Definition.OriginalJson,ready.Packet!.Body.Inputs.Requirement.OriginalJson);
        Assert.Equal("REQUIREMENT_REVIEW_INDEPENDENT_SIGNER_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(EvaluationCommand(ready,"APPROVE")))).Message);
        var drafts=EvaluationDrafts(h);Assert.Equal("REQUIREMENT_REVIEW_FROZEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>drafts.CommandAsync(ReviseRequirement(f.Definition,f.Definition.Body)))).Message);
        var one=Evaluations(DecisionActor(h,f.First));var c=EvaluationCommand(await one.ReadAsync(f.Definition.Reference.Id),"APPROVE");
        var first=await one.CommandAsync(c);Assert.Equal(first,await one.CommandAsync(c));Assert.Equal(first,(await one.OperationAsync(c.OperationId)).Result);
        Assert.Null((await o.ReadAsync(f.Definition.Reference.Id)).CurrentApproval);
        var two=Evaluations(DecisionActor(h,f.Second));await two.CommandAsync(EvaluationCommand(await two.ReadAsync(f.Definition.Reference.Id),"APPROVE"));
        var approved=await o.ReadAsync(f.Definition.Reference.Id);Assert.Equal(2,approved.CurrentApproval!.Body.Votes.Length);Assert.Equal("DEFINITION_ONLY",approved.CurrentApproval.Body.Purpose);
        var historic=await o.ReadControlAsync(f.Definition.Reference.Id,ready.ControlReference!.Version);
        Assert.False(historic.ControlIsCurrent);Assert.Empty(historic.Votes);Assert.Null(historic.CurrentApproval);Assert.Equal(ready.Packet!.Reference,historic.Packet!.Reference);
        Assert.Equal("AVAILABLE",(await o.SourceAsync(f.Definition.Reference)).Availability);
        await drafts.CommandAsync(ReviseRequirement(f.Definition,f.Definition.Body with{Name="Successor draft"}));
        Assert.Equal("AVAILABLE",(await o.SourceAsync(f.Definition.Reference)).Availability);
        Assert.Equal("UNAVAILABLE",(await o.SourceAsync((await o.ReadAsync(f.Definition.Reference.Id)).Requirement.Reference)).Availability);
        await o.CommandAsync(EvaluationCommand(await o.ReadAsync(f.Definition.Reference.Id),"WITHDRAW"));
        Assert.Contains("REQUIREMENT_APPROVAL_WITHDRAWN",(await o.SourceAsync(f.Definition.Reference)).Missing);
        historic=await o.ReadControlAsync(f.Definition.Reference.Id,approved.ControlReference!.Version);Assert.Equal(approved.CurrentApproval.Reference,historic.CurrentApproval!.Reference);Assert.Equal(2,historic.Votes.Length);
        Assert.Equal(first,(await one.OperationAsync(c.OperationId)).Result);
        foreach(var sql in new[]{"UPDATE cp6_requirement_evaluation_v1.records SET body=body","DELETE FROM cp6_requirement_evaluation_v1.controls","TRUNCATE cp6_requirement_evaluation_v1.heads CASCADE"})await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync(sql));
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementEvaluation_unknown_boundary_native_unit_precision_and_disabled_base_fail_closed()
    {
        await using var h=await Seed();var f=await EvaluationSetup(h);var o=Evaluations(h);var drafts=EvaluationDrafts(h);var units=Units(h);
        var unit=await MakeUnit(units,BaseUnit("N","FORCE",0));var exact=new string('9',200)+"."+new string('0',199)+"1";
        var criterion=new RequirementCriterionV1("force","QUANTITATIVE","Body","Declared temperature","Load test",exact,true,null,null,"N","Source clause 1","Preserve source decimal string");
        await drafts.CommandAsync(ReviseRequirement(f.Definition,f.Definition.Body with{Requirement=f.Definition.Body.Requirement! with{Criteria=[criterion]}}));
        var v=await o.ReadAsync(f.Definition.Reference.Id);await o.CommandAsync(EvaluationCommand(v,"POLICY") with{Policy=EvaluationPolicy()});
        async Task<RequirementEvaluationViewV1> CheckMissing(string code)
        {var current=await o.ReadAsync(f.Definition.Reference.Id);Assert.Contains(current.Missing,x=>x.StartsWith(code));Assert.Equal("REQUIREMENT_REVIEW_SOURCE_INCOMPLETE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(EvaluationCommand(current,"SUBMIT") with{Requirement=current.Requirement.Reference}))).Message);return current;}
        v=await CheckMissing("EVALUATION_UNIT_AND_BOUNDARY_REQUIRED");
        criterion=criterion with{Evaluation=new("requirement-evaluation/1",unit.Reference,false,false)};
        await drafts.CommandAsync(ReviseRequirement(v.Requirement,v.Requirement.Body with{Requirement=v.Requirement.Body.Requirement! with{Criteria=[criterion]}}));v=await CheckMissing("UPPER_BOUND_INTENT_REQUIRED");
        criterion=criterion with{Evaluation=criterion.Evaluation! with{UpperUnbounded=true}};
        await drafts.CommandAsync(ReviseRequirement(v.Requirement,v.Requirement.Body with{Requirement=v.Requirement.Body.Requirement! with{Criteria=[criterion]}}));v=await o.ReadAsync(f.Definition.Reference.Id);
        Assert.Empty(v.Missing);Assert.Equal(exact,v.Requirement.Body.Requirement!.Criteria[0].Lower);
        await o.CommandAsync(EvaluationCommand(v,"SUBMIT") with{Requirement=v.Requirement.Reference});
        foreach(var actor in new[]{f.First,f.Second}){var reviewer=Evaluations(DecisionActor(h,actor));await reviewer.CommandAsync(EvaluationCommand(await reviewer.ReadAsync(f.Definition.Reference.Id),"APPROVE"));}
        Assert.Equal("AVAILABLE",(await o.SourceAsync(v.Requirement.Reference)).Availability);
        await units.CommandAsync(Revise(unit,unit.Profile with{Enabled=false}));Assert.Contains((await o.SourceAsync(v.Requirement.Reference)).Missing,x=>x.StartsWith("UNIT_SOURCE_CHANGED"));
        Assert.Equal(exact,(await o.ReadAsync(f.Definition.Reference.Id)).Packet!.Body.Inputs.Requirement.Body.Requirement!.Criteria[0].Lower);
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementEvaluation_return_policy_replacement_and_resubmit_do_not_reuse_old_votes()
    {
        await using var h=await Seed();var f=await EvaluationSetup(h);var o=Evaluations(h);var first=await EvaluationReady(h,f);
        var reviewer=Evaluations(DecisionActor(h,f.First));await reviewer.CommandAsync(EvaluationCommand(await reviewer.ReadAsync(f.Definition.Reference.Id),"RETURN"));
        var returned=await o.ReadAsync(f.Definition.Reference.Id);Assert.Equal("RETURNED",returned.Control!.State);Assert.False(Assert.Single(returned.Votes).Body.Approved);
        await o.CommandAsync(EvaluationCommand(returned,"SUBMIT") with{Requirement=returned.Requirement.Reference});var next=await o.ReadAsync(f.Definition.Reference.Id);
        Assert.NotEqual(first.Packet!.Reference,next.Packet!.Reference);Assert.Empty(next.Votes);
        await o.CommandAsync(EvaluationCommand(next,"POLICY") with{Policy=EvaluationPolicy() with{State="DISABLED"}});var disabled=await o.ReadAsync(f.Definition.Reference.Id);
        Assert.Equal("RETURNED",disabled.Control!.State);Assert.Equal("REQUIREMENT_REVIEW_POLICY_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(EvaluationCommand(disabled,"SUBMIT") with{Requirement=disabled.Requirement.Reference}))).Message);
        Assert.Null(disabled.CurrentApproval);
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementEvaluation_record_control_and_original_drop_roll_back_and_same_key_recovers()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await EvaluationSetup(h);var o=Evaluations(h);var v=await o.ReadAsync(f.Definition.Reference.Id);
        var c=EvaluationCommand(v,"POLICY") with{Policy=EvaluationPolicy()};
        foreach(var fragment in new[]{"INSERT INTO cp6_requirement_evaluation_v1.records","INSERT INTO cp6_requirement_evaluation_v1.controls","UPDATE cp6_requirement_evaluation_v1.heads SET revision=","INSERT INTO cp6_main_owner_v1.operations"})
        {
            hook.Before=cmd=>{if(cmd.CommandText.Contains(fragment))cmd.CommandText="SELECT 1";return Task.CompletedTask;};
            Assert.NotNull(await Record.ExceptionAsync(()=>o.CommandAsync(c)));hook.Before=null;
            Assert.Null((await o.ReadAsync(f.Definition.Reference.Id)).Control);Assert.Equal("UNKNOWN",(await o.OperationAsync(c.OperationId)).State);
        }
        var saved=await o.CommandAsync(c);Assert.Equal(saved,(await o.OperationAsync(c.OperationId)).Result);
        var closed=EvaluationCommand(await o.ReadAsync(f.Definition.Reference.Id),"POLICY") with{Policy=EvaluationPolicy()};await o.OperationAsync(closed.OperationId,true);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(closed))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementEvaluation_native_hold_rechecks_late_source_and_current_control()
    {
        await using var h=await Seed();var f=await EvaluationSetup(h);var approved=await EvaluationApprove(h,f);var o=Evaluations(h);
        var error=await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>EvaluationAuthority(h).ExecuteAsync("plm-engineering-requirement","query",true,async(a,t)=>{
            var proof=await o.HoldExact(a,f.Definition.Reference,approved.ControlReference!,t);Assert.Equal("HELD_IN_CURRENT_UOW",proof.Observation);
            var store=new EngineeringRequirementStoreV1(h.Db);await store.Fence(true,t);await store.Append(ReviseRequirement(f.Source,f.Source.Body with{Name="Late changed original"}),h.User,t);return true;
        }));Assert.Equal("REQUIREMENT_REVIEW_SOURCE_CHANGED",error.Message);
        Assert.Equal(f.Source.OriginalJson,(await EvaluationDrafts(h).ReadAsync(f.Source.Reference.Id)).OriginalJson);
        await o.CommandAsync(EvaluationCommand(approved,"POLICY") with{Policy=EvaluationPolicy()});
        Assert.Equal("REQUIREMENT_REVIEW_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>EvaluationAuthority(h).ExecuteAsync("plm-engineering-requirement","query",false,(a,t)=>o.HoldExact(a,f.Definition.Reference,approved.ControlReference!,t)))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementEvaluation_mode_environment_tenant_and_current_field_guard_history_and_recovery()
    {
        await using var h=await Seed();var f=await EvaluationSetup(h);var o=Evaluations(h);var v=await o.ReadAsync(f.Definition.Reference.Id);var c=EvaluationCommand(v,"POLICY") with{Policy=EvaluationPolicy()};await o.CommandAsync(c);
        Assert.Equal("REQUIREMENT_REVIEW_MODE_MISMATCH",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>Evaluations(h,"REAL").ReadAsync(f.Definition.Reference.Id))).Message);
        Assert.Equal("REQUIREMENT_REVIEW_ENVIRONMENT_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>Evaluations(h,"").ReadAsync(f.Definition.Reference.Id))).Message);
        Assert.Equal("SCOPE_ENVIRONMENT_MISMATCH",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>Evaluations(h,environment:Guid.NewGuid()).ReadAsync(f.Definition.Reference.Id))).Message);
        await using var other=await Seed();await EvaluationSetup(other);Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>Evaluations(other).ReadAsync(f.Definition.Reference.Id))).Status);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="plm-engineering-requirement",FieldName="Source",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ReadAsync(f.Definition.Reference.Id,"1"))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.OperationAsync(c.OperationId))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementEvaluation_actual_Program_HTTP_C02_policy_CAS_signatures_strict_contract_and_recovery()
    {
        await using var h=await Seed();var f=await EvaluationSetup(h);var settings=new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString(),["RequirementEvaluation__EvidenceMode"]="DEMO"};
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,settings);var cookies=await Login(host,h);const string url="/api/requirement-evaluations/owner-v1";
        async Task<RequirementEvaluationViewV1> Read(Dictionary<string,string> identity){using var response=await host.Send(OperationsRequest(identity,HttpMethod.Get,url+"/"+f.Definition.Reference.Id));return await DocumentHttpData<RequirementEvaluationViewV1>(response);}
        async Task<HttpResponseMessage> Save(Dictionary<string,string> identity,object body,bool csrf=true){using var request=OperationsRequest(identity,HttpMethod.Post,url+"/commands",body);if(!csrf)request.Headers.Remove("X-CSRF-Token");return await host.Send(request);}
        var v=await Read(cookies);var c=EvaluationCommand(v,"POLICY") with{Policy=EvaluationPolicy()};
        var missingDate=JsonSerializer.SerializeToNode(c,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        missingDate["policy"]!.AsObject().Remove("effectiveFrom");
        using(var invalidDate=await Save(cookies,missingDate))Assert.Equal(HttpStatusCode.BadRequest,invalidDate.StatusCode);
        using(var denied=await Save(cookies,c,false))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var saved=await Save(cookies,c)){await DocumentHttpData<RequirementEvaluationResultV1>(saved);Assert.True(saved.Headers.CacheControl?.NoStore);}
        using(var stale=await Save(cookies,EvaluationCommand(v,"SUBMIT") with{Requirement=v.Requirement.Reference}))Assert.Equal(HttpStatusCode.PreconditionFailed,stale.StatusCode);
        v=await Read(cookies);using(var submitted=await Save(cookies,EvaluationCommand(v,"SUBMIT") with{Requirement=v.Requirement.Reference}))Assert.Equal("IN_REVIEW",(await DocumentHttpData<RequirementEvaluationResultV1>(submitted)).State);
        foreach(var actor in new[]{f.First,f.Second}){var reviewer=await DocumentReviewerLogin(host,h,actor);using var signed=await Save(reviewer,EvaluationCommand(await Read(reviewer),"APPROVE"));await DocumentHttpData<RequirementEvaluationResultV1>(signed);}
        Assert.Equal("APPROVED",(await Read(cookies)).Control!.State);
        using(var original=await host.Send(OperationsRequest(cookies,HttpMethod.Get,url+"/operations/"+c.OperationId)))Assert.Equal("COMMITTED",(await DocumentHttpData<RequirementEvaluationOperationV1>(original)).State);
        var forged=JsonSerializer.SerializeToNode(c,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;forged["approved"]=true;
        using(var invalid=await Save(cookies,forged))Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
    }
}
