using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;

internal sealed class ControlledDocumentReviewStoreV1(CP6Context db, Guid environment)
{
    internal const string Owner = "Main.Plm.ControlledDocument.v1", PolicyOwner = "Main.Plm.DocumentReviewPolicy.v1",
        AuthorizationOwner = "Main.Plm.DocumentReviewAuthorization.v1", SubmissionOwner = "Main.Plm.DocumentReviewSubmission.v1", PackageOwner = "Main.Plm.DocumentReviewPackage.v1",
        SnapshotSchema = "Main.Plm.DocumentReview.snapshot/1", Journal = "Main.Plm.DocumentReview.operations.v1",
        Contract = "Main.Plm.DocumentReview.commands.v1", BizType = "PLM_DOCUMENT_REVIEW", SubjectKind = "DOCUMENT_REVISION";
    internal static string Json(object value) => Encoding.UTF8.GetString(ApprovalCanonicalV2.Bytes(value));
    internal static EngineeringSourceRefV1 Reference(DocumentReviewPolicyV1 policy)
        => new(PolicyOwner, policy.Id, "1", ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(policy)));
    internal async Task Fence(bool write, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction == null || environment == Guid.Empty)
            throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_document_review_v1.policies,cp6_document_review_v1.submissions,cp6_document_review_v1.outbox IN "
            + (write ? "SHARE ROW EXCLUSIVE" : "SHARE") + " MODE",ct);
        if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_document_review_v1.ready() AS \"Value\"").SingleAsync(ct))
            throw Error("DOCUMENT_REVIEW_STORE_UNAVAILABLE",503);
    }
    private sealed record PolicyRow(string Body,string Digest);
    internal async Task<DocumentReviewPolicyViewV1[]> Policies(Guid id,CancellationToken ct)
    {
        var rows=await db.Database.SqlQuery<PolicyRow>($"""
            SELECT body AS "Body",digest AS "Digest" FROM cp6_document_review_v1.policies
            WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId} AND document_id={id} ORDER BY sequence
            """).ToArrayAsync(ct);
        return rows.Select(row=>{
            var fact=JsonSerializer.Deserialize<DocumentReviewPolicyV1>(row.Body,ApprovalBindingOwnerV2.Json)??throw Error("DOCUMENT_POLICY_UNAVAILABLE",503);
            if(fact.EnvironmentId!=environment || fact.TenantId!=db.CurrentTenantId || fact.DocumentId!=id
                || Reference(fact).Digest!=row.Digest || Json(fact)!=row.Body)throw Error("DOCUMENT_POLICY_UNAVAILABLE",503);
            return new DocumentReviewPolicyViewV1(Reference(fact),fact);
        }).ToArray();
    }
    internal async Task<DocumentReviewPolicyViewV1> AddPolicy(DocumentReviewPolicyV1 fact,CancellationToken ct)
    {
        var reference=Reference(fact);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_document_review_v1.policies(environment_id,tenant_id,document_id,id,actor_id,body,digest,operation_id)
            VALUES({environment},{db.CurrentTenantId},{fact.DocumentId},{fact.Id},{fact.ActorId},{Json(fact)},{reference.Digest},{fact.Body.OperationId})
            """,ct);
        return new(reference,fact);
    }
    internal sealed record SubmissionRow(Guid Id, Guid Document, long Revision, Guid Policy, Guid Actor,
        Guid Operation, string Intent, byte[] Payload, string Digest, string Authorization,byte[] Package,string PackageDigest);
    internal Task<SubmissionRow[]> Submissions(Guid document,CancellationToken ct)=>db.Database.SqlQuery<SubmissionRow>($"""
        SELECT id AS "Id",document_id AS "Document",document_revision AS "Revision",policy_id AS "Policy",actor_id AS "Actor",
          operation_id AS "Operation",intent_hash AS "Intent",payload AS "Payload",payload_digest AS "Digest",authorization_json AS "Authorization",
          package_bytes AS "Package",package_digest AS "PackageDigest"
        FROM cp6_document_review_v1.submissions WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId} AND document_id={document}
        ORDER BY document_revision,id
        """).ToArrayAsync(ct);
    internal Task<SubmissionRow?> Submission(Guid id,CancellationToken ct)=>db.Database.SqlQuery<SubmissionRow>($"""
        SELECT id AS "Id",document_id AS "Document",document_revision AS "Revision",policy_id AS "Policy",actor_id AS "Actor",
          operation_id AS "Operation",intent_hash AS "Intent",payload AS "Payload",payload_digest AS "Digest",authorization_json AS "Authorization",
          package_bytes AS "Package",package_digest AS "PackageDigest"
        FROM cp6_document_review_v1.submissions WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId} AND id={id}
        """).SingleOrDefaultAsync(ct);
    internal ApprovalSubmitCommandV2 Envelope(SubmissionRow row)
    {
        var p=ApprovalSubmissionWireV2.Parse(row.Payload);var e=p.Value;
        if(p.Full!=row.Digest || !p.Canonical.AsSpan().SequenceEqual(row.Payload) || e.SubmissionId!=row.Id
            || e.CommandId!=row.Operation || e.StarterId!=row.Actor || e.Scope!=new ApprovalScopeV2(environment.ToString(),db.CurrentTenantId)
            || e.Subject.BusinessOwner!=Owner || e.Subject.SubjectId!=row.Document.ToString() || e.Subject.BusinessRevision!=row.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture)
            || e.Policy.Ref.Id!=row.Policy.ToString() || e.Subject.BizType!=BizType || e.Subject.SubjectKind!=SubjectKind)
            throw Error("DOCUMENT_SUBMISSION_UNAVAILABLE",503);
        var package=ReadPackage(row);
        var snapshot=JsonSerializer.Deserialize<DocumentReviewSnapshotV1>(Convert.FromBase64String(e.Snapshot.CanonicalBytesBase64),ApprovalBindingOwnerV2.Json)
            ??throw Error("DOCUMENT_REVIEW_PACKAGE_UNAVAILABLE",503);
        if(snapshot.Document!=package.Document || snapshot.Previous!=package.Previous || snapshot.ChangeDigest!=package.ChangeDigest
            || snapshot.SourceKind!=package.Source.Kind || snapshot.ReviewPackage!=new EngineeringSourceRefV1(PackageOwner,row.Id,"1",row.PackageDigest)
            || package.Document!=new EngineeringSourceRefV1(Owner,row.Document,e.Subject.BusinessRevision,e.Subject.ContentDigest.Value))
            throw Error("DOCUMENT_REVIEW_PACKAGE_UNAVAILABLE",503);
        return e;
    }
    internal static DocumentReviewPackageV1 ReadPackage(SubmissionRow row)
    {
        var value=JsonSerializer.Deserialize<DocumentReviewPackageV1>(row.Package,ApprovalBindingOwnerV2.Json)
            ??throw Error("DOCUMENT_REVIEW_PACKAGE_UNAVAILABLE",503);
        if(ApprovalCanonicalV2.Hash(row.Package)!=row.PackageDigest || !ApprovalCanonicalV2.Bytes(value).AsSpan().SequenceEqual(row.Package))
            throw Error("DOCUMENT_REVIEW_PACKAGE_UNAVAILABLE",503);
        return value;
    }
    internal DocumentReviewSubmissionV1 View(SubmissionRow row)
    {
        var e=Envelope(row);
        return new(row.Id,new(Owner,row.Document,e.Subject.BusinessRevision,e.Subject.ContentDigest.Value),
            new(PolicyOwner,row.Policy,e.Policy.Version,e.Policy.Digest.Value),row.Actor,e.OwnerFrozenAt,e.Snapshot.Digest.Value);
    }
    internal async Task Add(SubmissionRow row,CancellationToken ct)
    {
        var e=Envelope(row);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_document_review_v1.submissions(environment_id,tenant_id,id,document_id,document_revision,policy_id,actor_id,
              operation_id,intent_hash,payload,payload_digest,authorization_json,outbox_id,package_bytes,package_digest)
            VALUES({environment},{db.CurrentTenantId},{row.Id},{row.Document},{row.Revision},{row.Policy},{row.Actor},{row.Operation},
              {row.Intent},{row.Payload},{row.Digest},{row.Authorization},{e.OwnerOutboxId},{row.Package},{row.PackageDigest});
            INSERT INTO cp6_document_review_v1.outbox(environment_id,tenant_id,id,submission_id,payload,payload_digest)
            VALUES({environment},{db.CurrentTenantId},{e.OwnerOutboxId},{row.Id},{row.Payload},{row.Digest})
            """,ct);
    }
}
