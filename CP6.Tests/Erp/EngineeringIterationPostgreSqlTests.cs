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
    private static OperationsOwnerV1 IterationAuthority(Harness h)
    {
        var catalog=new AuthorizationCatalogStoreV1();
        foreach(var key in new[]{"plm-engineering-iteration","space-site","plm-engineering-product","plm-engineering-revision","plm-controlled-document","file-content","plm-requirement-set","plm-engineering-requirement","uom-profile"})
        {
            catalog.RegisterScope(key,key,[1,2,3,4,5],5);catalog.RegisterFields(key,key switch{
                "plm-engineering-iteration"=>[new("Metadata","Metadata"),new("Inputs","Inputs"),new("Tasks","Tasks")],
                "plm-requirement-set"=>[new("Scope","Scope"),new("Members","Members")],"plm-engineering-requirement"=>[new("Definition","Definition"),new("Source","Source")],
                "plm-controlled-document"=>[new("Content","Content"),new("Metadata","Metadata")],"file-content"=>[new("Content","Content"),new("FileName","FileName")],_=>[]});
        }
        catalog.Seal();return new(h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),catalog.Capture);
    }
    private static EngineeringIterationService Iterations(Harness h,ContentStore? files=null)=>new(h.Db,IterationAuthority(h),Sets(h),Documents(h,files??new()),ScopeFence(h));
    private static async Task GrantIteration(Harness h,bool rules=true)
    {
        foreach(var action in new[]{"query","add","edit","start","hold","resume","task-submit","task-confirm"})await EngineeringAction(h,"plm-engineering-iteration",action);
        await EngineeringAction(h,"space-site","query");
        if(rules)h.Db.Pub_DocSequences.AddRange(new(){BizKey="PLM_PROJECT",Prefix="PRJ",SeqLength=4,ResetCycle=0},new(){BizKey="PLM_ITERATION",Prefix="ITER",SeqLength=4,ResetCycle=0});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
    }
    private static IterationProjectInputV1 ProjectDraft(Harness h,Guid? member=null)=>new("Native project",h.User,member==null?[h.User]:[h.User,member.Value],null,null,"",null,null,[]);
    private static async Task<EngineeringIterationCommandV1> NewProject(EngineeringIterationService owner,IterationProjectInputV1 p)=>new(){OperationId=Guid.NewGuid(),ObjectId=Guid.NewGuid(),Action="CREATE_PROJECT",Project=p,Reason="Native engineering project",ExpectedAuthorityVersion=(await owner.CreationContextAsync()).AuthorityVersion};
    private static EngineeringIterationCommandV1 IterationChange(EngineeringIterationSnapshotV1 v,string action)=>new(){OperationId=Guid.NewGuid(),ObjectId=v.Reference.Id,Action=action,ExpectedVersion=v.Reference.Version,ExpectedEtag=v.Etag,Reason="Explicit iteration change"};
    private static async Task<EngineeringIterationSnapshotV1> IterationWrite(EngineeringIterationService o,EngineeringIterationCommandV1 c){await o.CommandAsync(c);return await o.ReadAsync(c.ObjectId);}
    private sealed record IterationFixture(EngineeringIterationSnapshotV1 Project,EngineeringIterationSnapshotV1 Iteration,SetApplicationFixture Set,Guid Reviewer);
    private async Task<IterationFixture> IterationSetup(Harness h,ContentStore? files=null,EngineeringProductRefV1? product=null)
    {
        var f=await SetApplicationSetup(h);FactAck(await f.Receiver.ReceiveAsync(f.Payload));var reviews=SetReviews(h);var ready=await reviews.SubmissionAsync(f.Set.Source.Set.Reference.Id,f.Set.Submission);
        await reviews.ApplyAsync(f.Set.Source.Set.Reference.Id,ready.Application.Etag,new(Guid.NewGuid(),f.Set.Submission,null,"Approve accurate iteration input"));
        await GrantIteration(h);var reviewer=Guid.NewGuid();h.Db.Sys_Users.Add(new(){Id=reviewer,UserName="iteration-confirmer",Password="fixture",RoleId=72});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var o=Iterations(h,files);var siteId=await PhysicalSite(h,"ITER-SITE");var site=(await o.SitesAsync()).Single(x=>x.Reference.Id==siteId).Reference;
        var draft=ProjectDraft(h,reviewer) with{Site=site,Product=product,Objective="Deliver accurate design",StartDate=new(2026,10,1),EndDate=new(2026,12,31),Milestones=[new(Guid.NewGuid(),"Review", "Exact evidence accepted by assigned reviewer",new(2026,12,20))]};
        var project=await IterationWrite(o,await NewProject(o,draft));project=await IterationWrite(o,IterationChange(project,"ACTIVATE_PROJECT"));
        var command=new EngineeringIterationCommandV1{OperationId=Guid.NewGuid(),ObjectId=Guid.NewGuid(),Action="CREATE_ITERATION",ProjectId=project.Reference.Id,Plan=new("Design iteration",h.User,"Produce exact design evidence",new(2026,10,28),new(2026,11,30)),Origin=new("NEW_PRODUCT","First product without previous baseline"),RequirementSet=f.Set.Source.Set.Reference,Reason="Adopt exact approved complete input"};
        return new(project,await IterationWrite(o,command),f,reviewer);
    }
    private static IterationTaskPlanV1 IterationTask(Harness h,Guid reviewer,string weight="1")=>new(Guid.NewGuid(),"Deliver drawing",h.User,reviewer,weight,new(2026,11,20),"Read exact drawing and confirm specified task deliverable");
    [CurrentPermissionPostgresFact]
    public async Task EngineeringIteration_project_number_original_history_CAS_and_late_write_closure()
    {
        await using var h=await Seed();await RegisterScope(h);await GrantIteration(h,false);var o=Iterations(h);var c=await NewProject(o,ProjectDraft(h));
        Assert.Equal("ITERATION_NUMBER_RULE_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(c))).Message);
        Assert.Empty(await o.ListAsync());Assert.Equal("UNKNOWN",(await o.OperationAsync(c.OperationId,false)).State);
        h.Db.Pub_DocSequences.Add(new(){BizKey="PLM_PROJECT",Prefix="PRJ",SeqLength=4,ResetCycle=0});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var result=await o.CommandAsync(c);Assert.Equal(result,await o.CommandAsync(c));Assert.Equal(result,(await o.OperationAsync(c.OperationId,false)).Result);
        var first=await o.ReadAsync(c.ObjectId);Assert.Equal("PRJ0001",first.Body.Number);Assert.Contains("SITE_REQUIRED",first.Missing);
        var edit=IterationChange(first,"EDIT_PROJECT") with{Project=first.Body.Project!.Definition with{Name="Changed name"}};var next=await IterationWrite(o,edit);
        Assert.Equal("2",next.Reference.Version);Assert.Equal(first.OriginalJson,(await o.ReadAsync(c.ObjectId,"1")).OriginalJson);
        Assert.Equal("ITERATION_VERSION_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(edit with{OperationId=Guid.NewGuid()}))).Message);
        var late=IterationChange(next,"EDIT_PROJECT") with{Project=next.Body.Project!.Definition};await o.OperationAsync(late.OperationId,true);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(late))).Message);
        foreach(var sql in new[]{"DELETE FROM cp6_engineering_iteration_v1.versions","TRUNCATE cp6_engineering_iteration_v1.heads CASCADE","UPDATE cp6_engineering_iteration_v1.versions SET body_bytes=body_bytes"})await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync(sql));
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringIteration_exact_approved_input_hold_resume_and_withdrawn_source_keep_original_versions()
    {
        await using var h=await Seed();var f=await IterationSetup(h);var o=Iterations(h);var first=f.Iteration;
        Assert.Equal("UNCHANGED",first.SourceDrift);Assert.Equal(f.Set.Set.Source.Set.OriginalJson,first.Body.Iteration!.Input.RequirementSetOriginal);Assert.Null(first.Progress!.Percent);
        var task=IterationTask(h,f.Reviewer,"1000000000000000000000000000000.00000000000000000001");var v=await IterationWrite(o,IterationChange(first,"SAVE_TASK") with{Task=task});
        Assert.Equal("2",v.Body.Iteration!.PlanVersion);Assert.Equal("1",v.Body.Iteration.InputVersion);v=await IterationWrite(o,IterationChange(v,"START"));
        v=await IterationWrite(o,IterationChange(v,"BLOCK_TASK") with{TaskId=task.Id});Assert.Equal("0.00",v.Progress!.Percent);Assert.Equal(task.Weight,v.Progress.PlannedWeight);
        v=await IterationWrite(o,IterationChange(v,"HOLD"));Assert.Equal("ITERATION_HELD",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(IterationChange(v,"SAVE_TASK") with{Task=task}))).Message);
        v=await IterationWrite(o,IterationChange(v,"RESUME"));Assert.Equal("IN_DESIGN",v.Body.Iteration!.Stage);
        await Sets(h).CommandAsync(SetRevise(f.Set.Set.Source.Set,f.Set.Set.Source.Set.Body with{Name="Later set draft"}));Assert.Equal("UNCHANGED",(await o.ReadAsync(v.Reference.Id)).SourceDrift);
        v=await IterationWrite(o,IterationChange(v,"HOLD"));var evaluations=Evaluations(h);await evaluations.CommandAsync(EvaluationCommand(await evaluations.ReadAsync(f.Set.Set.Source.Member.Definition.Reference.Id),"WITHDRAW"));
        Assert.Equal("CHANGED",(await o.ReadAsync(v.Reference.Id)).SourceDrift);Assert.Equal(first.OriginalJson,(await o.ReadAsync(v.Reference.Id,"1")).OriginalJson);
        Assert.Equal(422,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(IterationChange(v,"RESUME")))).Status);
        Assert.Equal("HELD",(await o.ReadAsync(v.Reference.Id)).Body.Iteration!.HoldState);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringIteration_independent_task_confirmation_requires_native_file_deliverable_and_never_closes_iteration()
    {
        await using var h=await Seed();var files=new ContentStore();var doc=await DocumentInput(h,files);await Documents(h,files).CommandAsync(doc);var exact=await Documents(h,files).ReadAsync(doc.DocumentId);
        var f=await IterationSetup(h,files,doc.Body.ProductRef);var o=Iterations(h,files);var task=IterationTask(h,f.Reviewer,"0.000000000000000000000000001");
        var other=IterationTask(h,f.Reviewer,"0.000000000000000000000000002");
        var v=await IterationWrite(o,IterationChange(f.Iteration,"SAVE_TASK") with{Task=task});
        v=await IterationWrite(o,IterationChange(v,"SAVE_TASK") with{Task=other});v=await IterationWrite(o,IterationChange(v,"START"));
        v=await IterationWrite(o,IterationChange(v,"BLOCK_TASK") with{TaskId=other.Id});
        Assert.Equal("ITERATION_DELIVERABLE_OR_APPROVED_EXEMPTION_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(IterationChange(v,"SUBMIT_TASK") with{TaskId=task.Id,Deliverables=[]}))).Message);
        v=await IterationWrite(o,IterationChange(v,"SUBMIT_TASK") with{TaskId=task.Id,Deliverables=[exact.Reference]});Assert.Equal("0.00",v.Progress!.Percent);
        Assert.Equal("ITERATION_INDEPENDENT_CONFIRMER_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(IterationChange(v,"CONFIRM_TASK") with{TaskId=task.Id}))).Message);
        var reviewer=Iterations(DecisionActor(h,f.Reviewer),files);
        await h.Db.Sys_Users.Where(x=>x.Id==h.User).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Enable,false));
        v=await IterationWrite(reviewer,IterationChange(v,"RETURN_TASK") with{TaskId=task.Id,Reason="Clarify drawing before confirmation"});Assert.Equal("BLOCKED",v.Body.Iteration!.Tasks.Single(x=>x.Plan.Id==task.Id).State);Assert.Equal("0.00",v.Progress!.Percent);
        await h.Db.Sys_Users.Where(x=>x.Id==h.User).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Enable,true));
        v=await IterationWrite(o,IterationChange(v,"SUBMIT_TASK") with{TaskId=task.Id,Deliverables=[exact.Reference]});
        var done=IterationChange(v,"CONFIRM_TASK") with{TaskId=task.Id};var result=await reviewer.CommandAsync(done);
        Assert.Equal(result,await reviewer.CommandAsync(done));v=await o.ReadAsync(v.Reference.Id);Assert.Equal("33.33",v.Progress!.Percent);Assert.Equal("0.000000000000000000000000003",v.Progress.PlannedWeight);
        Assert.Equal("IN_DESIGN",v.Body.Iteration!.Stage);Assert.Equal("DONE",v.Body.Iteration.Tasks.Single(x=>x.Plan.Id==task.Id).State);
        Assert.Equal(f.Reviewer,v.Body.Iteration.Tasks.Single(x=>x.Plan.Id==task.Id).ConfirmedBy);Assert.Equal("3",v.Body.Iteration.PlanVersion);Assert.Equal("1",v.Body.Iteration.InputVersion);
        v=await IterationWrite(o,IterationChange(v,"CANCEL_TASK") with{TaskId=other.Id});Assert.Equal("100.00",v.Progress!.Percent);Assert.Equal("4",v.Body.Iteration!.PlanVersion);Assert.Equal("IN_DESIGN",v.Body.Iteration.Stage);
        Assert.Equal("ITERATION_CONFIRMED_INPUT_IMPACT_REVIEW_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(IterationChange(v,"REPLACE_INPUTS") with{RequirementSet=f.Set.Set.Source.Set.Reference}))).Message);
        Assert.Equal("ITERATION_TASK_PLAN_LOCKED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(IterationChange(v,"CANCEL_TASK") with{TaskId=task.Id}))).Message);
        await IterationWrite(o,IterationChange(f.Project,"EDIT_PROJECT") with{Project=f.Project.Body.Project!.Definition with{Product=null}});
        Assert.Equal("CHANGED",(await o.ReadAsync(v.Reference.Id)).SourceDrift);Assert.Equal(v.OriginalJson,(await o.ReadAsync(v.Reference.Id,v.Reference.Version)).OriginalJson);
        Assert.Equal(result,(await reviewer.OperationAsync(done.OperationId,false)).Result);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringIteration_membership_revocation_filters_pages_and_original_recovery_even_with_full_data_scope()
    {
        await using var h=await Seed();var f=await IterationSetup(h);var o=Iterations(h);var member=Iterations(DecisionActor(h,f.Reviewer));
        Assert.Equal(2,(await member.ListAsync()).Length);var c=IterationChange(f.Iteration,"SAVE_TASK") with{Task=IterationTask(h,f.Reviewer)};await o.CommandAsync(c);
        await IterationWrite(o,IterationChange(f.Project,"EDIT_PROJECT") with{Project=f.Project.Body.Project!.Definition with{Members=[h.User]}});
        Assert.Empty(await member.ListAsync());Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>member.ReadAsync(f.Iteration.Reference.Id,"1"))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>member.OperationAsync(c.OperationId,false))).Status);
        await using var other=await Seed();await RegisterScope(other);await GrantIteration(other);Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>Iterations(other).ReadAsync(f.Project.Reference.Id))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringIteration_original_journal_failure_rolls_back_number_and_project_then_exact_key_recovers()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);await RegisterScope(h);await GrantIteration(h);var o=Iterations(h);var c=await NewProject(o,ProjectDraft(h));
        hook.Before=cmd=>{if(cmd.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations"))cmd.CommandText="SELECT 1";return Task.CompletedTask;};
        Assert.NotNull(await Record.ExceptionAsync(()=>o.CommandAsync(c)));hook.Before=null;
        Assert.Empty(await o.ListAsync());Assert.Equal("UNKNOWN",(await o.OperationAsync(c.OperationId,false)).State);Assert.Equal(0,(await h.Db.Pub_DocSequences.AsNoTracking().SingleAsync(x=>x.BizKey=="PLM_PROJECT")).CurrentValue);
        Assert.Equal("PRJ0001",(await IterationWrite(o,c)).Body.Number);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringIteration_current_field_revocation_and_native_scope_before_pagination()
    {
        await using var h=await Seed();await RegisterScope(h);await GrantIteration(h);var o=Iterations(h);var c=await NewProject(o,ProjectDraft(h));var first=await IterationWrite(o,c);
        h.Db.Sys_RoleDataScopes.Add(new(){RoleId=72,ResourceKey="plm-engineering-iteration",ScopeType=4});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Empty(await o.ListAsync());Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ReadAsync(first.Reference.Id))).Status);
        await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey=="plm-engineering-iteration").ExecuteDeleteAsync();
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="plm-engineering-iteration",FieldName="Inputs",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.OperationAsync(c.OperationId,false))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringIteration_actual_Program_HTTP_project_commands_replay_strict_input_and_revocation()
    {
        await using var h=await Seed();await RegisterScope(h);await GrantIteration(h);var settings=new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString(),["RequirementEvaluation__EvidenceMode"]="DEMO"};
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,settings);var cookies=await Login(host,h);
        using var context=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/engineering-iterations/owner-v1/creation-context"));Assert.Equal(HttpStatusCode.OK,context.StatusCode);
        using var json=JsonDocument.Parse(await context.Content.ReadAsStringAsync());var c=await NewProject(Iterations(h),ProjectDraft(h));c=c with{ExpectedAuthorityVersion=json.RootElement.GetProperty("data").GetProperty("authorityVersion").GetString()};
        async Task<HttpResponseMessage> Save(string body){var request=OperationsRequest(cookies,HttpMethod.Post,"api/engineering-iterations/owner-v1/commands");request.Content=new StringContent(body,Encoding.UTF8,"application/json");return await host.Send(request);}
        var wire=JsonSerializer.Serialize(c,new JsonSerializerOptions(JsonSerializerDefaults.Web));using var response=await Save(wire);Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.True(response.Headers.CacheControl?.NoStore);
        using var replay=await Save(wire);Assert.Equal(await response.Content.ReadAsStringAsync(),await replay.Content.ReadAsStringAsync());
        using var bad=await Save(wire[..^1]+",\"tenantId\":\""+Guid.NewGuid()+"\"}");Assert.Equal(HttpStatusCode.BadRequest,bad.StatusCode);
        var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="plm-engineering-iteration");await h.Db.Sys_RoleActions.Where(x=>x.MenuId==menu.MenuId&&x.ActionCode=="query").ExecuteDeleteAsync();
        using var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"api/engineering-iterations/owner-v1/operations/{c.OperationId}"));Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
    }
}
