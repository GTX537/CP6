using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static LocationRuleReviewOwnerV1 RuleReviews(Harness h,string mode="DEMO")=>new(h.Db,RuleReferenceAuthority(h),ScopeFence(h),new(h.Db,mode));
    private sealed record RuleReviewFixture(Guid Location,Guid Warehouse,LocationRuleResultV1 Candidate,Guid First,Guid Second);
    private static LocationRuleReviewPolicyCommandV1 RuleReviewPolicy(bool separate=true,string rejection="RETURNED")=>new(Guid.NewGuid(),"ENABLED",
        [new("CAPACITY_AND_OPERATION","Review exact capacity, permitted operations and location suitability",74,2)],[],separate,
        DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds()),
        DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeMilliseconds()),"Explicit rule review policy",rejection);
    private static LocationRuleReviewSubmitV1 RuleReviewSubmit(LocationRuleReviewContextV1 c,Guid? replaces=null)=>new(Guid.NewGuid(),c.LocationRule.Reference,c.Policies.Last().Reference,
        "Review exact proposal original and current dictionary/unit bindings",[],Guid.NewGuid(),replaces,"Submit exact rule version");
    private static async Task<RuleReviewFixture> RuleReviewSetup(Harness h,bool register=true,bool separate=true,bool self=false)
    {
        if(register)await RegisterScope(h);var f=await RuleReferencesSetup(h);var p=await RuleReferences(h).SaveAsync(f.Warehouse,f.Command);
        var rules=ReferenceRules(h);var draft=await rules.CreateAsync(f.Location,new(Guid.NewGuid(),ResolvableRule()));
        var candidate=await rules.ResolveAsync(f.Location,draft.Reference.Id,draft.Etag,new(Guid.NewGuid(),p.Reference));
        foreach(var action in new[]{"review-policy","review-submit","review-decide","review-bind"})await EngineeringAction(h,"wms-location-rule",action);
        if(!await h.Db.Sys_Roles.AnyAsync(x=>x.RoleId==74))h.Db.Sys_Roles.Add(new(){RoleId=74,RoleName="Rule professional"});
        var first=self?h.User:Guid.NewGuid();var second=Guid.NewGuid();
        if(!self)h.Db.Sys_Users.Add(new(){Id=first,UserName="rule-reviewer-one",Password="fixture",RoleId=72});
        h.Db.Sys_Users.Add(new(){Id=second,UserName="rule-reviewer-two",Password="fixture",RoleId=72});
        h.Db.Sys_UserRoles.AddRange(new(){UserId=first,RoleId=74},new(){UserId=second,RoleId=74});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var owner=RuleReviews(h);var c=await owner.ContextAsync(candidate.Reference.Id);await owner.PolicyAsync(candidate.Reference.Id,c.Etag,RuleReviewPolicy(separate));
        return new(f.Location,f.Warehouse,candidate,first,second);
    }
    private sealed record RuleDecisionFixture(RuleReviewFixture Source,Guid Submission);
    private async Task<RuleDecisionFixture> RuleDecisionSetup(Harness h,bool receive=true,bool separate=true,bool self=false)
    {
        var(binding,flows,initial)=await PrepareBinding(h);var f=await RuleReviewSetup(h,false,separate,self);
        if(!await h.Db.Sys_Roles.AnyAsync(x=>x.RoleId==73))h.Db.Sys_Roles.Add(new(){RoleId=73,RoleName="Rule FLOW assignment"});await h.Db.SaveChangesAsync();
        foreach(var id in new[]{f.First,f.Second}){await h.Db.Sys_Users.Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RoleId,73));await TaskPermission(h,id);}
        await EngineeringAction(h,"oa-inbox","query");h.Db.ChangeTracker.Clear();
        var head=await flows.ReadHeadAsync("binding-flow");var draft=await flows.CreateNextDraftAsync("binding-flow",NextDraftCommand(head.Version,initial,"rule-next"));
        var edit=await flows.SaveAsync("binding-flow",AuthoringCommand("rule-flow",draft.ResourceVersion,"location rule",StartSchema));
        var publication=await flows.PublishAsync("binding-flow",PublicationCommand(await Preview(flows,"binding-flow",edit),"rule-publish"));var version=publication.Result!.Publication!.VersionId;var principal=Guid.NewGuid();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_approval_binding_v2.registrations(tenant_id,environment_id,biz_type,subject_kind,owner,revision,snapshot_schema,snapshot_fields,enabled,service_principal,audience,fixed_route,evidence_mode)
            VALUES({h.Tenant},{ScopeEnvironment},'WMS_LOCATION_RULE_REVIEW','LOCATION_RULE_PROPOSAL','Main.Wms.LocationRuleProposal.v1',{Guid.NewGuid()},
              'Main.Wms.LocationRuleReview.snapshot/1','[]',true,{principal},'CP6.Services','/internal/wms/location-rule-review-results/v1','DEMO')
            """);
        var at=BindingTime();await binding.SaveAsync(null,null,BindingBytes(BindingInput(version,at) with{BizType="WMS_LOCATION_RULE_REVIEW",SubjectKind="LOCATION_RULE_PROPOSAL",DetailRoute="/wms/location-rule/{bizId}/review"}));
        var owner=RuleReviews(h);var ready=await owner.ContextAsync(f.Candidate.Reference.Id);var submitted=await owner.SubmitAsync(f.Candidate.Reference.Id,ready.Etag,RuleReviewSubmit(ready));
        var payload=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_location_rule_review_v1.outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        var service=new IntakeAuthority(new(ScopeEnvironment,h.Tenant,principal,"fixture-service","CP6.Services","test-registration",true,"DEMO"));
        var tool=ToolApproval(h);var intake=new ApprovalIntakeOwnerV2(h.Db,ScopeFence(h),service,tool,flows,new(tool,rules:owner)){ReceiveClock=()=>at.AddSeconds(1)};
        if(receive){await intake.SubmitAsync(payload);Assert.Equal("RUNNING",(await Starter(h).RunAsync(h.Tenant)).State);}h.Db.ChangeTracker.Clear();
        return new(f,submitted.Submission!.SubmissionId);
    }
    private static ApprovalTaskDecisionOwnerV2 RuleDecisions(Harness h)=>new(h.Db,ScopeFence(h),TaskAuthority(h),ToolApproval(h),new(ToolApproval(h),rules:RuleReviews(h)));
    private static async Task<ApprovalTaskDecisionResultV2> RuleVote(Harness h,string decision="approve")
    {
        var task=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(x=>x.AssigneeId==h.User&&x.Status==0);var owner=RuleDecisions(h);var v=await owner.ContextAsync(task.Id);
        return await owner.DecideAsync(task.Id,new(Guid.NewGuid(),decision,"Reviewed fixed rule candidate, dictionary authority and exact units",v.TaskRowVersion,v.InstanceRowVersion),null,"\""+v.TaskRowVersion+"\"");
    }
    private async Task<(LocationRuleReviewFactReceiverV1 Receiver,byte[] Payload,IntakeAuthority Authority)> RuleReceiver(Harness h,RuleDecisionFixture f)
    {
        var bytes=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_approval_decision_v2.outbox WHERE tenant_id={h.Tenant} AND convert_from(payload,'UTF8')::jsonb->>'consumerOwner'='Main.Wms.LocationRuleProposal.v1'").SingleAsync();
        var service=new IntakeAuthority(new(ScopeEnvironment,h.Tenant,Guid.NewGuid(),"rule-workflow-producer","CP6.Services","test-registration",true,"DEMO"));
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_location_rule_review_fact_v1.registrations(environment_id,tenant_id,principal_id,revision,enabled,can_receive,can_query,evidence_mode)
            VALUES({ScopeEnvironment},{h.Tenant},{service.Identity.Principal},{Guid.NewGuid()},true,true,true,'DEMO');
            INSERT INTO cp6_location_rule_review_fact_v1.subject_grants(environment_id,tenant_id,principal_id,submission_id,revision,enabled,can_receive,can_query)
            VALUES({ScopeEnvironment},{h.Tenant},{service.Identity.Principal},{f.Submission},{Guid.NewGuid()},true,true,true);
            """);
        return(new(h.Db,ScopeFence(h),service,new(h.Db,ScopeFence(h)),FactCursorProtection),bytes,service);
    }
    [CurrentPermissionPostgresFact]
    public async Task RuleReview_exact_submission_freezes_version_and_original_recovery_without_approval()
    {
        await using var h=await Seed();var f=await RuleReviewSetup(h);var owner=RuleReviews(h);var id=f.Candidate.Reference.Id;
        var c=await owner.ContextAsync(id);var command=RuleReviewSubmit(c);var saved=await owner.SubmitAsync(id,c.Etag,command);
        Assert.Equal(saved,await owner.SubmitAsync(id,c.Etag,command));Assert.Equal(saved,(await owner.OperationAsync(command.OperationId,false)).Result);
        var source=await ReferenceRules(h).ReadAsync(f.Location,id);Assert.Equal("SUBMITTED",source.State);Assert.Equal(f.Candidate.Reference,source.Reference);Assert.False(source.AdmissionGrant);
        Assert.Equal("RULE_PROPOSAL_NOT_EDITABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>ReferenceRules(h).PatchAsync(f.Location,id,source.Etag,new(Guid.NewGuid(),RulePatch("""{"isPickable":{"op":"SET","value":true}}"""))))).Message);
        Assert.Null((await owner.SubmissionAsync(id,saved.Submission!.SubmissionId)).Application.Application);
        foreach(var sql in new[]{"DELETE FROM cp6_location_rule_review_v1.submissions","UPDATE cp6_location_rule_review_v1.policies SET body=body"})await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync(sql));
    }
    [CurrentPermissionPostgresFact]
    public async Task RuleReview_native_FLOW_decision_binds_pending_and_never_activates_rule_or_location()
    {
        await using var h=await Seed();var f=await RuleDecisionSetup(h);var id=f.Source.Candidate.Reference.Id;
        Assert.Single(await RuleDecisions(DecisionActor(h,f.Source.First)).LocationRuleTasksAsync(id));
        Assert.Null((await RuleVote(DecisionActor(h,f.Source.First))).Result.DecisionId);
        Assert.NotNull((await RuleVote(DecisionActor(h,f.Source.Second))).Result.DecisionId);
        var owner=RuleReviews(h);Assert.Null((await owner.SubmissionAsync(id,f.Submission)).Application.Application);
        var receiver=await RuleReceiver(h,f);FactAck(await receiver.Receiver.ReceiveAsync(receiver.Payload));
        var ready=await owner.SubmissionAsync(id,f.Submission);var command=new LocationRuleApplicationCommandV1(Guid.NewGuid(),f.Submission,null,"Bind trusted native decision");
        var bound=await owner.ApplyAsync(id,ready.Application.Etag,command);Assert.Equal("APPLIED",bound.Receipt.Application);
        Assert.Equal(bound,await owner.ApplyAsync(id,ready.Application.Etag,command));Assert.Equal(bound,(await owner.ApplicationOperationAsync(command.OperationId,false)).Result);
        var rule=await ReferenceRules(h).ReadAsync(f.Source.Location,id);Assert.Equal("APPROVED_PENDING",rule.State);Assert.False(rule.AdmissionGrant);
        Assert.Equal(f.Source.Candidate.Reference,rule.Reference);Assert.All((await ReferenceRules(h).ContextAsync(f.Source.Location)).Selectors,x=>{Assert.Equal("0",x.Revision);Assert.Null(x.CurrentRuleVersionId);});
        var location=await h.Db.Locations.IgnoreQueryFilters().AsNoTracking().SingleAsync(x=>x.Id==f.Source.Location);
        Assert.True(location.IsBlocked);Assert.True(location.IsInitializationQuarantined);Assert.Equal(0,location.CapacityQty);Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());
    }
}
