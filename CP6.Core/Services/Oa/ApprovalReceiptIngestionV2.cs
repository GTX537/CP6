using System.Globalization;
using System.Text;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed record ApprovalReceiptIngestResultV2(int Status, object Body);
public sealed record ApprovalReceiptNotObservedV2(string Kind, Guid ReceiptEventId, DateTimeOffset ObservedAt, string QueryRef);
public sealed record ApprovalReceiptPendingV2(string Kind, Guid ReceiptEventId, Guid ReceiptStreamId, Guid ReceiptId,
    string ReceiptRevision, ApprovalDigestV2 PayloadDigest, string HighestContinuousRevision, string ExpectedNextRevision, string QueryRef);
public sealed record ApprovalReceiptRecordedV2(string Kind, Guid ReceiptEventId, Guid ReceiptStreamId, Guid ReceiptId,
    string ReceiptRevision, ApprovalDigestV2 PayloadDigest, string HighestContinuousRevision, Guid? ProjectionReceiptId, string QueryRef);
public sealed record ApprovalReceiptProjectionV2(Guid ReceiptStreamId, string HighestContinuousRevision,
    ApprovalApplicationReceiptV2? Receipt, bool PendingGap);

/// <summary>Workflow TxR: current service authority, native Submission, stream projection, then Inbox.
/// Receipt history is separate from the immutable decision ACK. This never executes Owner business effects.</summary>
public sealed class ApprovalReceiptIngestionV2(CP6Context db, ScopeAuthorizationFenceV1 scope, IApprovalServiceAuthorityV2 authority)
{
    public const string Route = "/internal/oa/app/v2/application-receipts";
    public const string QueryRoute = "/internal/oa/app/v2/application-receipt-ingestions/";
    public const string ProjectionRoute = "/internal/oa/app/v2/application-receipt-projections/";
    internal const string Owner = "Main.Wms.ToolDisposition.v1";
    private static ScopeAuthorizationExceptionV1 Error(string code, int status = 503, Exception? inner = null) => new(code, status, inner);
    private static string Revision(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Json(object value) => Encoding.UTF8.GetString(ApprovalCanonicalV2.Bytes(value));
    private sealed record Registration(string Owner, Guid Revision, bool Enabled, bool Receive, bool Query, string Mode);
    private sealed record Grant(Guid Revision, bool Enabled, bool Receive, bool Query);
    private sealed record Source(byte[] Payload, string Hash, string Mode);
    private sealed record Projection(Guid Id, Guid Submission, Guid Instance, Guid Decision, string Owner, string SubjectRevision,
        string Unit, long Highest, Guid? Latest, Guid Version);
    private sealed record Inbox(Guid Event, Guid Receipt, Guid Stream, Guid Submission, long Revision, byte[] Payload, string Hash);
    private void Entry()
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction != null || db.ChangeTracker.HasChanges()
            || System.Transactions.Transaction.Current != null) throw Error("RECEIPT_TRANSACTION_REQUIRED");
    }
    private async Task Ready(CancellationToken ct)
    {
        if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_approval_receipt_v2.ready() AS \"Value\"").SingleAsync(ct))
            throw Error("RECEIPT_STORE_UNAVAILABLE");
    }
    private Task<DateTimeOffset> Now(CancellationToken ct) => db.Database.SqlQueryRaw<DateTimeOffset>(
        "SELECT date_trunc('milliseconds',clock_timestamp()) AS \"Value\"").SingleAsync(ct);
    private async Task<(ApprovalServiceIdentityV2 Service, Registration Registration)> Service(CancellationToken ct)
    {
        var s = await authority.CurrentAsync(ct);
        if (s.Environment != scope.EnvironmentId || s.Tenant != db.CurrentTenantId || s.Principal == Guid.Empty || s.Audience != "CP6.Services")
            throw Error("PRODUCER_NOT_ALLOWED", 403);
        var r = await db.Database.SqlQuery<Registration>($"""
            SELECT owner AS "Owner",revision AS "Revision",enabled AS "Enabled",can_receive AS "Receive",can_query AS "Query",evidence_mode AS "Mode"
            FROM cp6_approval_receipt_v2.registrations WHERE environment_id={s.Environment} AND tenant_id={s.Tenant} AND principal_id={s.Principal}
            """).SingleOrDefaultAsync(ct);
        if (r == null || !r.Enabled || !r.Query || r.Owner is not (ApprovalBusinessProfileV2.ToolOwner or ApprovalBusinessProfileV2.DocumentOwner or ApprovalBusinessProfileV2.RequirementSetOwner or ApprovalBusinessProfileV2.IterationOwner or ApprovalBusinessProfileV2.LocationRuleOwner) || r.Mode != s.EvidenceMode) throw Error("PRODUCER_NOT_ALLOWED", 403);
        if(r.Owner==ApprovalBusinessProfileV2.DocumentOwner
            && !await db.Database.SqlQueryRaw<bool>("SELECT cp6_document_review_application_v1.ready() AS \"Value\"").SingleAsync(ct))
            throw Error("RECEIPT_SOURCE_UNAVAILABLE");
        if(r.Owner==ApprovalBusinessProfileV2.RequirementSetOwner
            && !await db.Database.SqlQueryRaw<bool>("SELECT cp6_requirement_set_review_application_v1.ready() AS \"Value\"").SingleAsync(ct))
            throw Error("RECEIPT_SOURCE_UNAVAILABLE");
        if(r.Owner==ApprovalBusinessProfileV2.LocationRuleOwner
            && !await db.Database.SqlQueryRaw<bool>("SELECT cp6_location_rule_review_application_v1.ready() AS \"Value\"").SingleAsync(ct))
            throw Error("RECEIPT_SOURCE_UNAVAILABLE");
        if(r.Owner==ApprovalBusinessProfileV2.IterationOwner
            && !await db.Database.SqlQueryRaw<bool>("SELECT cp6_iteration_review_application_v1.ready() AS \"Value\"").SingleAsync(ct))
            throw Error("RECEIPT_SOURCE_UNAVAILABLE");
        return (s, r);
    }
    private async Task<Grant> Subject(ApprovalServiceIdentityV2 s, Guid submission, Registration registration, bool write, CancellationToken ct)
    {
        var g = await db.Database.SqlQuery<Grant>($"""
            SELECT revision AS "Revision",enabled AS "Enabled",can_receive AS "Receive",can_query AS "Query"
            FROM cp6_approval_receipt_v2.subject_grants WHERE environment_id={s.Environment} AND tenant_id={s.Tenant}
              AND principal_id={s.Principal} AND submission_id={submission} AND owner={registration.Owner}
            """).SingleOrDefaultAsync(ct);
        if (g == null || !g.Enabled || !g.Query) throw Error("RESOURCE_NOT_FOUND", 404);
        if (write && (!s.CanSubmit || !registration.Receive || !g.Receive)) throw Error("COMMAND_FORBIDDEN", 403);
        return g;
    }
    private async Task LockSubmission(Guid submission,string businessOwner,CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM cp6_approval_intake_v2.submissions WHERE tenant_id={db.CurrentTenantId}
              AND environment_id={scope.EnvironmentId} AND owner={businessOwner} AND id={submission} FOR UPDATE
            """).ToArrayAsync(ct);
        if (rows.Length != 1) throw Error("RESOURCE_NOT_FOUND", 404);
    }
    private Task<Projection?> Stream(Guid id, CancellationToken ct) => db.Database.SqlQuery<Projection>($"""
        SELECT id AS "Id",submission_id AS "Submission",instance_id AS "Instance",decision_id AS "Decision",owner AS "Owner",
          subject_revision AS "SubjectRevision",application_unit_id AS "Unit",highest_revision AS "Highest",latest_receipt_id AS "Latest",row_version AS "Version"
        FROM cp6_approval_receipt_v2.streams WHERE environment_id={scope.EnvironmentId} AND tenant_id={db.CurrentTenantId} AND id={id} FOR UPDATE
        """).SingleOrDefaultAsync(ct);
    private Task<Inbox?> Event(Guid id, CancellationToken ct) => db.Database.SqlQuery<Inbox>($"""
        SELECT event_id AS "Event",receipt_id AS "Receipt",stream_id AS "Stream",submission_id AS "Submission",revision AS "Revision",payload AS "Payload",payload_hash AS "Hash"
        FROM cp6_approval_receipt_v2.inbox WHERE environment_id={scope.EnvironmentId} AND tenant_id={db.CurrentTenantId} AND event_id={id}
        """).SingleOrDefaultAsync(ct);
    private Task<Inbox?> At(Guid stream, long revision, CancellationToken ct) => db.Database.SqlQuery<Inbox>($"""
        SELECT event_id AS "Event",receipt_id AS "Receipt",stream_id AS "Stream",submission_id AS "Submission",revision AS "Revision",payload AS "Payload",payload_hash AS "Hash"
        FROM cp6_approval_receipt_v2.inbox WHERE environment_id={scope.EnvironmentId} AND tenant_id={db.CurrentTenantId} AND stream_id={stream} AND revision={revision}
        """).SingleOrDefaultAsync(ct);
    private static ApprovalApplicationReceiptV2 Read(Inbox row)
    {
        if (ApprovalCanonicalV2.Hash(row.Payload) != row.Hash) throw Error("RECEIPT_EVIDENCE_UNAVAILABLE");
        var e = ApprovalApplicationReceiptWireV2.Parse(row.Payload);
        if (e.ReceiptEventId != row.Event || e.ReceiptId != row.Receipt || e.ReceiptStreamId != row.Stream || e.SubmissionId != row.Submission
            || e.ReceiptRevision != Revision(row.Revision)) throw Error("RECEIPT_EVIDENCE_UNAVAILABLE");
        return e;
    }
    private async Task Final(ScopeAuthorizationLeaseV1 lease, (ApprovalServiceIdentityV2 Service, Registration Registration) auth,
        Guid? submission, Grant? grant, bool write, CancellationToken ct)
    {
        await scope.ValidateAsync(lease, ct); await Ready(ct);
        if (await Service(ct) != auth || submission is Guid id && await Subject(auth.Service, id, auth.Registration, write, ct) != grant)
            throw Error("AUTHORITY_CHANGED", 403);
        await scope.ValidateAsync(lease, ct);
    }
    private static ApprovalReceiptIngestResultV2 Result(Inbox row, Projection p)
    {
        var e = Read(row); var query = QueryRoute + row.Event;
        return row.Revision <= p.Highest
            ? new(200, new ApprovalReceiptRecordedV2("RECORDED", row.Event, row.Stream, row.Receipt, e.ReceiptRevision,
                e.PayloadDigest, Revision(p.Highest), p.Latest, query))
            : new(202, new ApprovalReceiptPendingV2("PENDING_GAP", row.Event, row.Stream, row.Receipt, e.ReceiptRevision,
                e.PayloadDigest, Revision(p.Highest), Revision(checked(p.Highest + 1)), query));
    }
    private async Task<ApprovalReceiptIngestResultV2> Conflict(byte[] raw, ApprovalApplicationReceiptV2 e, string hash,
        Guid principal, string? original, string code, CancellationToken ct)
    {
        var id = Guid.NewGuid(); var at = await Now(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_approval_receipt_v2.conflicts(environment_id,tenant_id,id,event_id,submission_id,principal_id,original_hash,raw,raw_hash,payload_hash,code,recorded_at)
            VALUES({scope.EnvironmentId},{db.CurrentTenantId},{id},{e.ReceiptEventId},{e.SubmissionId},{principal},{original},{raw},{ApprovalCanonicalV2.Hash(raw)},{hash},{code},{at})
            ON CONFLICT(environment_id,tenant_id,event_id,payload_hash,code) DO NOTHING
            """, ct);
        return new(409, new { code, messageKey = code, correlationId = id, retryable = false,
            fieldErrors = Array.Empty<object>(), currentRowVersion = (string?)null, resultQueryRef = QueryRoute + e.ReceiptEventId });
    }
    private async Task<string?> SourceConflict(ApprovalApplicationReceiptV2 e, byte[] canonical, string hash, string mode, CancellationToken ct)
    {
        var source = await db.Database.SqlQuery<Source>($"""
            SELECT d.payload AS "Payload",d.payload_digest AS "Hash",s.selection_json::jsonb->'registration'->>'evidenceMode' AS "Mode"
            FROM cp6_approval_decision_v2.decisions d JOIN cp6_approval_intake_v2.submissions s
              ON s.environment_id=d.environment_id AND s.tenant_id=d.tenant_id AND s.id=d.submission_id AND s.owner={e.ProducerOwner}
            WHERE d.environment_id={scope.EnvironmentId} AND d.tenant_id={db.CurrentTenantId} AND d.id={e.DecisionId}
              AND d.submission_id={e.SubmissionId} AND d.instance_id={e.InstanceId}
            """).SingleOrDefaultAsync(ct);
        if (source == null) return "RECEIPT_DECISION_CONFLICT";
        var decision = ApprovalDecisionWireV2.Parse(source.Payload);
        if (decision.Full != source.Hash) throw Error("DECISION_EVIDENCE_UNAVAILABLE");
        if (source.Mode != mode) throw Error("PRODUCER_NOT_ALLOWED", 403);
        if (decision.Value.Subject != e.Subject || decision.Value.PayloadDigest != e.DecisionPayloadDigest || decision.Value.ConsumerOwner != e.ProducerOwner)
            return "RECEIPT_DECISION_CONFLICT";
        // Closed registered native business provider: original bytes prove provenance without entering or locking the Owner transaction.
        var original = await db.Database.SqlQuery<byte[]>(ApprovalBusinessProfileV2.For(e.ProducerOwner).FactSql($"""
            SELECT payload AS "Value" FROM cp6_tool_application_v2.receipts
            WHERE environment_id={scope.EnvironmentId} AND tenant_id={db.CurrentTenantId} AND id={e.ReceiptId} AND event_id={e.ReceiptEventId}
              AND payload_hash={hash}
            """)).SingleOrDefaultAsync(ct);
        return original == null || !original.AsSpan().SequenceEqual(canonical) ? "RECEIPT_SOURCE_CONFLICT" : null;
    }
    public async Task<ApprovalReceiptIngestResultV2> ReceiveAsync(byte[] raw, CancellationToken ct = default)
    {
        var e = ApprovalApplicationReceiptWireV2.Parse(raw); var canonical = ApprovalCanonicalV2.Bytes(e); var hash = ApprovalCanonicalV2.Hash(canonical);
        var revision = long.Parse(e.ReceiptRevision, CultureInfo.InvariantCulture); Entry();
        await using var tx = await db.Database.BeginTransactionAsync(ct); var committing = false;
        try
        {
            var lease = await scope.AcquireAsync(ct); await Ready(ct); var auth = await Service(ct); var s = auth.Service;
            if (e.Scope != new ApprovalScopeV2(s.Environment.ToString(), s.Tenant) || e.ProducerOwner != auth.Registration.Owner
                || e.Subject.BizType != ApprovalBusinessProfileV2.For(auth.Registration.Owner).BizType || e.Subject.SubjectKind != ApprovalBusinessProfileV2.For(auth.Registration.Owner).SubjectKind || e.Action != ApprovalBusinessProfileV2.For(auth.Registration.Owner).ApplicationAction)
                throw Error("PRODUCER_NOT_ALLOWED", 403);
            var grant = await Subject(s, e.SubmissionId, auth.Registration, false, ct); await LockSubmission(e.SubmissionId,auth.Registration.Owner,ct);
            var projection = await Stream(e.ReceiptStreamId, ct);
            _ = await db.Database.SqlQuery<Guid>($"""
                SELECT event_id AS "Value" FROM cp6_approval_receipt_v2.inbox WHERE environment_id={s.Environment}
                  AND tenant_id={s.Tenant} AND event_id={e.ReceiptEventId} FOR UPDATE
                """).ToArrayAsync(ct);
            var old = await Event(e.ReceiptEventId, ct);
            if (old != null && old.Submission != e.SubmissionId) await Subject(s, old.Submission, auth.Registration, false, ct);
            if (old != null && old.Hash == hash)
            {
                var result = Result(old, projection ?? throw Error("RECEIPT_EVIDENCE_UNAVAILABLE"));
                await Final(lease, auth, e.SubmissionId, grant, false, ct); await tx.CommitAsync(ct); return result;
            }
            grant = await Subject(s, e.SubmissionId, auth.Registration, true, ct);
            string? code = old != null ? "RECEIPT_EVENT_PAYLOAD_CONFLICT" : null;
            string? originalHash = old?.Hash;
            if (code == null && projection != null && (projection.Submission != e.SubmissionId || projection.Instance != e.InstanceId
                || projection.Decision != e.DecisionId || projection.Owner != e.ProducerOwner || projection.SubjectRevision != e.Subject.BusinessRevision
                || projection.Unit != ApprovalBusinessProfileV2.For(e.ProducerOwner).ApplicationUnit(e.Subject))) code = "RECEIPT_STREAM_IDENTITY_CONFLICT";
            if (code == null)
            {
                var otherStream = await db.Database.SqlQuery<Guid>($"""
                    SELECT id AS "Value" FROM cp6_approval_receipt_v2.streams WHERE environment_id={s.Environment} AND tenant_id={s.Tenant}
                      AND instance_id={e.InstanceId} AND subject_revision={e.Subject.BusinessRevision} AND application_unit_id={ApprovalBusinessProfileV2.For(e.ProducerOwner).ApplicationUnit(e.Subject)} AND id<>{e.ReceiptStreamId}
                    """).ToArrayAsync(ct);
                if (otherStream.Length != 0) code = "RECEIPT_STREAM_IDENTITY_CONFLICT";
            }
            if (code == null)
            {
                var fork = await db.Database.SqlQuery<string>($"""
                    SELECT payload_hash AS "Value" FROM cp6_approval_receipt_v2.inbox WHERE environment_id={s.Environment} AND tenant_id={s.Tenant}
                      AND (receipt_id={e.ReceiptId} OR (stream_id={e.ReceiptStreamId} AND revision={revision})) LIMIT 1
                    """).SingleOrDefaultAsync(ct);
                if (fork != null) { code = "RECEIPT_REVISION_CONFLICT"; originalHash = fork; }
            }
            code ??= await SourceConflict(e, canonical, hash, s.EvidenceMode, ct);
            ApprovalReceiptIngestResultV2 output;
            if (code != null) output = await Conflict(raw, e, hash, s.Principal, originalHash, code, ct);
            else
            {
                if (projection == null)
                {
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO cp6_approval_receipt_v2.streams(environment_id,tenant_id,id,owner,submission_id,instance_id,decision_id,subject_revision,application_unit_id,row_version)
                        VALUES({s.Environment},{s.Tenant},{e.ReceiptStreamId},{e.ProducerOwner},{e.SubmissionId},{e.InstanceId},{e.DecisionId},{e.Subject.BusinessRevision},{ApprovalBusinessProfileV2.For(e.ProducerOwner).ApplicationUnit(e.Subject)},{Guid.NewGuid()})
                        """, ct);
                    projection = await Stream(e.ReceiptStreamId, ct) ?? throw Error("RECEIPT_EVIDENCE_UNAVAILABLE");
                }
                var at = await Now(ct); var audit = Json(new { s.Principal, s.Client, s.EvidenceMode, auth.Registration.Revision, subjectGrant = grant.Revision,
                    e.ReceiptEventId, e.ReceiptId, e.ReceiptStreamId, e.ReceiptRevision, payloadHash = hash, scope = lease.Snapshot, receivedAt = at });
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_approval_receipt_v2.inbox(environment_id,tenant_id,event_id,receipt_id,stream_id,revision,previous_id,submission_id,principal_id,raw,raw_hash,payload,payload_hash,audit_json,audit_hash,received_at)
                    VALUES({s.Environment},{s.Tenant},{e.ReceiptEventId},{e.ReceiptId},{e.ReceiptStreamId},{revision},{e.PreviousReceiptId},{e.SubmissionId},{s.Principal},{raw},
                      {ApprovalCanonicalV2.Hash(raw)},{canonical},{hash},{audit},{ApprovalCanonicalV2.Hash(Encoding.UTF8.GetBytes(audit))},{at})
                    """, ct);
                projection = await Drain(projection, s.Principal, ct);
                var maximum = await db.Database.SqlQuery<long>($"""
                    SELECT max(revision) AS "Value" FROM cp6_approval_receipt_v2.inbox WHERE environment_id={s.Environment} AND tenant_id={s.Tenant} AND stream_id={e.ReceiptStreamId}
                    """).SingleAsync(ct);
                if (maximum > projection.Highest)
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO cp6_approval_receipt_v2.gaps(environment_id,tenant_id,stream_id,target_revision,completed)
                        VALUES({s.Environment},{s.Tenant},{e.ReceiptStreamId},{maximum},false)
                        ON CONFLICT(environment_id,tenant_id,stream_id) DO UPDATE SET target_revision=greatest(cp6_approval_receipt_v2.gaps.target_revision,EXCLUDED.target_revision),completed=false
                        """, ct);
                else
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        UPDATE cp6_approval_receipt_v2.gaps SET completed=true,error_code=NULL WHERE environment_id={s.Environment} AND tenant_id={s.Tenant}
                          AND stream_id={e.ReceiptStreamId} AND target_revision<={projection.Highest}
                        """, ct);
                output = Result(new(e.ReceiptEventId, e.ReceiptId, e.ReceiptStreamId, e.SubmissionId, revision, canonical, hash), projection);
            }
            await Final(lease, auth, e.SubmissionId, grant, true, ct); committing = true; await tx.CommitAsync(ct); return output;
        }
        catch (Exception ex) when (committing)
        { throw Error("RECEIPT_OUTCOME_UNKNOWN", 503, ex); }
    }
    private async Task<Projection> Drain(Projection p, Guid principal, CancellationToken ct)
    {
        while (p.Highest < long.MaxValue)
        {
            var row = await At(p.Id, p.Highest + 1, ct); if (row == null) break;
            var e = Read(row);
            if (e.PreviousReceiptId != p.Latest) throw Error("RECEIPT_CHAIN_CONFLICT", 409);
            if (p.Latest != null && Read(await At(p.Id, p.Highest, ct) ?? throw Error("RECEIPT_EVIDENCE_UNAVAILABLE")).Application != "BLOCKED")
                throw Error("RECEIPT_CHAIN_TERMINAL", 409);
            var at = await Now(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_approval_receipt_v2.history(environment_id,tenant_id,stream_id,revision,receipt_id,event_id,principal_id,recorded_at)
                VALUES({scope.EnvironmentId},{db.CurrentTenantId},{p.Id},{row.Revision},{row.Receipt},{row.Event},{principal},{at})
                """, ct);
            var advanced = await Stream(p.Id, ct) ?? throw Error("RECEIPT_EVIDENCE_UNAVAILABLE");
            if (advanced.Highest != row.Revision || advanced.Latest != row.Receipt || advanced.Version == p.Version)
                throw Error("RECEIPT_PROJECTION_INCOMPLETE");
            p = advanced;
        }
        return p;
    }
    public async Task<ApprovalReceiptIngestResultV2> OriginalAsync(Guid eventId, CancellationToken ct = default)
    {
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var lease = await scope.AcquireAsync(ct); await Ready(ct); var auth = await Service(ct); var row = await Event(eventId, ct);
        Grant? grant = null; ApprovalReceiptIngestResultV2 result;
        if (row == null) result = new(200, new ApprovalReceiptNotObservedV2("NOT_OBSERVED", eventId, await Now(ct), QueryRoute + eventId));
        else
        {
            grant = await Subject(auth.Service, row.Submission, auth.Registration, false, ct); await LockSubmission(row.Submission,auth.Registration.Owner,ct);
            result = Result(row, await Stream(row.Stream, ct) ?? throw Error("RECEIPT_EVIDENCE_UNAVAILABLE"));
        }
        await Final(lease, auth, row?.Submission, grant, false, ct); await tx.CommitAsync(ct); return result;
    }
    public async Task<ApprovalReceiptProjectionV2> CurrentAsync(Guid streamId, CancellationToken ct = default)
    {
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var lease = await scope.AcquireAsync(ct); await Ready(ct); var auth = await Service(ct);
        // Discover identity without acquiring a stream row lock; Submission is always locked first.
        var submission = await db.Database.SqlQuery<Guid>($"""
            SELECT submission_id AS "Value" FROM cp6_approval_receipt_v2.streams WHERE environment_id={scope.EnvironmentId} AND tenant_id={db.CurrentTenantId} AND id={streamId}
            """).SingleOrDefaultAsync(ct);
        if (submission == Guid.Empty) throw Error("RESOURCE_NOT_FOUND", 404);
        var grant = await Subject(auth.Service, submission, auth.Registration, false, ct); await LockSubmission(submission,auth.Registration.Owner,ct);
        var p = await Stream(streamId, ct) ?? throw Error("RECEIPT_EVIDENCE_UNAVAILABLE");
        var gap = await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT FROM cp6_approval_receipt_v2.gaps WHERE environment_id={scope.EnvironmentId} AND tenant_id={db.CurrentTenantId} AND stream_id={streamId} AND NOT completed) AS "Value"
            """).SingleAsync(ct);
        var current = p.Latest == null ? null : Read(await At(p.Id, p.Highest, ct) ?? throw Error("RECEIPT_EVIDENCE_UNAVAILABLE"));
        await Final(lease, auth, submission, grant, false, ct); await tx.CommitAsync(ct); return new(streamId, Revision(p.Highest), current, gap);
    }
    public static byte[] ResponseBytes(object body) => body is ApprovalReceiptNotObservedV2 or ApprovalReceiptPendingV2
        or ApprovalReceiptRecordedV2 or ApprovalReceiptProjectionV2 ? ApprovalCanonicalV2.Bytes(body) : throw new ArgumentException("Unsupported receipt response.", nameof(body));
}
