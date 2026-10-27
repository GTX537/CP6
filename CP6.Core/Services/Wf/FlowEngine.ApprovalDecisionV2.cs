using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

public partial class FlowEngine
{
    private bool _approvalDecisionV2;
    private FlowSchema? _approvalDecisionSchema;

    // The Owner has already locked the native Scope, command, instance and task, checked
    // both versions and acquired current human authority. This adapter only stages the
    // existing algorithm; its caller records the vote/decision and owns the single commit.
    internal async Task<Wf_FlowInstance> StageApprovalDecisionV2(FlowPublicationV2 publication,
        ApprovalSubmitCommandV2 original, ApprovalTaskAuthorityEvidenceV2 authority,
        bool approve, string comment, CancellationToken ct)
    {
        if (!_db.Database.IsNpgsql() || _db.Database.CurrentTransaction == null || _db.ChangeTracker.HasChanges())
            throw new ScopeAuthorizationExceptionV1("APP_DECISION_TRANSACTION_REQUIRED");
        _approvalStartV2 = true;
        _approvalDecisionV2 = true;
        _approvalOriginal = original;
        _approvalDecisionSchema = Deserialize(publication.Fields["SchemaJson"]!);
        if (publication.VersionId != authority.Assignment.FlowVersionId
            || original.SubmissionId != authority.Assignment.SubmissionId)
            throw new ScopeAuthorizationExceptionV1("APP_TASK_EVIDENCE_UNAVAILABLE");
        await ActOnceAsync(authority.Assignment.TaskId, authority.ActualHandlerId, approve, comment, authority.OnBehalfOfId);
        ct.ThrowIfCancellationRequested();
        return await _db.Wf_FlowInstances.SingleAsync(x => x.Id == authority.Assignment.InstanceId, ct);
    }
}
