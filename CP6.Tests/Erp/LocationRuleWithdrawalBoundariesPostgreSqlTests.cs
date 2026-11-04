using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private async Task<RuleDecisionFixture> RuleWithdrawalApplicationReady(Harness h)
    {
        var f=await RuleDecisionSetup(h);await EngineeringAction(h,"wms-location-rule","review-withdraw");
        await RuleVote(DecisionActor(h,f.Source.First));await RuleVote(DecisionActor(h,f.Source.Second));
        var receiver=await RuleReceiver(h,f);FactAck(await receiver.Receiver.ReceiveAsync(receiver.Payload));return f;
    }

    [CurrentPermissionPostgresFact]
    public async Task LocationRuleWithdrawal_atomic_failure_restores_native_proposal_and_two_connections_choose_one_winner()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await RuleWithdrawalApplicationReady(h);var id=f.Source.Candidate.Reference.Id;
        var owner=RuleReviews(h);var detail=await owner.SubmissionAsync(id,f.Submission);var c=await owner.WithdrawalContextAsync(id,f.Submission);
        var native=await ReferenceRules(h).ReadAsync(f.Source.Location,id);
        var apply=new LocationRuleApplicationCommandV1(Guid.NewGuid(),f.Submission,null,"Apply exact decision");
        var command=new ApprovalUserWithdrawV2(Guid.NewGuid(),"Withdraw competing exact review");
        hook.Before=cmd=>{if(cmd.CommandText.Contains("INSERT INTO cp6_location_rule_review_control_v1.withdraw_outbox"))cmd.CommandText="SELECT 1";return Task.CompletedTask;};
        Assert.NotNull(await Record.ExceptionAsync(()=>owner.WithdrawAsync(id,f.Submission,"\""+c.ControlRowVersion+"\"",command)));hook.Before=null;
        Assert.Equal("UNKNOWN",(await owner.WithdrawalOperationAsync(command.CommandId,false)).State);
        Assert.Equal(c.ControlRowVersion,(await owner.WithdrawalContextAsync(id,f.Submission)).ControlRowVersion);
        Assert.Equal(native.Etag,(await ReferenceRules(h).ReadAsync(f.Source.Location,id)).Etag);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_main_owner_v1.operations WHERE tenant_id={h.Tenant} AND operation_id={command.CommandId}").SingleAsync());
        await using var other=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var peer=RuleReviews(h with{Db=other});
        LocationRuleApplicationResultV1? applied=null;LocationRuleWithdrawalResultV1? withdrawn=null;
        var errors=await Task.WhenAll(Record.ExceptionAsync(async()=>{applied=await owner.ApplyAsync(id,detail.Application.Etag,apply);}),
            Record.ExceptionAsync(async()=>{withdrawn=await peer.WithdrawAsync(id,f.Submission,"\""+c.ControlRowVersion+"\"",command);}));
        Assert.True(applied?.Receipt.Application=="APPLIED" ^ withdrawn?.State=="WITHDRAWN",string.Join("\n",errors.Select(x=>x?.ToString())));
        if(applied!=null)
        {
            Assert.Equal("CONFLICT",withdrawn!.State);Assert.Null(withdrawn.Fact);Assert.Equal(ApprovalCanonicalV2.Bytes(applied.Receipt),ApprovalCanonicalV2.Bytes(withdrawn.AppliedReceipt!));
            Assert.Equal("APPROVED_PENDING",(await ReferenceRules(h).ReadAsync(f.Source.Location,id)).State);
        }
        else
        {
            Assert.NotNull(errors[0]);detail=await owner.SubmissionAsync(id,f.Submission);
            Assert.Equal("STALE",(await owner.ApplyAsync(id,detail.Application.Etag,apply)).Receipt.Application);
        }
        Assert.NotEqual(c.ControlRowVersion,(await owner.WithdrawalContextAsync(id,f.Submission)).ControlRowVersion);
        Assert.Equal(native.OriginalJson,(await ReferenceRules(h).ReadAsync(f.Source.Location,id)).OriginalJson);
    }

    [CurrentPermissionPostgresFact]
    public async Task LocationRuleWithdrawal_legacy_proposal_withdrawal_needs_new_explicit_fact_and_keeps_both_originals()
    {
        await using var h=await Seed();var f=await RuleDecisionSetup(h);var id=f.Source.Candidate.Reference.Id;
        await EngineeringAction(h,"wms-location-rule","review-withdraw");var native=ReferenceRules(h);var before=await native.ReadAsync(f.Source.Location,id);
        var oldCommand=new LocationRuleWithdrawV1(Guid.NewGuid(),"Original proposal withdrawal");
        var old=await native.WithdrawAsync(f.Source.Location,id,before.Etag,oldCommand);
        var owner=RuleReviews(h);var context=await owner.WithdrawalContextAsync(id,f.Submission);
        Assert.Equal("WITHDRAWN",context.State);Assert.True(context.CanWithdraw);Assert.Empty(context.Authorizations);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_location_rule_review_control_v1.withdrawals WHERE tenant_id={h.Tenant}").SingleAsync());
        var command=new ApprovalUserWithdrawV2(Guid.NewGuid(),"Explicit current review withdrawal after proposal withdrawal");
        var result=await owner.WithdrawAsync(id,f.Submission,"\""+context.ControlRowVersion+"\"",command);
        Assert.Equal(context.ControlRowVersion,result.Fact!.BusinessBeforeRowVersion);Assert.NotEqual(context.ControlRowVersion,result.Fact.BusinessAfterRowVersion);
        Assert.Equal(h.User,result.Fact.Actor.ActualHandlerId);Assert.NotEqual(old.Etag,(await native.ReadAsync(f.Source.Location,id)).Etag);
        Assert.Equal(old,await native.WithdrawAsync(f.Source.Location,id,before.Etag,oldCommand));
        Assert.Equal(result,(await owner.WithdrawalOperationAsync(command.CommandId,false)).Result);
        Assert.False((await owner.WithdrawalContextAsync(id,f.Submission)).CanWithdraw);
        Assert.Equal("LOCATION_RULE_REVIEW_ROUND_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.WithdrawAsync(id,f.Submission,"\""+result.Fact.BusinessAfterRowVersion+"\"",new(Guid.NewGuid(),"Cannot silently replace prior fact")))).Message);
        Assert.Equal(before.OriginalJson,(await native.ReadAsync(f.Source.Location,id)).OriginalJson);
    }

    [CurrentPermissionPostgresFact]
    public async Task LocationRuleWithdrawal_after_applied_returns_original_approval_without_changing_proposal_or_receipt()
    {
        await using var h=await Seed();var f=await RuleWithdrawalApplicationReady(h);var id=f.Source.Candidate.Reference.Id;var owner=RuleReviews(h);
        var before=await owner.WithdrawalContextAsync(id,f.Submission);var detail=await owner.SubmissionAsync(id,f.Submission);
        var applied=await owner.ApplyAsync(id,detail.Application.Etag,new(Guid.NewGuid(),f.Submission,null,"Apply before withdrawal"));
        var native=await ReferenceRules(h).ReadAsync(f.Source.Location,id);
        var result=await owner.WithdrawAsync(id,f.Submission,"\""+before.ControlRowVersion+"\"",new(Guid.NewGuid(),"Observe original applied outcome"));
        Assert.Equal("CONFLICT",result.State);Assert.Equal(ApprovalCanonicalV2.Bytes(applied.Receipt),ApprovalCanonicalV2.Bytes(result.AppliedReceipt!));Assert.Null(result.Fact);
        Assert.Equal(native.Etag,(await ReferenceRules(h).ReadAsync(f.Source.Location,id)).Etag);
        Assert.Equal("APPROVED_PENDING",native.State);Assert.False(native.AdmissionGrant);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_location_rule_review_control_v1.withdrawals WHERE tenant_id={h.Tenant}").SingleAsync());
    }
}
