using System.Net;
using System.Text.Json;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Erp;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static RequirementSetService ImpactSets(Harness h)=>new(h.Db,IterationAuthority(h),Evaluations(h),ScopeFence(h));
    private static async Task GrantImpact(Harness h)
    {await EngineeringAction(h,"plm-requirement-set","impact-request");await EngineeringAction(h,"plm-engineering-iteration","impact-assess");}
    private static async Task<RequirementSetSnapshotV1> ImpactSuccessor(Harness h,IterationFixture f)
    {
        var old=f.Set.Set.Source.Set;var owner=ImpactSets(h);
        await owner.CommandAsync(SetRevise(old,old.Body with{Name="Reviewed successor",Scope="Explicit successor applicability"}));
        return await owner.ReadAsync(old.Reference.Id);
    }
    private static async Task<RequirementImpactRequestCommandV1> ImpactRequest(RequirementSetService owner,EngineeringSourceRefV1 candidate,string action="REQUEST")
    {var context=await owner.ImpactAsync(candidate.Id,candidate.Version);return new(Guid.NewGuid(),candidate,action,context.Etag,"Owner assessment of exact successor applicability");}
    private static IterationImpactAssessmentCommandV1 ImpactAssessment(RequirementImpactConsumerV1 row,string decision)=>
        new(Guid.NewGuid(),row.Case!.Id,row.Iteration,row.Case.Assessment?.Id,decision,"Retain accurate original requirements for this iteration scope");

    [CurrentPermissionPostgresFact]
    public async Task RequirementImpact_owner_assessment_gates_work_reuses_unchanged_plan_and_requires_reassessment_after_plan_change()
    {
        await using var h=await Seed();var f=await IterationSetup(h);await GrantImpact(h);var sets=ImpactSets(h);var iterations=Iterations(h);
        var task=IterationTask(h,f.Reviewer);var v=await IterationWrite(iterations,IterationChange(f.Iteration,"SAVE_TASK") with{Task=task});
        var candidate=await ImpactSuccessor(h,f);var request=await ImpactRequest(sets,candidate.Reference);
        var saved=await sets.RequestImpactAsync(request);Assert.Single(saved.Impact!.CreatedCases);
        Assert.Equal(saved.Impact.RequestId,(await sets.RequestImpactAsync(request)).Impact!.RequestId);
        Assert.Equal(saved.Impact.RequestId,(await sets.OperationAsync(request.OperationId,false)).Result!.Impact!.RequestId);
        Assert.Equal(v.OriginalJson,(await iterations.ReadAsync(v.Reference.Id)).OriginalJson);
        Assert.Equal("ITERATION_REQUIREMENT_IMPACT_PENDING",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>iterations.CommandAsync(IterationChange(v,"START")))).Message);
        var ready=await SetReady(h,candidate.Reference.Id);
        Assert.Equal("REQUIREMENT_SET_IMPACT_ASSESSMENT_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>SetReviews(h).SubmitAsync(candidate.Reference.Id,ready.Etag,SetSubmit(ready)))).Message);
        var row=Assert.Single((await iterations.ImpactAsync(v.Reference.Id)).Cases);var command=ImpactAssessment(row,"KEEP_CURRENT");
        var assessment=await iterations.AssessImpactAsync(command);Assert.Equal(command.OperationId,assessment.ImpactAssessmentId);
        Assert.Equal(assessment,await iterations.AssessImpactAsync(command));
        Assert.Equal("ITERATION_IMPACT_ASSESSMENT_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>iterations.AssessImpactAsync(command with{OperationId=Guid.NewGuid(),Decision="BLOCKED"}))).Message);
        v=await IterationWrite(iterations,IterationChange(v,"START"));v=await IterationWrite(iterations,IterationChange(v,"START_TASK") with{TaskId=task.Id});
        row=Assert.Single((await iterations.ImpactAsync(v.Reference.Id)).Cases);Assert.Equal("KEEP_CURRENT",row.State);Assert.Equal(command.OperationId,row.Case!.Assessment!.Id);
        v=await IterationWrite(iterations,IterationChange(v,"SAVE_TASK") with{Task=task with{Acceptance="Additional exact design check"}});
        Assert.Equal("REASSESSMENT_REQUIRED",Assert.Single((await iterations.ImpactAsync(v.Reference.Id)).Cases).State);
        Assert.Equal("ITERATION_REQUIREMENT_IMPACT_PENDING",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>iterations.CommandAsync(IterationChange(v,"START_TASK") with{TaskId=task.Id}))).Message);
        row=Assert.Single((await iterations.ImpactAsync(v.Reference.Id)).Cases);await iterations.AssessImpactAsync(ImpactAssessment(row,"READY_TO_ADOPT"));
        Assert.Equal("ITERATION_REQUIREMENT_IMPACT_PENDING",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>iterations.CommandAsync(IterationChange(v,"START_TASK") with{TaskId=task.Id}))).Message);
        Assert.Equal(f.Set.Set.Source.Set.Reference,(await iterations.ReadAsync(v.Reference.Id)).Body.Iteration!.Input.Proof.RequirementSet);
        ready=await SetReviews(h).ContextAsync(candidate.Reference.Id);
        Assert.NotNull((await SetReviews(h).SubmitAsync(candidate.Reference.Id,ready.Etag,SetSubmit(ready))).Submission);
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementImpact_late_old_input_adoption_is_atomic_and_withdrawal_retains_original_cases()
    {
        await using var h=await Seed();var f=await IterationSetup(h);await GrantImpact(h);var sets=ImpactSets(h);var iterations=Iterations(h);
        var candidate=await ImpactSuccessor(h,f);await sets.RequestImpactAsync(await ImpactRequest(sets,candidate.Reference));
        var create=new EngineeringIterationCommandV1{OperationId=Guid.NewGuid(),ObjectId=Guid.NewGuid(),Action="CREATE_ITERATION",ProjectId=f.Project.Reference.Id,
            Plan=f.Iteration.Body.Iteration!.Plan with{Name="Late consumer"},Origin=f.Iteration.Body.Iteration.Origin,RequirementSet=f.Set.Set.Source.Set.Reference,Reason="Explicitly adopt still-approved old input"};
        var result=await iterations.CommandAsync(create);Assert.Single(result.ImpactCases!);
        Assert.Equal(result.ImpactCases,(await iterations.CommandAsync(create)).ImpactCases);
        var context=await sets.ImpactAsync(candidate.Reference.Id,candidate.Reference.Version);Assert.Equal(2,context.Consumers.Length);Assert.All(context.Consumers,r=>Assert.Equal("PENDING",r.State));
        var late=Assert.Single((await iterations.ImpactAsync(create.ObjectId)).Cases);Assert.Equal(result.ImpactCases![0],late.Case!.Id);
        Assert.Equal(result.OperationId,await h.Db.Database.SqlQuery<Guid>($"SELECT operation_id AS \"Value\" FROM cp6_requirement_impact_v1.cases WHERE tenant_id={h.Tenant} AND id={late.Case.Id}").SingleAsync());
        var original=await iterations.ReadAsync(create.ObjectId);
        await sets.CommandAsync(SetRevise(candidate,candidate.Body with{Name="Further draft does not erase older impact obligations"}));
        Assert.False((await sets.ImpactAsync(candidate.Reference.Id,candidate.Reference.Version)).IsCurrent);
        var withdrawal=await sets.RequestImpactAsync(await ImpactRequest(sets,candidate.Reference,"WITHDRAW"));
        Assert.Empty(withdrawal.Impact!.CreatedCases);Assert.Equal("WITHDRAWN",Assert.Single((await iterations.ImpactAsync(create.ObjectId)).Cases).State);
        Assert.Equal(original.OriginalJson,(await iterations.ReadAsync(create.ObjectId)).OriginalJson);
        Assert.Equal(2,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_requirement_impact_v1.cases WHERE tenant_id={h.Tenant}").SingleAsync());
        var ready=await SetReviews(h).ContextAsync(candidate.Reference.Id);
        Assert.Equal("REQUIREMENT_SET_IMPACT_REQUEST_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>SetReviews(h).SubmitAsync(candidate.Reference.Id,ready.Etag,SetSubmit(ready)))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementImpact_current_permissions_hidden_consumer_and_coverage_CAS_fail_without_disclosing_partial_zero()
    {
        await using var h=await Seed();var f=await IterationSetup(h);var sets=ImpactSets(h);var candidate=await ImpactSuccessor(h,f);var command=await ImpactRequest(sets,candidate.Reference);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>sets.RequestImpactAsync(command))).Status);await GrantImpact(h);
        await IterationWrite(Iterations(h),IterationChange(f.Iteration,"SAVE_TASK") with{Task=IterationTask(h,f.Reviewer)});
        Assert.Equal("REQUIREMENT_IMPACT_COVERAGE_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>sets.RequestImpactAsync(command))).Message);
        Assert.Equal("UNKNOWN",(await sets.OperationAsync(command.OperationId,false)).State);
        var committed=await sets.RequestImpactAsync(await ImpactRequest(sets,candidate.Reference));
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="plm-engineering-iteration",FieldName="Inputs",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>sets.ImpactAsync(candidate.Reference.Id,candidate.Reference.Version))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>sets.RequestImpactAsync(command))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>sets.OperationAsync(committed.OperationId,false))).Status);
        await using var other=await Seed();await RegisterScope(other);await GrantIteration(other);await GrantImpact(other);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>ImpactSets(other).ImpactAsync(candidate.Reference.Id,candidate.Reference.Version))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementImpact_missing_journal_rolls_back_request_and_cases_then_exact_operation_recovers()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await IterationSetup(h);await GrantImpact(h);
        var sets=ImpactSets(h);var candidate=await ImpactSuccessor(h,f);var command=await ImpactRequest(sets,candidate.Reference);
        hook.Before=cmd=>{if(cmd.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations"))cmd.CommandText="SELECT 1";return Task.CompletedTask;};
        Assert.NotNull(await Record.ExceptionAsync(()=>sets.RequestImpactAsync(command)));hook.Before=null;
        var context=await sets.ImpactAsync(candidate.Reference.Id,candidate.Reference.Version);Assert.Null(context.Request);Assert.Null(Assert.Single(context.Consumers).Case);
        Assert.Equal("UNKNOWN",(await sets.OperationAsync(command.OperationId,false)).State);Assert.Single((await sets.RequestImpactAsync(command)).Impact!.CreatedCases);
    }

    [CurrentPermissionPostgresFact]
    public async Task RequirementImpact_actual_HTTP_context_lists_exact_existing_iteration_adoption_without_replacing_it()
    {
        await using var h=await Seed();var f=await IterationSetup(h);var owner=Sets(h);var prior=f.Set.Set.Source.Set;
        await owner.CommandAsync(SetRevise(prior,prior.Body with{Name="Proposed successor scope review"}));var candidate=await owner.ReadAsync(prior.Reference.Id);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,SetHostSettings());var cookies=await Login(host,h);
        using var response=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"/api/requirement-set/owner-v1/{prior.Reference.Id}/impact?version={candidate.Reference.Version}"));
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        using var body=JsonDocument.Parse(await response.Content.ReadAsStringAsync());var data=body.RootElement.GetProperty("data");
        Assert.Equal("CURRENT_MAIN_ITERATION_ONLY",data.GetProperty("coverage").GetString());var consumer=Assert.Single(data.GetProperty("consumers").EnumerateArray());
        Assert.Equal(f.Iteration.Reference.Id,consumer.GetProperty("iteration").GetProperty("id").GetGuid());
        Assert.Equal(prior.Reference.Version,consumer.GetProperty("adopted").GetProperty("version").GetString());
        Assert.Equal(f.Iteration.OriginalJson,(await Iterations(h).ReadAsync(f.Iteration.Reference.Id)).OriginalJson);
    }
}
