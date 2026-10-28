using System.Net;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed record RuleApplicationFixture(RuleDecisionFixture Rule,LocationRuleReviewFactReceiverV1 Receiver,IntakeAuthority Authority,byte[] Payload,ApprovalDecisionEnvelopeV2 Decision);
    private async Task<RuleApplicationFixture> RuleApplicationSetup(Harness h,bool approve=true)
    {
        var f=await RuleDecisionSetup(h);await RuleVote(DecisionActor(h,f.Source.First),approve?"approve":"reject");
        if(approve)await RuleVote(DecisionActor(h,f.Source.Second));var receiver=await RuleReceiver(h,f);
        return new(f,receiver.Receiver,receiver.Authority,receiver.Payload,ApprovalDecisionWireV2.Parse(receiver.Payload).Value);
    }
    [CurrentPermissionPostgresFact]
    public async Task RuleReview_returned_round_can_resubmit_exact_version_and_old_decision_cannot_overwrite_new_round()
    {
        await using var h=await Seed();var f=await RuleApplicationSetup(h,false);var id=f.Rule.Source.Candidate.Reference.Id;var owner=RuleReviews(h);
        FactAck(await f.Receiver.ReceiveAsync(f.Payload));var ready=await owner.SubmissionAsync(id,f.Rule.Submission);
        await owner.ApplyAsync(id,ready.Application.Etag,new(Guid.NewGuid(),f.Rule.Submission,null,"Return to rule author"));
        var c=await owner.ContextAsync(id);Assert.Equal("RETURNED",c.LocationRule.State);
        var next=await owner.SubmitAsync(id,c.Etag,RuleReviewSubmit(c,f.Rule.Submission));Assert.NotEqual(f.Rule.Submission,next.Submission!.SubmissionId);
        var old=await owner.SubmissionAsync(id,f.Rule.Submission);
        await owner.ApplyAsync(id,old.Application.Etag,new(Guid.NewGuid(),f.Rule.Submission,old.Application.Receipt!.ReceiptId,"Read old rejection"));
        var current=await owner.ContextAsync(id);Assert.Equal("SUBMITTED",current.LocationRule.State);Assert.Equal(next.Submission.SubmissionId,current.LocationRule.ActiveSubmissionId);
        Assert.Equal(f.Rule.Source.Candidate.Reference,current.LocationRule.Reference);
    }
    [CurrentPermissionPostgresFact]
    public async Task RuleReview_returned_edit_creates_unresolved_successor_and_never_reuses_approval()
    {
        await using var h=await Seed();var f=await RuleApplicationSetup(h,false);var id=f.Rule.Source.Candidate.Reference.Id;var owner=RuleReviews(h);
        FactAck(await f.Receiver.ReceiveAsync(f.Payload));var ready=await owner.SubmissionAsync(id,f.Rule.Submission);
        await owner.ApplyAsync(id,ready.Application.Etag,new(Guid.NewGuid(),f.Rule.Submission,null,"Return for amendment"));
        var rules=ReferenceRules(h);var before=await rules.ReadAsync(f.Rule.Source.Location,id);
        var next=await rules.PatchAsync(f.Rule.Source.Location,id,before.Etag,new(Guid.NewGuid(),RulePatch("""{"allowedOperations":{"op":"SET","value":["RECEIVE"]}}""")));
        Assert.Equal("DRAFT",next.State);Assert.Equal("3",next.Reference.Version);Assert.Equal("UNRESOLVED",(await rules.ReadAsync(f.Rule.Source.Location,id)).ReferenceResolution);
        var c=await owner.ContextAsync(id);Assert.Null(c.LocationRule.ActiveSubmissionId);
        Assert.Equal("RULE_REFERENCES_UNRESOLVED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.SubmitAsync(id,c.Etag,RuleReviewSubmit(c)))).Message);
        Assert.Equal(f.Rule.Source.Candidate.Reference,(await owner.SubmissionAsync(id,f.Rule.Submission)).Package.LocationRule);
    }
    [CurrentPermissionPostgresFact]
    public async Task RuleReview_withdraw_before_late_approval_preserves_fact_and_emits_stale_receipt_without_reactivation()
    {
        await using var h=await Seed();var f=await RuleApplicationSetup(h);var id=f.Rule.Source.Candidate.Reference.Id;var rules=ReferenceRules(h);var owner=RuleReviews(h);
        var current=await rules.ReadAsync(f.Rule.Source.Location,id);await rules.WithdrawAsync(f.Rule.Source.Location,id,current.Etag,new(Guid.NewGuid(),"Withdraw before receipt binding"));
        FactAck(await f.Receiver.ReceiveAsync(f.Payload));var ready=await owner.SubmissionAsync(id,f.Rule.Submission);
        Assert.Equal("WITHDRAWN",ready.Submission.State);Assert.Equal("WITHDRAWN",Assert.Single((await owner.ContextAsync(id)).Submissions).State);
        var command=new LocationRuleApplicationCommandV1(Guid.NewGuid(),f.Rule.Submission,null,"Retain late approval as history");
        var result=await owner.ApplyAsync(id,ready.Application.Etag,command);Assert.Equal("STALE",result.Receipt.Application);Assert.Null(result.Receipt.BusinessResultRef);
        Assert.Equal("WITHDRAWN",(await rules.ReadAsync(f.Rule.Source.Location,id)).State);Assert.Null((await owner.SubmissionAsync(id,f.Rule.Submission)).Application.Application);
        await RevokeTransferAction(h,"wms-location-rule","review-bind");Assert.Equal(result,await owner.ApplyAsync(id,ready.Application.Etag,command));
        Assert.Equal(result,(await owner.ApplicationOperationAsync(command.OperationId,false)).Result);
    }
    [CurrentPermissionPostgresFact]
    public async Task RuleReview_explicit_nonseparation_policy_allows_author_only_through_actual_assigned_human_task()
    {
        await using var h=await Seed();var f=await RuleDecisionSetup(h,separate:false,self:true);
        Assert.Equal(h.User,f.Source.First);Assert.Null((await RuleVote(h)).Result.DecisionId);
        Assert.NotNull((await RuleVote(DecisionActor(h,f.Source.Second))).Result.DecisionId);
        var receiver=await RuleReceiver(h,f);FactAck(await receiver.Receiver.ReceiveAsync(receiver.Payload));var owner=RuleReviews(h);var id=f.Source.Candidate.Reference.Id;
        var detail=await owner.SubmissionAsync(id,f.Submission);Assert.False(detail.Policy.Fact.Body.SeparateAuthorAndReviewer);
        await owner.ApplyAsync(id,detail.Application.Etag,new(Guid.NewGuid(),f.Submission,null,"Explicit policy permits author vote"));Assert.Equal("APPROVED_PENDING",(await owner.ContextAsync(id)).LocationRule.State);
    }
    [CurrentPermissionPostgresFact]
    public async Task RuleReview_current_policy_source_fields_and_roles_gate_new_signatures_and_bindings()
    {
        await using var h=await Seed();var f=await RuleDecisionSetup(h);var id=f.Source.Candidate.Reference.Id;var first=DecisionActor(h,f.Source.First);
        await h.Db.Sys_UserRoles.Where(x=>x.UserId==f.Source.First&&x.RoleId==74).ExecuteDeleteAsync();
        Assert.Equal("LOCATION_RULE_PROFESSIONAL_ROLE_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>RuleVote(first))).Message);
        h.Db.Sys_UserRoles.Add(new(){UserId=f.Source.First,RoleId=74});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await RuleVote(first);await RuleVote(DecisionActor(h,f.Source.Second));var receiver=await RuleReceiver(h,f);FactAck(await receiver.Receiver.ReceiveAsync(receiver.Payload));
        var owner=RuleReviews(h);var c=await owner.ContextAsync(id);await owner.PolicyAsync(id,c.Etag,RuleReviewPolicy() with{State="REVOKED"});
        var detail=await owner.SubmissionAsync(id,f.Submission);
        Assert.Equal("LOCATION_RULE_REVIEW_POLICY_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.ApplyAsync(id,detail.Application.Etag,new(Guid.NewGuid(),f.Submission,null,"Stale policy cannot confer approval")))).Message);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="wms-location-rule",FieldName="Capacity",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.SubmissionAsync(id,f.Submission))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.ContextAsync(id))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task RuleReview_current_native_dictionary_change_blocks_approved_binding_without_changing_original()
    {
        await using var h=await Seed();var f=await RuleApplicationSetup(h);var id=f.Rule.Source.Candidate.Reference.Id;var owner=RuleReviews(h);
        FactAck(await f.Receiver.ReceiveAsync(f.Payload));var before=await owner.SubmissionAsync(id,f.Rule.Submission);
        var references=RuleReferences(h);var policy=(await references.ContextAsync(f.Rule.Source.Warehouse)).Policy!;
        await references.SaveAsync(f.Rule.Source.Warehouse,new(Guid.NewGuid(),policy.Reference.Version,policy.Original.Definition with{Enabled=false},"Disable current native source"));
        var current=await owner.SubmissionAsync(id,f.Rule.Submission);var operation=Guid.NewGuid();
        Assert.Equal("RULE_DICTIONARY_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.ApplyAsync(id,current.Application.Etag,new(operation,f.Rule.Submission,null,"Check actual sources at binding")))).Message);
        Assert.Equal(before.Package.LocationRule,current.Package.LocationRule);Assert.Equal("SUBMITTED",(await owner.ContextAsync(id)).LocationRule.State);
        Assert.Null((await owner.SubmissionAsync(id,f.Rule.Submission)).Application.Receipt);Assert.Equal("UNKNOWN",(await owner.ApplicationOperationAsync(operation,false)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task RuleReview_submission_failure_rolls_back_original_outbox_and_state_together()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await RuleReviewSetup(h);var owner=RuleReviews(h);var id=f.Candidate.Reference.Id;var c=await owner.ContextAsync(id);var command=RuleReviewSubmit(c);
        foreach(var fragment in new[]{"INSERT INTO cp6_location_rule_review_v1.submissions","UPDATE cp6_location_rule_v1.proposals SET revision=", "INSERT INTO cp6_main_owner_v1.operations"})
        {
            hook.Before=cmd=>{if(cmd.CommandText.Contains(fragment))cmd.CommandText="SELECT 1";return Task.CompletedTask;};
            Assert.NotNull(await Record.ExceptionAsync(()=>owner.SubmitAsync(id,c.Etag,command)));hook.Before=null;
            Assert.Equal("DRAFT",(await owner.ContextAsync(id)).LocationRule.State);Assert.Empty((await owner.ContextAsync(id)).Submissions);Assert.Equal("UNKNOWN",(await owner.OperationAsync(command.OperationId,false)).State);
        }
        await owner.SubmitAsync(id,c.Etag,command);
        Assert.Equal("COMMITTED",(await owner.OperationAsync(command.OperationId,false)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task RuleReview_binding_failure_and_competing_withdraw_have_one_durable_state_and_original()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await RuleApplicationSetup(h);var id=f.Rule.Source.Candidate.Reference.Id;var owner=RuleReviews(h);
        FactAck(await f.Receiver.ReceiveAsync(f.Payload));var ready=await owner.SubmissionAsync(id,f.Rule.Submission);
        var command=new LocationRuleApplicationCommandV1(Guid.NewGuid(),f.Rule.Submission,null,"Atomic review binding");
        foreach(var fragment in new[]{"UPDATE cp6_location_rule_v1.proposals SET revision=","INSERT INTO cp6_location_rule_review_application_v1.applications","INSERT INTO cp6_main_owner_v1.operations"})
        {
            hook.Before=cmd=>{if(cmd.CommandText.Contains(fragment))cmd.CommandText="SELECT 1";return Task.CompletedTask;};
            Assert.NotNull(await Record.ExceptionAsync(()=>owner.ApplyAsync(id,ready.Application.Etag,command)));hook.Before=null;
            Assert.Equal("SUBMITTED",(await owner.ContextAsync(id)).LocationRule.State);Assert.Null((await owner.SubmissionAsync(id,f.Rule.Submission)).Application.Receipt);
            Assert.Equal("UNKNOWN",(await owner.ApplicationOperationAsync(command.OperationId,false)).State);
        }
        var current=await ReferenceRules(h).ReadAsync(f.Rule.Source.Location,id);
        await using var peerDb=server.Context(new CP6.Core.Services.Common.TenantContext{CurrentTenantId=h.Tenant});
        var peer=h with{Db=peerDb};
        var bind=Record.ExceptionAsync(()=>owner.ApplyAsync(id,ready.Application.Etag,command));
        var withdraw=Record.ExceptionAsync(()=>ReferenceRules(peer).WithdrawAsync(f.Rule.Source.Location,id,current.Etag,new(Guid.NewGuid(),"Competing withdraw")));
        var results=await Task.WhenAll(bind,withdraw);Assert.Single(results,x=>x==null);
        Assert.Contains((await owner.ContextAsync(id)).LocationRule.State,new[]{"APPROVED_PENDING","WITHDRAWN"});
        if(results[0]==null)Assert.Equal("COMMITTED",(await owner.ApplicationOperationAsync(command.OperationId,false)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task RuleReview_forged_state_missing_guard_and_foreign_tenant_cannot_adopt_candidate()
    {
        await using var h=await Seed();var f=await RuleReviewSetup(h);var id=f.Candidate.Reference.Id;
        await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_location_rule_v1.proposals SET state='APPROVED_PENDING',active_submission_id={Guid.NewGuid()},token={Guid.NewGuid()},revision=revision+1,operation_id={Guid.NewGuid()} WHERE tenant_id={h.Tenant} AND id={id}"));
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_location_rule_v1.proposals DISABLE TRIGGER rule_review_provenance");
        try{Assert.Equal("RULE_STORE_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>RuleReviews(h).ContextAsync(id))).Message);}
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_location_rule_v1.proposals ENABLE TRIGGER rule_review_provenance");}
        await using var other=await Seed();await RuleReviewSetup(other);Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>RuleReviews(other).ContextAsync(id))).Status);
        Assert.Equal("LOCATION_RULE_REVIEW_ENVIRONMENT_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>RuleReviews(h,"").ScopeAsync())).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task RuleReview_actual_Program_HTTP_strict_submit_CAS_CSRF_and_read_only_original_recovery()
    {
        await using var h=await Seed();var f=await RuleReviewSetup(h);var id=f.Candidate.Reference.Id;var owner=RuleReviews(h);var c=await owner.ContextAsync(id);var command=RuleReviewSubmit(c);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString(),["LocationRuleReview__EvidenceMode"]="DEMO"});
        var cookies=await Login(host,h);var path=$"api/location-rule/owner-v1/{id}/review-submissions";
        async Task<HttpResponseMessage> Send(object input,string? etag,bool csrf=true)
        {using var r=OperationsRequest(cookies,HttpMethod.Post,path,input);if(etag!=null)r.Headers.TryAddWithoutValidation("If-Match",etag);if(!csrf)r.Headers.Remove("X-CSRF-Token");return await host.Send(r);}
        var forged=JsonSerializer.SerializeToNode(command,new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();forged["approved"]=true;
        using(var bad=await Send(forged,c.Etag))Assert.Equal(HttpStatusCode.BadRequest,bad.StatusCode);
        using(var denied=await Send(command,c.Etag,false))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var missing=await Send(command,null))Assert.Equal((HttpStatusCode)428,missing.StatusCode);
        using(var stale=await Send(command,"\"stale\""))Assert.Equal(HttpStatusCode.PreconditionFailed,stale.StatusCode);
        using(var saved=await Send(command,c.Etag)){Assert.True(saved.Headers.CacheControl?.NoStore);await DocumentHttpData<LocationRuleReviewResultV1>(saved);}
        await RevokeTransferAction(h,"wms-location-rule","review-submit");using(var replay=await Send(command,c.Etag))Assert.Equal(HttpStatusCode.OK,replay.StatusCode);
        using(var recovery=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/location-rule/owner-v1/review-operations/"+command.OperationId)))Assert.Equal("COMMITTED",(await DocumentHttpData<LocationRuleReviewOperationV1>(recovery)).State);
        await RevokeTransferAction(h,"wms-location-rule","query");using var hidden=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/location-rule/owner-v1/review-operations/"+command.OperationId));Assert.Equal(HttpStatusCode.Forbidden,hidden.StatusCode);
    }
}
