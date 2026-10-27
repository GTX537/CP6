using System.Text;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

/// <summary>Read-only native Workflow Owner participant. The caller must already hold
/// the native Scope transaction and current service/precise-subject authorization.</summary>
public sealed class ApprovalDecisionSourceV2(CP6Context db, ScopeAuthorizationFenceV1 scope)
{
    internal sealed record Proof(ApprovalDecisionWireV2.Parsed Original, string Evidence, string EvidenceHash, string Mode);
    private sealed record Stored(byte[] Payload, string PayloadHash, string Evidence, string EvidenceHash, string Mode);
    internal async Task<Proof> Read(ApprovalDecisionWireV2.Parsed incoming, CancellationToken ct)
    {
        await scope.Root(ct);
        if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_approval_decision_v2.ready() AS \"Value\"").SingleAsync(ct))
            throw new ScopeAuthorizationExceptionV1("DECISION_SOURCE_UNAVAILABLE");
        var e = incoming.Value;
        var row = await db.Database.SqlQuery<Stored>($"""
            SELECT d.payload AS "Payload",d.payload_digest AS "PayloadHash",d.evidence_json AS "Evidence",d.evidence_digest AS "EvidenceHash",
              s.selection_json::jsonb->'registration'->>'evidenceMode' AS "Mode"
            FROM cp6_approval_decision_v2.decisions d JOIN cp6_approval_intake_v2.submissions s
              ON s.tenant_id=d.tenant_id AND s.environment_id=d.environment_id AND s.id=d.submission_id
            WHERE d.tenant_id={db.CurrentTenantId} AND d.environment_id={scope.EnvironmentId}
              AND d.id={e.DecisionId} AND d.instance_id={e.InstanceId} AND d.submission_id={e.SubmissionId}
            """).SingleOrDefaultAsync(ct) ?? throw new ScopeAuthorizationExceptionV1("DECISION_EVIDENCE_UNAVAILABLE");
        var original = ApprovalDecisionWireV2.Parse(row.Payload);
        if (original.Full != row.PayloadHash || ApprovalCanonicalV2.Hash(Encoding.UTF8.GetBytes(row.Evidence)) != row.EvidenceHash
            || original.Value.DecisionEvidenceDigest?.Value != row.EvidenceHash)
            throw new ScopeAuthorizationExceptionV1("DECISION_EVIDENCE_UNAVAILABLE");
        // A registered producer may retry a logical decision under another event id;
        // retain the exact native source event mapping, never replace the original.
        if (original.Core != incoming.Core) throw new ScopeAuthorizationExceptionV1("DECISION_PAYLOAD_CONFLICT", 409);
        return new(original, row.Evidence, row.EvidenceHash, row.Mode);
    }
}
