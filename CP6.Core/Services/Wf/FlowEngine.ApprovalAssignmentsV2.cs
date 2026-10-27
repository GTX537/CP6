using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

public partial class FlowEngine
{
    private ApprovalSubmitCommandV2? _approvalOriginal;
    private readonly List<ApprovalTaskAssignmentV2> _approvalAssignments = [];

    internal void RecordApprovalAssignment(Wf_FlowInstance instance, Wf_FlowTask task,
        ApproverRule rule, IEnumerable<Guid> candidates, string? stageCode = null)
    {
        if (!_approvalStartV2) return;
        var original = _approvalOriginal ?? throw new ScopeAuthorizationExceptionV1("APP_TASK_EVIDENCE_UNAVAILABLE");
        if (task.TokenId == null || instance.FlowDefVersionId == null || task.AssigneeId == Guid.Empty)
            throw new ScopeAuthorizationExceptionV1("APP_TASK_EVIDENCE_UNAVAILABLE");
        // Freeze the complete resolver result and rule now, not by reconstructing mutable IAM later.
        var fact = new ApprovalTaskAssignmentV2("oa.app.assignment/2.0", _db.CurrentTenantId,
            Guid.Parse(original.Scope.EnvironmentId), original.SubmissionId, instance.Id, task.Id, task.TokenId.Value,
            instance.FlowDefVersionId.Value, task.NodeId, task.StageIndex, task.StageRound, stageCode,
            task.Countersign, task.AssigneeId, candidates.Distinct().ToArray(), rule,
            DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        _approvalAssignments.Add(System.Text.Json.JsonSerializer.Deserialize<ApprovalTaskAssignmentV2>(
            ApprovalCanonicalV2.Bytes(fact), ApprovalBindingOwnerV2.Json)!);
    }

    internal Task SaveApprovalAssignments(CancellationToken ct)
        => ApprovalTaskAssignmentsV2.Store(_db, _approvalAssignments, ct);
}
