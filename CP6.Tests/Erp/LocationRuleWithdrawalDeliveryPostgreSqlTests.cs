using System.Text;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<IntakeAuthority> GrantRuleWithdrawRelay(Harness h,RuleDecisionFixture f)
    {
        var principal=await h.Db.Database.SqlQuery<Guid>($"SELECT service_principal AS \"Value\" FROM cp6_approval_binding_v2.registrations WHERE tenant_id={h.Tenant} AND owner='Main.Wms.LocationRuleProposal.v1'").SingleAsync();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_approval_withdraw_v2.registrations(environment_id,tenant_id,principal_id,owner,fact_owner,revision,enabled,can_query,can_relay,evidence_mode)
            VALUES({ScopeEnvironment},{h.Tenant},{principal},'Main.Wms.LocationRuleProposal.v1','Main.Wms.LocationRuleWithdrawal.v1',{Guid.NewGuid()},true,true,true,'DEMO');
            INSERT INTO cp6_approval_withdraw_v2.subject_grants(environment_id,tenant_id,principal_id,owner,submission_id,revision,enabled,can_query,can_relay)
            VALUES({ScopeEnvironment},{h.Tenant},{principal},'Main.Wms.LocationRuleProposal.v1',{f.Submission},{Guid.NewGuid()},true,true,true)
            """);
        await EngineeringAction(h,"wms-location-rule","review-withdraw");
        return new(new(ScopeEnvironment,h.Tenant,principal,"fixture-service","CP6.Services","test-registration",true,"DEMO"));
    }
    private static ApprovalWithdrawalOwnerV2 RuleWithdrawals(Harness h,IntakeAuthority service)
        =>new(h.Db,ScopeFence(h),service,new(ToolApproval(h),rules:RuleReviews(h)));
    private static LocationRuleBusinessGrantCommandV1 RuleGrantInput(Guid submission,Guid actual)
        =>new(Guid.NewGuid(),submission,null,null,actual,DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddHours(1),["review-withdraw"],true,"Explicit exact-round rule withdrawal delegation");
    private sealed class NativeRuleWithdrawalTransport(Harness h,ApprovalWithdrawalOwnerV2 workflow,Guid principal):ILocationRuleWithdrawalTransportV1
    {
        public int Posts,Queries;public bool LoseFirstResponse=true,StaleFirstContext;
        public readonly List<byte[]> Sent=[];
        public ToolApprovalDeliveryRouteV1? Route(Guid tenant)=>tenant==h.Tenant?new(ScopeEnvironment,tenant,principal,"document-owner","https://issuer.example","https://main.example","v1","DEMO"):null;
        private void Outside(){Assert.Null(h.Db.Database.CurrentTransaction);Assert.False(h.Db.ChangeTracker.HasChanges());}
        public async Task<ToolApprovalDeliveryResponseV1> QueryAsync(ToolApprovalDeliveryRouteV1 route,Guid command,CancellationToken ct)
        {
            Outside();Queries++;
            try{var r=await workflow.OriginalAsync(command,ct);return new(r.Status==409?200:r.Status,ApprovalWithdrawalResponseV2.Bytes(r.Ack));}
            catch(ScopeAuthorizationExceptionV1 e)when(e.Status==404){return new(404,Encoding.UTF8.GetBytes("{\"code\":\"RESOURCE_NOT_FOUND\"}"));}
        }
        public async Task<ToolApprovalDeliveryResponseV1> ContextAsync(ToolApprovalDeliveryRouteV1 route,Guid submission,CancellationToken ct)
        {
            Outside();var context=await workflow.ContextAsync(submission,ct);
            if(StaleFirstContext){StaleFirstContext=false;context=context with{SubmissionRowVersion=Convert.ToBase64String(new byte[8])};}
            return new(200,ApprovalWithdrawalResponseV2.Bytes(context));
        }
        public async Task<ToolApprovalDeliveryResponseV1> SendAsync(ToolApprovalDeliveryRouteV1 route,Guid submission,string version,byte[] original,CancellationToken ct)
        {
            Outside();Posts++;Sent.Add(original.ToArray());
            ApprovalWithdrawResultV2 r;
            try{r=await workflow.WithdrawAsync(submission,"\""+version+"\"",original,ct);}
            catch(ScopeAuthorizationExceptionV1 e)when(e.Status==412){return new(412,Encoding.UTF8.GetBytes("{\"code\":\"PRECONDITION_FAILED\"}"));}
            if(LoseFirstResponse){LoseFirstResponse=false;throw new HttpRequestException("Committed native Tx W; response lost");}
            return new(r.Status,ApprovalWithdrawalResponseV2.Bytes(r.Ack));
        }
    }


    [CurrentPermissionPostgresFact]
    public async Task LocationRuleWithdrawal_business_grant_is_independent_and_BLOCKED_requires_new_explicit_fact()
    {
        await using var h=await Seed();var f=await RuleDecisionSetup(h);var service=await GrantRuleWithdrawRelay(h,f);var id=f.Source.Candidate.Reference.Id;
        await EngineeringAction(h,"wms-location-rule","review-delegate");
        var actor=DecisionActor(h,await GrantDelegate(h,"rule-business-delegate"));var owner=RuleReviews(h);var actual=RuleReviews(actor);
        await CreateTaskGrant(h,h.User,actor.User,"binding-flow");var context=await actual.WithdrawalContextAsync(id,f.Submission);
        var command=new ApprovalUserWithdrawV2(Guid.NewGuid(),"Delegate withdraws the exact review");
        Assert.Equal("LOCATION_RULE_BUSINESS_GRANT_DENIED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>actual.WithdrawAsync(id,f.Submission,"\""+context.ControlRowVersion+"\"",command,behalf:h.User))).Message);
        var input=RuleGrantInput(f.Submission,actor.User);var grant=(await owner.GrantBusinessAsync(id,input)).Grant;
        var withdrawn=await actual.WithdrawAsync(id,f.Submission,"\""+context.ControlRowVersion+"\"",command,behalf:h.User);
        Assert.Equal(h.User,withdrawn.Fact!.Actor.RequesterId);Assert.Equal(actor.User,withdrawn.Fact.Actor.ActualHandlerId);
        var raw=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_location_rule_review_control_v1.withdrawals WHERE command_id={withdrawn.TransportCommandId}").SingleAsync();
        var revoked=(await owner.GrantBusinessAsync(id,input with{OperationId=Guid.NewGuid(),GrantId=grant.GrantId,ExpectedVersion=grant.Version,Enabled=false})).Grant;
        var workflow=RuleWithdrawals(h,service);var current=await workflow.ContextAsync(f.Submission);
        Assert.Equal("BLOCKED",(await workflow.WithdrawAsync(f.Submission,"\""+current.SubmissionRowVersion+"\"",raw)).Ack.State);
        var renewed=(await owner.GrantBusinessAsync(id,input with{OperationId=Guid.NewGuid(),GrantId=grant.GrantId,ExpectedVersion=revoked.Version})).Grant;
        Assert.Equal("BLOCKED",(await workflow.WithdrawAsync(f.Submission,"\""+current.SubmissionRowVersion+"\"",raw)).Ack.State);
        context=await actual.WithdrawalContextAsync(id,f.Submission);
        var next=await actual.ReauthorizeWithdrawalAsync(id,f.Submission,withdrawn.TransportCommandId!.Value,"\""+context.ControlRowVersion+"\"",
            new(Guid.NewGuid(),"Explicit new authorization"),behalf:h.User);
        Assert.Equal(renewed.Revision,next.Fact!.Actor.GrantRevision);Assert.Equal(withdrawn.TransportCommandId,next.PreviousCommandId);
        Assert.Equal(withdrawn.Fact.BusinessAfterRowVersion,next.Fact.BusinessBeforeRowVersion);
        var obsolete=await workflow.WithdrawAsync(f.Submission,"\""+current.SubmissionRowVersion+"\"",raw);
        Assert.Equal("BLOCKED",obsolete.Ack.State);Assert.Equal("LOCATION_RULE_WITHDRAWAL_SUPERSEDED",obsolete.Ack.ErrorCode);
        var transport=new NativeRuleWithdrawalTransport(h,workflow,service.Identity.Principal){LoseFirstResponse=false};
        var delivery=new LocationRuleWithdrawalDeliveryV1(h.Db,ScopeFence(h),transport);
        Assert.Equal("WITHDRAWN",(await delivery.RunAsync(h.Tenant)).State);
        var nextRaw=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_location_rule_review_control_v1.withdrawals WHERE command_id={next.TransportCommandId}").SingleAsync();
        Assert.Equal(nextRaw,Assert.Single(transport.Sent));Assert.Equal("IDLE_OR_DISABLED",(await delivery.RunAsync(h.Tenant)).State);
        Assert.Equal(raw,await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_location_rule_review_control_v1.withdrawals WHERE command_id={withdrawn.TransportCommandId}").SingleAsync());
        Assert.Equal(next,(await actual.WithdrawalOperationAsync(next.OperationId,false)).Result);
        Assert.Equal("BLOCKED",(await workflow.OriginalAsync(withdrawn.TransportCommandId.Value)).Ack.State);
    }

    [CurrentPermissionPostgresFact]
    public async Task LocationRuleWithdrawal_returned_workflow_412_and_lost_response_keep_original_body_actor_and_command()
    {
        await using var h=await Seed();var f=await RuleDecisionSetup(h);var service=await GrantRuleWithdrawRelay(h,f);var id=f.Source.Candidate.Reference.Id;
        await ReturnApprovalToStarter(h,RuleDecisions(DecisionActor(h,f.Source.First)),f.Source.First);
        var owner=RuleReviews(h);var c=await owner.WithdrawalContextAsync(id,f.Submission);
        var result=await owner.WithdrawAsync(id,f.Submission,"\""+c.ControlRowVersion+"\"",new(Guid.NewGuid(),"Withdraw returned rule review"));
        var raw=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_location_rule_review_control_v1.withdraw_outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        var workflow=RuleWithdrawals(h,service);var context=await workflow.ContextAsync(f.Submission);
        CheckReturnedWithdrawalContext(context,raw,LocationRuleWithdrawalDeliveryV1.Context);
        var transport=new NativeRuleWithdrawalTransport(h,workflow,service.Identity.Principal){StaleFirstContext=true};
        var delivery=new LocationRuleWithdrawalDeliveryV1(h.Db,ScopeFence(h),transport);
        Assert.Equal("OUTCOME_UNKNOWN",(await delivery.RunAsync(h.Tenant)).State);
        async Task Due()=>await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_location_rule_withdrawal_delivery_v1.delivery SET lease_until=clock_timestamp()-interval '1 second',next_attempt=clock_timestamp()-interval '1 second' WHERE tenant_id={h.Tenant}");
        await Due();Assert.Equal("OUTCOME_UNKNOWN",(await delivery.RunAsync(h.Tenant)).State);
        await Due();Assert.Equal("WITHDRAWN",(await delivery.RunAsync(h.Tenant)).State);
        Assert.Equal(2,transport.Posts);Assert.All(transport.Sent,x=>Assert.Equal(raw,x));
        Assert.Equal("WITHDRAWN",(await workflow.OriginalAsync(result.TransportCommandId!.Value)).Ack.State);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_location_rule_review_control_v1.withdrawals WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task LocationRuleWithdrawal_pending_intake_can_freeze_actual_business_withdrawal_then_relay_canonical_original(bool explicitReauthorization)
    {
        await using var h=await Seed();var f=await RuleDecisionSetup(h,false);await EngineeringAction(h,"wms-location-rule","review-withdraw");
        var owner=RuleReviews(h);var c=await owner.WithdrawalContextAsync(f.Source.Candidate.Reference.Id,f.Submission);
        var result=await owner.WithdrawAsync(f.Source.Candidate.Reference.Id,f.Submission,"\""+c.ControlRowVersion+"\"",new(Guid.NewGuid(),"Withdraw while original submission delivery is pending"));
        Assert.Equal("WITHDRAWN",result.State);
        Assert.False(await h.Db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM cp6_approval_intake_v2.submissions WHERE tenant_id={h.Tenant}) AS \"Value\"").SingleAsync());
        var principal=await h.Db.Database.SqlQuery<Guid>($"SELECT service_principal AS \"Value\" FROM cp6_approval_binding_v2.registrations WHERE tenant_id={h.Tenant} AND owner='Main.Wms.LocationRuleProposal.v1'").SingleAsync();
        var service=new IntakeAuthority(new(ScopeEnvironment,h.Tenant,principal,"fixture-service","CP6.Services","test-registration",true,"DEMO"));
        var intake=new ApprovalIntakeOwnerV2(h.Db,ScopeFence(h),service,ToolApproval(h),Authoring(h),new(ToolApproval(h),rules:owner)){ReceiveClock=()=>BindingTime().AddSeconds(1)};
        var submission=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_location_rule_review_v1.outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        await intake.SubmitAsync(submission);await GrantRuleWithdrawRelay(h,f);
        var workflow=RuleWithdrawals(h,service);var current=await workflow.ContextAsync(f.Submission);
        var bytes=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_location_rule_review_control_v1.withdraw_outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        var blocked=await workflow.WithdrawAsync(f.Submission,"\""+current.SubmissionRowVersion+"\"",bytes);
        Assert.Equal("BLOCKED",blocked.Ack.State);Assert.Equal("WORKFLOW_NOT_STARTED",blocked.Ack.ErrorCode);
        LocationRuleWithdrawalResultV1? nextAuthorization=null;
        if(explicitReauthorization)
        {
            c=await owner.WithdrawalContextAsync(f.Source.Candidate.Reference.Id,f.Submission);
            nextAuthorization=await owner.ReauthorizeWithdrawalAsync(f.Source.Candidate.Reference.Id,f.Submission,result.TransportCommandId!.Value,"\""+c.ControlRowVersion+"\"",
                new(Guid.NewGuid(),"Explicit new fact while original is BLOCKED"));
        }
        await Starter(h).RunAsync(h.Tenant);current=await workflow.ContextAsync(f.Submission);
        if(nextAuthorization!=null)
        {
            var obsolete=await workflow.WithdrawAsync(f.Submission,"\""+current.SubmissionRowVersion+"\"",bytes);
            Assert.Equal("BLOCKED",obsolete.Ack.State);Assert.Equal("LOCATION_RULE_WITHDRAWAL_SUPERSEDED",obsolete.Ack.ErrorCode);
            Assert.True(await h.Db.Wf_FlowTasks.AsNoTracking().AnyAsync(t=>t.Status==0));
            bytes=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_location_rule_review_control_v1.withdraw_outbox WHERE command_id={nextAuthorization.TransportCommandId}").SingleAsync();
        }
        Assert.Equal("WITHDRAWN",(await workflow.WithdrawAsync(f.Submission,"\""+current.SubmissionRowVersion+"\"",bytes)).Ack.State);
        Assert.False(await h.Db.Wf_FlowTasks.AsNoTracking().AnyAsync(t=>t.Status==0));
        Assert.Equal(result,(await owner.WithdrawalOperationAsync(result.OperationId,false)).Result);
        var ready=await owner.ContextAsync(f.Source.Candidate.Reference.Id);Assert.Equal("WITHDRAWN",ready.Submissions.Single().State);
    }
}
