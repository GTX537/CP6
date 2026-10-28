using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Plm;
internal sealed class IterationReviewStoreV1(CP6Context db,Guid environment)
{
    internal const string Owner="Main.Plm.EngineeringIteration.v1",PolicyOwner="Main.Plm.IterationReviewPolicy.v1",
        AuthorizationOwner="Main.Plm.IterationReviewAuthorization.v1",SubmissionOwner="Main.Plm.IterationReviewSubmission.v1",
        PackageOwner="Main.Plm.IterationReviewPackage.v1",Journal="Main.Plm.IterationReview.operations.v1",
        Contract="Main.Plm.IterationReview.commands.v1",BizType="PLM_ITER_REVIEW",SubjectKind="PLM_ITER_REVIEW";
    internal static string Json(object value)=>Encoding.UTF8.GetString(ApprovalCanonicalV2.Bytes(value));
    internal static EngineeringSourceRefV1 Reference(IterationReviewPolicyV1 p)=>new(PolicyOwner,p.Id,"1",ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(p)));
    internal async Task Fence(bool write,CancellationToken ct)
    {
        if(!db.Database.IsNpgsql() || db.Database.CurrentTransaction==null || db.CurrentTenantId==Guid.Empty || environment==Guid.Empty)throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_iteration_review_v1.policies,cp6_iteration_review_v1.submissions,cp6_iteration_review_v1.outbox IN "+(write?"SHARE ROW EXCLUSIVE":"SHARE")+" MODE",ct);
        if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_iteration_review_v1.ready() AS \"Value\"").SingleAsync(ct))throw Error("ITERATION_REVIEW_STORE_UNAVAILABLE",503);
    }
    private sealed record PolicyRow(string Body,string Digest);
    internal async Task<IterationReviewPolicyViewV1[]> Policies(Guid id,CancellationToken ct)
    {
        var rows=await db.Database.SqlQuery<PolicyRow>($"""
            SELECT body AS "Body",digest AS "Digest" FROM cp6_iteration_review_v1.policies
            WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId} AND iteration_id={id} ORDER BY sequence
            """).ToArrayAsync(ct);
        return rows.Select(row=>{
            var p=JsonSerializer.Deserialize<IterationReviewPolicyV1>(row.Body,ApprovalBindingOwnerV2.Json)??throw Error("ITERATION_REVIEW_POLICY_UNAVAILABLE",503);
            if(p.EnvironmentId!=environment || p.TenantId!=db.CurrentTenantId || p.IterationId!=id || Json(p)!=row.Body || Reference(p).Digest!=row.Digest)throw Error("ITERATION_REVIEW_POLICY_UNAVAILABLE",503);
            return new IterationReviewPolicyViewV1(Reference(p),p);
        }).ToArray();
    }
    internal async Task<IterationReviewPolicyViewV1> AddPolicy(IterationReviewPolicyV1 p,CancellationToken ct)
    {
        var r=Reference(p);
        if(await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_iteration_review_v1.policies(environment_id,tenant_id,iteration_id,id,actor_id,body,digest,operation_id)
            VALUES({environment},{db.CurrentTenantId},{p.IterationId},{p.Id},{p.ActorId},{Json(p)},{r.Digest},{p.Body.OperationId})
            """,ct)!=1)throw Error("ITERATION_REVIEW_WRITE_UNAVAILABLE",503);
        return (await Policies(p.IterationId,ct)).SingleOrDefault(x=>x.Reference==r)??throw Error("ITERATION_REVIEW_WRITE_UNAVAILABLE",503);
    }
    internal sealed record SubmissionRow(Guid Id,Guid Iteration,long Revision,long FrozenRevision,Guid Policy,Guid Actor,Guid Operation,
        Guid Round,Guid Aggregate,Guid Binding,string Intent,byte[] Payload,string Digest,string Authorization,byte[] Package,string PackageDigest,string Registration);
    private IQueryable<SubmissionRow> Rows()=>db.Database.SqlQuery<SubmissionRow>($"""
        SELECT id AS "Id",iteration_id AS "Iteration",iteration_revision AS "Revision",frozen_revision AS "FrozenRevision",
          policy_id AS "Policy",actor_id AS "Actor",operation_id AS "Operation",round_id AS "Round",aggregate_id AS "Aggregate",binding_id AS "Binding",
          intent_hash AS "Intent",payload AS "Payload",payload_digest AS "Digest",authorization_json AS "Authorization",
          package_bytes AS "Package",package_digest AS "PackageDigest",registration_json AS "Registration"
        FROM cp6_iteration_review_v1.submissions WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId}
        """);
    internal Task<SubmissionRow[]> Submissions(Guid id,CancellationToken ct)=>Rows().Where(x=>x.Iteration==id).OrderBy(x=>x.FrozenRevision).ToArrayAsync(ct);
    internal Task<SubmissionRow?> Submission(Guid id,CancellationToken ct)=>Rows().SingleOrDefaultAsync(x=>x.Id==id,ct);
    internal static IterationReviewPackageV1 ReadPackage(SubmissionRow row)
    {
        var p=JsonSerializer.Deserialize<IterationReviewPackageV1>(row.Package,ApprovalBindingOwnerV2.Json)??throw Error("ITERATION_REVIEW_PACKAGE_UNAVAILABLE",503);
        if(IterationReviewCanonicalV1.Hash(row.Package)!=row.PackageDigest || !IterationReviewCanonicalV1.Bytes(p).AsSpan().SequenceEqual(row.Package))throw Error("ITERATION_REVIEW_PACKAGE_UNAVAILABLE",503);
        return p;
    }
    internal async Task<ApprovalSubmitCommandV2> Envelope(SubmissionRow row,CancellationToken ct)
    {
        var parsed=ApprovalSubmissionWireV2.Parse(row.Payload);var e=parsed.Value;var package=ReadPackage(row);
        if(parsed.Full!=row.Digest || !parsed.Canonical.AsSpan().SequenceEqual(row.Payload) || e.SubmissionId!=row.Id || e.CommandId!=row.Operation
            || e.StarterId!=row.Actor || e.Scope!=new ApprovalScopeV2(environment.ToString(),db.CurrentTenantId) || package.Scope!=e.Scope
            || package.Schema!=IterationReviewCanonicalV1.PackageSchema || package.Iteration.Owner!=Owner
            || package.Iteration.Id!=row.Iteration || Revision(package.Iteration.Version)!=row.Revision || package.RoundId!=row.Round
            || e.Subject.BusinessOwner!=Owner || e.Subject.BizType!=BizType || e.Subject.SubjectKind!=SubjectKind
            || e.Subject.SubjectId!=row.Round.ToString("D") || e.RoundId!=e.Subject.SubjectId || e.Subject.BusinessRevision!=row.Binding.ToString("D")
            || e.BusinessSubmittedRowVersion!=ApprovalBindingTimelineBuilderV2.Version(row.FrozenRevision) || e.ReplacesSubmissionId!=null)
            throw Error("ITERATION_REVIEW_SUBMISSION_UNAVAILABLE",503);
        var policy=(await Policies(row.Iteration,ct)).SingleOrDefault(p=>p.Fact.Id==row.Policy)??throw Error("ITERATION_REVIEW_POLICY_UNAVAILABLE",503);
        var registration=JsonSerializer.Deserialize<IterationReviewRegistrationV1>(row.Registration,ApprovalBindingOwnerV2.Json)??throw Error("ITERATION_REVIEW_REGISTRATION_UNAVAILABLE",503);
        if(Json(registration)!=row.Registration || registration.EnvironmentId!=environment || registration.TenantId!=db.CurrentTenantId
            || registration.Owner!=Owner || registration.BizType!=BizType || registration.SubjectKind!=SubjectKind
            || registration.SnapshotSchema!=IterationReviewCanonicalV1.SnapshotSchema || registration.Route!="/internal/plm/iteration-review-results/v1"
            || registration.Audience!="CP6.Services" || registration.Principal==Guid.Empty || registration.Revision==Guid.Empty
            || registration.EvidenceMode is not("REAL" or "DEMO") || !registration.Enabled)throw Error("ITERATION_REVIEW_REGISTRATION_UNAVAILABLE",503);
        var snapshot=IterationReviewMappingV1.Snapshot(package,row.Package,policy,registration,row.Aggregate,row.Binding,row.Id);
        var bytes=IterationReviewCanonicalV1.Bytes(snapshot);var digest=IterationReviewCanonicalV1.Digest(IterationReviewCanonicalV1.SnapshotSchema,bytes);
        if(package.Policy!=policy.Reference || e.Policy!=new ApprovalPolicyV2(new(PolicyOwner,row.Policy.ToString("D"),"1"),"1",snapshot.Binding.PolicyDigest)
            || e.Snapshot.Schema!=IterationReviewCanonicalV1.SnapshotSchema || e.Snapshot.CanonicalBytesBase64!=Convert.ToBase64String(bytes)
            || e.Snapshot.Digest!=digest || e.Subject.ContentDigest!=digest || e.Engineering==null
            || Json(e.Engineering.Value)!=Json(IterationReviewMappingV1.Engineering(snapshot))
            || e.SubmitAuthorizationRef!=new ApprovalRefV2(AuthorizationOwner,row.Id.ToString("D"),"1"))throw Error("ITERATION_REVIEW_MAPPING_UNAVAILABLE",503);
        return e;
    }
    internal async Task<IterationReviewSubmissionV1> View(SubmissionRow row,CancellationToken ct)
    {
        var e=await Envelope(row,ct);var p=ReadPackage(row);
        return new(row.Id,row.Round,p.Iteration,new(PackageOwner,row.Id,"1",row.PackageDigest),p.Policy,e.Snapshot.Digest.Value,"PENDING");
    }
    internal async Task Add(SubmissionRow row,CancellationToken ct)
    {
        var e=await Envelope(row,ct);
        if(await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_iteration_review_v1.submissions(environment_id,tenant_id,id,iteration_id,iteration_revision,frozen_revision,policy_id,actor_id,
              operation_id,round_id,aggregate_id,binding_id,intent_hash,payload,payload_digest,authorization_json,outbox_id,package_bytes,package_digest,registration_json)
            VALUES({environment},{db.CurrentTenantId},{row.Id},{row.Iteration},{row.Revision},{row.FrozenRevision},{row.Policy},{row.Actor},
              {row.Operation},{row.Round},{row.Aggregate},{row.Binding},{row.Intent},{row.Payload},{row.Digest},{row.Authorization},{e.OwnerOutboxId},{row.Package},{row.PackageDigest},{row.Registration});
            INSERT INTO cp6_iteration_review_v1.outbox(environment_id,tenant_id,id,submission_id,payload,payload_digest)
            VALUES({environment},{db.CurrentTenantId},{e.OwnerOutboxId},{row.Id},{row.Payload},{row.Digest})
            """,ct)!=2)throw Error("ITERATION_REVIEW_WRITE_UNAVAILABLE",503);
        var saved=await Submission(row.Id,ct);
        if(saved==null || Json(await Envelope(saved,ct))!=Json(e))throw Error("ITERATION_REVIEW_WRITE_UNAVAILABLE",503);
    }
}
