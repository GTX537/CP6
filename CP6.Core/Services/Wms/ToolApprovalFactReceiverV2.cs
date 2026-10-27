using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public sealed record ApprovalFactAckV2(string Kind, Guid EventId, Guid DecisionId, Guid SubmissionId,
    ApprovalDigestV2 PayloadDigest, Guid InboxId, string ReceiveState, DateTimeOffset ReceivedAt, string Application,
    Guid? ApplicationReceiptId, Guid? ReceiptStreamId, string? ReceiptQueryRef, string? ErrorCode);
public sealed record ApprovalFactNotObservedV2(string Kind, Guid EventId, DateTimeOffset ObservedAt, string QueryRef);
[JsonDerivedType(typeof(ApprovalApplicationNotAppliedV2)), JsonDerivedType(typeof(ApprovalApplicationFoundV2))]
public abstract record ApprovalApplicationResultV2(string Kind);
public sealed record ApprovalApplicationNotAppliedV2(string Kind, Guid ReceiptStreamId, DateTimeOffset ObservedAt) : ApprovalApplicationResultV2(Kind);
public sealed record ApprovalApplicationFoundV2(ApprovalApplicationReceiptV2 Receipt) : ApprovalApplicationResultV2("FOUND");
public sealed record ApprovalFactReceiveResultV2(int Status, object Body);

