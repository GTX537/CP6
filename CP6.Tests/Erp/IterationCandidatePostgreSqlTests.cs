using System.Net;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Common;
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
    private static OperationsOwnerV1 CandidateAuthority(Harness h,Func<CancellationToken,Task<Guid>>? actor=null)
    {
        var catalog=new AuthorizationCatalogStoreV1();
        foreach(var key in new[]{"plm-engineering-baseline","plm-engineering-iteration","plm-configuration-rule","quality-specification","plm-controlled-document","file-content","plm-engineering-revision","plm-engineering-product","space-site","uom-profile","plm-engineering-bom","plm-engineering-routing","plm-tool-specification","erp-plate-mold","mes-machine","mes-work-center","product","plm-requirement-set","plm-engineering-requirement"})
        {
            catalog.RegisterScope(key,key,[1,2,3,4,5],5);catalog.RegisterFields(key,key switch{
                // RegistryVersion hashes the complete definitions, including labels. Keep the
                // native setup consistent with Program when its original is consumed over HTTP.
                "plm-engineering-iteration"=>[new("Metadata","项目计划"),new("Inputs","准确输入"),new("Tasks","任务交付")],
                "plm-requirement-set"=>[new("Scope","适用范围"),new("Members","准确成员")],"plm-engineering-requirement"=>[new("Definition","需求定义"),new("Source","需求来源")],
                "quality-specification"=>[new("Metadata","规格资料"),new("Requirements","完整质量要求")],
                "plm-controlled-document"=>[new("Content","文档内容"),new("Metadata","文档资料")],"file-content"=>[new("Content","文件内容"),new("FileName","文件名称")],_=>[]});
        }
        catalog.Seal();return new(h.Db,new OperationsHttp{HttpContext=h.Http},actor??(_=>Task.FromResult(h.User)),catalog.Capture);
    }
    private static EngineeringSourceCheckService CandidateChecks(Harness h)=>new(h.Db,CandidateAuthority(h),Documents(h,new ContentStore()),Specifications(h,new ContentStore()));
    private static IterationCandidateService Candidates(Harness h)=>new(h.Db,CandidateAuthority(h),Iterations(h),CandidateChecks(h),ScopeFence(h));
    private sealed record CandidateFixture(IterationFixture Iteration,EngineeringRevisionSnapshotV1 Revision,EngineeringCheckViewV1 Check,IterationCandidateCommandV1 Command);
    private async Task<CandidateFixture> CandidateSetup(Harness h,bool start=true)
    {
        await GrantEngineering(h);var product=await MakeEngineering(h);var iteration=await IterationSetup(h,product:product.Reference);
        await EngineeringAction(h,"plm-engineering-iteration","candidate-write");await GrantEngineeringRevision(h);await RevisionRule(h);await GrantConfiguration(h);
        var rules=ConfigurationRules(h);var command=await NewConfiguration(rules,product.Reference);
        command=command with{Body=command.Body! with{Scope=EngineeringSourceCheckValuesV1.PolicyScope,
            EngineeringChecks=new("engineering-check-policy/1",product.Product.Category,"INTERNAL","APPLIES","Actual draft requires all engineering content",
                EngineeringSourceCheckValuesV1.Kinds.Select(k=>new EngineeringCompositionPolicyV1(k,"APPLIES",1,"Required native source; unresolved remains blocked")).ToArray())}};
        await rules.CommandAsync(command);var policy=await rules.ReadAsync(command.RuleId,null);policy=await Step(rules,await Step(rules,policy,"SUBMIT"),"APPROVE");
        var revisions=ConfigurationRevisions(h);var revision=await MakeRevision(revisions,product.Reference);
        await revisions.CommandAsync(PatchRevision(revision,new EngineeringDraftPatchV1{Op="SET_CONFIGURATION",Configuration=new(ValidOptions(),"APPLIES","Exact current policy",policy.Reference)}));
        revision=await revisions.ReadAsync(revision.Reference.Id,null);
        var checks=CandidateChecks(h);var checkedResult=await checks.CheckAsync(new(Guid.NewGuid(),revision.Reference,revision.CandidateRef,revision.Etag,policy.Reference,"Native incomplete design observation"));
        var check=await checks.ReadAsync(checkedResult.CheckRunId);Assert.Equal("BLOCK",check.Original.State);Assert.Equal("CURRENT",check.Freshness);
        var work=iteration.Iteration;var o=Iterations(h);work=await IterationWrite(o,IterationChange(work,"SAVE_TASK") with{Task=IterationTask(h,iteration.Reviewer)});
        if(start)work=await IterationWrite(o,IterationChange(work,"START"));iteration=iteration with{Iteration=work};
        return new(iteration,revision,check,new(Guid.NewGuid(),work.Reference.Id,Guid.NewGuid(),"CREATE",work.Reference,work.Etag,null,null,
            "方案甲","准确预检原件；技术内容尚未齐备","待完成专业技术对象",check.Reference,null,"ADOPT_CURRENT","","Explicit candidate draft"));
    }
    [CurrentPermissionPostgresFact]
    public async Task IterationCandidate_multiple_native_candidates_exact_history_parent_diff_and_recovery()
    {
        await using var h=await Seed();var f=await CandidateSetup(h);var o=Candidates(h);var c=f.Command;
        var first=await o.CommandAsync(c);Assert.Equal(first,await o.CommandAsync(c));Assert.Equal(first,(await o.OperationAsync(c.OperationId,false)).Result);
        var a=await o.ReadAsync(c.IterationId,c.CandidateId);Assert.NotEqual(a.Reference.Id,a.Original.Definition.EngineeringCandidate.Id);
        Assert.Equal(f.Check.OriginalJson,a.Original.Definition.TechnicalSourceOriginal);Assert.Equal(f.Check.Original.Manifest!.Digest,a.Original.Definition.TechnicalSource.Manifest!.Digest);
        Assert.Contains("TECHNICAL_CHECK_BLOCK",a.Findings);Assert.Equal("NOT_SUBMITTED",a.Review);Assert.Equal("NOT_REQUESTED",a.Freeze);
        var b=c with{OperationId=Guid.NewGuid(),CandidateId=Guid.NewGuid(),Title="方案乙",ParentCandidate=a.Reference};await o.CommandAsync(b);var branched=await o.ReadAsync(c.IterationId,b.CandidateId);
        Assert.Equal(a.Reference,branched.Original.Definition.ParentCandidate);Assert.Contains(branched.Original.ParentChanges,x=>x.Path=="title");Assert.Equal(2,(await o.ListAsync(c.IterationId)).Length);
        var revise=c with{OperationId=Guid.NewGuid(),Action="REVISE",ExpectedVersion="1",ExpectedEtag=a.Original.Etag,Title="方案甲修订"};await o.CommandAsync(revise);var next=await o.ReadAsync(c.IterationId,c.CandidateId);
        Assert.Equal("2",next.Reference.Version);Assert.Equal("1",next.Original.PreviousVersion);Assert.Equal(a.OriginalJson,(await o.ReadAsync(c.IterationId,c.CandidateId,"1")).OriginalJson);
        Assert.Contains((await o.CompareAsync(c.IterationId,a.Reference,next.Reference)).Changes,x=>x.Path=="title");Assert.Equal(first,(await o.OperationAsync(c.OperationId,false)).Result);
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(c with{Title="Altered original"}))).Message);
        Assert.Equal("ITERATION_CANDIDATE_VERSION_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(revise with{OperationId=Guid.NewGuid()}))).Message);
        Assert.Equal("ITERATION_CANDIDATE_PARENT_IMMUTABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(b with{Action="REVISE",OperationId=Guid.NewGuid(),ExpectedVersion="1",ExpectedEtag=branched.Original.Etag,ParentCandidate=null}))).Message);
        var late=c with{OperationId=Guid.NewGuid(),CandidateId=Guid.NewGuid()};await o.OperationAsync(late.OperationId,true);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(late))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task IterationCandidate_stage_hold_and_input_change_do_not_retarget_original()
    {
        await using var h=await Seed();var f=await CandidateSetup(h,false);var o=Candidates(h);var i=Iterations(h);var c=f.Command;
        Assert.Equal("ITERATION_CANDIDATE_STAGE_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(c))).Message);
        var work=await IterationWrite(i,IterationChange(f.Iteration.Iteration,"START"));c=c with{ExpectedIteration=work.Reference,ExpectedIterationEtag=work.Etag};await o.CommandAsync(c);
        var before=await o.ReadAsync(c.IterationId,c.CandidateId);work=await IterationWrite(i,IterationChange(work,"HOLD"));
        Assert.Equal("ITERATION_HELD",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(c with{OperationId=Guid.NewGuid(),CandidateId=Guid.NewGuid()}))).Message);
        work=await IterationWrite(i,IterationChange(work,"RESUME"));work=await IterationWrite(i,IterationChange(work,"REPLACE_INPUTS") with{RequirementSet=work.Body.Iteration!.Input.Proof.RequirementSet});
        var after=await o.ReadAsync(c.IterationId,c.CandidateId);Assert.Equal(before.OriginalJson,after.OriginalJson);Assert.Contains("ITERATION_INPUT_CHANGED",after.Findings);Assert.Equal("CHANGED",after.SourceDrift);
        Assert.Equal("ITERATION_VERSION_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(c with{OperationId=Guid.NewGuid(),CandidateId=Guid.NewGuid()}))).Message);
        Assert.Equal("1",after.Original.Definition.InputVersion);Assert.Equal("2",work.Body.Iteration!.InputVersion);
    }
    [CurrentPermissionPostgresFact]
    public async Task IterationCandidate_native_source_drift_requires_explicit_choice_and_preserves_occurrences()
    {
        await using var h=await Seed();var f=await CandidateSetup(h);var o=Candidates(h);await o.CommandAsync(f.Command);
        var revisions=ConfigurationRevisions(h);var first=Guid.NewGuid();var second=Guid.NewGuid();
        await revisions.CommandAsync(PatchRevision(f.Revision,
            new EngineeringDraftPatchV1{Op="UPSERT_COMPONENT",Component=new(first,"DOCUMENT","/one",0,"APPLIES","","",null)},
            new EngineeringDraftPatchV1{Op="UPSERT_COMPONENT",Component=new(second,"DOCUMENT","/two",1,"APPLIES","","",null)}));
        Assert.Equal("CHANGED",(await o.ReadAsync(f.Command.IterationId,f.Command.CandidateId)).SourceDrift);
        var changed=f.Command with{OperationId=Guid.NewGuid(),CandidateId=Guid.NewGuid()};
        Assert.Equal("ITERATION_CANDIDATE_SOURCE_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(changed))).Message);
        await o.CommandAsync(changed with{SourceDecision="KEEP_ORIGINAL",DriftReason="Retain original for comparison; no approval inferred"});
        var current=await revisions.ReadAsync(f.Revision.Reference.Id,null);var checks=CandidateChecks(h);var res=await checks.CheckAsync(f.Check.Original.Command with{OperationId=Guid.NewGuid(),Revision=current.Reference,Candidate=current.CandidateRef,ExpectedEtag=current.Etag});
        var check=await checks.ReadAsync(res.CheckRunId);var add=changed with{OperationId=Guid.NewGuid(),CandidateId=Guid.NewGuid(),SourceCheck=check.Reference};await o.CommandAsync(add);
        var value=await o.ReadAsync(add.IterationId,add.CandidateId);Assert.Equal("UNKNOWN",value.SourceDrift);
        Assert.Equal(new[]{first,second},value.Original.Definition.TechnicalSource.Input.Components.Select(x=>x.OccurrenceId));
        using var manifest=JsonDocument.Parse(value.Original.Definition.TechnicalSourceOriginal);Assert.Equal(2,manifest.RootElement.GetProperty("input").GetProperty("components").GetArrayLength());
    }
    [CurrentPermissionPostgresFact]
    public async Task IterationCandidate_current_source_and_project_membership_revocation_protect_original_and_diff()
    {
        await using var h=await Seed();var f=await CandidateSetup(h);var o=Candidates(h);await o.CommandAsync(f.Command);
        var v=await o.ReadAsync(f.Command.IterationId,f.Command.CandidateId);var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="plm-configuration-rule");
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==menu.MenuId&&x.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ReadAsync(f.Command.IterationId,f.Command.CandidateId))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.OperationAsync(f.Command.OperationId,false))).Status);
        await EngineeringAction(h,"plm-configuration-rule","query");var p=f.Iteration.Project;var i=Iterations(h);
        p=await IterationWrite(i,IterationChange(p,"EDIT_PROJECT") with{Project=p.Body.Project!.Definition with{OwnerId=f.Iteration.Reviewer}});
        await IterationWrite(Iterations(DecisionActor(h,f.Iteration.Reviewer)),IterationChange(p,"EDIT_PROJECT") with{Project=p.Body.Project!.Definition with{Members=[f.Iteration.Reviewer]}});
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ListAsync(f.Command.IterationId))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CompareAsync(f.Command.IterationId,v.Reference,v.Reference))).Status);
        await using var other=await Seed();await RegisterScope(other);await GrantIteration(other);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>Candidates(other).ReadAsync(f.Command.IterationId,f.Command.CandidateId))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task IterationCandidate_immutable_provenance_unknown_outcome_and_concurrent_CAS()
    {
        var fault=new InterruptCurrencyCommit();await using var h=await Seed(fault);var f=await CandidateSetup(h);var o=Candidates(h);fault.Interrupt=true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(f.Command))).Message);fault.Interrupt=false;
        Assert.Equal("UNKNOWN",(await o.OperationAsync(f.Command.OperationId,false)).State);await o.CommandAsync(f.Command);var v=await o.ReadAsync(f.Command.IterationId,f.Command.CandidateId);
        await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var peer=Candidates(h with{Db=db});
        var revise=f.Command with{OperationId=Guid.NewGuid(),Action="REVISE",ExpectedVersion="1",ExpectedEtag=v.Original.Etag,Title="Concurrent"};
        async Task<string> Run(IterationCandidateService service,IterationCandidateCommandV1 c){try{return(await service.CommandAsync(c)).Version;}catch(OrderPriceCorrectionException e){return e.Message;}}
        var results=await Task.WhenAll(Run(o,revise),Run(peer,revise with{OperationId=Guid.NewGuid()}));Assert.Single(results,x=>x=="2");Assert.Single(results,x=>x=="ITERATION_CANDIDATE_VERSION_CHANGED");
        foreach(var sql in new[]{"DELETE FROM cp6_iteration_candidate_v1.versions","UPDATE cp6_iteration_candidate_v1.versions SET body_bytes=body_bytes","TRUNCATE cp6_iteration_candidate_v1.heads CASCADE"})await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync(sql));
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_iteration_candidate_v1.versions DISABLE TRIGGER iteration_candidate_provenance");
        try{Assert.Equal("ITERATION_CANDIDATE_STORE_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ReadAsync(f.Command.IterationId,f.Command.CandidateId))).Message);}
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_iteration_candidate_v1.versions ENABLE TRIGGER iteration_candidate_provenance");}
    }
    [CurrentPermissionPostgresFact]
    public async Task IterationCandidate_actual_Program_HTTP_command_replay_strict_binding_and_revocation()
    {
        await using var h=await Seed();var f=await CandidateSetup(h);var settings=new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString(),["RequirementEvaluation__EvidenceMode"]="DEMO"};
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,settings);var cookies=await Login(host,h);
        async Task<HttpResponseMessage> Save(string body){var request=OperationsRequest(cookies,HttpMethod.Post,"api/iteration-candidates/owner-v1/commands");request.Content=new StringContent(body,Encoding.UTF8,"application/json");return await host.Send(request);}
        // Re-observe through the actual Program authority catalog before use by that same host.
        var checkRequest=OperationsRequest(cookies,HttpMethod.Post,"api/engineering-source-checks/owner-v1/commands");
        checkRequest.Content=new StringContent(JsonSerializer.Serialize(f.Check.Original.Command with{OperationId=Guid.NewGuid()},new JsonSerializerOptions(JsonSerializerDefaults.Web)),Encoding.UTF8,"application/json");
        using var checkResponse=await host.Send(checkRequest);Assert.Equal(HttpStatusCode.OK,checkResponse.StatusCode);
        using var checkJson=JsonDocument.Parse(await checkResponse.Content.ReadAsStringAsync());var checkData=checkJson.RootElement.GetProperty("data");
        var checkRef=new EngineeringSourceRefV1(EngineeringSourceCheckValuesV1.Owner,checkData.GetProperty("checkRunId").GetGuid(),"1",checkData.GetProperty("digest").GetString()!);
        var wire=JsonSerializer.Serialize(f.Command with{SourceCheck=checkRef},new JsonSerializerOptions(JsonSerializerDefaults.Web));using var first=await Save(wire);Assert.Equal(HttpStatusCode.OK,first.StatusCode);Assert.True(first.Headers.CacheControl?.NoStore);
        using var replay=await Save(wire);Assert.Equal(await first.Content.ReadAsStringAsync(),await replay.Content.ReadAsStringAsync());
        using var bad=await Save(wire[..^1]+",\"tenantId\":\""+Guid.NewGuid()+"\"}");Assert.Equal(HttpStatusCode.BadRequest,bad.StatusCode);
        var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="plm-engineering-iteration");await h.Db.Sys_RoleActions.Where(x=>x.MenuId==menu.MenuId&&x.ActionCode=="query").ExecuteDeleteAsync();
        using var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"api/iteration-candidates/owner-v1/operations/{f.Command.OperationId}"));Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
    }
}
