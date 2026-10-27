using System.Text.Json;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Oa;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.ToolInstanceStoreV1;

namespace CP6.Core.Services.Wms;

public sealed partial class ToolDispositionApprovalOwnerV1
{
    internal async Task<ToolDispositionPolicyCommandV1> ApplicationPolicy(OperationsAccessV1 access, Guid tool,
        ApprovalSubmitCommandV2 original, CancellationToken ct)
    {
        var c = await Prepare(access, tool, false, ct);
        var frozen = c.Policies.SingleOrDefault(x => x.Reference.Id.ToString() == original.Policy.Ref.Id
            && x.Reference.Owner == original.Policy.Ref.Owner && x.Reference.Version == original.Policy.Ref.Version
            && x.Reference.Digest == original.Policy.Digest.Value) ?? throw Error("POLICY_UNRESOLVED", 422);
        return await RequirePolicy(c, frozen.Reference, ct);
    }
    internal sealed record DecisionPolicyProof(ToolDispositionPolicyV1 FrozenPolicy, string ReadAuthority);
    private sealed record DecisionSource(Guid Tool, Guid Policy, byte[] Payload);

    // A read/qualification participant in Workflow's already-open local transaction. It
    // neither approves disposal nor rewrites the current Owner policy/control head.
    internal async Task<DecisionPolicyProof> ReadDecisionPolicy(ApprovalSubmitCommandV2 original, CancellationToken ct)
    {
        await scope.Root(ct);
        if (original.Subject.BusinessOwner != ToolDispositionStoreV1.DispositionOwner
            || original.Subject.SubjectKind != "TOOL_DISPOSITION_REQUEST" || original.Subject.BizType != "WMS_TOOL_DISCARD")
            throw Error("DECISION_POLICY_OWNER_UNAVAILABLE", 422);
        if (!Guid.TryParseExact(original.SubmitAuthorizationRef.Id, "D", out var frozen)) throw Error("MISSING_EVIDENCE", 422);
        var source = await db.Database.SqlQuery<DecisionSource>($"""
            SELECT tool_id AS "Tool",policy_id AS "Policy",payload AS "Payload" FROM cp6_tool_approval_v1.submissions
            WHERE tenant_id={db.CurrentTenantId} AND environment_id={Environment} AND id={frozen}
            """).SingleOrDefaultAsync(ct) ?? throw Error("MISSING_EVIDENCE", 422);
        var expected = ApprovalSubmissionWireV2.Parse(source.Payload);
        var incoming = ApprovalSubmissionWireV2.Parse(ApprovalCanonicalV2.Bytes(original));
        if (expected.IntentCore != incoming.IntentCore) throw Error("OWNER_EVIDENCE_MISMATCH", 422);
        var authority = await Authority.AuthorityAsync(OperationsOwnerV1.ToolInstance, "query", ct);
        var access = new OperationsAccessV1(db, owner, authority);
        await access.RequireToolInstance(db, source.Tool, ct);
        // The decision concerns the frozen request's complete reason, never a masked
        // substitute. The Owner's current field gate must allow that disclosure.
        access.RequireToolFieldRead("Remarks");
        var policy = (await new ToolDispositionApprovalStoreV1(db, Environment).Policies(source.Tool, ct))
            .SingleOrDefault(p => p.Reference.Id == source.Policy) ?? throw Error("MISSING_EVIDENCE", 422);
        if (original.Policy.Ref.Owner != policy.Reference.Owner || original.Policy.Ref.Id != policy.Reference.Id.ToString()
            || original.Policy.Ref.Version != policy.Reference.Version || original.Policy.Digest.Value != policy.Reference.Digest
            || policy.Fact.Body.RequiredHumanApprovals < 1)
            throw Error("DECISION_POLICY_MISMATCH", 422);
        var evidence = ApprovalBindingOwnerV2.Serialize(new { profile = "Main.Wms.ToolDisposition.decision-read/1",
            source.Tool, original.Subject, authority.Actor.UserId, authority.Source.SourcePermissionVersion,
            authority.OrganizationVersion, authority.Scope, policy = policy.Reference });
        return new(policy.Fact, evidence);
    }
}
