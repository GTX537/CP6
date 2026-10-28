using CP6.Core.Services.Common;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Plm;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [Fact]
    public void IterationApplication_issue_version_preserves_full_Int64_at_the_JSON_boundary()
    {
        var command=new IterationReviewIssueCommandV1(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"NOTE",long.MaxValue,"Exact historical note",null,null,null,[],"Preserve exact revision");
        var bytes=ApprovalCanonicalV2.Bytes(command);using var json=System.Text.Json.JsonDocument.Parse(bytes);
        Assert.Equal("9223372036854775807",json.RootElement.GetProperty("expectedRevision").GetString());
        Assert.Equal(long.MaxValue,System.Text.Json.JsonSerializer.Deserialize<IterationReviewIssueCommandV1>(bytes,ApprovalBindingOwnerV2.Json)!.ExpectedRevision);
    }
    [CurrentPermissionPostgresFact]
    public async Task IterationApplication_frozen_RETURN_and_REJECT_map_native_stages_without_implicit_closure()
    {
        foreach(var disposition in new[]{"RETURN","REJECT"})
        {
            await using var h=await Seed();var f=await IterationDecisionSetup(h,disposition);var id=f.Source.Context.IterationId;
            await IterationVote(h,f,"reject");var receiver=await IterationReceiver(h,f);FactAck(await receiver.Receiver.ReceiveAsync(receiver.Payload));
            var o=IterationReviews(h,f.Source.Files);var c=await o.ApplicationAsync(id,f.Submission);
            var result=await o.ApplyAsync(id,c.Etag,new(Guid.NewGuid(),f.Submission,null,"Apply exact frozen reject mapping"));
            Assert.Equal("APPLIED",result.Receipt.Application);var iteration=(await Iterations(h,f.Source.Files).ReadAsync(id)).Body.Iteration!;
            Assert.Equal(disposition=="RETURN"?"IN_DESIGN":"AWAITING_EVALUATION",iteration.Stage);
            if(disposition=="RETURN")Assert.Null(iteration.CurrentReview);else Assert.Equal(f.Submission,iteration.CurrentReview!.SubmissionId);
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task IterationApplication_and_withdraw_missing_outbox_roll_back_then_two_connections_compete_for_one_control_transition()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await IterationDecisionSetup(h);var id=f.Source.Context.IterationId;
        await IterationVote(h,f,"reject");var receiver=await IterationReceiver(h,f);FactAck(await receiver.Receiver.ReceiveAsync(receiver.Payload));
        var o=IterationReviews(h,f.Source.Files);var c=await o.ApplicationAsync(id,f.Submission);var original=await Iterations(h,f.Source.Files).ReadAsync(id);
        var application=new IterationApplicationCommandV1(Guid.NewGuid(),f.Submission,null,"Atomic application with original receipt");
        hook.Before=cmd=>{if(cmd.CommandText.Contains("INSERT INTO cp6_iteration_review_application_v1.outbox"))cmd.CommandText="SELECT 1";return Task.CompletedTask;};
        Assert.NotNull(await Record.ExceptionAsync(()=>o.ApplyAsync(id,c.Etag,application)));hook.Before=null;
        Assert.Equal("UNKNOWN",(await o.ApplicationOperationAsync(application.OperationId,false)).State);Assert.Equal(original.OriginalJson,(await Iterations(h,f.Source.Files).ReadAsync(id)).OriginalJson);
        var withdrawal=new ApprovalUserWithdrawV2(Guid.NewGuid(),"Atomic withdrawal with original fact");
        hook.Before=cmd=>{if(cmd.CommandText.Contains("INSERT INTO cp6_iteration_review_control_v1.withdraw_outbox"))cmd.CommandText="SELECT 1";return Task.CompletedTask;};
        Assert.NotNull(await Record.ExceptionAsync(()=>o.WithdrawAsync(id,f.Submission,"\""+c.ControlRowVersion+"\"",withdrawal)));hook.Before=null;
        Assert.Equal("UNKNOWN",(await o.WithdrawalOperationAsync(withdrawal.CommandId,false)).State);Assert.Equal(original.OriginalJson,(await Iterations(h,f.Source.Files).ReadAsync(id)).OriginalJson);
        await using var other=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var peer=IterationReviews(h with{Db=other},f.Source.Files);
        IterationApplicationResultV1? applied=null;IterationWithdrawalResultV1? withdrawn=null;
        var errors=await Task.WhenAll(Record.ExceptionAsync(async()=>{applied=await o.ApplyAsync(id,c.Etag,application);}),
            Record.ExceptionAsync(async()=>{withdrawn=await peer.WithdrawAsync(id,f.Submission,"\""+c.ControlRowVersion+"\"",withdrawal);}));
        Assert.True(applied?.Receipt.Application=="APPLIED" ^ withdrawn?.State=="WITHDRAWN",string.Join("\n",errors.Select(x=>x?.ToString())));
        var after=await o.ApplicationAsync(id,f.Submission);Assert.Equal("IN_DESIGN",after.BusinessStage);Assert.NotEqual(c.ControlRowVersion,after.ControlRowVersion);
        if(applied!=null)Assert.Equal("CONFLICT",withdrawn!.State);
        else
        {
            Assert.NotNull(errors[0]);Assert.Equal("STALE",(await o.ApplyAsync(id,after.Etag,application)).Receipt.Application);
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task IterationWithdrawal_native_workflow_missing_outbox_rolls_back_tasks_decision_and_original_command()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await IterationDecisionSetup(h);await GrantIterationWithdrawRelay(h,f);
        var owner=IterationReviews(h,f.Source.Files);var id=f.Source.Context.IterationId;var c=await owner.ApplicationAsync(id,f.Submission);
        var result=await owner.WithdrawAsync(id,f.Submission,"\""+c.ControlRowVersion+"\"",new(Guid.NewGuid(),"Withdraw current native round"));
        var bytes=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_iteration_review_control_v1.withdraw_outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        var workflow=IterationWithdrawals(h,f);var before=await workflow.ContextAsync(f.Submission);
        hook.Before=cmd=>{if(cmd.CommandText.Contains("INSERT INTO cp6_approval_decision_v2.outbox"))cmd.CommandText="SELECT 1";return Task.CompletedTask;};
        Assert.NotNull(await Record.ExceptionAsync(()=>workflow.WithdrawAsync(f.Submission,"\""+before.SubmissionRowVersion+"\"",bytes)));hook.Before=null;
        Assert.Single(await h.Db.Wf_FlowTasks.Where(x=>x.Status==0).ToArrayAsync());
        Assert.Empty(await h.Db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM cp6_approval_withdraw_v2.commands WHERE tenant_id={h.Tenant}").ToArrayAsync());
        Assert.Empty(await h.Db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM cp6_approval_decision_v2.decisions WHERE tenant_id={h.Tenant} AND submission_id={f.Submission}").ToArrayAsync());
        Assert.Equal(before.SubmissionRowVersion,(await workflow.ContextAsync(f.Submission)).SubmissionRowVersion);
        Assert.Equal("WITHDRAWN",(await workflow.WithdrawAsync(f.Submission,"\""+before.SubmissionRowVersion+"\"",bytes)).Ack.State);
        Assert.Equal("WITHDRAWN",(await workflow.OriginalAsync(result.TransportCommandId!.Value)).Ack.State);
    }
}
