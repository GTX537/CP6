using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wms;
using CP6.Entity.Contracts.Wire;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Oa.ApprovalSubmissionWireV2;
namespace CP6.Core.Services.Plm;
public interface IRequirementSetApplicationTransportV1:IToolApplicationTransportV2 {}
/// <summary>Returns native immutable application receipt bytes. Each network exchange runs outside
/// database transactions. An ingestion acknowledgement never re-executes a business application.</summary>
public sealed class RequirementSetApplicationDeliveryV1(CP6Context db, ScopeAuthorizationFenceV1 scope, IRequirementSetApplicationTransportV1 transport)
{
    public const string Producer = "Main.Plm.RequirementSet.v1";
    internal sealed record Original(Guid Id, Guid Submission, byte[] Payload, string Digest);
    internal sealed record Registration(Guid Revision, bool Enabled, bool Query, bool Receive, string Mode);
    internal sealed record Grant(Guid Revision, bool Enabled, bool Query, bool Receive);
    internal sealed record AuthorityProof(Registration Registration, Grant Grant);
    internal sealed record Lease(Original Original, ApprovalApplicationReceiptV2 Envelope, ToolApprovalDeliveryRouteV1 Route,
        AuthorityProof Authority, long Epoch, Guid Token);
    internal sealed record ReceiptResult(string Kind, string State, string Json);
    private static string Json(object value) => Encoding.UTF8.GetString(ApprovalCanonicalV2.Bytes(value));
    private static string Fingerprint(ToolApprovalDeliveryRouteV1 route) => ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(route));
    private void Clean()
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction != null || db.ChangeTracker.HasChanges()
            || System.Transactions.Transaction.Current != null) throw Error("DELIVERY_TRANSACTION_REQUIRED", 503);
    }
    private async Task Ready(CancellationToken ct)
    {
        await scope.Root(ct);
        if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_requirement_set_application_delivery_v1.ready() AS \"Value\"").SingleAsync(ct))
            throw Error("DELIVERY_STORE_UNAVAILABLE", 503);
    }
    private async Task<Registration> Registered(ToolApprovalDeliveryRouteV1 r, CancellationToken ct)
    {
        if (r.Environment != scope.EnvironmentId || r.Tenant != db.CurrentTenantId || transport.Route(r.Tenant) != r)
            throw Error("DELIVERY_ROUTE_CHANGED", 403);
        var registration = await db.Database.SqlQuery<Registration>($"""
            SELECT revision AS "Revision",enabled AS "Enabled",can_query AS "Query",can_receive AS "Receive",evidence_mode AS "Mode"
            FROM cp6_approval_receipt_v2.registrations WHERE environment_id={r.Environment} AND tenant_id={r.Tenant} AND principal_id={r.Principal} AND owner={Producer}
            """).SingleOrDefaultAsync(ct);
        if (registration is not { Enabled: true, Query: true } || registration.Mode != r.EvidenceMode
            || !await db.Sys_Tenants.AsNoTracking().AnyAsync(t => t.Id == r.Tenant && t.Enable && (t.ExpireDate == null || t.ExpireDate > DateTime.Now), ct))
            throw Error("DELIVERY_SERVICE_UNAVAILABLE", 403);
        return registration;
    }
    private async Task<AuthorityProof> Authority(ToolApprovalDeliveryRouteV1 r, Guid submission, CancellationToken ct)
    {
        var registration = await Registered(r, ct);
        var grant = await db.Database.SqlQuery<Grant>($"""
            SELECT revision AS "Revision",enabled AS "Enabled",can_query AS "Query",can_receive AS "Receive"
            FROM cp6_approval_receipt_v2.subject_grants WHERE environment_id={r.Environment} AND tenant_id={r.Tenant}
              AND principal_id={r.Principal} AND submission_id={submission} AND owner={Producer}
            """).SingleOrDefaultAsync(ct);
        if (grant is not { Enabled: true, Query: true }) throw Error("DELIVERY_SUBJECT_UNAVAILABLE", 403);
        return new(registration, grant);
    }
    internal async Task<Lease?> ClaimAsync(Guid tenant, CancellationToken ct = default)
    {
        Clean(); if (tenant != db.CurrentTenantId) throw Error("DELIVERY_SCOPE_MISMATCH", 403);
        var route = transport.Route(tenant); if (route == null) return null;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var fence = await scope.AcquireAsync(ct); await Ready(ct); var registered = await Registered(route, ct);
        var routeHash = Fingerprint(route);
        // The native authorization root and Scope lock serialize claims, including a first absent delivery row.
        var original = await db.Database.SqlQuery<Original>($"""
            SELECT o.event_id AS "Id",r.submission_id AS "Submission",o.payload AS "Payload",o.payload_hash AS "Digest"
            FROM cp6_requirement_set_review_application_v1.outbox o
            JOIN cp6_requirement_set_review_application_v1.receipts r ON r.environment_id=o.environment_id AND r.tenant_id=o.tenant_id AND r.id=o.receipt_id
            JOIN cp6_approval_receipt_v2.subject_grants g ON g.environment_id=o.environment_id AND g.tenant_id=o.tenant_id
              AND g.submission_id=r.submission_id AND g.owner={Producer} AND g.principal_id={route.Principal} AND g.enabled AND g.can_query
            LEFT JOIN cp6_requirement_set_application_delivery_v1.delivery d ON d.environment_id=o.environment_id AND d.tenant_id=o.tenant_id AND d.outbox_id=o.event_id
            WHERE o.environment_id={route.Environment} AND o.tenant_id={tenant}
              AND (d.outbox_id IS NULL OR (d.lease_until<=clock_timestamp() AND
                ((d.state IN('OUTCOME_UNKNOWN','PENDING_GAP') AND d.next_attempt<=clock_timestamp()) OR
                 (d.state='BLOCKED' AND (d.route_hash<>{routeHash} OR d.registration_revision<>{registered.Revision} OR d.subject_revision<>g.revision)))))
            ORDER BY coalesce(d.next_attempt,'-infinity'::timestamptz),r.revision,o.event_id LIMIT 1
            """).SingleOrDefaultAsync(ct);
        if (original == null) return null;
        var authority = await Authority(route, original.Submission, ct);
        var savedRoute = await db.Database.SqlQuery<string>($"""
            SELECT route_json AS "Value" FROM cp6_requirement_set_application_delivery_v1.delivery
            WHERE environment_id={route.Environment} AND tenant_id={tenant} AND outbox_id={original.Id}
            """).SingleOrDefaultAsync(ct);
        if (savedRoute != null)
        {
            var prior = JsonSerializer.Deserialize<ToolApprovalDeliveryRouteV1>(savedRoute, ApprovalBindingOwnerV2.Json) ?? throw Error("DELIVERY_ROUTE_INVALID", 503);
            if (prior != route with { Version = prior.Version }) throw Error("DELIVERY_ROUTE_IDENTITY_CHANGED", 409);
        }
        var e = ApprovalApplicationReceiptWireV2.Parse(original.Payload);
        if (ApprovalCanonicalV2.Hash(original.Payload) != original.Digest || !ApprovalCanonicalV2.Bytes(e).AsSpan().SequenceEqual(original.Payload)
            || e.ReceiptEventId != original.Id || e.SubmissionId != original.Submission || e.Scope != new ApprovalScopeV2(route.Environment.ToString(), tenant)
            || e.ProducerOwner != Producer) throw Error("DELIVERY_ORIGINAL_INVALID", 503);
        var sourceMode = await db.Database.SqlQuery<string>($"""
            SELECT selection_json::jsonb->'registration'->>'evidenceMode' AS "Value" FROM cp6_approval_intake_v2.submissions
            WHERE tenant_id={tenant} AND environment_id={route.Environment} AND id={e.SubmissionId} AND owner={Producer}
            """).SingleAsync(ct);
        if (sourceMode != route.EvidenceMode) throw Error("DELIVERY_SOURCE_MODE_MISMATCH", 403);
        var token = Guid.NewGuid(); var routeJson = Json(route); var authorityJson = Json(authority);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_requirement_set_application_delivery_v1.delivery(environment_id,tenant_id,outbox_id,state,epoch,lease_token,lease_until,next_attempt,
              route_hash,route_json,registration_revision,subject_revision)
            VALUES({route.Environment},{tenant},{original.Id},'OUTCOME_UNKNOWN',1,{token},clock_timestamp()+interval '90 seconds',clock_timestamp(),
              {routeHash},{routeJson},{authority.Registration.Revision},{authority.Grant.Revision})
            ON CONFLICT(environment_id,tenant_id,outbox_id) DO UPDATE SET state='OUTCOME_UNKNOWN',epoch=cp6_requirement_set_application_delivery_v1.delivery.epoch+1,
              lease_token=EXCLUDED.lease_token,lease_until=EXCLUDED.lease_until,route_hash=EXCLUDED.route_hash,route_json=EXCLUDED.route_json,
              registration_revision=EXCLUDED.registration_revision,subject_revision=EXCLUDED.subject_revision,error_code=NULL,ack_json=NULL
            """, ct);
        var epoch = await db.Database.SqlQuery<long>($"""
            SELECT epoch AS "Value" FROM cp6_requirement_set_application_delivery_v1.delivery WHERE environment_id={route.Environment} AND tenant_id={tenant} AND outbox_id={original.Id}
            """).SingleAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_requirement_set_application_delivery_v1.attempts(environment_id,tenant_id,outbox_id,epoch,lease_token,route_json,registration_revision,subject_revision,authority_json,started_at)
            VALUES({route.Environment},{tenant},{original.Id},{epoch},{token},{routeJson},{authority.Registration.Revision},{authority.Grant.Revision},{authorityJson},clock_timestamp())
            """, ct);
        await scope.ValidateAsync(fence, ct);
        if (await Authority(route, original.Submission, ct) != authority) throw Error("DELIVERY_AUTHORITY_CHANGED", 403);
        await tx.CommitAsync(ct); return new(original, e, route, authority, epoch, token);
    }

    internal static ReceiptResult Receipt(byte[] bytes, ApprovalApplicationReceiptV2 e)
    {
        try
        {
            using var doc = WorkflowCommandJsonV1.Parse(bytes); var root = doc.RootElement;
            var kind = root.GetProperty("kind").GetString();
            if (kind == "NOT_OBSERVED")
            {
                Shape(root, "kind", "receiptEventId", "observedAt", "queryRef"); Id(root.GetProperty("receiptEventId"));
                if (root.GetProperty("receiptEventId").GetGuid() != e.ReceiptEventId || root.GetProperty("queryRef").GetString() != ApprovalReceiptIngestionV2.QueryRoute + e.ReceiptEventId) throw new JsonException();
                _ = root.GetProperty("observedAt").GetDateTimeOffset(); return new(kind, kind, Json(root));
            }
            if (kind == "PENDING_GAP") Shape(root, "kind", "receiptEventId", "receiptStreamId", "receiptId", "receiptRevision", "payloadDigest", "highestContinuousRevision", "expectedNextRevision", "queryRef");
            else if (kind == "RECORDED") Shape(root, "kind", "receiptEventId", "receiptStreamId", "receiptId", "receiptRevision", "payloadDigest", "highestContinuousRevision", "projectionReceiptId", "queryRef");
            else throw new JsonException();
            foreach (var n in new[] { "receiptEventId", "receiptStreamId", "receiptId" }) Id(root.GetProperty(n));
            Digest(root.GetProperty("payloadDigest"));
            if (root.GetProperty("receiptEventId").GetGuid() != e.ReceiptEventId || root.GetProperty("receiptStreamId").GetGuid() != e.ReceiptStreamId
                || root.GetProperty("receiptId").GetGuid() != e.ReceiptId || root.GetProperty("receiptRevision").GetString() != e.ReceiptRevision
                || root.GetProperty("payloadDigest").Deserialize<ApprovalDigestV2>(ApprovalBindingOwnerV2.Json) != e.PayloadDigest
                || root.GetProperty("queryRef").GetString() != ApprovalReceiptIngestionV2.QueryRoute + e.ReceiptEventId) throw new JsonException();
            var raw = root.GetProperty("highestContinuousRevision").GetString();
            if (!long.TryParse(raw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var high)
                || high < 0 || raw != high.ToString(System.Globalization.CultureInfo.InvariantCulture)) throw new JsonException();
            var revision = long.Parse(e.ReceiptRevision, System.Globalization.CultureInfo.InvariantCulture);
            if (kind == "PENDING_GAP")
            {
                if (high >= revision - 1 || root.GetProperty("expectedNextRevision").GetString() != checked(high + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)) throw new JsonException();
            }
            else
            {
                Id(root.GetProperty("projectionReceiptId"));
                if (high < revision || high == revision && root.GetProperty("projectionReceiptId").GetGuid() != e.ReceiptId) throw new JsonException();
            }
            return new(kind!, kind!, Json(root));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or FormatException or KeyNotFoundException or OverflowException)
        { throw Error("DELIVERY_RECEIPT_INVALID", 409); }
    }
    internal async Task<string> FinishAsync(Lease lease, string state, string? error, ReceiptResult? receipt, CancellationToken ct = default)
    {
        Clean(); var r = lease.Route; await using var tx = await db.Database.BeginTransactionAsync(ct);
        var fence = await scope.AcquireAsync(ct); await Ready(ct);
        var authority = await Authority(r, lease.Original.Submission, ct);
        if (authority != lease.Authority) throw Error("DELIVERY_AUTHORITY_CHANGED", 403);
        if (receipt != null)
        {
            receipt = Receipt(Encoding.UTF8.GetBytes(receipt.Json), lease.Envelope);
            if (receipt.Kind == "NOT_OBSERVED" || receipt.State != state) throw Error("DELIVERY_RECEIPT_INVALID", 409);
        }
        var json = receipt?.Json;
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_requirement_set_application_delivery_v1.delivery SET state={state},ack_json={json},error_code={error},lease_until=clock_timestamp(),next_attempt=clock_timestamp()+interval '30 seconds'
            WHERE environment_id={r.Environment} AND tenant_id={r.Tenant} AND outbox_id={lease.Original.Id} AND epoch={lease.Epoch}
              AND lease_token={lease.Token} AND state='OUTCOME_UNKNOWN' AND lease_until>clock_timestamp()
            """, ct);
        if (affected == 0) return "LEASE_LOST";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_requirement_set_application_delivery_v1.results(environment_id,tenant_id,outbox_id,epoch,lease_token,state,error_code,ack_json,recorded_at)
            VALUES({r.Environment},{r.Tenant},{lease.Original.Id},{lease.Epoch},{lease.Token},{state},{error},{json},clock_timestamp())
            """, ct);
        await scope.ValidateAsync(fence, ct);
        if (await Authority(r, lease.Original.Submission, ct) != authority) throw Error("DELIVERY_AUTHORITY_CHANGED", 403);
        await tx.CommitAsync(ct); return state;
    }
    public async Task<ToolApplicationDeliveryRunV2> RunAsync(Guid tenant, CancellationToken ct = default)
    {
        Lease? lease = null;
        try
        {
            lease = await ClaimAsync(tenant, ct); if (lease == null) return new("IDLE_OR_DISABLED");
            var state = "OUTCOME_UNKNOWN"; string? error = null; ReceiptResult? receipt = null;
            try
            {
                var response = await transport.SendAsync(lease.Route, lease.Envelope.ReceiptEventId, lease.Original.Payload, true, ct);
                if (response.Status is 200 or 202)
                {
                    var observed = Receipt(response.Body, lease.Envelope);
                    if (observed.Kind == "NOT_OBSERVED")
                    {
                        if (response.Status != 200) throw Error("DELIVERY_RECEIPT_INVALID", 409);
                        response = lease.Authority.Registration.Receive && lease.Authority.Grant.Receive
                            ? await transport.SendAsync(lease.Route, lease.Envelope.ReceiptEventId, lease.Original.Payload, false, ct)
                            : new(403, []);
                    }
                }
                if (response.Status is 200 or 202)
                {
                    receipt = Receipt(response.Body, lease.Envelope);
                    if (receipt.Kind == "NOT_OBSERVED" || receipt.Kind == "PENDING_GAP" && response.Status != 202 || receipt.Kind == "RECORDED" && response.Status != 200)
                        throw Error("DELIVERY_RECEIPT_INVALID", 409);
                    state = receipt.State;
                }
                else if (response.Status is 429 or >= 500) error = "RECEIVER_PENDING_OR_UNAVAILABLE";
                else { state = response.Status == 409 ? "CONFLICT" : "BLOCKED"; error = "RECEIVER_HTTP_" + response.Status; }
            }
            catch (ScopeAuthorizationExceptionV1 e) when (e.Status == 409) { state = "CONFLICT"; error = e.Message; receipt = null; }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException or TimeoutException)
            { ct.ThrowIfCancellationRequested(); error = "TRANSPORT_OUTCOME_UNKNOWN"; }
            return new(await FinishAsync(lease, state, error, receipt, ct), lease.Original.Id);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is System.Data.Common.DbException or DbUpdateException or ScopeAuthorizationExceptionV1 or TimeoutException or IOException)
        { return new("OUTCOME_UNKNOWN", lease?.Original.Id); }
    }
    // The caller must already authorize the exact original business Subject in its read transaction.
    internal static async Task<ToolApplicationDeliveryViewV2?> ReadAsync(CP6Context db, Guid environment, Guid eventId, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<DeliveryRow>($"""
            SELECT d.state AS "State",d.epoch AS "Attempts",d.error_code AS "Error",d.ack_json AS "Ack",o.payload AS "Payload"
            FROM cp6_requirement_set_application_delivery_v1.delivery d JOIN cp6_requirement_set_review_application_v1.outbox o
              ON o.environment_id=d.environment_id AND o.tenant_id=d.tenant_id AND o.event_id=d.outbox_id
            WHERE d.environment_id={environment} AND d.tenant_id={db.CurrentTenantId} AND d.outbox_id={eventId}
            """).SingleOrDefaultAsync(ct);
        return row == null ? null : new(eventId, row.State, row.Attempts, row.Error, row.Ack == null ? null
            : JsonSerializer.Deserialize<JsonElement>(Receipt(Encoding.UTF8.GetBytes(row.Ack), ApprovalApplicationReceiptWireV2.Parse(row.Payload)).Json));
    }
    private sealed record DeliveryRow(string State, long Attempts, string? Error, string? Ack, byte[] Payload);
}
