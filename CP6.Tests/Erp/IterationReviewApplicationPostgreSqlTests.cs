using System.Text;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed record IterationDecisionFixture(IterationReviewFixture Source,Guid Submission,IntakeAuthority Service);
    private async Task<IterationDecisionFixture> IterationDecisionSetup(Harness h,string rejectDisposition="RETURN",Func<Task>? beforeCheck=null)
    {
        var f=await IterationReviewSetup(h,1,async iteration=>{
            foreach(var action in new[]{"review-apply","review-withdraw","review-issue","review-issue-verify","review-issue-degrade"})await EngineeringAction(h,"plm-engineering-iteration",action);
            h.Db.Sys_Roles.Add(new(){RoleId=76,RoleName="Iteration FLOW reviewer"});await h.Db.SaveChangesAsync();
            await h.Db.Sys_Users.Where(x=>x.Id==iteration.Reviewer).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RoleId,76));
            await TaskPermission(h,iteration.Reviewer);await EngineeringAction(h,"oa-inbox","query");
            if(beforeCheck!=null)await beforeCheck();
        },rejectDisposition);
        var(binding,flows,initial)=await ExistingReviewBinding(h);var head=await flows.ReadHeadAsync("binding-flow");
        var draft=await flows.CreateNextDraftAsync("binding-flow",NextDraftCommand(head.Version,initial,"iteration-decision-next"));
        var edit=await flows.SaveAsync("binding-flow",AuthoringCommand("iteration-decision-flow",draft.ResourceVersion,"iteration professional",StartSchema.Replace("\"approverRoleId\":73","\"approverRoleId\":76")));
        var published=await flows.PublishAsync("binding-flow",PublicationCommand(await Preview(flows,"binding-flow",edit),"iteration-decision-publish"));
        var at=BindingTime();await binding.SaveAsync(null,null,BindingBytes(BindingInput(published.Result!.Publication!.VersionId,at) with{BizType="PLM_ITER_REVIEW",SubjectKind="PLM_ITER_REVIEW",DetailRoute="/plm/engineering-iteration/{bizId}"}));
        var owner=IterationReviews(h,f.Files);f=f with{Context=await owner.ContextAsync(f.Context.IterationId)};
        var result=await owner.SubmitAsync(f.Context.IterationId,f.Context.Etag,IterationSubmit(f));var submission=result.Submission!.SubmissionId;
        var bytes=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_iteration_review_v1.outbox WHERE tenant_id={h.Tenant} AND submission_id={submission}").SingleAsync();
        var service=new IntakeAuthority(new(ScopeEnvironment,h.Tenant,f.Principal,"iteration-owner","CP6.Services","test-registration",true,"DEMO"));
        var intake=new ApprovalIntakeOwnerV2(h.Db,ScopeFence(h),service,ToolApproval(h),flows,new(ToolApproval(h),null,null,owner)){ReceiveClock=()=>at.AddSeconds(1)};
        await intake.SubmitAsync(bytes);Assert.Equal("RUNNING",(await Starter(h).RunAsync(h.Tenant)).State);h.Db.ChangeTracker.Clear();
        return new(f,submission,service);
    }
    private static async Task<ApprovalTaskDecisionResultV2> IterationVote(Harness h,IterationDecisionFixture f,string decision="approve")
    {
        var actor=DecisionActor(h,f.Source.Iteration.Reviewer);var owner=new ApprovalTaskDecisionOwnerV2(h.Db,ScopeFence(h),TaskAuthority(actor),ToolApproval(actor),new(ToolApproval(actor),null,null,IterationReviews(actor,f.Source.Files)));
        var task=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(x=>x.AssigneeId==actor.User && x.Status==0);var c=await owner.ContextAsync(task.Id);
        return await owner.DecideAsync(task.Id,new(Guid.NewGuid(),decision,"Reviewed exact frozen iteration and complete technical evidence",c.TaskRowVersion,c.InstanceRowVersion),null,"\""+c.TaskRowVersion+"\"");
    }
    private async Task<(IterationReviewFactReceiverV1 Receiver,byte[] Payload,IntakeAuthority Authority)> IterationReceiver(Harness h,IterationDecisionFixture f)
    {
        var bytes=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_approval_decision_v2.outbox WHERE tenant_id={h.Tenant} AND submission_id={f.Submission}").SingleAsync();
        var service=new IntakeAuthority(new(ScopeEnvironment,h.Tenant,Guid.NewGuid(),"iteration-workflow-producer","CP6.Services","test-registration",true,"DEMO"));
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_iteration_review_fact_v1.registrations(environment_id,tenant_id,principal_id,revision,enabled,can_receive,can_query,evidence_mode)
            VALUES({ScopeEnvironment},{h.Tenant},{service.Identity.Principal},{Guid.NewGuid()},true,true,true,'DEMO');
            INSERT INTO cp6_iteration_review_fact_v1.subject_grants(environment_id,tenant_id,principal_id,submission_id,revision,enabled,can_receive,can_query)
            VALUES({ScopeEnvironment},{h.Tenant},{service.Identity.Principal},{f.Submission},{Guid.NewGuid()},true,true,true)
            """);
        return(new(h.Db,ScopeFence(h),service,new(h.Db,ScopeFence(h)),FactCursorProtection),bytes,service);
    }
    private static ApprovalWithdrawalOwnerV2 IterationWithdrawals(Harness h,IterationDecisionFixture f)
        =>new(h.Db,ScopeFence(h),f.Service,new(ToolApproval(h),null,null,IterationReviews(h,f.Source.Files)));
    private static async Task GrantIterationWithdrawRelay(Harness h,IterationDecisionFixture f)
    {
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_approval_withdraw_v2.registrations(environment_id,tenant_id,principal_id,owner,fact_owner,revision,enabled,can_query,can_relay,evidence_mode)
            VALUES({ScopeEnvironment},{h.Tenant},{f.Source.Principal},'Main.Plm.EngineeringIteration.v1','Main.Plm.IterationWithdrawal.v1',{Guid.NewGuid()},true,true,true,'DEMO');
            INSERT INTO cp6_approval_withdraw_v2.subject_grants(environment_id,tenant_id,principal_id,owner,submission_id,revision,enabled,can_query,can_relay)
            VALUES({ScopeEnvironment},{h.Tenant},{f.Source.Principal},'Main.Plm.EngineeringIteration.v1',{f.Submission},{Guid.NewGuid()},true,true,true)
            """);
    }
    private sealed class NativeIterationWithdrawalTransport(Harness h,ApprovalWithdrawalOwnerV2 workflow,Guid principal):IIterationWithdrawalTransportV1
    {
        public int Posts,Queries;public bool LoseFirstResponse=true;
        public ToolApprovalDeliveryRouteV1? Route(Guid tenant)=>tenant==h.Tenant?new(ScopeEnvironment,tenant,principal,"iteration-owner","https://issuer.example","https://main.example","v1","DEMO"):null;
        private void Outside(){Assert.Null(h.Db.Database.CurrentTransaction);Assert.False(h.Db.ChangeTracker.HasChanges());}
        public async Task<ToolApprovalDeliveryResponseV1> QueryAsync(ToolApprovalDeliveryRouteV1 route,Guid command,CancellationToken ct)
        {
            Outside();Queries++;
            try{var r=await workflow.OriginalAsync(command,ct);return new(r.Status==409?200:r.Status,ApprovalWithdrawalResponseV2.Bytes(r.Ack));}
            catch(ScopeAuthorizationExceptionV1 e)when(e.Status==404){return new(404,Encoding.UTF8.GetBytes("{\"code\":\"RESOURCE_NOT_FOUND\"}"));}
        }
        public async Task<ToolApprovalDeliveryResponseV1> ContextAsync(ToolApprovalDeliveryRouteV1 route,Guid submission,CancellationToken ct)
        {Outside();return new(200,ApprovalWithdrawalResponseV2.Bytes(await workflow.ContextAsync(submission,ct)));}
        public async Task<ToolApprovalDeliveryResponseV1> SendAsync(ToolApprovalDeliveryRouteV1 route,Guid submission,string version,byte[] original,CancellationToken ct)
        {
            Outside();Posts++;var r=await workflow.WithdrawAsync(submission,"\""+version+"\"",original,ct);
            if(LoseFirstResponse){LoseFirstResponse=false;throw new HttpRequestException("Committed native Tx W; response lost");}
            return new(r.Status,ApprovalWithdrawalResponseV2.Bytes(r.Ack));
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task IterationApplication_open_issue_blocks_then_independent_verification_applies_atomic_stage_and_receipt_chain()
    {
        await using var h=await Seed();var f=await IterationDecisionSetup(h);var id=f.Source.Context.IterationId;var o=IterationReviews(h,f.Source.Files);
        var issues=await o.IssuesAsync(id);var create=new IterationReviewIssueCommandV1(Guid.NewGuid(),f.Submission,Guid.NewGuid(),"CREATE",null,
            "Explain the packaging clearance",new("CANDIDATE",f.Source.Context.Candidates[0].Reference,""),h.User,new(2026,11,1),null,"Blocking safety clarification");
        var issue=await o.IssueAsync(id,issues.Etag,create);Assert.Equal("BLOCKING",issue.Issue.Severity);
        await IterationVote(h,f);var receiver=await IterationReceiver(h,f);FactAck(await receiver.Receiver.ReceiveAsync(receiver.Payload));
        var c=await o.ApplicationAsync(id,f.Submission);var blocked=await o.ApplyAsync(id,c.Etag,new(Guid.NewGuid(),f.Submission,null,"Check actual application gate"));
        Assert.Equal("BLOCKED",blocked.Receipt.Application);Assert.Equal("IN_REVIEW",(await Iterations(h,f.Source.Files).ReadAsync(id)).Body.Iteration!.Stage);
        issues=await o.IssuesAsync(id);var response=await o.IssueAsync(id,issues.Etag,new(Guid.NewGuid(),f.Submission,create.IssueId,"RESPOND",1,"Clearance checked at exact candidate",null,null,null,[],"Original response"));
        issues=await o.IssuesAsync(id);var verify=new IterationReviewIssueCommandV1(Guid.NewGuid(),f.Submission,create.IssueId,"VERIFY",2,"Independently checked",null,null,null,null,"Verifier accepts clarification");
        Assert.Equal("ITERATION_REVIEW_ISSUE_INDEPENDENT_VERIFIER_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.IssueAsync(id,issues.Etag,verify))).Message);
        var independent=IterationReviews(DecisionActor(h,f.Source.Iteration.Reviewer),f.Source.Files);var ready=await independent.IssuesAsync(id);
        await independent.IssueAsync(id,ready.Etag,verify);ready=await independent.IssuesAsync(id);
        var note=await independent.IssueAsync(id,ready.Etag,new(Guid.NewGuid(),f.Submission,create.IssueId,"NOTE",3,"Additional independent observation",null,null,null,[],"Preserve verification and original response"));
        Assert.Equal("VERIFIED_CLOSED",note.Issue.State);Assert.Equal(ApprovalCanonicalV2.Bytes(response.Issue.Responses[0]),ApprovalCanonicalV2.Bytes(note.Issue.Responses[0]));
        c=await o.ApplicationAsync(id,f.Submission);
        var command=new IterationApplicationCommandV1(Guid.NewGuid(),f.Submission,blocked.Receipt.ReceiptId,"Apply after independent verification");
        var applied=await o.ApplyAsync(id,c.Etag,command);Assert.Equal("APPLIED",applied.Receipt.Application);Assert.Equal("2",applied.Receipt.ReceiptRevision);
        Assert.Equal(blocked.Receipt.ReceiptId,applied.Receipt.PreviousReceiptId);Assert.Equal("PLM_ITER_REVIEW_DECISION",applied.Receipt.Action);
        Assert.Equal("REVIEW_APPROVED",(await Iterations(h,f.Source.Files).ReadAsync(id)).Body.Iteration!.Stage);
        Assert.Equal((await Iterations(h,f.Source.Files).ReadAsync(id)).Reference.Version,applied.Receipt.CurrentSubjectRevision);
        Assert.Equal(applied,await o.ApplyAsync(id,c.Etag,command));Assert.Equal(applied,(await o.ApplicationOperationAsync(command.OperationId,false)).Result);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_approval_receipt_v2.registrations(environment_id,tenant_id,principal_id,owner,revision,enabled,can_receive,can_query,evidence_mode)
            VALUES({ScopeEnvironment},{h.Tenant},{f.Source.Principal},{ApprovalBusinessProfileV2.IterationOwner},{Guid.NewGuid()},true,true,true,'DEMO');
            INSERT INTO cp6_approval_receipt_v2.subject_grants(environment_id,tenant_id,principal_id,submission_id,owner,revision,enabled,can_receive,can_query)
            VALUES({ScopeEnvironment},{h.Tenant},{f.Source.Principal},{f.Submission},{ApprovalBusinessProfileV2.IterationOwner},{Guid.NewGuid()},true,true,true)
            """);
        var projection=new ApprovalReceiptIngestionV2(h.Db,ScopeFence(h),f.Service);
        var wrongAction=ApprovalApplicationReceiptWireV2.Seal(applied.Receipt with{Action=applied.Receipt.Subject.BizType});
        Assert.Equal("PRODUCER_NOT_ALLOWED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>projection.ReceiveAsync(ReceiptBytes(wrongAction)))).Message);
        Assert.Equal(202,(await projection.ReceiveAsync(ReceiptBytes(applied.Receipt))).Status);
        Assert.Null((await projection.CurrentAsync(applied.Receipt.ReceiptStreamId)).Receipt);
        Assert.Equal(200,(await projection.ReceiveAsync(ReceiptBytes(blocked.Receipt))).Status);
        Assert.Equal(applied.Receipt,(await projection.CurrentAsync(applied.Receipt.ReceiptStreamId)).Receipt);
        var page=Assert.IsType<ApprovalReceiptChainPageV2>(await receiver.Receiver.ReceiptChainAsync(applied.Receipt.ReceiptStreamId,limit:1));
        Assert.Equal(blocked.Receipt,Assert.Single(page.Items));
        var next=Assert.IsType<ApprovalReceiptChainPageV2>(await receiver.Receiver.ReceiptChainAsync(applied.Receipt.ReceiptStreamId,limit:1,cursor:page.NextCursor));
        Assert.Equal(applied.Receipt,Assert.Single(next.Items));
        var before=await o.ApplicationAsync(id,f.Submission);var withdraw=new ApprovalUserWithdrawV2(Guid.NewGuid(),"Try withdrawal after application");
        var conflict=await o.WithdrawAsync(id,f.Submission,"\""+before.ControlRowVersion+"\"",withdraw);Assert.Equal("CONFLICT",conflict.State);Assert.Equal(applied.Receipt,conflict.AppliedReceipt);
        Assert.Equal(conflict,(await o.WithdrawalOperationAsync(withdraw.CommandId,false)).Result);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_iteration_review_control_v1.withdraw_outbox WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task IterationWithdrawal_preserves_real_actor_and_control_versions_native_TxW_cancels_tasks_and_replays_original()
    {
        await using var h=await Seed();var f=await IterationDecisionSetup(h);await GrantIterationWithdrawRelay(h,f);var id=f.Source.Context.IterationId;
        var o=IterationReviews(h,f.Source.Files);var c=await o.ApplicationAsync(id,f.Submission);var command=new ApprovalUserWithdrawV2(Guid.NewGuid(),"Rework this exact round");
        var result=await o.WithdrawAsync(id,f.Submission,"\""+c.ControlRowVersion+"\"",command);Assert.Equal("WITHDRAWN",result.State);
        Assert.Equal(c.ControlRowVersion,result.Fact!.BusinessBeforeRowVersion);Assert.NotEqual(result.Fact.BusinessBeforeRowVersion,result.Fact.BusinessAfterRowVersion);
        Assert.Equal(h.User,result.Fact.Actor.ActualHandlerId);Assert.Equal(command.CommandId,result.Fact.Actor.WithdrawCommandId);
        var bytes=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_iteration_review_control_v1.withdraw_outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        var workflow=IterationWithdrawals(h,f);var ctx=await workflow.ContextAsync(f.Submission);
        var badVersion=await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>workflow.WithdrawAsync(f.Submission,"\"old version\"",bytes));Assert.Equal(412,badVersion.Status);
        var transport=new NativeIterationWithdrawalTransport(h,workflow,f.Source.Principal);var delivery=new IterationWithdrawalDeliveryV1(h.Db,ScopeFence(h),transport);
        Assert.Equal("OUTCOME_UNKNOWN",(await delivery.RunAsync(h.Tenant)).State);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_iteration_withdrawal_delivery_v1.delivery SET next_attempt=clock_timestamp()-interval '1 second' WHERE tenant_id={h.Tenant}");
        Assert.Equal("WITHDRAWN",(await delivery.RunAsync(h.Tenant)).State);Assert.Equal(1,transport.Posts);Assert.Equal(2,transport.Queries);
        var ack=await workflow.OriginalAsync(result.TransportCommandId!.Value);Assert.Equal(200,ack.Status);Assert.Equal("WITHDRAWN",ack.Ack.State);
        Assert.Equal(ack,await workflow.WithdrawAsync(f.Submission,"\"old version\"",bytes));Assert.Equal(ack,await workflow.OriginalAsync(result.TransportCommandId!.Value));
        var receiver=await IterationReceiver(h,f);var decision=ApprovalDecisionWireV2.Parse(receiver.Payload).Value;
        Assert.Equal("WITHDRAWN",decision.Decision);Assert.Equal("WITHDRAWAL",decision.DecisionMode);Assert.Null(decision.DecisionEvidenceDigest);Assert.Null(decision.DecisionEvidenceSetRef);
        Assert.Equal(result.Fact.Actor.ActualHandlerId,decision.TerminalActor.GetProperty("actualHandlerId").GetGuid());
        Assert.Equal(result.Fact.WithdrawnAt,decision.TerminalActor.GetProperty("occurredAt").GetDateTimeOffset());
        var received=FactAck(await receiver.Receiver.ReceiveAsync(receiver.Payload));Assert.Equal("STALE_RECEIPT",received.ReceiveState);
        c=await o.ApplicationAsync(id,f.Submission);var stale=await o.ApplyAsync(id,c.Etag,new(Guid.NewGuid(),f.Submission,null,"Record withdrawn round disposition"));
        Assert.Equal("STALE",stale.Receipt.Application);Assert.Equal("IN_DESIGN",(await Iterations(h,f.Source.Files).ReadAsync(id)).Body.Iteration!.Stage);
        var instance=(await workflow.ContextAsync(f.Submission)).InstanceId;
        Assert.False(await h.Db.Wf_FlowTasks.AnyAsync(x=>x.InstanceId==instance && (x.Status==0 || x.Status==4)));
        Assert.False(await h.Db.Wf_FlowTokens.AnyAsync(x=>x.InstanceId==instance && x.Status==0));
    }
    [CurrentPermissionPostgresFact]
    public async Task IterationWithdrawal_blocked_resumes_original_person_and_service_read_revocation_is_not_not_observed()
    {
        await using var h=await Seed();var f=await IterationDecisionSetup(h);await GrantIterationWithdrawRelay(h,f);var id=f.Source.Context.IterationId;
        var o=IterationReviews(h,f.Source.Files);var c=await o.ApplicationAsync(id,f.Submission);
        var withdrawn=await o.WithdrawAsync(id,f.Submission,"\""+c.ControlRowVersion+"\"",new(Guid.NewGuid(),"Withdraw with original human"));
        var bytes=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_iteration_review_control_v1.withdraw_outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        var roles=await h.Db.Sys_RoleActions.AsNoTracking().Where(x=>x.ActionCode=="review-withdraw").ToArrayAsync();Assert.NotEmpty(roles);
        await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="review-withdraw").ExecuteDeleteAsync();
        var workflow=IterationWithdrawals(h,f);var context=await workflow.ContextAsync(f.Submission);
        var blocked=await workflow.WithdrawAsync(f.Submission,"\""+context.SubmissionRowVersion+"\"",bytes);Assert.Equal("BLOCKED",blocked.Ack.State);
        Assert.Null(blocked.Ack.DecisionId);Assert.Equal("RUNNING",(await workflow.ContextAsync(f.Submission)).WorkflowState);
        Assert.Equal(blocked,await workflow.OriginalAsync(withdrawn.TransportCommandId!.Value));
        h.Db.Sys_RoleActions.AddRange(roles);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var terminal=await workflow.WithdrawAsync(f.Submission,"\""+context.SubmissionRowVersion+"\"",bytes);Assert.Equal("WITHDRAWN",terminal.Ack.State);
        await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="review-withdraw").ExecuteDeleteAsync();
        Assert.Equal(terminal,await workflow.WithdrawAsync(f.Submission,null,bytes));
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_approval_withdraw_v2.subject_grants SET can_query=false,revision={Guid.NewGuid()} WHERE tenant_id={h.Tenant}");
        Assert.Equal(403,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>workflow.OriginalAsync(withdrawn.TransportCommandId.Value))).Status);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_decision_v2.decisions WHERE tenant_id={h.Tenant} AND submission_id={f.Submission}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task IterationWithdrawal_late_approved_fact_is_stale_and_successor_round_carries_original_issues()
    {
        await using var h=await Seed();var f=await IterationDecisionSetup(h);await GrantIterationWithdrawRelay(h,f);var id=f.Source.Context.IterationId;var o=IterationReviews(h,f.Source.Files);
        var issues=await o.IssuesAsync(id);var issue=Guid.NewGuid();
        await o.IssueAsync(id,issues.Etag,new(Guid.NewGuid(),f.Submission,issue,"CREATE",null,"Carry this unresolved technical question",new("PROCESS",null,null),h.User,new(2026,11,1),null,"Independent review concern"));
        await IterationVote(h,f);var c=await o.ApplicationAsync(id,f.Submission);
        await o.WithdrawAsync(id,f.Submission,"\""+c.ControlRowVersion+"\"",new(Guid.NewGuid(),"Owner withdrawal wins before business application"));
        var nextContext=await o.ContextAsync(id);var next=await o.SubmitAsync(id,nextContext.Etag,IterationSubmit(f.Source with{Context=nextContext}));
        Assert.NotEqual(f.Submission,next.Submission!.SubmissionId);var package=await o.SubmissionAsync(id,next.Submission.SubmissionId);
        Assert.Equal(issue,Assert.Single(package.Package.Issues).GetProperty("id").GetGuid());Assert.NotEqual(f.Source.Context.Iteration.Reference,package.Package.Iteration);
        var receiver=await IterationReceiver(h,f);Assert.Equal("STALE_RECEIPT",FactAck(await receiver.Receiver.ReceiveAsync(receiver.Payload)).ReceiveState);
        c=await o.ApplicationAsync(id,f.Submission);var stale=await o.ApplyAsync(id,c.Etag,new(Guid.NewGuid(),f.Submission,null,"Preserve late original result"));
        Assert.Equal("STALE",stale.Receipt.Application);Assert.Equal(next.Submission.SubmissionId,(await Iterations(h,f.Source.Files).ReadAsync(id)).Body.Iteration!.CurrentReview!.SubmissionId);
        issues=await o.IssuesAsync(id);await o.IssueAsync(id,issues.Etag,new(Guid.NewGuid(),f.Submission,issue,"RESPOND",1,"Answer original issue in successor round",null,null,null,[],"Document original response"));
        var reviewer=IterationReviews(DecisionActor(h,f.Source.Iteration.Reviewer),f.Source.Files);issues=await reviewer.IssuesAsync(id);
        await reviewer.IssueAsync(id,issues.Etag,new(Guid.NewGuid(),f.Submission,issue,"VERIFY",2,"Independent successor review",null,null,null,null,"Verified carried issue"));
        Assert.Equal("VERIFIED_CLOSED",Assert.Single((await o.IssuesAsync(id)).Issues).State);
        var bytes=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_iteration_review_control_v1.withdraw_outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        var workflow=IterationWithdrawals(h,f);var context=await workflow.ContextAsync(f.Submission);
        var conflict=await workflow.WithdrawAsync(f.Submission,"\""+context.SubmissionRowVersion+"\"",bytes);Assert.Equal("TERMINAL_CONFLICT",conflict.Ack.State);
        Assert.Equal("WORKFLOW_ALREADY_DECIDED",conflict.Ack.ErrorCode);Assert.Equal(next.Submission.SubmissionId,(await Iterations(h,f.Source.Files).ReadAsync(id)).Body.Iteration!.CurrentReview!.SubmissionId);
    }
}
