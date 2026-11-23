using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed partial class ApprovalTaskDecisionOwnerV2
{
    private const string ConnectorResumeService = "Main.Workflow.ConnectorWorker";
    private sealed record ConnectorResumeSource(byte[] Payload, string Ack, Guid Root);
    private sealed record ConnectorResumeEvidence(Guid ExecutionId, Guid JobId, string ServiceRegistration,
        ScopeAuthorizationSnapshotV1 Authority, DateTimeOffset OccurredAt);

    internal bool OwnsConnectorContext(CP6Context context) => ReferenceEquals(db, context);

    // 工作流内核、真实 APP 决策证据和 Outbox 共用调用方的 Tx D。
    internal async Task<Guid?> ApplyConnectorResumeAsync(Wf_FlowInstance instance, Wf_FlowToken token, FlowSchema schema,
        ConnectorOriginalOperationV1 operation, string serviceRegistration, Guid receipt, Dictionary<string, object?> output,
        ScopeAuthorizationLeaseV1 fence, CancellationToken ct)
    {
        await scope.ValidateAsync(fence, ct);
        await Ready(ct);
        var source = await db.Database.SqlQuery<ConnectorResumeSource>($"""
            SELECT p.payload AS "Payload",p.ack_json AS "Ack",s.root_instance_id AS "Root"
            FROM cp6_approval_runtime_v2.instances s
            JOIN cp6_approval_intake_v2.submissions p ON
                (p.tenant_id,p.environment_id,p.owner,p.id)=(s.tenant_id,s.environment_id,s.owner,s.submission_id)
            WHERE s.tenant_id={db.CurrentTenantId} AND s.environment_id={scope.EnvironmentId} AND s.instance_id={instance.Id}
            """).SingleOrDefaultAsync(ct) ?? throw Error("APP_RESUME_SOURCE_UNAVAILABLE");
        var original = ApprovalSubmissionWireV2.Parse(source.Payload).Value;
        var ack = Parse<ApprovalSubmissionAckV2>(source.Ack);
        var occurred = await db.Database.SqlQueryRaw<DateTimeOffset>(
            "SELECT date_trunc('milliseconds',clock_timestamp()) AS \"Value\"").SingleAsync(ct);
        var evidence = new ConnectorResumeEvidence(receipt, operation.Identity.JobId, serviceRegistration, fence.Snapshot, occurred);
        var raw = ApprovalCanonicalV2.Bytes(new { operation.Identity, receipt, operation.EffectKey });
        var command = new PersonalFormOwnerV2.Command<ConnectorDispatchIdentityV1>(receipt.ToString("D"), null,
            operation.Identity, ApprovalCanonicalV2.Hash(raw), raw, null);
        await BeginOperation(receipt, Guid.Empty, "OA_CONNECTOR_RESUME", operation.Identity.JobId, source.Root, null, command, ct);

        var engine = new FlowEngine(db, new ApproverResolver(db, exactConditions: true));
        var root = await engine.StageApprovalConnectorResumeV2(original, source.Root, instance, token, schema, output, ct);
        await engine.SaveApprovalRuntime(ct);
        await db.SaveChangesAsync(ct);
        await engine.SaveApprovalAssignments(ct);
        await engine.SavePendingApproversV2(receipt, "APP_OPERATION", ct);
        foreach (var created in engine.CreatedApprovalAssignments)
            await OperationNotification(receipt, created.PrincipalId, created.InstanceId, created.TaskId, "TASK_ASSIGNED", "连接器结果已生成待办", ct);
        await db.SaveChangesAsync(ct);

        Guid? decision = null;
        if (root.Status is FlowInstanceStatus.Approved or FlowInstanceStatus.Rejected)
        {
            var policy = await Business.Decision(original, null, false, ct);
            decision = await Terminal(original, ack, root, null, policy, ct, evidence);
        }
        await CompleteOperation(receipt, command, operation.Identity.JobId, Convert.ToBase64String(root.RowVersion!),
            new { root.Id, root.Status, DecisionId = decision }, evidence, ct);
        await Ready(ct);
        return decision;
    }
}
