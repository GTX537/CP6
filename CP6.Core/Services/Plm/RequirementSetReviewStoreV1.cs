using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;

internal sealed class RequirementSetReviewStoreV1(CP6Context db, Guid environment)
{
    internal const string Owner = "Main.Plm.RequirementSet.v1", PolicyOwner = "Main.Plm.RequirementSetReviewPolicy.v1",
        AuthorizationOwner = "Main.Plm.RequirementSetReviewAuthorization.v1", SubmissionOwner = "Main.Plm.RequirementSetReviewSubmission.v1", PackageOwner = "Main.Plm.RequirementSetReviewPackage.v1",
        SnapshotSchema = "Main.Plm.RequirementSetReview.snapshot/1", Journal = "Main.Plm.RequirementSetReview.operations.v1",
        Contract = "Main.Plm.RequirementSetReview.commands.v1", BizType = "PLM_REQUIREMENT_SET_REVIEW", SubjectKind = "REQUIREMENT_SET_REVISION";
    internal static string Json(object value) => Encoding.UTF8.GetString(ApprovalCanonicalV2.Bytes(value));
    internal static EngineeringSourceRefV1 Reference(RequirementSetReviewPolicyV1 policy)
        => new(PolicyOwner, policy.Id, "1", ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(policy)));
    internal async Task Fence(bool write, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction == null || environment == Guid.Empty)
            throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_requirement_set_review_v1.policies,cp6_requirement_set_review_v1.submissions,cp6_requirement_set_review_v1.outbox IN "
            + (write ? "SHARE ROW EXCLUSIVE" : "SHARE") + " MODE",ct);
        if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_requirement_set_review_v1.ready() AS \"Value\"").SingleAsync(ct))
            throw Error("REQUIREMENT_SET_REVIEW_STORE_UNAVAILABLE",503);
    }
    private sealed record PolicyRow(string Body,string Digest);
    internal async Task<RequirementSetReviewPolicyViewV1[]> Policies(Guid id,CancellationToken ct)
    {
        var rows=await db.Database.SqlQuery<PolicyRow>($"""
            SELECT body AS "Body",digest AS "Digest" FROM cp6_requirement_set_review_v1.policies
            WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId} AND requirement_set_id={id} ORDER BY sequence
            """).ToArrayAsync(ct);
        return rows.Select(row=>{
            var fact=JsonSerializer.Deserialize<RequirementSetReviewPolicyV1>(row.Body,ApprovalBindingOwnerV2.Json)??throw Error("REQUIREMENT_SET_POLICY_UNAVAILABLE",503);
            if(fact.EnvironmentId!=environment || fact.TenantId!=db.CurrentTenantId || fact.RequirementSetId!=id
                || Reference(fact).Digest!=row.Digest || Json(fact)!=row.Body)throw Error("REQUIREMENT_SET_POLICY_UNAVAILABLE",503);
            return new RequirementSetReviewPolicyViewV1(Reference(fact),fact);
        }).ToArray();
    }
    internal async Task<RequirementSetReviewPolicyViewV1> AddPolicy(RequirementSetReviewPolicyV1 fact,CancellationToken ct)
    {
        var reference=Reference(fact);
        if(await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_requirement_set_review_v1.policies(environment_id,tenant_id,requirement_set_id,id,actor_id,body,digest,operation_id)
            VALUES({environment},{db.CurrentTenantId},{fact.RequirementSetId},{fact.Id},{fact.ActorId},{Json(fact)},{reference.Digest},{fact.Body.OperationId})
            """,ct)!=1)throw Error("REQUIREMENT_SET_REVIEW_WRITE_UNAVAILABLE",503);
        if(!(await Policies(fact.RequirementSetId,ct)).Any(p=>p.Reference==reference && Json(p.Fact)==Json(fact)))throw Error("REQUIREMENT_SET_REVIEW_WRITE_UNAVAILABLE",503);
        return new(reference,fact);
    }
    internal sealed record SubmissionRow(Guid Id, Guid RequirementSet, long Revision, Guid Policy, Guid Actor,
        Guid Operation, string Intent, byte[] Payload, string Digest, string Authorization,byte[] Package,string PackageDigest);
    internal Task<SubmissionRow[]> Submissions(Guid requirementSet,CancellationToken ct)=>db.Database.SqlQuery<SubmissionRow>($"""
        SELECT id AS "Id",requirement_set_id AS "RequirementSet",requirement_set_revision AS "Revision",policy_id AS "Policy",actor_id AS "Actor",
          operation_id AS "Operation",intent_hash AS "Intent",payload AS "Payload",payload_digest AS "Digest",authorization_json AS "Authorization",
          package_bytes AS "Package",package_digest AS "PackageDigest"
        FROM cp6_requirement_set_review_v1.submissions WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId} AND requirement_set_id={requirementSet}
        ORDER BY requirement_set_revision,id
        """).ToArrayAsync(ct);
    internal Task<SubmissionRow?> Submission(Guid id,CancellationToken ct)=>db.Database.SqlQuery<SubmissionRow>($"""
        SELECT id AS "Id",requirement_set_id AS "RequirementSet",requirement_set_revision AS "Revision",policy_id AS "Policy",actor_id AS "Actor",
          operation_id AS "Operation",intent_hash AS "Intent",payload AS "Payload",payload_digest AS "Digest",authorization_json AS "Authorization",
          package_bytes AS "Package",package_digest AS "PackageDigest"
        FROM cp6_requirement_set_review_v1.submissions WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId} AND id={id}
        """).SingleOrDefaultAsync(ct);
    internal ApprovalSubmitCommandV2 Envelope(SubmissionRow row)
    {
        var p=ApprovalSubmissionWireV2.Parse(row.Payload);var e=p.Value;
        if(p.Full!=row.Digest || !p.Canonical.AsSpan().SequenceEqual(row.Payload) || e.SubmissionId!=row.Id
            || e.CommandId!=row.Operation || e.StarterId!=row.Actor || e.Scope!=new ApprovalScopeV2(environment.ToString(),db.CurrentTenantId)
            || e.Subject.BusinessOwner!=Owner || e.Subject.SubjectId!=row.RequirementSet.ToString() || e.Subject.BusinessRevision!=row.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture)
            || e.Policy.Ref.Id!=row.Policy.ToString() || e.Subject.BizType!=BizType || e.Subject.SubjectKind!=SubjectKind)
            throw Error("REQUIREMENT_SET_SUBMISSION_UNAVAILABLE",503);
        var package=ReadPackage(row);
        var snapshot=JsonSerializer.Deserialize<RequirementSetReviewSnapshotV1>(Convert.FromBase64String(e.Snapshot.CanonicalBytesBase64),ApprovalBindingOwnerV2.Json)
            ??throw Error("REQUIREMENT_SET_REVIEW_PACKAGE_UNAVAILABLE",503);
        if(snapshot.RequirementSet!=package.RequirementSet || snapshot.Previous!=package.Previous || snapshot.ChangeDigest!=package.ChangeDigest
            || snapshot.ReviewPackage!=new EngineeringSourceRefV1(PackageOwner,row.Id,"1",row.PackageDigest)
            || package.RequirementSet!=new EngineeringSourceRefV1(Owner,row.RequirementSet,e.Subject.BusinessRevision,e.Subject.ContentDigest.Value))
            throw Error("REQUIREMENT_SET_REVIEW_PACKAGE_UNAVAILABLE",503);
        return e;
    }
    internal static RequirementSetReviewPackageV1 ReadPackage(SubmissionRow row)
    {
        var value=JsonSerializer.Deserialize<RequirementSetReviewPackageV1>(row.Package,ApprovalBindingOwnerV2.Json)
            ??throw Error("REQUIREMENT_SET_REVIEW_PACKAGE_UNAVAILABLE",503);
        if(ApprovalCanonicalV2.Hash(row.Package)!=row.PackageDigest || !ApprovalCanonicalV2.Bytes(value).AsSpan().SequenceEqual(row.Package))
            throw Error("REQUIREMENT_SET_REVIEW_PACKAGE_UNAVAILABLE",503);
        return value;
    }
    internal RequirementSetReviewSubmissionV1 View(SubmissionRow row)
    {
        var e=Envelope(row);
        return new(row.Id,new(Owner,row.RequirementSet,e.Subject.BusinessRevision,e.Subject.ContentDigest.Value),
            new(PolicyOwner,row.Policy,e.Policy.Version,e.Policy.Digest.Value),row.Actor,e.OwnerFrozenAt,e.Snapshot.Digest.Value);
    }
    internal async Task Add(SubmissionRow row,CancellationToken ct)
    {
        var e=Envelope(row);
        if(await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_requirement_set_review_v1.submissions(environment_id,tenant_id,id,requirement_set_id,requirement_set_revision,policy_id,actor_id,
              operation_id,intent_hash,payload,payload_digest,authorization_json,outbox_id,package_bytes,package_digest)
            VALUES({environment},{db.CurrentTenantId},{row.Id},{row.RequirementSet},{row.Revision},{row.Policy},{row.Actor},{row.Operation},
              {row.Intent},{row.Payload},{row.Digest},{row.Authorization},{e.OwnerOutboxId},{row.Package},{row.PackageDigest});
            INSERT INTO cp6_requirement_set_review_v1.outbox(environment_id,tenant_id,id,submission_id,payload,payload_digest)
            VALUES({environment},{db.CurrentTenantId},{e.OwnerOutboxId},{row.Id},{row.Payload},{row.Digest})
            """,ct)!=2)throw Error("REQUIREMENT_SET_REVIEW_WRITE_UNAVAILABLE",503);
        var saved=await Submission(row.Id,ct);
        if(saved==null || Json(Envelope(saved))!=Json(e))throw Error("REQUIREMENT_SET_REVIEW_WRITE_UNAVAILABLE",503);
    }
}
