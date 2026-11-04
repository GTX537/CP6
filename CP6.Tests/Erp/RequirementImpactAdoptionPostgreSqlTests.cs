using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Plm;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task RequirementImpact_zero_current_consumers_still_requires_successor_request_to_cover_future_adoptions()
    {
        await using var h=await Seed();var f=await SetApplicationSetup(h);FactAck(await f.Receiver.ReceiveAsync(f.Payload));var reviews=SetReviews(h);
        var detail=await reviews.SubmissionAsync(f.Set.Source.Set.Reference.Id,f.Set.Submission);
        await reviews.ApplyAsync(f.Set.Source.Set.Reference.Id,detail.Application.Etag,new(Guid.NewGuid(),f.Set.Submission,null,"Approve original before any consumer exists"));
        var sets=ImpactSets(h);await sets.CommandAsync(SetRevise(f.Set.Source.Set,f.Set.Source.Set.Body with{Scope="Changed scope before first adoption"}));
        var candidate=await sets.ReadAsync(f.Set.Source.Set.Reference.Id);var context=await sets.ImpactAsync(candidate.Reference.Id,candidate.Reference.Version);Assert.Empty(context.Consumers);
        var ready=await reviews.ContextAsync(candidate.Reference.Id);
        Assert.Equal("REQUIREMENT_SET_IMPACT_REQUEST_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>reviews.SubmitAsync(candidate.Reference.Id,ready.Etag,SetSubmit(ready)))).Message);
        await EngineeringAction(h,"plm-requirement-set","impact-request");var requested=await sets.RequestImpactAsync(await ImpactRequest(sets,candidate.Reference));Assert.Empty(requested.Impact!.CreatedCases);
        ready=await reviews.ContextAsync(candidate.Reference.Id);Assert.NotNull((await reviews.SubmitAsync(candidate.Reference.Id,ready.Etag,SetSubmit(ready))).Submission);
    }
    private async Task ApproveImpactSuccessor(Harness h,IterationFixture f,RequirementSetSnapshotV1 candidate)
    {
        var reviews=SetReviews(h);var ready=await reviews.ContextAsync(candidate.Reference.Id);
        var submission=(await reviews.SubmitAsync(candidate.Reference.Id,ready.Etag,SetSubmit(ready))).Submission!.SubmissionId;
        var principal=await h.Db.Database.SqlQuery<Guid>($"SELECT service_principal AS \"Value\" FROM cp6_approval_binding_v2.registrations WHERE tenant_id={h.Tenant} AND owner='Main.Plm.RequirementSet.v1'").SingleAsync();
        var service=new IntakeAuthority(new(ScopeEnvironment,h.Tenant,principal,"fixture-service","CP6.Services","test-registration",true,"DEMO"));
        var intake=new ApprovalIntakeOwnerV2(h.Db,ScopeFence(h),service,ToolApproval(h),Authoring(h),new(ToolApproval(h),null,reviews)){ReceiveClock=()=>BindingTime().AddSeconds(1)};
        var bytes=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_requirement_set_review_v1.outbox WHERE tenant_id={h.Tenant} AND submission_id={submission}").SingleAsync();
        await intake.SubmitAsync(bytes);Assert.Equal("RUNNING",(await Starter(h).RunAsync(h.Tenant)).State);
        var iterations=Iterations(h);var work=await iterations.ReadAsync(f.Iteration.Reference.Id);
        work=await IterationWrite(iterations,IterationChange(work,"EDIT_PLAN") with{Plan=work.Body.Iteration!.Plan with{Goal="Plan changed after source submission"}});
        Assert.Equal("REQUIREMENT_SET_IMPACT_ASSESSMENT_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>SetVote(DecisionActor(h,f.Set.Set.Source.Member.First)))).Message);
        await iterations.AssessImpactAsync(ImpactAssessment(Assert.Single((await iterations.ImpactAsync(work.Reference.Id)).Cases),"READY_TO_ADOPT"));
        await SetVote(DecisionActor(h,f.Set.Set.Source.Member.First));await SetVote(DecisionActor(h,f.Set.Set.Source.Member.Second));
        var decision=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_approval_decision_v2.outbox WHERE tenant_id={h.Tenant} AND submission_id={submission}").SingleAsync();
        var producer=f.Set.Authority.Identity.Principal;
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_requirement_set_review_fact_v1.subject_grants(environment_id,tenant_id,principal_id,submission_id,revision,enabled,can_receive,can_query)
            VALUES({ScopeEnvironment},{h.Tenant},{producer},{submission},{Guid.NewGuid()},true,true,true)
            """);
        FactAck(await f.Set.Receiver.ReceiveAsync(decision));var detail=await reviews.SubmissionAsync(candidate.Reference.Id,submission);
        work=await IterationWrite(iterations,IterationChange(work,"EDIT_PLAN") with{Plan=work.Body.Iteration!.Plan with{Goal="Plan changed after positive votes"}});
        var apply=new RequirementSetApplicationCommandV1(Guid.NewGuid(),submission,null,"Apply actual native FLOW successor approval");
        Assert.Equal("REQUIREMENT_SET_IMPACT_ASSESSMENT_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>reviews.ApplyAsync(candidate.Reference.Id,detail.Application.Etag,apply))).Message);
        Assert.Equal("UNKNOWN",(await reviews.ApplicationOperationAsync(apply.OperationId,false)).State);
        await iterations.AssessImpactAsync(ImpactAssessment(Assert.Single((await iterations.ImpactAsync(work.Reference.Id)).Cases),"READY_TO_ADOPT"));
        Assert.Equal("APPLIED",(await reviews.ApplyAsync(candidate.Reference.Id,detail.Application.Etag,apply)).Receipt.Application);
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementImpact_actual_FLOW_successor_approval_and_explicit_input_replacement_preserve_old_adoption()
    {
        await using var h=await Seed();var f=await IterationSetup(h);await GrantImpact(h);var sets=ImpactSets(h);var iterations=Iterations(h);
        var candidate=await ImpactSuccessor(h,f);var request=await sets.RequestImpactAsync(await ImpactRequest(sets,candidate.Reference));
        var row=Assert.Single((await iterations.ImpactAsync(f.Iteration.Reference.Id)).Cases);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>Iterations(DecisionActor(h,f.Reviewer)).AssessImpactAsync(ImpactAssessment(row,"KEEP_CURRENT")))).Status);
        var assessed=await iterations.AssessImpactAsync(ImpactAssessment(row,"READY_TO_ADOPT"));
        Assert.Equal("REQUIREMENT_SET_APPROVAL_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>iterations.CommandAsync(IterationChange(f.Iteration,"REPLACE_INPUTS") with{RequirementSet=candidate.Reference}))).Message);
        await ApproveImpactSuccessor(h,f,candidate);Assert.Equal("APPROVED",(await sets.ReadAsync(candidate.Reference.Id)).State);
        Assert.Equal("READY_TO_ADOPT",Assert.Single((await iterations.ImpactAsync(f.Iteration.Reference.Id)).Cases).State);
        var current=await iterations.ReadAsync(f.Iteration.Reference.Id);
        var adopted=await IterationWrite(iterations,IterationChange(current,"REPLACE_INPUTS") with{RequirementSet=candidate.Reference});
        Assert.Equal(candidate.Reference,adopted.Body.Iteration!.Input.Proof.RequirementSet);Assert.Equal("2",adopted.Body.Iteration.InputVersion);
        Assert.Equal("ADOPTED",Assert.Single((await iterations.ImpactAsync(adopted.Reference.Id)).Cases).State);
        Assert.Equal(3,Assert.Single((await iterations.ImpactAsync(adopted.Reference.Id)).Cases).Case!.AssessmentHistory!.Length);
        Assert.Equal(f.Iteration.OriginalJson,(await iterations.ReadAsync(adopted.Reference.Id,f.Iteration.Reference.Version)).OriginalJson);
        Assert.Equal(assessed,(await iterations.OperationAsync(assessed.OperationId,false)).Result);
        Assert.Equal("REQUIREMENT_IMPACT_WITHDRAWAL_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>Withdraw())).Message);
        async Task Withdraw()=>await sets.RequestImpactAsync(await ImpactRequest(sets,candidate.Reference,"WITHDRAW"));
        // A later deliberate adoption of the still-approved predecessor creates a new unresolved case, not an implicit latest-version replacement.
        var late=await iterations.CommandAsync(new(){OperationId=Guid.NewGuid(),ObjectId=Guid.NewGuid(),Action="CREATE_ITERATION",ProjectId=f.Project.Reference.Id,
            Plan=f.Iteration.Body.Iteration!.Plan with{Name="Post-approval old-input consumer"},Origin=f.Iteration.Body.Iteration.Origin,
            RequirementSet=f.Set.Set.Source.Set.Reference,Reason="Explicit old-input adoption after successor approval"});
        Assert.Single(late.ImpactCases!);Assert.Equal("PENDING",Assert.Single((await iterations.ImpactAsync(late.ObjectId)).Cases).State);
        Assert.Equal("APPROVED",(await sets.ReadAsync(candidate.Reference.Id)).State);
        Assert.Equal(request.Impact!.RequestId,(await sets.OperationAsync(request.OperationId,false)).Result!.Impact!.RequestId);
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementImpact_two_connections_racing_request_and_adoption_never_omit_a_committed_consumer()
    {
        await using var h=await Seed();var f=await IterationSetup(h);await GrantImpact(h);var sets=ImpactSets(h);var candidate=await ImpactSuccessor(h,f);
        var request=await ImpactRequest(sets,candidate.Reference);
        await using var other=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var peer=Iterations(h with{Db=other});
        var command=new EngineeringIterationCommandV1{OperationId=Guid.NewGuid(),ObjectId=Guid.NewGuid(),Action="CREATE_ITERATION",ProjectId=f.Project.Reference.Id,
            Plan=f.Iteration.Body.Iteration!.Plan with{Name="Concurrent adoption"},Origin=f.Iteration.Body.Iteration.Origin,RequirementSet=f.Set.Set.Source.Set.Reference,Reason="Race actual adoption with actual source request"};
        var results=await Task.WhenAll(Record.ExceptionAsync(async()=>{await sets.RequestImpactAsync(request);}),Record.ExceptionAsync(async()=>{await peer.CommandAsync(command);}));
        Assert.Null(results[1]);var context=await sets.ImpactAsync(candidate.Reference.Id,candidate.Reference.Version);Assert.Equal(2,context.Consumers.Length);
        if(results[0]==null)Assert.All(context.Consumers,c=>Assert.NotNull(c.Case));
        else
        {
            Assert.Equal("REQUIREMENT_IMPACT_COVERAGE_CHANGED",Assert.IsType<OrderPriceCorrectionException>(results[0]).Message);Assert.Null(context.Request);
            await sets.RequestImpactAsync(await ImpactRequest(sets,candidate.Reference));
            Assert.All((await sets.ImpactAsync(candidate.Reference.Id,candidate.Reference.Version)).Consumers,c=>Assert.NotNull(c.Case));
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementImpact_exact_successor_diff_retains_clause_necessity_scope_and_criterion_values()
    {
        await using var h=await Seed();var f=await SetSetup(h);var drafts=EvaluationDrafts(h);var original=f.Member.Definition;
        await drafts.CommandAsync(ReviseRequirement(f.Member.Source,f.Member.Source.Body with{Source=f.Member.Source.Body.Source! with{Clauses=[new("strength","Strength at least 10.0000000000000000000001","Clause 1"),new("water","Resist moisture","Clause 2")]}}));
        var source=await drafts.ReadAsync(f.Member.Source.Reference.Id);
        var changed=original.Body with{Requirement=original.Body.Requirement! with{Source=source.Reference,Necessity="Mandatory exact source",ApplicableObject="Product label variant B",
            Criteria=[new("strength","QUANTITATIVE","Label","Daylight","Measured comparison",Lower:"10.0000000000000000000001",LowerInclusive:true,Upper:"100000000000000000000000000.0000000000000000000002",UpperInclusive:false,ProposedUnit:"N",UnitBasis:"Proposed force unit",Precision:"0.0000000000000000000001")]}};
        await drafts.CommandAsync(ReviseRequirement(original,changed));var definition=await drafts.ReadAsync(original.Reference.Id);var sets=ImpactSets(h);
        await sets.CommandAsync(SetRevise(f.Set,f.Set.Body with{Members=[f.Set.Body.Members[0] with{Requirement=definition.Reference}]}));var candidate=await sets.ReadAsync(f.Set.Reference.Id);
        var difference=await sets.SuccessorDifferenceAsync(f.Set.Reference.Id,f.Set.Reference.Version,candidate.Reference.Version);
        Assert.Contains(difference.Changes,c=>c.Area=="SOURCE_CLAUSE" && c.Item=="strength" && c.After!.Contains("10.0000000000000000000001"));
        Assert.Contains(difference.Changes,c=>c.Area=="CRITERION" && c.Item=="strength" && c.After!.Contains("100000000000000000000000000.0000000000000000000002"));
        Assert.Contains(difference.Changes,c=>c.Item=="necessity" && c.After!.Contains("Mandatory exact source"));
        Assert.Contains(difference.Changes,c=>c.Item=="applicableObject" && c.After!.Contains("variant B"));
        Assert.Equal(f.Set.OriginalJson,(await sets.ReadAsync(f.Set.Reference.Id,f.Set.Reference.Version)).OriginalJson);
    }
}
