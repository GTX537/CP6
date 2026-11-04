using CP6.Core.Services.Oa;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private async Task ReturnApprovalToStarter(Harness h, ApprovalTaskDecisionOwnerV2 tasks, Guid reviewer)
    {
        await NativeOperationPermissions(h);
        var task = await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(x => x.AssigneeId == reviewer && x.Status == 0);
        var preview = await tasks.SendBackPreviewAsync(task.Id, new("STARTER", null));
        await tasks.SendBackTaskAsync(task.Id, SendBackCommand(preview, "withdraw-returned"));
        Assert.Equal(FlowInstanceStatus.Draft, (await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync(x => x.Id == preview.InstanceId)).Status);
    }

    private static void CheckReturnedWithdrawalContext(ApprovalWithdrawContextV2 context, byte[] original,
        Func<byte[], ApprovalWithdrawCommandV2, string> consume)
    {
        Assert.Equal("RETURNED", context.WorkflowState);
        Assert.True(context.CanRelayWithdraw);
        var envelope = ApprovalWithdrawalWireV2.Parse(original).Value;
        var error = Assert.Throws<ScopeAuthorizationExceptionV1>(() => consume(
            ApprovalWithdrawalResponseV2.Bytes(context with { WorkflowState = "UNRECOGNIZED" }), envelope));
        Assert.Equal("DELIVERY_CONTEXT_INVALID", error.Message);
        Assert.Equal(context.SubmissionRowVersion, consume(ApprovalWithdrawalResponseV2.Bytes(context), envelope));
    }

    [CurrentPermissionPostgresFact]
    public async Task ReturnedWithdrawal_Document_real_sendback_delivers_original_and_recovers_lost_response()
    {
        await using var h = await Seed(); var f = await DocumentDecisionSetup(h);
        var service = await GrantDocumentWithdrawRelay(h, f);
        await ReturnApprovalToStarter(h, DocumentDecisions(DecisionActor(h, f.First), f.Files), f.First);
        var owner = DocumentReviews(h, f.Files); var before = await owner.WithdrawalContextAsync(f.Document, f.Submission);
        var command = new ApprovalUserWithdrawV2(Guid.NewGuid(), "Withdraw returned document review");
        var result = await owner.WithdrawAsync(f.Document, f.Submission, "\"" + before.ControlRowVersion + "\"", command);
        var original = await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_document_review_control_v1.withdraw_outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        var workflow = DocumentWithdrawals(h, f, service); var context = await workflow.ContextAsync(f.Submission);
        CheckReturnedWithdrawalContext(context, original, DocumentWithdrawalDeliveryV1.Context);
        var transport = new NativeDocumentWithdrawalTransport(h, workflow, service.Identity.Principal);
        var delivery = new DocumentWithdrawalDeliveryV1(h.Db, ScopeFence(h), transport);
        Assert.Equal("OUTCOME_UNKNOWN", (await delivery.RunAsync(h.Tenant)).State);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_document_withdrawal_delivery_v1.delivery SET lease_until=clock_timestamp()-interval '1 second',next_attempt=clock_timestamp()-interval '1 second' WHERE tenant_id={h.Tenant}");
        Assert.Equal("WITHDRAWN", (await delivery.RunAsync(h.Tenant)).State);
        Assert.Equal(1, transport.Posts); Assert.Equal(original, Assert.Single(transport.Sent));
        Assert.Equal("WITHDRAWN", (await workflow.OriginalAsync(result.TransportCommandId!.Value)).Ack.State);
        Assert.Equal("IDLE_OR_DISABLED", (await delivery.RunAsync(h.Tenant)).State);
        Assert.Equal(ApprovalCanonicalV2.Bytes(result), ApprovalCanonicalV2.Bytes(await owner.WithdrawAsync(f.Document, f.Submission, "\"" + before.ControlRowVersion + "\"", command)));
        Assert.Equal(original, await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_document_review_control_v1.withdraw_outbox WHERE tenant_id={h.Tenant}").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task ReturnedWithdrawal_RequirementSet_real_sendback_delivers_original_and_recovers_lost_response()
    {
        await using var h = await Seed(); var f = await SetDecisionSetup(h);
        var service = await GrantSetWithdrawRelay(h, f); var id = f.Source.Set.Reference.Id;
        await ReturnApprovalToStarter(h, SetDecisions(DecisionActor(h, f.Source.Member.First)), f.Source.Member.First);
        var owner = SetReviews(h); var before = await owner.WithdrawalContextAsync(id, f.Submission);
        var command = new ApprovalUserWithdrawV2(Guid.NewGuid(), "Withdraw returned requirement-set review");
        var result = await owner.WithdrawAsync(id, f.Submission, "\"" + before.ControlRowVersion + "\"", command);
        var original = await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_requirement_set_review_control_v1.withdraw_outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        var workflow = SetWithdrawals(h, f, service); var context = await workflow.ContextAsync(f.Submission);
        CheckReturnedWithdrawalContext(context, original, RequirementSetWithdrawalDeliveryV1.Context);
        var transport = new NativeSetWithdrawalTransport(h, workflow, service.Identity.Principal);
        var delivery = new RequirementSetWithdrawalDeliveryV1(h.Db, ScopeFence(h), transport);
        Assert.Equal("OUTCOME_UNKNOWN", (await delivery.RunAsync(h.Tenant)).State);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_requirement_set_withdrawal_delivery_v1.delivery SET lease_until=clock_timestamp()-interval '1 second',next_attempt=clock_timestamp()-interval '1 second' WHERE tenant_id={h.Tenant}");
        Assert.Equal("WITHDRAWN", (await delivery.RunAsync(h.Tenant)).State);
        Assert.Equal(1, transport.Posts); Assert.Equal(original, Assert.Single(transport.Sent));
        Assert.Equal("WITHDRAWN", (await workflow.OriginalAsync(result.TransportCommandId!.Value)).Ack.State);
        Assert.Equal("IDLE_OR_DISABLED", (await delivery.RunAsync(h.Tenant)).State);
        Assert.Equal(ApprovalCanonicalV2.Bytes(result), ApprovalCanonicalV2.Bytes(await owner.WithdrawAsync(id, f.Submission, "\"" + before.ControlRowVersion + "\"", command)));
        Assert.Equal(original, await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_requirement_set_review_control_v1.withdraw_outbox WHERE tenant_id={h.Tenant}").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task ReturnedWithdrawal_Iteration_real_sendback_delivers_original_and_recovers_lost_response()
    {
        await using var h = await Seed(); var f = await IterationDecisionSetup(h); await GrantIterationWithdrawRelay(h, f);
        var reviewer = DecisionActor(h, f.Source.Iteration.Reviewer);
        var tasks = new ApprovalTaskDecisionOwnerV2(h.Db, ScopeFence(h), TaskAuthority(reviewer), ToolApproval(h),
            new(ToolApproval(h), null, null, IterationReviews(reviewer, f.Source.Files)));
        await ReturnApprovalToStarter(h, tasks, reviewer.User);
        var owner = IterationReviews(h, f.Source.Files); var id = f.Source.Context.IterationId;
        var before = await owner.ApplicationAsync(id, f.Submission);
        var command = new ApprovalUserWithdrawV2(Guid.NewGuid(), "Withdraw returned iteration review");
        var result = await owner.WithdrawAsync(id, f.Submission, "\"" + before.ControlRowVersion + "\"", command);
        var original = await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_iteration_review_control_v1.withdraw_outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        var workflow = IterationWithdrawals(h, f); var context = await workflow.ContextAsync(f.Submission);
        CheckReturnedWithdrawalContext(context, original, IterationWithdrawalDeliveryV1.Context);
        var transport = new NativeIterationWithdrawalTransport(h, workflow, f.Source.Principal);
        var delivery = new IterationWithdrawalDeliveryV1(h.Db, ScopeFence(h), transport);
        Assert.Equal("OUTCOME_UNKNOWN", (await delivery.RunAsync(h.Tenant)).State);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_iteration_withdrawal_delivery_v1.delivery SET lease_until=clock_timestamp()-interval '1 second',next_attempt=clock_timestamp()-interval '1 second' WHERE tenant_id={h.Tenant}");
        Assert.Equal("WITHDRAWN", (await delivery.RunAsync(h.Tenant)).State); Assert.Equal(1, transport.Posts);
        Assert.Equal("WITHDRAWN", (await workflow.OriginalAsync(result.TransportCommandId!.Value)).Ack.State);
        Assert.Equal("IDLE_OR_DISABLED", (await delivery.RunAsync(h.Tenant)).State);
        Assert.Equal(ApprovalCanonicalV2.Bytes(result), ApprovalCanonicalV2.Bytes(await owner.WithdrawAsync(id, f.Submission, "\"" + before.ControlRowVersion + "\"", command)));
        Assert.Equal(original, await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_iteration_review_control_v1.withdraw_outbox WHERE tenant_id={h.Tenant}").SingleAsync());
    }
}