/// <summary>Actual Tool Owner fact reception. Receipt means an immutable approval fact
/// was received, not that a physical tool was discarded. Application uses a separate Owner transaction.</summary>
public sealed partial class ToolApprovalFactReceiverV2(CP6Context db, ScopeAuthorizationFenceV1 scope,
    IApprovalServiceAuthorityV2 authority, ApprovalDecisionSourceV2 source,
    Microsoft.AspNetCore.DataProtection.IDataProtectionProvider? protection = null)
{
    public const string Route = "/internal/wms/approval-results/v2";
    public const string ApplicationRoute = "/internal/wms/approval-application-streams/";
    private const string Owner = "Main.Wms.ToolDisposition.v1";
    private static ScopeAuthorizationExceptionV1 Error(string code, int status = 503) => new(code, status);
    private static string Json(object value) => Encoding.UTF8.GetString(ApprovalCanonicalV2.Bytes(value));
    private static T Parse<T>(string json) => JsonSerializer.Deserialize<T>(json, ApprovalBindingOwnerV2.Json)!;
    private sealed record Registration(Guid Revision, bool Enabled, bool Receive, bool Query, string Mode);
    private sealed record Grant(Guid Revision, bool Enabled, bool Receive, bool Query);
    private sealed record Frozen(Guid Tool, Guid Request, byte[] Payload, string Digest);
    private sealed record Inbox(Guid Submission, string Hash, string Ack);
    private sealed record Fact(Guid Decision, Guid Instance, Guid Submission, string Core, string Disposition);
    private sealed record Stream(Guid Id, Guid Decision, Guid Submission);
    private void Entry()
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction != null || db.ChangeTracker.HasChanges()
            || System.Transactions.Transaction.Current != null) throw Error("OWNER_FACT_TRANSACTION_REQUIRED");
    }
    private async Task Ready(CancellationToken ct)
    {
        if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_tool_approval_fact_v2.ready() AS \"Value\"").SingleAsync(ct))
            throw Error("OWNER_FACT_STORE_UNAVAILABLE");
    }
    private Task<DateTimeOffset> Now(CancellationToken ct) => db.Database.SqlQueryRaw<DateTimeOffset>(
        "SELECT date_trunc('milliseconds',clock_timestamp()) AS \"Value\"").SingleAsync(ct);
    private async Task<(ApprovalServiceIdentityV2 Service, Registration Registration)> Service(CancellationToken ct)
    {
        var s = await authority.CurrentAsync(ct);
        if (s.Environment != scope.EnvironmentId || s.Tenant != db.CurrentTenantId || s.Principal == Guid.Empty || s.Audience != "CP6.Services")
            throw Error("PRODUCER_NOT_ALLOWED", 403);
        var r = await db.Database.SqlQuery<Registration>($"""
            SELECT revision AS "Revision",enabled AS "Enabled",can_receive AS "Receive",can_query AS "Query",evidence_mode AS "Mode"
            FROM cp6_tool_approval_fact_v2.registrations WHERE environment_id={s.Environment} AND tenant_id={s.Tenant} AND principal_id={s.Principal}
            """).SingleOrDefaultAsync(ct);
        if (r == null || !r.Enabled || !r.Query || r.Mode != s.EvidenceMode) throw Error("PRODUCER_NOT_ALLOWED", 403);
        return (s, r);
    }
    private async Task<Grant> Subject(ApprovalServiceIdentityV2 s, Guid submission, bool write, Registration r, CancellationToken ct)
    {
        var g = await db.Database.SqlQuery<Grant>($"""
            SELECT revision AS "Revision",enabled AS "Enabled",can_receive AS "Receive",can_query AS "Query"
            FROM cp6_tool_approval_fact_v2.subject_grants WHERE environment_id={s.Environment} AND tenant_id={s.Tenant}
              AND principal_id={s.Principal} AND submission_id={submission}
            """).SingleOrDefaultAsync(ct);
        if (g == null || !g.Enabled || !g.Query) throw Error("RESOURCE_NOT_FOUND", 404);
        if (write && (!s.CanSubmit || !r.Receive || !g.Receive)) throw Error("COMMAND_FORBIDDEN", 403);
        return g;
    }
    private Task<Frozen?> Original(Guid submission, CancellationToken ct) => db.Database.SqlQuery<Frozen>($"""
        SELECT tool_id AS "Tool",request_id AS "Request",payload AS "Payload",payload_digest AS "Digest"
        FROM cp6_tool_approval_v1.submissions WHERE environment_id={scope.EnvironmentId} AND tenant_id={db.CurrentTenantId} AND id={submission}
        """).SingleOrDefaultAsync(ct);
    private Task<Inbox?> Received(Guid id, CancellationToken ct) => db.Database.SqlQuery<Inbox>($"""
        SELECT submission_id AS "Submission",payload_hash AS "Hash",ack_json AS "Ack" FROM cp6_tool_approval_fact_v2.inbox
        WHERE environment_id={scope.EnvironmentId} AND tenant_id={db.CurrentTenantId} AND event_id={id}
        """).SingleOrDefaultAsync(ct);
    private async Task Final(ScopeAuthorizationLeaseV1 lease, (ApprovalServiceIdentityV2 Service, Registration Registration) initial,
        Guid? submission, Grant? grant, bool write, CancellationToken ct)
    {
        await scope.ValidateAsync(lease, ct); await Ready(ct);
        if (await Service(ct) != initial) throw Error("AUTHORITY_CHANGED", 403);
        if (submission is Guid id && await Subject(initial.Service, id, write, initial.Registration, ct) != grant)
            throw Error("AUTHORITY_CHANGED", 403);
        await scope.ValidateAsync(lease, ct);
    }
    private async Task<ApprovalFactReceiveResultV2> Conflict(byte[] raw, ApprovalDecisionWireV2.Parsed p,
        ApprovalServiceIdentityV2 s, string? originalHash, string code, CancellationToken ct)
    {
        var now = await Now(ct); var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_tool_approval_fact_v2.conflicts(environment_id,tenant_id,id,event_id,submission_id,principal_id,original_hash,raw,raw_hash,payload_hash,code,recorded_at)
            VALUES({s.Environment},{s.Tenant},{id},{p.Value.EventId},{p.Value.SubmissionId},{s.Principal},{originalHash},{raw},
              {ApprovalCanonicalV2.Hash(raw)},{p.Full},{code},{now}) ON CONFLICT(environment_id,tenant_id,event_id,payload_hash,code) DO NOTHING
            """, ct);
        return new(409, new { code, messageKey = code, correlationId = id, retryable = false,
            fieldErrors = Array.Empty<object>(), currentRowVersion = (string?)null, resultQueryRef = Route + "/" + p.Value.EventId });
    }
    public async Task<ApprovalFactReceiveResultV2> ReceiveAsync(byte[] raw, CancellationToken ct = default)
    {
        var p = ApprovalDecisionWireV2.Parse(raw); var e = p.Value; Entry();
        await using var tx = await db.Database.BeginTransactionAsync(ct); var committing = false;
        try
        {
            var lease = await scope.AcquireAsync(ct); await Ready(ct); var auth = await Service(ct); var s = auth.Service;
            if (e.Scope != new ApprovalScopeV2(s.Environment.ToString(), s.Tenant) || e.ConsumerOwner != Owner
                || e.Subject.BizType != "WMS_TOOL_DISCARD" || e.Subject.SubjectKind != "TOOL_DISPOSITION_REQUEST") throw Error("PRODUCER_NOT_ALLOWED", 403);
            var grant = await Subject(s, e.SubmissionId, false, auth.Registration, ct);
            var frozen = await Original(e.SubmissionId, ct) ?? throw Error("RESOURCE_NOT_FOUND", 404);
            var old = await Received(e.EventId, ct);
            if (old != null && old.Submission != e.SubmissionId)
                await Subject(s, old.Submission, false, auth.Registration, ct);
            if (old != null && old.Hash == p.Full)
            {
                if (old.Submission != e.SubmissionId) throw Error("RESOURCE_NOT_FOUND", 404);
                var ack = Parse<ApprovalFactAckV2>(old.Ack);
                await Final(lease, auth, e.SubmissionId, grant, false, ct); await tx.CommitAsync(ct);
                return new(ack.ReceiveState == "GAP" ? 202 : 200, ack.ReceiveState == "GAP" ? ack : ack with { ReceiveState = "DUPLICATE" });
            }
            grant = await Subject(s, e.SubmissionId, true, auth.Registration, ct);
            ApprovalFactReceiveResultV2? conflict = null;
            if (old != null) conflict = await Conflict(raw, p, s, old.Hash, "EVENT_ID_PAYLOAD_CONFLICT", ct);
            var original = ApprovalSubmissionWireV2.Parse(frozen.Payload);
            if (original.Full != frozen.Digest) throw Error("OWNER_EVIDENCE_UNAVAILABLE");
            if (conflict == null && (e.Subject != original.Value.Subject || e.Policy != original.Value.Policy || e.RoundId != original.Value.RoundId
                || Json(e.Engineering!) != Json(original.Value.Engineering!)))
                conflict = await Conflict(raw, p, s, frozen.Digest, "SUBJECT_PAYLOAD_CONFLICT", ct);
            ApprovalDecisionSourceV2.Proof? proof = null;
            if (conflict == null && e.Sequence == "1")
                try { proof = await source.Read(p, ct); }
                catch (ScopeAuthorizationExceptionV1 error) when (error.Status == 409)
                { conflict = await Conflict(raw, p, s, null, error.Message, ct); }
            if (proof != null && proof.Mode != s.EvidenceMode) throw Error("PRODUCER_NOT_ALLOWED", 403);
            ApprovalFactReceiveResultV2 result;
            if (conflict != null) result = conflict;
            else
            {
                var state = e.Sequence == "1" ? "RECORDED" : "GAP"; Guid? stream = null; var duplicate = false;
                if (proof != null)
                {
                    var fact = await db.Database.SqlQuery<Fact>($"""
                        SELECT decision_id AS "Decision",instance_id AS "Instance",submission_id AS "Submission",core_hash AS "Core",disposition AS "Disposition"
                        FROM cp6_tool_approval_fact_v2.facts WHERE environment_id={s.Environment} AND tenant_id={s.Tenant}
                          AND (decision_id={e.DecisionId} OR instance_id={e.InstanceId} OR submission_id={e.SubmissionId})
                        """).SingleOrDefaultAsync(ct);
                    if (fact != null && (fact.Decision != e.DecisionId || fact.Instance != e.InstanceId || fact.Submission != e.SubmissionId || fact.Core != p.Core))
                        conflict = await Conflict(raw, p, s, fact.Core, "DECISION_PAYLOAD_CONFLICT", ct);
                    if (conflict == null)
                    {
                        if (fact == null)
                        {
                            await new ToolInstanceStoreV1(db).Fence(ct);
                            var dispositions = new ToolDispositionStoreV1(db); await dispositions.Fence(false, ct);
                            var request = (await dispositions.Facts(frozen.Tool, ct)).SingleOrDefault(x => x.Fact.Kind == "REQUEST");
                            if (request == null || request.Reference.Id != frozen.Request || request.Reference.Digest != e.Subject.ContentDigest.Value) state = "STALE_RECEIPT";
                            await db.Database.ExecuteSqlInterpolatedAsync($"""
                                INSERT INTO cp6_tool_approval_fact_v2.facts(environment_id,tenant_id,decision_id,submission_id,instance_id,tool_id,source_event_id,core_hash,payload,payload_hash,evidence_json,evidence_hash,disposition)
                                VALUES({s.Environment},{s.Tenant},{e.DecisionId},{e.SubmissionId},{e.InstanceId},{frozen.Tool},{proof.Original.Value.EventId},{p.Core},
                                  {proof.Original.Canonical},{proof.Original.Full},{proof.Evidence},{proof.EvidenceHash},{state})
                                """, ct);
                            stream = Guid.NewGuid();
                            await db.Database.ExecuteSqlInterpolatedAsync($"""
                                INSERT INTO cp6_tool_approval_fact_v2.streams(environment_id,tenant_id,id,decision_id,instance_id,subject_revision,application_unit_id)
                                VALUES({s.Environment},{s.Tenant},{stream.Value},{e.DecisionId},{e.InstanceId},{e.Subject.BusinessRevision},{frozen.Request.ToString()})
                                """, ct);
                        }
                        else
                        {
                            duplicate = true; state = fact.Disposition;
                            stream = await db.Database.SqlQuery<Guid>($"""
                                SELECT id AS "Value" FROM cp6_tool_approval_fact_v2.streams WHERE environment_id={s.Environment} AND tenant_id={s.Tenant} AND decision_id={e.DecisionId}
                                """).SingleAsync(ct);
                        }
                    }
                }
                if (conflict != null) result = conflict;
                else
                {
                    var inbox = Guid.NewGuid(); var at = await Now(ct);
                    var ack = new ApprovalFactAckV2("PROCESSED", e.EventId, e.DecisionId, e.SubmissionId, e.PayloadDigest, inbox,
                        state, at, "NOT_APPLIED", null, stream, stream == null ? null : ApplicationRoute + stream + "/result", state == "GAP" ? "SEQUENCE_GAP" : null);
                    var audit = Json(new { profile = "Main.Wms.ToolDisposition.fact/2", service = s, auth.Registration, grant,
                        originalSubmissionHash = frozen.Digest, sourceEventId = proof?.Original.Value.EventId, sourceEvidenceHash = proof?.EvidenceHash,
                        scope = lease.Snapshot, application = "NOT_APPLIED" });
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO cp6_tool_approval_fact_v2.inbox(environment_id,tenant_id,event_id,id,submission_id,instance_id,decision_id,principal_id,sequence,
                          raw,raw_hash,payload,payload_hash,core_hash,state,ack_json,audit_json,received_at)
                        VALUES({s.Environment},{s.Tenant},{e.EventId},{inbox},{e.SubmissionId},{e.InstanceId},{e.DecisionId},{s.Principal},{e.Sequence},
                          {raw},{ApprovalCanonicalV2.Hash(raw)},{p.Canonical},{p.Full},{p.Core},{state},{Json(ack)},{audit},{at})
                        """, ct);
                    result = new(state == "GAP" ? 202 : 200, duplicate ? ack with { ReceiveState = "DUPLICATE" } : ack);
                }
            }
            await Final(lease, auth, e.SubmissionId, grant, true, ct);
            committing = true; await tx.CommitAsync(ct); return result;
        }
        catch (Exception error)
        {
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch (Exception rollback) { throw new ScopeAuthorizationExceptionV1("OUTCOME_UNKNOWN", 504, new AggregateException(error, rollback)); }
            finally { db.ChangeTracker.Clear(); }
            if (committing) throw new ScopeAuthorizationExceptionV1("OUTCOME_UNKNOWN", 504, error);
            throw;
        }
    }
    public async Task<ApprovalFactReceiveResultV2> ReceivedAsync(Guid eventId, CancellationToken ct = default)
    {
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var lease = await scope.AcquireAsync(ct); await Ready(ct); var auth = await Service(ct); var row = await Received(eventId, ct);
        Grant? grant = null; ApprovalFactReceiveResultV2 result;
        if (row == null) result = new(200, new ApprovalFactNotObservedV2("NOT_OBSERVED", eventId, await Now(ct), Route + "/" + eventId));
        else
        {
            grant = await Subject(auth.Service, row.Submission, false, auth.Registration, ct); var ack = Parse<ApprovalFactAckV2>(row.Ack);
            result = new(ack.ReceiveState == "GAP" ? 202 : 200, ack);
        }
        await Final(lease, auth, row?.Submission, grant, false, ct); await tx.CommitAsync(ct); return result;
    }
    public async Task<ApprovalApplicationResultV2> ApplicationAsync(Guid streamId, CancellationToken ct = default)
    {
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var lease = await scope.AcquireAsync(ct); await Ready(ct); var auth = await Service(ct);
        var stream = await db.Database.SqlQuery<Stream>($"""
            SELECT s.id AS "Id",s.decision_id AS "Decision",f.submission_id AS "Submission" FROM cp6_tool_approval_fact_v2.streams s
            JOIN cp6_tool_approval_fact_v2.facts f ON f.environment_id=s.environment_id AND f.tenant_id=s.tenant_id AND f.decision_id=s.decision_id
            WHERE s.environment_id={scope.EnvironmentId} AND s.tenant_id={db.CurrentTenantId} AND s.id={streamId}
            """).SingleOrDefaultAsync(ct) ?? throw Error("RESOURCE_NOT_FOUND", 404);
        var grant = await Subject(auth.Service, stream.Submission, false, auth.Registration, ct);
        var store = new ToolApplicationReceiptStoreV2(db, scope); await store.Ready(ct);
        var receipt = await store.Latest(streamId, ct);
        ApprovalApplicationResultV2 result = receipt == null ? new ApprovalApplicationNotAppliedV2("NOT_APPLIED", streamId, await Now(ct))
            : new ApprovalApplicationFoundV2(receipt);
        await Final(lease, auth, stream.Submission, grant, false, ct); await tx.CommitAsync(ct); return result;
    }
}
