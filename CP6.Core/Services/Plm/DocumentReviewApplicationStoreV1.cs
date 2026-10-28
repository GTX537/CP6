using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Plm;

internal sealed class DocumentReviewApplicationStoreV1(CP6Context db, ScopeAuthorizationFenceV1 scope)
{
    private sealed record ApplicationRow(byte[] Body,string Digest,Guid Receipt);
    internal async Task<DocumentApprovalV1?> Application(Guid submission,CancellationToken ct)
    {
        var row=await db.Database.SqlQuery<ApplicationRow>($"""
            SELECT body AS "Body",digest AS "Digest",receipt_id AS "Receipt" FROM cp6_document_review_application_v1.applications
            WHERE environment_id={scope.EnvironmentId} AND tenant_id={db.CurrentTenantId} AND submission_id={submission}
            """).SingleOrDefaultAsync(ct);
        if(row==null)return null;
        var value=JsonSerializer.Deserialize<DocumentApprovalV1>(row.Body,ApprovalBindingOwnerV2.Json);
        var receipt=await Exact(row.Receipt,ct);
        if(value==null || ApprovalCanonicalV2.Hash(row.Body)!=row.Digest || !ApprovalCanonicalV2.Bytes(value).AsSpan().SequenceEqual(row.Body)
            || value.SubmissionId!=submission || receipt.SubmissionId!=submission || receipt.DecisionId!=value.DecisionId
            || receipt.Application!="APPLIED" || receipt.BusinessResultRef!=new ApprovalRefV2("Main.Plm.DocumentApproval.v1",value.Id.ToString(),"1")
            || receipt.OutcomeCode!=(value.Approved?"DOCUMENT_APPROVED":"DOCUMENT_REJECTED"))
            throw new ScopeAuthorizationExceptionV1("DOCUMENT_APPLICATION_UNAVAILABLE");
        return value;
    }
    internal async Task<DocumentApprovalV1?> Approved(EngineeringSourceRefV1 document,CancellationToken ct)
    {
        var id=await db.Database.SqlQuery<Guid?>($"""
            SELECT submission_id AS "Value" FROM cp6_document_review_application_v1.applications
            WHERE environment_id={scope.EnvironmentId} AND tenant_id={db.CurrentTenantId} AND document_id={document.Id}
              AND document_revision={long.Parse(document.Version,CultureInfo.InvariantCulture)} AND approved
            """).SingleOrDefaultAsync(ct);
        if(id==null)return null;
        var application=await Application(id.Value,ct);
        if(application?.Document!=document || !application.Approved)throw new ScopeAuthorizationExceptionV1("DOCUMENT_APPLICATION_UNAVAILABLE");
        return application;
    }
    internal const string Action = "PLM_DOCUMENT_REVIEW";
    internal const string Journal = "Main.Plm.DocumentApplication.operations.v1", Contract = "Main.Plm.DocumentApplication.commands.v1";
    internal sealed record Source(Guid Stream, Guid Document, string Unit, ApprovalDecisionEnvelopeV2 Decision,
        ApprovalSubmitCommandV2 Submission, string EvidenceMode, string OriginalHash);
    private sealed record SourceRow(Guid Stream, Guid Document, string Unit, byte[] Payload, string Digest, byte[] Submission, string SubmissionDigest);
    internal async Task Ready(CancellationToken ct)
    {
        await scope.Root(ct);
        if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_document_review_application_v1.ready() AS \"Value\"").SingleAsync(ct))
            throw new ScopeAuthorizationExceptionV1("APPLICATION_STORE_UNAVAILABLE");
    }
    internal async Task<Source?> ForSubmission(Guid submissionId, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<SourceRow>($"""
            SELECT s.id AS "Stream",f.document_id AS "Document",s.application_unit_id AS "Unit",f.payload AS "Payload",f.payload_hash AS "Digest",
              o.payload AS "Submission",o.payload_digest AS "SubmissionDigest"
            FROM cp6_document_review_fact_v1.streams s JOIN cp6_document_review_fact_v1.facts f
              ON f.environment_id=s.environment_id AND f.tenant_id=s.tenant_id AND f.decision_id=s.decision_id
            JOIN cp6_document_review_v1.submissions o ON o.environment_id=f.environment_id AND o.tenant_id=f.tenant_id AND o.id=f.submission_id
            WHERE f.environment_id={scope.EnvironmentId} AND f.tenant_id={db.CurrentTenantId} AND f.submission_id={submissionId}
            """).SingleOrDefaultAsync(ct);
        if (row == null) return null;
        var decision = ApprovalDecisionWireV2.Parse(row.Payload); var submission = ApprovalSubmissionWireV2.Parse(row.Submission);
        if (decision.Full != row.Digest || submission.Full != row.SubmissionDigest || decision.Value.Subject != submission.Value.Subject
            || decision.Value.Policy != submission.Value.Policy || decision.Value.SubmissionId != submission.Value.SubmissionId
            || row.Unit != decision.Value.Subject.SubjectId || decision.Value.ConsumerOwner != ControlledDocumentReviewStoreV1.Owner)
            throw new ScopeAuthorizationExceptionV1("APPLICATION_SOURCE_UNAVAILABLE");
        var proof = await new ApprovalDecisionSourceV2(db, scope).Read(decision, ct);
        return new(row.Stream, row.Document, row.Unit, decision.Value, submission.Value, proof.Mode, row.Digest);
    }
    private sealed record ReceiptRow(byte[] Payload, string Hash);
    internal async Task<ApprovalApplicationReceiptV2?> Latest(Guid stream, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<ReceiptRow>($"""
            SELECT payload AS "Payload",payload_hash AS "Hash" FROM cp6_document_review_application_v1.receipts
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
            SELECT payload AS "Payload",payload_hash AS "Hash" FROM cp6_document_review_application_v1.receipts
            WHERE environment_id={scope.EnvironmentId} AND tenant_id={db.CurrentTenantId} AND id={receipt}
            """).SingleOrDefaultAsync(ct) ?? throw new ScopeAuthorizationExceptionV1("APPLICATION_RECEIPT_UNAVAILABLE");
        return Parse(row);
    }
    internal async Task<ApprovalApplicationReceiptV2[]> Page(Guid stream, long after, long maximum, int limit, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<ReceiptRow>($"""
            SELECT payload AS "Payload",payload_hash AS "Hash" FROM cp6_document_review_application_v1.receipts
            WHERE environment_id={scope.EnvironmentId} AND tenant_id={db.CurrentTenantId} AND stream_id={stream}
              AND revision>{after} AND revision<={maximum} ORDER BY revision LIMIT {limit}
            """).ToArrayAsync(ct);
        return rows.Select(Parse).ToArray();
    }
    internal async Task<ApprovalApplicationReceiptV2> Append(Source source, Guid actor, Guid operation,
        ApprovalApplicationReceiptV2? prior, string state, string outcome, string currentRevision, object evidence, DocumentApprovalV1? application, CancellationToken ct)
    {
        if(state is not ("APPLIED" or "BLOCKED" or "STALE" or "CONFLICT") || (state=="APPLIED")!=(application!=null))
            throw new ScopeAuthorizationExceptionV1("APPLICATION_EFFECT_UNPROVEN");
        var revision = prior == null ? 1 : checked(long.Parse(prior.ReceiptRevision, CultureInfo.InvariantCulture) + 1);
        var id = Guid.NewGuid(); var eventId = Guid.NewGuid(); var d = source.Decision;
        var at = await db.Database.SqlQueryRaw<DateTimeOffset>("SELECT date_trunc('milliseconds',clock_timestamp()) AS \"Value\"").SingleAsync(ct);
        var audit = Encoding.UTF8.GetString(ApprovalCanonicalV2.Bytes(evidence)); var auditHash = ApprovalCanonicalV2.Hash(Encoding.UTF8.GetBytes(audit));
        var receipt = ApprovalApplicationReceiptWireV2.Seal(new("oa.app/2.0", d.Scope, id, source.Stream, revision.ToString(CultureInfo.InvariantCulture),
            eventId, ControlledDocumentReviewStoreV1.Owner, d.SubmissionId, d.InstanceId, d.DecisionId, d.PayloadDigest, d.Subject,
            Action, state, outcome, application?.AppliedAt, actor, null,
            application==null?null:new("Main.Plm.DocumentApproval.v1",application.Id.ToString(),"1"),application==null?null:"1",currentRevision,
            new("Main.Plm.DocumentApplicationAudit.v1", id.ToString(), auditHash), prior?.ReceiptId, at, new("sha256", ApprovalCanonicalV2.Codec, new string('0', 64))));
        var payload = ApprovalCanonicalV2.Bytes(receipt); _ = ApprovalApplicationReceiptWireV2.Parse(payload); var hash = ApprovalCanonicalV2.Hash(payload); var previous = prior?.ReceiptId;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_document_review_application_v1.receipts(environment_id,tenant_id,id,stream_id,revision,event_id,previous_id,document_id,submission_id,decision_id,
              actor_id,operation_id,state,payload,payload_hash,audit_json,audit_hash)
            VALUES({scope.EnvironmentId},{db.CurrentTenantId},{id},{source.Stream},{revision},{eventId},{previous},
              {source.Document},{d.SubmissionId},{d.DecisionId},{actor},{operation},{state},{payload},{hash},{audit},{auditHash})
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_document_review_application_v1.outbox(environment_id,tenant_id,event_id,receipt_id,payload,payload_hash)
            VALUES({scope.EnvironmentId},{db.CurrentTenantId},{eventId},{id},{payload},{hash});
            """, ct);
        if(application!=null)
        {
            var body=ApprovalCanonicalV2.Bytes(application);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_document_review_application_v1.applications(environment_id,tenant_id,id,document_id,document_revision,
                  submission_id,decision_id,receipt_id,approved,body,digest)
                VALUES({scope.EnvironmentId},{db.CurrentTenantId},{application.Id},{source.Document},
                  {long.Parse(d.Subject.BusinessRevision,CultureInfo.InvariantCulture)},{d.SubmissionId},{d.DecisionId},{id},
                  {application.Approved},{body},{ApprovalCanonicalV2.Hash(body)})
                """,ct);
        }
        return receipt;
    }
}
