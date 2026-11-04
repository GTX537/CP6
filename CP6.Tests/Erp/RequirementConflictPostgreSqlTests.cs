using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Plm;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string QualifiedSourceJson="""
        {"Kind":"SOURCE","Name":"Customer-specific internal requirement","Source":{"Kind":"INTERNAL_PROPOSAL",
         "OriginDescription":"Explicit internal product proposal","SourceLabel":"SIM-REQ-308","AcquisitionBasis":"Internal source authority",
         "Clauses":[{"Id":"strength","OriginalText":"Retain required strength","Locator":"1","Terms":{
          "Schema":"requirement-clause-terms/1","Customers":{"Mode":"VALUES","Namespace":"sim.customer","Keys":["A"]},
          "Products":{"Mode":"ALL","Namespace":null,"Keys":[]},"Configurations":{"Mode":"ALL","Namespace":null,"Keys":[]},
          "From":null,"Until":null,"FromUnbounded":true,"UntilUnbounded":true,
          "Necessity":"MANDATORY","Waivability":"PROHIBITED","Basis":"Confirmed original internal scope; no waiver"}}]}}
        """;

    [CurrentPermissionPostgresFact]
    public async Task RequirementConflict_source_terms_require_explicit_current_source_authority_and_replay_after_revoke()
    {
        await using var h=await Seed();await GrantRequirement(h);var owner=Requirements(h);
        var body=JsonSerializer.Deserialize<EngineeringRequirementBodyV1>(QualifiedSourceJson)!;
        var command=await NewRequirement(owner,body);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(command))).Status);
        Assert.Equal("UNKNOWN",(await owner.OperationAsync(command.OperationId,false)).State);
        await EngineeringAction(h,"plm-engineering-requirement","source-confirm");
        // The changed authority invalidates the original creation context, so prepare a fresh intent.
        command=await NewRequirement(owner,body);var result=await owner.CommandAsync(command);
        var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="plm-engineering-requirement");
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.MenuId==menu.MenuId&&x.ActionCode=="source-confirm").ExecuteDeleteAsync();
        Assert.Equal(result,await owner.CommandAsync(command));
        var snapshot=await owner.ReadAsync(result.ObjectId);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(ReviseRequirement(snapshot,body with{Name="New title still carries confirmed terms"})))).Status);
        Assert.Equal("1",(await owner.ReadAsync(result.ObjectId)).Reference.Version);
    }

    private sealed record ConflictFixture(EvaluationFixture Left,EngineeringRequirementSnapshotV1 RightSource,RequirementSetSnapshotV1 Set);
    private async Task<ConflictFixture> ConflictSetup(Harness h,string rightCustomer="B",string rightNamespace="sim.customer",bool unknown=false)
    {
        var f=await EvaluationSetup(h);await EngineeringAction(h,"plm-engineering-requirement","source-confirm");
        foreach(var action in new[]{"query","add","edit","review-policy","review-submit","conflict-decide"})await EngineeringAction(h,"plm-requirement-set",action);
        var drafts=EvaluationDrafts(h);var sourceBody=JsonSerializer.Deserialize<EngineeringRequirementBodyV1>(QualifiedSourceJson)!;
        await drafts.CommandAsync(ReviseRequirement(f.Source,sourceBody));var leftSource=await drafts.ReadAsync(f.Source.Reference.Id);
        var definition=f.Definition.Body with{Requirement=f.Definition.Body.Requirement! with{Source=leftSource.Reference,ClauseIds=["strength"]}};
        await drafts.CommandAsync(ReviseRequirement(f.Definition,definition));var left=await drafts.ReadAsync(f.Definition.Reference.Id);
        f=f with{Source=leftSource,Definition=left};var leftApproval=await EvaluationApprove(h,f);
        var clause=sourceBody.Source!.Clauses[0];var terms=clause.Terms!;
        var rightSource=await CreateRequirement(drafts,sourceBody with{Source=sourceBody.Source with{Clauses=[clause with{Terms=unknown?null:terms with{Customers=new("VALUES",rightNamespace,[rightCustomer])}}]}});
        var right=await CreateRequirement(drafts,definition with{Requirement=definition.Requirement! with{Source=rightSource.Reference}});
        var rightApproval=await EvaluationApprove(h,f with{Source=rightSource,Definition=right});
        var owner=Sets(h);var command=new RequirementSetCommandV1{OperationId=Guid.NewGuid(),ObjectId=Guid.NewGuid(),Action="CREATE",ExpectedAuthorityVersion=(await owner.CreationContextAsync()).AuthorityVersion,Reason="Preserve both source requirements",
            Body=new("SET","Same title, different source customers","Qualified source scope","Both accurate originals retained",
                [new(left.Reference,leftApproval.ControlReference,leftApproval.CurrentApproval!.Reference,"INCLUDED","Mandatory"),new(right.Reference,rightApproval.ControlReference,rightApproval.CurrentApproval!.Reference,"INCLUDED","Mandatory")],
                [new("C1",left.Reference,right.Reference,"Human text is not authoritative intersection","SCOPE_SEPARATION","Proposed split")])};
        await owner.CommandAsync(command);return new(f,rightSource,await owner.ReadAsync(command.ObjectId));
    }
    private static RequirementSetBodyV1 ConflictDecision(RequirementSetSnapshotV1 set,string kind="SCOPE_SEPARATION")
        =>set.Body with{Conflicts=[set.Body.Conflicts[0] with{Proposal=kind,Decision=new("requirement-conflict-decision/1",kind,"Both source authorities confirm unchanged requirements and exact scope",true)}]};

    [CurrentPermissionPostgresFact]
    public async Task RequirementConflict_disjoint_customers_need_real_source_decision_then_professional_submission_and_keep_originals()
    {
        await using var h=await Seed();var f=await ConflictSetup(h);var owner=Sets(h);
        Assert.Equal("DISJOINT",Assert.Single(f.Set.ConflictStatus!).Intersection);Assert.Contains("CONFLICT_SOURCE_DECISION_REQUIRED",f.Set.Missing);
        var ready=await SetReady(h,f.Set.Reference.Id);Assert.Equal(422,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>SetReviews(h).SubmitAsync(f.Set.Reference.Id,ready.Etag,SetSubmit(ready)))).Status);
        var command=SetRevise(f.Set,ConflictDecision(f.Set));var result=await owner.CommandAsync(command);
        var resolved=await owner.ReadAsync(f.Set.Reference.Id);Assert.Equal("RESOLVED",Assert.Single(resolved.ConflictStatus!).State);
        Assert.DoesNotContain("CONFLICT_SOURCE_DECISION_REQUIRED",resolved.Missing);Assert.Contains("REQUIREMENT_SET_REVIEW_REQUIRED",resolved.Missing);
        Assert.Equal(f.Set.OriginalJson,(await owner.ReadAsync(f.Set.Reference.Id,"1")).OriginalJson);
        Assert.Equal(f.Left.Source.OriginalJson,resolved.MemberStatus![0].Source.OriginalJson);Assert.Equal(2,resolved.Body.Members.Length);
        ready=await SetReady(h,f.Set.Reference.Id);Assert.NotNull((await SetReviews(h).SubmitAsync(f.Set.Reference.Id,ready.Etag,SetSubmit(ready))).Submission);
        Assert.Equal(result,await owner.CommandAsync(command));
    }

    [CurrentPermissionPostgresFact]
    public async Task RequirementConflict_overlap_rejects_fake_separation_and_forbidden_deviation_but_retains_ecm_pending()
    {
        await using var h=await Seed();var f=await ConflictSetup(h,"A");var owner=Sets(h);
        Assert.Equal("OVERLAP",Assert.Single(f.Set.ConflictStatus!).Intersection);
        foreach(var kind in new[]{"SCOPE_SEPARATION","DEV"})
        {
            var c=SetRevise(f.Set,ConflictDecision(f.Set,kind));Assert.Equal(422,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(c))).Status);
            Assert.Equal("UNKNOWN",(await owner.OperationAsync(c.OperationId,false)).State);Assert.Equal("1",(await owner.ReadAsync(f.Set.Reference.Id)).Reference.Version);
        }
        await owner.CommandAsync(SetRevise(f.Set,ConflictDecision(f.Set,"ECM")));var pending=await owner.ReadAsync(f.Set.Reference.Id);
        Assert.Equal("ECM_PENDING",Assert.Single(pending.ConflictStatus!).State);Assert.Contains("CONFLICT_SOURCE_DECISION_REQUIRED",pending.Missing);
        await owner.CommandAsync(SetRevise(pending,ConflictDecision(pending,"CLARIFY")));var clarified=await owner.ReadAsync(f.Set.Reference.Id);
        Assert.Equal("RESOLVED",Assert.Single(clarified.ConflictStatus!).State);Assert.Equal(f.Left.Definition.OriginalJson,clarified.MemberStatus![0].Requirement.OriginalJson);
        Assert.All(clarified.MemberStatus!,m=>Assert.Equal("PROHIBITED",m.Source.Body.Source!.Clauses[0].Terms!.Waivability));
    }

    [CurrentPermissionPostgresFact]
    public async Task RequirementConflict_unknown_scope_or_distinct_namespaces_never_prove_separation_or_allow_deviation()
    {
        await using var h=await Seed();var f=await ConflictSetup(h,"B","different.customer.namespace");var owner=Sets(h);
        Assert.Equal("UNKNOWN",Assert.Single(f.Set.ConflictStatus!).Intersection);
        foreach(var kind in new[]{"SCOPE_SEPARATION","CLARIFY","DEV"})
            Assert.Equal(422,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(SetRevise(f.Set,ConflictDecision(f.Set,kind))))).Status);
        var drafts=EvaluationDrafts(h);var old=f.RightSource;
        await drafts.CommandAsync(ReviseRequirement(old,old.Body with{Source=old.Body.Source! with{Clauses=old.Body.Source.Clauses.Select(c=>c with{Terms=null}).ToArray()}}));
        Assert.Contains("MEMBER_UNAVAILABLE:"+f.Set.Body.Members[1].Requirement.Id,(await owner.ReadAsync(f.Set.Reference.Id)).Missing);
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(SetRevise(f.Set,ConflictDecision(f.Set,"ECM"))))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task RequirementConflict_current_object_authority_and_atomic_receipt_guard_decisions()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await ConflictSetup(h);var owner=Sets(h);var command=SetRevise(f.Set,ConflictDecision(f.Set));
        var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="plm-engineering-requirement");
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.MenuId==menu.MenuId&&x.ActionCode=="source-confirm").ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(command))).Status);
        await EngineeringAction(h,"plm-engineering-requirement","source-confirm");
        hook.Before=c=>{if(c.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations"))throw new InvalidOperationException("W308 receipt storage fault");return Task.CompletedTask;};
        var failure=await Record.ExceptionAsync(()=>owner.CommandAsync(command));hook.Before=null;Assert.Contains("W308 receipt storage fault",failure!.ToString());
        Assert.Equal("UNKNOWN",(await owner.OperationAsync(command.OperationId,false)).State);Assert.Equal("1",(await owner.ReadAsync(f.Set.Reference.Id)).Reference.Version);
        var result=await owner.CommandAsync(command);Assert.Equal(result,(await owner.OperationAsync(command.OperationId,false)).Result);
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.MenuId==menu.MenuId&&x.ActionCode=="source-confirm").ExecuteDeleteAsync();
        Assert.Equal(result,await owner.CommandAsync(command));
        // A normal successor may retain the exact prior decision; it is not a second source attestation.
        var current=await owner.ReadAsync(f.Set.Reference.Id);await Sets(DecisionActor(h,f.Left.First)).CommandAsync(SetRevise(current,current.Body with{Name="Retained exact decision"}));
        var inherited=await owner.ReadAsync(f.Set.Reference.Id);var decision=Assert.Single(inherited.ConflictStatus!);
        Assert.Equal("RESOLVED",decision.State);Assert.Equal(current.Reference,decision.DecisionVersion);Assert.Equal(h.User,decision.DecisionActor);Assert.Equal(f.Left.First,inherited.ActorId);
    }

    [CurrentPermissionPostgresFact]
    public async Task RequirementConflict_final_authority_failure_rolls_back_and_concurrent_decisions_have_one_frontier()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var f=await ConflictSetup(h);var owner=Sets(h);var command=SetRevise(f.Set,ConflictDecision(f.Set));
        var reached=false;hook.Before=async c=>{
            if(!c.CommandText.Contains("WITH field_subject"))return;
            await using var probe=c.Connection!.CreateCommand();probe.Transaction=c.Transaction;
            probe.CommandText=$"SELECT EXISTS(SELECT FROM cp6_requirement_set_v1.versions WHERE tenant_id='{h.Tenant}' AND object_id='{f.Set.Reference.Id}' AND revision=2)";
            if((bool)(await probe.ExecuteScalarAsync())!){reached=true;throw new IOException("W308 final authority unavailable");}
        };
        try{Assert.Contains("W308 final authority unavailable",(await Record.ExceptionAsync(()=>owner.CommandAsync(command)))!.ToString());}finally{hook.Before=null;}
        Assert.True(reached);Assert.Equal("1",(await owner.ReadAsync(f.Set.Reference.Id)).Reference.Version);Assert.Equal("UNKNOWN",(await owner.OperationAsync(command.OperationId,false)).State);
        await using var db=server.Context(new CP6.Core.Services.Common.TenantContext{CurrentTenantId=h.Tenant});var peer=Sets(h with{Db=db});
        async Task<string> Write(RequirementSetService target,RequirementSetCommandV1 c){try{return(await target.CommandAsync(c)).Version;}catch(OrderPriceCorrectionException e){return e.Message;}}
        var outcomes=await Task.WhenAll(Write(owner,command),Write(peer,command with{OperationId=Guid.NewGuid()}));
        Assert.Single(outcomes,x=>x=="2");Assert.Single(outcomes,x=>x=="REQUIREMENT_SET_VERSION_CHANGED");
    }

    [Fact]
    public void RequirementConflict_scope_time_is_half_open_and_clause_union_preserves_an_overlapping_obligation()
    {
        var body=JsonSerializer.Deserialize<EngineeringRequirementBodyV1>(QualifiedSourceJson)!;var terms=body.Source!.Clauses[0].Terms!;
        var start=DateTimeOffset.Parse("2026-11-04T00:00:00Z");
        RequirementSetMemberObservationV1 Member(params RequirementClauseTermsV1[] ranges)
        {
            var source=new EngineeringSourceRefV1("Main.Plm.EngineeringRequirement.v1",Guid.NewGuid(),"1",new string('a',64));
            var clauses=ranges.Select((t,i)=>new RequirementClauseV1(i.ToString(),"Required",i.ToString(),t)).ToArray();
            EngineeringRequirementSnapshotV1 Snapshot(EngineeringRequirementBodyV1 b)=>new(Guid.NewGuid(),source,Guid.NewGuid(),true,b,Guid.NewGuid(),Guid.NewGuid(),null,"fixture",start,"{}",[]);
            var definition=DefinitionBody(source);definition=definition with{Requirement=definition.Requirement! with{ClauseIds=clauses.Select(c=>c.Id).ToArray()}};
            return new(Snapshot(definition),null!,Snapshot(body with{Source=body.Source with{Clauses=clauses}}));
        }
        var a=terms with{From=start,Until=start.AddHours(1),FromUnbounded=false,UntilUnbounded=false};
        var b=a with{From=start.AddHours(1),Until=start.AddHours(2)};
        Assert.Equal("DISJOINT",RequirementApplicabilityV1.Intersections(Member(a),Member(b)).State);
        Assert.Equal("OVERLAP",RequirementApplicabilityV1.Intersections(Member(a),Member(b with{From=start.AddHours(1).AddTicks(-1)})).State);
        Assert.Equal("UNKNOWN",RequirementApplicabilityV1.Intersections(Member(a),Member(b with{From=null})).State);
        Assert.Equal("DISJOINT",RequirementApplicabilityV1.Intersections(Member(a),Member(b with{From=null,Customers=new("VALUES","sim.customer",["B"])})).State);
        Assert.Equal("OVERLAP",RequirementApplicabilityV1.Intersections(Member(a,b),Member(b)).State);
        Assert.Equal(2,RequirementApplicabilityV1.Intersections(Member(a,b),Member(b)).PairCount);
    }

    [CurrentPermissionPostgresFact]
    public async Task RequirementConflict_omission_cannot_launder_unresolved_obligation_through_successive_drafts()
    {
        await using var h=await Seed();var f=await ConflictSetup(h);var owner=Sets(h);
        await owner.CommandAsync(SetRevise(f.Set,f.Set.Body with{Conflicts=[]}));var second=await owner.ReadAsync(f.Set.Reference.Id);
        Assert.Contains("PREVIOUS_CONFLICT_OMITTED:C1",second.Missing);
        await owner.CommandAsync(SetRevise(second,second.Body with{Name="No visible conflict"}));var third=await owner.ReadAsync(f.Set.Reference.Id);
        Assert.Contains("PREVIOUS_CONFLICT_OMITTED:C1",third.Missing);var ready=await SetReady(h,f.Set.Reference.Id);
        Assert.Equal(422,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>SetReviews(h).SubmitAsync(f.Set.Reference.Id,ready.Etag,SetSubmit(ready)))).Status);
        await owner.CommandAsync(SetRevise(third,ConflictDecision(f.Set)));var restored=await owner.ReadAsync(f.Set.Reference.Id);
        Assert.DoesNotContain("PREVIOUS_CONFLICT_OMITTED:C1",restored.Missing);Assert.Equal("RESOLVED",Assert.Single(restored.ConflictStatus!).State);
    }
}
