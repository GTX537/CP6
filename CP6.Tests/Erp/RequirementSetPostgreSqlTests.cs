using System.Net;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static OperationsOwnerV1 SetAuthority(Harness h)
    {
        var catalog=new AuthorizationCatalogStoreV1();
        foreach(var key in new[]{"plm-requirement-set","plm-engineering-requirement","uom-profile"})
        {catalog.RegisterScope(key,key,[1,2,3,4,5],5);catalog.RegisterFields(key,key=="plm-requirement-set"?[new("Scope","Scope"),new("Members","Members")]:key=="plm-engineering-requirement"?[new("Definition","Definition"),new("Source","Source")]:[]);}
        catalog.Seal();return new(h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),catalog.Capture);
    }
    private static RequirementSetService Sets(Harness h)=>new(h.Db,SetAuthority(h),Evaluations(h),ScopeFence(h));
    private static RequirementSetReviewOwnerV1 SetReviews(Harness h)=>new(h.Db,SetAuthority(h),ScopeFence(h),Sets(h));
    private sealed record SetFixture(EvaluationFixture Member,RequirementSetSnapshotV1 Set);
    private async Task<SetFixture> SetSetup(Harness h,bool registerScope=true)
    {
        var f=await EvaluationSetup(h,registerScope);var approved=await EvaluationApprove(h,f);
        foreach(var action in new[]{"query","add","edit","review-policy","review-submit","review-decide","review-apply"})await EngineeringAction(h,"plm-requirement-set",action);
        var owner=Sets(h);var context=await owner.CreationContextAsync();var command=new RequirementSetCommandV1{OperationId=Guid.NewGuid(),ObjectId=Guid.NewGuid(),Action="CREATE",ExpectedAuthorityVersion=context.AuthorityVersion,
            Reason="Assemble exact approved requirement",Body=new("SET","Label requirement set","Internal product label proposal","All clauses of the exact original label proposal are included",
                [new(f.Definition.Reference,approved.ControlReference,approved.CurrentApproval!.Reference,"INCLUDED","Required original clause")],[])};
        await owner.CommandAsync(command);return new(f,await owner.ReadAsync(command.ObjectId));
    }
    private static RequirementSetCommandV1 SetRevise(RequirementSetSnapshotV1 v,RequirementSetBodyV1 body)=>new(){OperationId=Guid.NewGuid(),ObjectId=v.Reference.Id,Action="REVISE",ExpectedVersion=v.Reference.Version,ExpectedEtag=v.Etag,Body=body,Reason="Explicit successor change"};
    private static RequirementSetReviewPolicyCommandV1 SetPolicy()=>new(Guid.NewGuid(),"ENABLED",[new("COMPLETE_SET","Complete scope, necessity, exact approved sources and conflict review",74,2)],[],true,DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds()),DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeMilliseconds()),"Independent complete-set review");
    private static RequirementSetReviewSubmitV1 SetSubmit(RequirementSetReviewContextV1 c)=>new(Guid.NewGuid(),c.RequirementSet.Reference,c.Policies.Last().Reference,"Review the full fixed set and original member necessity",[],Guid.NewGuid(),null,"Freeze set for professional review");
    private static async Task<RequirementSetReviewContextV1> SetReady(Harness h,Guid id)
    {var owner=SetReviews(h);var c=await owner.ContextAsync(id);await owner.PolicyAsync(id,c.Etag,SetPolicy());return await owner.ContextAsync(id);}
    [CurrentPermissionPostgresFact]
    public async Task RequirementSet_exact_members_submit_history_and_original_recovery_do_not_claim_approval()
    {
        await using var h=await Seed();var f=await SetSetup(h);var owner=Sets(h);var reviews=SetReviews(h);var ready=await SetReady(h,f.Set.Reference.Id);
        var command=SetSubmit(ready);var result=await reviews.SubmitAsync(f.Set.Reference.Id,ready.Etag,command);
        Assert.Equal(result,await reviews.SubmitAsync(f.Set.Reference.Id,ready.Etag,command));
        Assert.Equal(result,(await reviews.OperationAsync(command.OperationId,false)).Result);
        var detail=await reviews.SubmissionAsync(f.Set.Reference.Id,result.Submission!.SubmissionId);
        Assert.Equal(f.Set.Reference,detail.Package.RequirementSet);Assert.False(detail.Submission.IsApproved);Assert.Equal("NOT_RECEIVED",detail.Application.State);
        Assert.Equal(f.Member.Definition.Reference,detail.Package.Body.Members.Single().Requirement);
        var revised=SetRevise(f.Set,f.Set.Body with{Name="Later draft"});await owner.CommandAsync(revised);
        Assert.Equal(f.Set.OriginalJson,(await owner.ReadAsync(f.Set.Reference.Id,"1")).OriginalJson);
        Assert.Equal(f.Set.Reference,(await reviews.SubmissionAsync(f.Set.Reference.Id,result.Submission.SubmissionId)).Package.RequirementSet);
        Assert.Equal("CLOSED",(await owner.OperationAsync(Guid.NewGuid(),true)).State);
        foreach(var sql in new[]{"DELETE FROM cp6_requirement_set_v1.versions","UPDATE cp6_requirement_set_review_v1.submissions SET payload=payload","TRUNCATE cp6_requirement_set_v1.heads CASCADE"})await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync(sql));
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementSet_missing_approval_exclusion_omission_and_conflict_never_pass_submission()
    {
        await using var h=await Seed();var f=await SetSetup(h);var owner=Sets(h);var reviews=SetReviews(h);
        var member=f.Set.Body.Members[0];var value=f.Set;
        foreach(var body in new[]{f.Set.Body with{Members=[member with{Disposition="EXCLUDED",Basis="Higher commercial priority"}]},f.Set.Body with{Members=[]},f.Set.Body with{Members=[member with{EvaluationControl=null,Approval=null}]}})
        {
            await owner.CommandAsync(SetRevise(value,body));value=await owner.ReadAsync(value.Reference.Id);var ready=await SetReady(h,value.Reference.Id);
            Assert.Equal("REQUIREMENT_SET_SOURCE_INCOMPLETE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>reviews.SubmitAsync(value.Reference.Id,ready.Etag,SetSubmit(ready)))).Message);
        }
        var second=await CreateRequirement(EvaluationDrafts(h),f.Member.Definition.Body with{Name="Additional approved clause"});
        var approved=await EvaluationApprove(h,f.Member with{Definition=second});
        var other=new RequirementSetMemberV1(second.Reference,approved.ControlReference,approved.CurrentApproval!.Reference,"INCLUDED","Second mandatory source clause");
        var conflict=new RequirementSetConflictV1("unresolved",member.Requirement,other.Requirement,"Same product scope","SCOPE_SEPARATION","Proposed separation is not authority");
        var bodyWithConflict=f.Set.Body with{Members=[member,other],Conflicts=[conflict]};
        foreach(var malformed in new[]{conflict with{Left=null!},conflict with{Right=null!}})
            Assert.Equal(400,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(SetRevise(value,bodyWithConflict with{Conflicts=[malformed]})))).Status);
        await owner.CommandAsync(SetRevise(value,bodyWithConflict));value=await owner.ReadAsync(value.Reference.Id);
        Assert.Contains("CONFLICT_SOURCE_DECISION_REQUIRED",value.Missing);
        var conflictReady=await SetReady(h,value.Reference.Id);
        Assert.Equal("REQUIREMENT_SET_SOURCE_INCOMPLETE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>reviews.SubmitAsync(value.Reference.Id,conflictReady.Etag,SetSubmit(conflictReady)))).Message);
        Assert.Empty((await reviews.ContextAsync(value.Reference.Id)).Submissions);
    }
    private sealed record SetDecisionFixture(SetFixture Source,Guid Submission);
    private async Task<SetDecisionFixture> SetDecisionSetup(Harness h,bool receive=true)
    {
        var(binding,flows,initial)=await PrepareBinding(h);var f=await SetSetup(h,false);
        h.Db.Sys_Roles.Add(new(){RoleId=73,RoleName="Set FLOW assignment"});await h.Db.SaveChangesAsync();
        foreach(var id in new[]{f.Member.First,f.Member.Second})
        {await h.Db.Sys_Users.Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RoleId,73));await TaskPermission(h,id);}
        await EngineeringAction(h,"oa-inbox","query");h.Db.ChangeTracker.Clear();
        var head=await flows.ReadHeadAsync("binding-flow");var draft=await flows.CreateNextDraftAsync("binding-flow",NextDraftCommand(head.Version,initial));
        var edit=await flows.SaveAsync("binding-flow",AuthoringCommand("set-flow",draft.ResourceVersion,"requirement set",StartSchema));
        var publication=await flows.PublishAsync("binding-flow",PublicationCommand(await Preview(flows,"binding-flow",edit),"set-publish"));var version=publication.Result!.Publication!.VersionId;var principal=Guid.NewGuid();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_approval_binding_v2.registrations(tenant_id,environment_id,biz_type,subject_kind,owner,revision,snapshot_schema,snapshot_fields,enabled,service_principal,audience,fixed_route,evidence_mode)
            VALUES({h.Tenant},{ScopeEnvironment},'PLM_REQUIREMENT_SET_REVIEW','REQUIREMENT_SET_REVISION','Main.Plm.RequirementSet.v1',{Guid.NewGuid()},
              'Main.Plm.RequirementSetReview.snapshot/1','[]',true,{principal},'CP6.Services','/internal/plm/requirement-set-review-results/v1','DEMO')
            """);
        var at=BindingTime();await binding.SaveAsync(null,null,BindingBytes(BindingInput(version,at) with{BizType="PLM_REQUIREMENT_SET_REVIEW",SubjectKind="REQUIREMENT_SET_REVISION",DetailRoute="/plm/requirement-set/{bizId}"}));
        var ready=await SetReady(h,f.Set.Reference.Id);var submitted=await SetReviews(h).SubmitAsync(f.Set.Reference.Id,ready.Etag,SetSubmit(ready));
        var bytes=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_requirement_set_review_v1.outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        var service=new IntakeAuthority(new(ScopeEnvironment,h.Tenant,principal,"fixture-service","CP6.Services","test-registration",true,"DEMO"));
        var intake=new ApprovalIntakeOwnerV2(h.Db,ScopeFence(h),service,ToolApproval(h),flows,new(ToolApproval(h),null,SetReviews(h))){ReceiveClock=()=>at.AddSeconds(1)};
        if(receive){await intake.SubmitAsync(bytes);Assert.Equal("RUNNING",(await Starter(h).RunAsync(h.Tenant)).State);}h.Db.ChangeTracker.Clear();return new(f,submitted.Submission!.SubmissionId);
    }
    private static ApprovalTaskDecisionOwnerV2 SetDecisions(Harness h)=>new(h.Db,ScopeFence(h),TaskAuthority(h),ToolApproval(h),new(ToolApproval(h),null,SetReviews(h)));
    private static async Task<ApprovalTaskDecisionResultV2> SetVote(Harness h,string decision="approve")
    {
        var task=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(x=>x.AssigneeId==h.User&&x.Status==0);var owner=SetDecisions(h);var v=await owner.ContextAsync(task.Id);
        return await owner.DecideAsync(task.Id,new(Guid.NewGuid(),decision,"Reviewed entire fixed set, exact requirement approvals, necessity and scope",v.TaskRowVersion,v.InstanceRowVersion),null,"\""+v.TaskRowVersion+"\"");
    }
    private async Task<(RequirementSetReviewFactReceiverV1 Receiver,byte[] Payload,IntakeAuthority Authority)> SetReceiver(Harness h,SetDecisionFixture f)
    {
        var bytes=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_approval_decision_v2.outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        var service=new IntakeAuthority(new(ScopeEnvironment,h.Tenant,Guid.NewGuid(),"set-workflow-producer","CP6.Services","test-registration",true,"DEMO"));
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_requirement_set_review_fact_v1.registrations(environment_id,tenant_id,principal_id,revision,enabled,can_receive,can_query,evidence_mode)
            VALUES({ScopeEnvironment},{h.Tenant},{service.Identity.Principal},{Guid.NewGuid()},true,true,true,'DEMO');
            INSERT INTO cp6_requirement_set_review_fact_v1.subject_grants(environment_id,tenant_id,principal_id,submission_id,revision,enabled,can_receive,can_query)
            VALUES({ScopeEnvironment},{h.Tenant},{service.Identity.Principal},{f.Submission},{Guid.NewGuid()},true,true,true);
            """);
        return(new(h.Db,ScopeFence(h),service,new(h.Db,ScopeFence(h)),FactCursorProtection),bytes,service);
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementSet_native_FLOW_approval_requires_Owner_application_and_preserves_exact_old_set()
    {
        await using var h=await Seed();var f=await SetDecisionSetup(h);var id=f.Source.Set.Reference.Id;var reviews=SetReviews(h);
        var first=await SetVote(DecisionActor(h,f.Source.Member.First));Assert.Null(first.Result.DecisionId);
        var second=await SetVote(DecisionActor(h,f.Source.Member.Second));Assert.NotNull(second.Result.DecisionId);
        Assert.False((await reviews.SubmissionAsync(id,f.Submission)).Submission.IsApproved);
        var(receiver,bytes,_)=await SetReceiver(h,f);var decision=ApprovalDecisionWireV2.Parse(bytes).Value;
        Assert.Equal("Main.Plm.RequirementSet.v1",decision.ConsumerOwner);Assert.Equal("APPROVED",decision.Decision);
        FactAck(await receiver.ReceiveAsync(bytes));var ready=await reviews.SubmissionAsync(id,f.Submission);
        var command=new RequirementSetApplicationCommandV1(Guid.NewGuid(),f.Submission,null,"Apply native exact-set decision");
        var applied=await reviews.ApplyAsync(id,ready.Application.Etag,command);Assert.Equal("REQUIREMENT_SET_APPROVED",applied.Receipt.OutcomeCode);
        Assert.Equal(applied,await reviews.ApplyAsync(id,ready.Application.Etag,command));Assert.Equal(applied,(await reviews.ApplicationOperationAsync(command.OperationId,false)).Result);
        Assert.Equal("APPROVED",(await Sets(h).ReadAsync(id)).State);
        await Sets(h).CommandAsync(SetRevise(f.Source.Set,f.Source.Set.Body with{Name="New scope draft"}));
        Assert.Equal("DRAFT",(await Sets(h).ReadAsync(id)).State);Assert.Equal("APPROVED",(await Sets(h).ReadAsync(id,"1")).State);
        Assert.True((await reviews.SubmissionAsync(id,f.Submission)).Submission.IsApproved);
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementSet_member_withdrawal_blocks_application_without_approving_or_rewriting_the_set()
    {
        await using var h=await Seed();var f=await SetDecisionSetup(h);var id=f.Source.Set.Reference.Id;
        await SetVote(DecisionActor(h,f.Source.Member.First));await SetVote(DecisionActor(h,f.Source.Member.Second));
        var(receiver,bytes,_)=await SetReceiver(h,f);FactAck(await receiver.ReceiveAsync(bytes));var reviews=SetReviews(h);var ready=await reviews.SubmissionAsync(id,f.Submission);
        var evaluations=Evaluations(h);await evaluations.CommandAsync(EvaluationCommand(await evaluations.ReadAsync(f.Source.Member.Definition.Reference.Id),"WITHDRAW"));
        Assert.Equal(422,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>reviews.ApplyAsync(id,ready.Application.Etag,new(Guid.NewGuid(),f.Submission,null,"Try stale member approval")))).Status);
        var after=await reviews.SubmissionAsync(id,f.Submission);Assert.Null(after.Application.Application);Assert.Equal("DRAFT",after.RequirementSet.State);
    }
}
