using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.ToolInstanceStoreV1;
namespace CP6.Core.Services.Wms;

internal sealed class ToolDispositionApprovalStoreV1(CP6Context db, Guid environment)
{
    internal const string PolicyOwner = "Main.Wms.ToolDispositionPolicy.v1", AuthorizationOwner = "Main.Wms.ToolDispositionAuthorization.v1";
    internal const string SnapshotSchema = "Main.Wms.ToolDisposition.snapshot/1", FactCodec = "Main.Wms.ToolDisposition.fact-json/1";
    internal const string Journal = "Main.Wms.ToolDispositionApproval.operations.v1", Contract = "Main.Wms.ToolDispositionApproval.commands.v1";
    internal static ToolDispositionRefV1 Reference(ToolDispositionPolicyV1 policy) => new(PolicyOwner, policy.RecordId, "1", ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(policy)));
    internal async Task Fence(bool write, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction == null || environment == Guid.Empty) throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_tool_approval_v1.policies,cp6_tool_approval_v1.submissions,cp6_tool_approval_v1.outbox IN " + (write ? "SHARE ROW EXCLUSIVE" : "SHARE") + " MODE", ct);
        if (!await db.Database.SqlQueryRaw<bool>("""
            SELECT count(*)=9 AS "Value" FROM pg_catalog.pg_trigger WHERE tgrelid IN
              ('cp6_tool_approval_v1.policies'::regclass,'cp6_tool_approval_v1.submissions'::regclass,'cp6_tool_approval_v1.outbox'::regclass)
              AND tgname IN ('tool_approval_immutable','tool_approval_no_truncate','tool_approval_validate') AND tgenabled IN ('O','A') AND NOT tgisinternal
            """).SingleAsync(ct)) throw Error("TOOL_APPROVAL_UNAVAILABLE", 503);
    }
    private sealed record PolicyRow(string Body, string Digest);
    internal async Task<ToolDispositionPolicyViewV1[]> Policies(Guid tool, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<PolicyRow>($"""
            SELECT body AS "Body",digest AS "Digest" FROM cp6_tool_approval_v1.policies
              WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId} AND tool_id={tool} ORDER BY sequence
            """).ToArrayAsync(ct);
        return rows.Select(r => {
            var p = JsonSerializer.Deserialize<ToolDispositionPolicyV1>(r.Body, ApprovalBindingOwnerV2.Json) ?? throw Error("TOOL_APPROVAL_UNAVAILABLE",503);
            if (p.EnvironmentId!=environment || p.TenantId!=db.CurrentTenantId || p.ToolId!=tool || Reference(p).Digest!=r.Digest
                || ApprovalCanonicalV2.Hash(Encoding.UTF8.GetBytes(r.Body))!=r.Digest) throw Error("TOOL_APPROVAL_UNAVAILABLE",503);
            return new ToolDispositionPolicyViewV1(Reference(p),p);
        }).ToArray();
    }
    internal async Task<ToolDispositionPolicyViewV1> AddPolicy(ToolDispositionPolicyV1 p, CancellationToken ct)
    {
        var reference=Reference(p);var body=Encoding.UTF8.GetString(ApprovalCanonicalV2.Bytes(p));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_tool_approval_v1.policies(environment_id,tenant_id,tool_id,id,body,digest,operation_id,journal)
            VALUES({environment},{db.CurrentTenantId},{p.ToolId},{p.RecordId},{body},{reference.Digest},{p.Body.OperationId},{Journal})
            """,ct);
        return new(reference,p);
    }
    internal sealed record SubmissionRow(Guid Id, Guid Tool, Guid Request, Guid Policy, Guid Actor, Guid Operation,
        string Intent, byte[] Payload, string Digest, string Authorization);
    internal async Task<SubmissionRow[]> Submissions(Guid tool, CancellationToken ct)
        => await db.Database.SqlQuery<SubmissionRow>($"""
            SELECT id AS "Id",tool_id AS "Tool",request_id AS "Request",policy_id AS "Policy",actor_id AS "Actor",operation_id AS "Operation",
              intent_hash AS "Intent",payload AS "Payload",payload_digest AS "Digest",authorization_json AS "Authorization"
            FROM cp6_tool_approval_v1.submissions WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId} AND tool_id={tool}
            """).ToArrayAsync(ct);
    internal ApprovalSubmitCommandV2 Envelope(SubmissionRow row)
    {
        if (ApprovalCanonicalV2.Hash(row.Payload)!=row.Digest) throw Error("TOOL_APPROVAL_UNAVAILABLE",503);
        var e=JsonSerializer.Deserialize<ApprovalSubmitCommandV2>(row.Payload,ApprovalBindingOwnerV2.Json) ?? throw Error("TOOL_APPROVAL_UNAVAILABLE",503);
        if (e.SubmissionId!=row.Id || e.CommandId!=row.Operation || e.StarterId!=row.Actor || e.Scope.EnvironmentId!=environment.ToString()
            || e.Scope.TenantId!=db.CurrentTenantId || e.Subject.SubjectId!=row.Request.ToString() || e.Policy.Ref.Id!=row.Policy.ToString()
            || !row.Payload.AsSpan().SequenceEqual(ApprovalCanonicalV2.Bytes(e))) throw Error("TOOL_APPROVAL_UNAVAILABLE",503);
        return e;
    }
    internal ToolDispositionApprovalViewV1 View(SubmissionRow row)
    {
        var e=Envelope(row);return new(e.SubmissionId,row.Tool,"DISPATCH_PENDING",e.Subject,e.Policy,e.StarterId,e.OwnerFrozenAt,
            e.Snapshot.Digest.Value,e.BusinessSubmittedRowVersion,e.OwnerOutboxId);
    }
    internal async Task Add(SubmissionRow row, CancellationToken ct)
    {
        var e=Envelope(row);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_tool_approval_v1.submissions(environment_id,tenant_id,tool_id,id,request_id,policy_id,actor_id,operation_id,
              intent_hash,outbox_id,payload,payload_digest,authorization_json,journal)
            VALUES({environment},{db.CurrentTenantId},{row.Tool},{row.Id},{row.Request},{row.Policy},{row.Actor},{row.Operation},
              {row.Intent},{e.OwnerOutboxId},{row.Payload},{row.Digest},{row.Authorization},{Journal})
            """,ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_tool_approval_v1.outbox(environment_id,tenant_id,id,submission_id,payload,payload_digest)
            VALUES({environment},{db.CurrentTenantId},{e.OwnerOutboxId},{row.Id},{row.Payload},{row.Digest})
            """,ct);
    }
}
