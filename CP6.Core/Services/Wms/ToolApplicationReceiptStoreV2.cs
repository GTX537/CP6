using System.Globalization;
using System.Text;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

internal sealed class ToolApplicationReceiptStoreV2(CP6Context db, ScopeAuthorizationFenceV1 scope)
{
    internal const string Action = "WMS_TOOL_DISCARD";
    internal const string Journal = "Main.Wms.ToolApplication.operations.v2", Contract = "Main.Wms.ToolApplication.commands.v2";
    internal sealed record Source(Guid Stream, Guid Tool, string Unit, ApprovalDecisionEnvelopeV2 Decision,
        ApprovalSubmitCommandV2 Submission, string EvidenceMode, string OriginalHash);
    private sealed record SourceRow(Guid Stream, Guid Tool, string Unit, byte[] Payload, string Digest, byte[] Submission, string SubmissionDigest);
    internal async Task Ready(CancellationToken ct)
    {
        await scope.Root(ct);
        if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_tool_application_v2.ready() AS \"Value\"").SingleAsync(ct))
            throw new ScopeAuthorizationExceptionV1("APPLICATION_STORE_UNAVAILABLE");
    }
    internal async Task<Source?> ForTool(Guid tool, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<SourceRow>($"""
            SELECT s.id AS "Stream",f.tool_id AS "Tool",s.application_unit_id AS "Unit",f.payload AS "Payload",f.payload_hash AS "Digest",
              o.payload AS "Submission",o.payload_digest AS "SubmissionDigest"
            FROM cp6_tool_approval_fact_v2.streams s JOIN cp6_tool_approval_fact_v2.facts f
              ON f.environment_id=s.environment_id AND f.tenant_id=s.tenant_id AND f.decision_id=s.decision_id
            JOIN cp6_tool_approval_v1.submissions o ON o.environment_id=f.environment_id AND o.tenant_id=f.tenant_id AND o.id=f.submission_id
            WHERE f.environment_id={scope.EnvironmentId} AND f.tenant_id={db.CurrentTenantId} AND f.tool_id={tool}
            """).SingleOrDefaultAsync(ct);
        if (row == null) return null;
        var decision = ApprovalDecisionWireV2.Parse(row.Payload); var submission = ApprovalSubmissionWireV2.Parse(row.Submission);
        if (decision.Full != row.Digest || submission.Full != row.SubmissionDigest || decision.Value.Subject != submission.Value.Subject
            || decision.Value.Policy != submission.Value.Policy || decision.Value.SubmissionId != submission.Value.SubmissionId
            || row.Unit != decision.Value.Subject.SubjectId || decision.Value.ConsumerOwner != ToolDispositionStoreV1.DispositionOwner)
            throw new ScopeAuthorizationExceptionV1("APPLICATION_SOURCE_UNAVAILABLE");
        var proof = await new ApprovalDecisionSourceV2(db, scope).Read(decision, ct);
        return new(row.Stream, row.Tool, row.Unit, decision.Value, submission.Value, proof.Mode, row.Digest);
    }
    private sealed record ReceiptRow(byte[] Payload, string Hash);
    internal async Task<ApprovalApplicationReceiptV2?> Latest(Guid stream, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<ReceiptRow>($"""
            SELECT payload AS "Payload",payload_hash AS "Hash" FROM cp6_tool_application_v2.receipts
            WHERE environment_id={scope.EnvironmentId} AND tenant_id={db.CurrentTenantId} AND stream_id={stream} ORDER BY revision DESC LIMIT 1
            """).SingleOrDefaultAsync(ct);
        return row == null ? null : Parse(row);
    }
    private static ApprovalApplicationReceiptV2 Parse(ReceiptRow row)
    {
        if (ApprovalCanonicalV2.Hash(row.Payload) != row.Hash) throw new ScopeAuthorizationExceptionV1("APPLICATION_RECEIPT_UNAVAILABLE");
        return ApprovalApplicationReceiptWireV2.Parse(row.Payload);
    }
    internal async Task<ApprovalApplicationReceiptV2> Exact(Guid receipt, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<ReceiptRow>($"""
            SELECT payload AS "Payload",payload_hash AS "Hash" FROM cp6_tool_application_v2.receipts
            WHERE environment_id={scope.EnvironmentId} AND tenant_id={db.CurrentTenantId} AND id={receipt}
            """).SingleOrDefaultAsync(ct) ?? throw new ScopeAuthorizationExceptionV1("APPLICATION_RECEIPT_UNAVAILABLE");
        return Parse(row);
    }
    internal async Task<ApprovalApplicationReceiptV2[]> Page(Guid stream, long after, long maximum, int limit, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<ReceiptRow>($"""
            SELECT payload AS "Payload",payload_hash AS "Hash" FROM cp6_tool_application_v2.receipts
            WHERE environment_id={scope.EnvironmentId} AND tenant_id={db.CurrentTenantId} AND stream_id={stream}
              AND revision>{after} AND revision<={maximum} ORDER BY revision LIMIT {limit}
            """).ToArrayAsync(ct);
        return rows.Select(Parse).ToArray();
    }
    internal async Task<ApprovalApplicationReceiptV2> Append(Source source, Guid actor, Guid operation,
        ApprovalApplicationReceiptV2? prior, string state, string outcome, string currentRevision, object evidence, CancellationToken ct)
    {
        // Physical finalization is deliberately unavailable until the real obligation Owners
        // supply their closure participants. This writer cannot manufacture an APPLIED result.
        if (state is not ("BLOCKED" or "STALE" or "CONFLICT")) throw new ScopeAuthorizationExceptionV1("APPLICATION_EFFECT_UNPROVEN");
        var revision = prior == null ? 1 : checked(long.Parse(prior.ReceiptRevision, CultureInfo.InvariantCulture) + 1);
        var id = Guid.NewGuid(); var eventId = Guid.NewGuid(); var d = source.Decision;
        var at = await db.Database.SqlQueryRaw<DateTimeOffset>("SELECT date_trunc('milliseconds',clock_timestamp()) AS \"Value\"").SingleAsync(ct);
        var audit = Encoding.UTF8.GetString(ApprovalCanonicalV2.Bytes(evidence)); var auditHash = ApprovalCanonicalV2.Hash(Encoding.UTF8.GetBytes(audit));
        var receipt = ApprovalApplicationReceiptWireV2.Seal(new("oa.app/2.0", d.Scope, id, source.Stream, revision.ToString(CultureInfo.InvariantCulture),
            eventId, ToolDispositionStoreV1.DispositionOwner, d.SubmissionId, d.InstanceId, d.DecisionId, d.PayloadDigest, d.Subject,
            Action, state, outcome, null, actor, null, null, null, currentRevision,
            new("Main.Wms.ToolApplicationAudit.v2", id.ToString(), auditHash), prior?.ReceiptId, at, new("sha256", ApprovalCanonicalV2.Codec, new string('0', 64))));
        var payload = ApprovalCanonicalV2.Bytes(receipt); _ = ApprovalApplicationReceiptWireV2.Parse(payload); var hash = ApprovalCanonicalV2.Hash(payload); var previous = prior?.ReceiptId;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_tool_application_v2.receipts(environment_id,tenant_id,id,stream_id,revision,event_id,previous_id,tool_id,submission_id,decision_id,
              actor_id,operation_id,state,payload,payload_hash,audit_json,audit_hash)
            VALUES({scope.EnvironmentId},{db.CurrentTenantId},{id},{source.Stream},{revision},{eventId},{previous},
              {source.Tool},{d.SubmissionId},{d.DecisionId},{actor},{operation},{state},{payload},{hash},{audit},{auditHash})
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_tool_application_v2.outbox(environment_id,tenant_id,event_id,receipt_id,payload,payload_hash)
            VALUES({scope.EnvironmentId},{db.CurrentTenantId},{eventId},{id},{payload},{hash});
            """, ct);
        return receipt;
    }
}
