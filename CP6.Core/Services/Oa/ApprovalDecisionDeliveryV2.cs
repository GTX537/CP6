using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Entity.Contracts.Wire;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Oa.ApprovalSubmissionWireV2;

namespace CP6.Core.Services.Oa;

public sealed record ApprovalDecisionDeliveryRouteV2(Guid Environment, Guid Tenant, Guid Principal,
    string Client, string Issuer, string Destination, string Version, string EvidenceMode, string ConsumerOwner);
public sealed record ApprovalDecisionTransportResponseV2(int Status, byte[] Body);
public interface IApprovalDecisionTransportV2
{
    ApprovalDecisionDeliveryRouteV2? Route(Guid tenant);
    Task<ApprovalDecisionTransportResponseV2> SendAsync(ApprovalDecisionDeliveryRouteV2 route, Guid eventId,
        byte[] original, bool query, CancellationToken ct);
}
public sealed record ApprovalDecisionDeliveryRunV2(string State, Guid? EventId = null);
public sealed record ApprovalDecisionDeliveryViewV2(Guid EventId, string State, long Attempts, string? ErrorCode, JsonElement? Receipt);

/// <summary>Delivers native immutable decision bytes. Each network exchange runs outside
/// database transactions. A transport receipt is never a business application result.</summary>
public sealed class ApprovalDecisionDeliveryV2(CP6Context db, ScopeAuthorizationFenceV1 scope, IApprovalDecisionTransportV2 transport,string businessOwner=ApprovalBusinessProfileV2.ToolOwner)
{
    private readonly ApprovalBusinessProfileV2 profile=ApprovalBusinessProfileV2.For(businessOwner);
    private IQueryable<T> SourceQuery<T>(FormattableString sql)=>db.Database.SqlQuery<T>(profile.FactSql(sql));
    public const string Consumer = "Main.Wms.ToolDisposition.v1";
    internal sealed record Original(Guid Id, Guid Submission, byte[] Payload, string Digest);
    internal sealed record Registration(Guid Revision, bool Enabled, bool Query, bool Receive, string Mode);
    internal sealed record Grant(Guid Revision, bool Enabled, bool Query, bool Receive);
    internal sealed record AuthorityProof(Registration Registration, Grant Grant);
    internal sealed record Lease(Original Original, ApprovalDecisionEnvelopeV2 Envelope, ApprovalDecisionDeliveryRouteV2 Route,
        AuthorityProof Authority, long Epoch, Guid Token);
    internal sealed record ReceiptResult(string Kind, string State, string Json);
    private static string Json(object value) => Encoding.UTF8.GetString(ApprovalCanonicalV2.Bytes(value));
    private static string Fingerprint(ApprovalDecisionDeliveryRouteV2 route) => ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(route));
    private void Clean()
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction != null || db.ChangeTracker.HasChanges()
            || System.Transactions.Transaction.Current != null) throw Error("DELIVERY_TRANSACTION_REQUIRED", 503);
    }
    private async Task Ready(CancellationToken ct)
    {
        await scope.Root(ct);
        if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_approval_delivery_v2.ready() AS \"Value\"").SingleAsync(ct))
            throw Error("DELIVERY_STORE_UNAVAILABLE", 503);
        if(profile.Owner==ApprovalBusinessProfileV2.DocumentOwner
            && !await db.Database.SqlQueryRaw<bool>("SELECT cp6_document_review_fact_v1.ready() AS \"Value\"").SingleAsync(ct))
            throw Error("DELIVERY_STORE_UNAVAILABLE",503);
    }
    private async Task<Registration> Registered(ApprovalDecisionDeliveryRouteV2 r, CancellationToken ct)
    {
        if (r.Environment != scope.EnvironmentId || r.Tenant != db.CurrentTenantId || r.ConsumerOwner != profile.Owner || transport.Route(r.Tenant) != r)
            throw Error("DELIVERY_ROUTE_CHANGED", 403);
        var registration = await SourceQuery<Registration>($"""
            SELECT revision AS "Revision",enabled AS "Enabled",can_query AS "Query",can_receive AS "Receive",evidence_mode AS "Mode"
            FROM cp6_tool_approval_fact_v2.registrations WHERE environment_id={r.Environment} AND tenant_id={r.Tenant} AND principal_id={r.Principal}
            """).SingleOrDefaultAsync(ct);
        if (registration is not { Enabled: true, Query: true } || registration.Mode != r.EvidenceMode
            || !await db.Sys_Tenants.AsNoTracking().AnyAsync(t => t.Id == r.Tenant && t.Enable && (t.ExpireDate == null || t.ExpireDate > DateTime.Now), ct))
            throw Error("DELIVERY_SERVICE_UNAVAILABLE", 403);
        return registration;
    }
    private async Task<AuthorityProof> Authority(ApprovalDecisionDeliveryRouteV2 r, Guid submission, CancellationToken ct)
    {
        var registration = await Registered(r, ct);
        var grant = await SourceQuery<Grant>($"""
            SELECT revision AS "Revision",enabled AS "Enabled",can_query AS "Query",can_receive AS "Receive"
            FROM cp6_tool_approval_fact_v2.subject_grants WHERE environment_id={r.Environment} AND tenant_id={r.Tenant}
              AND principal_id={r.Principal} AND submission_id={submission}
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
        var original = await SourceQuery<Original>($"""
            SELECT o.id AS "Id",o.submission_id AS "Submission",o.payload AS "Payload",o.payload_digest AS "Digest"
            FROM cp6_approval_decision_v2.outbox o
            JOIN cp6_tool_approval_fact_v2.subject_grants g ON g.environment_id=o.environment_id AND g.tenant_id=o.tenant_id
              AND g.submission_id=o.submission_id AND g.principal_id={route.Principal} AND g.enabled AND g.can_query
            LEFT JOIN cp6_approval_delivery_v2.delivery d ON d.environment_id=o.environment_id AND d.tenant_id=o.tenant_id AND d.outbox_id=o.id
            WHERE o.environment_id={route.Environment} AND o.tenant_id={tenant} AND o.consumer_owner={profile.Owner}
              AND (d.outbox_id IS NULL OR (d.lease_until<=clock_timestamp() AND
                ((d.state IN('OUTCOME_UNKNOWN','PENDING_VALIDATION') AND d.next_attempt<=clock_timestamp()) OR
                 (d.state='BLOCKED' AND (d.route_hash<>{routeHash} OR d.registration_revision<>{registered.Revision} OR d.subject_revision<>g.revision)))))
            ORDER BY coalesce(d.next_attempt,'-infinity'::timestamptz),o.id LIMIT 1
            """).SingleOrDefaultAsync(ct);
        if (original == null) return null;
        var authority = await Authority(route, original.Submission, ct);
        var savedRoute = await db.Database.SqlQuery<string>($"""
            SELECT route_json AS "Value" FROM cp6_approval_delivery_v2.delivery
            WHERE environment_id={route.Environment} AND tenant_id={tenant} AND outbox_id={original.Id}
            """).SingleOrDefaultAsync(ct);
        if (savedRoute != null)
        {
            var prior = JsonSerializer.Deserialize<ApprovalDecisionDeliveryRouteV2>(savedRoute, ApprovalBindingOwnerV2.Json) ?? throw Error("DELIVERY_ROUTE_INVALID", 503);
            if (prior != route with { Version = prior.Version }) throw Error("DELIVERY_ROUTE_IDENTITY_CHANGED", 409);
        }
        var parsed = ApprovalDecisionWireV2.Parse(original.Payload); var e = parsed.Value;
        if (parsed.Full != original.Digest || !parsed.Canonical.AsSpan().SequenceEqual(original.Payload) || e.EventId != original.Id
            || e.SubmissionId != original.Submission || e.Scope != new ApprovalScopeV2(route.Environment.ToString(), tenant) || e.ConsumerOwner != profile.Owner)
            throw Error("DELIVERY_ORIGINAL_INVALID", 503);
        var source = await new ApprovalDecisionSourceV2(db, scope).Read(parsed, ct);
        if (source.Mode != route.EvidenceMode) throw Error("DELIVERY_SOURCE_MODE_MISMATCH", 403);
        var token = Guid.NewGuid(); var routeJson = Json(route); var authorityJson = Json(authority);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_approval_delivery_v2.delivery(environment_id,tenant_id,outbox_id,state,epoch,lease_token,lease_until,next_attempt,
              route_hash,route_json,registration_revision,subject_revision)
            VALUES({route.Environment},{tenant},{original.Id},'OUTCOME_UNKNOWN',1,{token},clock_timestamp()+interval '90 seconds',clock_timestamp(),
              {routeHash},{routeJson},{authority.Registration.Revision},{authority.Grant.Revision})
            ON CONFLICT(environment_id,tenant_id,outbox_id) DO UPDATE SET state='OUTCOME_UNKNOWN',epoch=cp6_approval_delivery_v2.delivery.epoch+1,
              lease_token=EXCLUDED.lease_token,lease_until=EXCLUDED.lease_until,route_hash=EXCLUDED.route_hash,route_json=EXCLUDED.route_json,
              registration_revision=EXCLUDED.registration_revision,subject_revision=EXCLUDED.subject_revision,error_code=NULL,ack_json=NULL
            """, ct);
        var epoch = await db.Database.SqlQuery<long>($"""
            SELECT epoch AS "Value" FROM cp6_approval_delivery_v2.delivery WHERE environment_id={route.Environment} AND tenant_id={tenant} AND outbox_id={original.Id}
            """).SingleAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_approval_delivery_v2.attempts(environment_id,tenant_id,outbox_id,epoch,lease_token,route_json,registration_revision,subject_revision,authority_json,started_at)
            VALUES({route.Environment},{tenant},{original.Id},{epoch},{token},{routeJson},{authority.Registration.Revision},{authority.Grant.Revision},{authorityJson},clock_timestamp())
            """, ct);
        await scope.ValidateAsync(fence, ct);
        if (await Authority(route, original.Submission, ct) != authority) throw Error("DELIVERY_AUTHORITY_CHANGED", 403);
        await tx.CommitAsync(ct); return new(original, e, route, authority, epoch, token);
    }

    internal static ReceiptResult Receipt(byte[] bytes, ApprovalDecisionEnvelopeV2 e)
    {
        try
        {
            using var doc = WorkflowCommandJsonV1.Parse(bytes); var root = doc.RootElement;
            var kind = root.GetProperty("kind").GetString();
            if (kind == "NOT_OBSERVED")
            {
                Shape(root, "kind", "eventId", "observedAt", "queryRef"); Id(root.GetProperty("eventId"));
                if (root.GetProperty("eventId").GetGuid() != e.EventId || root.GetProperty("queryRef").GetString() != ApprovalBusinessProfileV2.For(e.ConsumerOwner).DecisionRoute + "/" + e.EventId) throw new JsonException();
                _ = root.GetProperty("observedAt").GetDateTimeOffset();
                return new(kind, "NOT_OBSERVED", Json(root));
            }
            var pending = kind == "PENDING_VALIDATION";
            if (pending) Shape(root, "kind", "eventId", "decisionId", "submissionId", "payloadDigest", "inboxId", "receivedAt", "queryRef");
            else
            {
                if (kind != "PROCESSED") throw new JsonException();
                Shape(root, "kind", "eventId", "decisionId", "submissionId", "payloadDigest", "inboxId", "receiveState", "receivedAt", "application",
                    "applicationReceiptId", "receiptStreamId", "receiptQueryRef", "errorCode");
            }
            foreach (var name in new[] { "eventId", "decisionId", "submissionId", "inboxId" }) Id(root.GetProperty(name));
            Digest(root.GetProperty("payloadDigest")); _ = root.GetProperty("receivedAt").GetDateTimeOffset();
            if (root.GetProperty("eventId").GetGuid() != e.EventId || root.GetProperty("decisionId").GetGuid() != e.DecisionId
                || root.GetProperty("submissionId").GetGuid() != e.SubmissionId || root.GetProperty("payloadDigest").Deserialize<ApprovalDigestV2>(ApprovalBindingOwnerV2.Json) != e.PayloadDigest)
                throw new JsonException();
            if (pending)
            {
                if (root.GetProperty("queryRef").GetString() != ApprovalBusinessProfileV2.For(e.ConsumerOwner).DecisionRoute + "/" + e.EventId) throw new JsonException();
                return new(kind!, "PENDING_VALIDATION", Json(root));
            }
            var ack = root.Deserialize<ApprovalFactAckV2>(ApprovalBindingOwnerV2.Json)!;
            if (ack.ReceiveState is not ("RECORDED" or "DUPLICATE" or "STALE_RECEIPT" or "GAP" or "CONFLICT")
                || ack.Application is not ("NOT_APPLIED" or "PENDING" or "APPLIED" or "STALE" or "CONFLICT" or "BLOCKED" or "UNKNOWN")) throw new JsonException();
            Id(root.GetProperty("applicationReceiptId"), true); Id(root.GetProperty("receiptStreamId"), true);
            if (ack.ReceiptStreamId == null ? ack.ReceiptQueryRef != null : ack.ReceiptQueryRef != ApprovalBusinessProfileV2.For(e.ConsumerOwner).ApplicationRoute + ack.ReceiptStreamId + "/result") throw new JsonException();
            if (ack.Application == "NOT_APPLIED" && ack.ApplicationReceiptId != null || ack.ApplicationReceiptId != null && ack.ReceiptStreamId == null) throw new JsonException();
            if (ack.ReceiveState is "RECORDED" or "DUPLICATE" or "STALE_RECEIPT" && (ack.ReceiptStreamId == null || ack.ErrorCode != null)) throw new JsonException();
            if (ack.ReceiveState == "GAP" && (e.Sequence == "1" || ack.Application != "NOT_APPLIED" || ack.ReceiptStreamId != null)) throw new JsonException();
            return new(kind!, ack.ReceiveState == "CONFLICT" ? "CONFLICT" : "RECEIVED", Json(root));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or FormatException or KeyNotFoundException)
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
            UPDATE cp6_approval_delivery_v2.delivery SET state={state},ack_json={json},error_code={error},lease_until=clock_timestamp(),next_attempt=clock_timestamp()+interval '30 seconds'
            WHERE environment_id={r.Environment} AND tenant_id={r.Tenant} AND outbox_id={lease.Original.Id} AND epoch={lease.Epoch}
              AND lease_token={lease.Token} AND state='OUTCOME_UNKNOWN' AND lease_until>clock_timestamp()
            """, ct);
        if (affected == 0) return "LEASE_LOST";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_approval_delivery_v2.results(environment_id,tenant_id,outbox_id,epoch,lease_token,state,error_code,ack_json,recorded_at)
            VALUES({r.Environment},{r.Tenant},{lease.Original.Id},{lease.Epoch},{lease.Token},{state},{error},{json},clock_timestamp())
            """, ct);
        await scope.ValidateAsync(fence, ct);
        if (await Authority(r, lease.Original.Submission, ct) != authority) throw Error("DELIVERY_AUTHORITY_CHANGED", 403);
        await tx.CommitAsync(ct); return state;
    }
    public async Task<ApprovalDecisionDeliveryRunV2> RunAsync(Guid tenant, CancellationToken ct = default)
    {
        Lease? lease = null;
        try
        {
            lease = await ClaimAsync(tenant, ct); if (lease == null) return new("IDLE_OR_DISABLED");
            var state = "OUTCOME_UNKNOWN"; string? error = null; ReceiptResult? receipt = null;
            try
            {
                var response = await transport.SendAsync(lease.Route, lease.Envelope.EventId, lease.Original.Payload, true, ct);
                if (response.Status is 200 or 202)
                {
                    var observed = Receipt(response.Body, lease.Envelope);
                    if (observed.Kind == "NOT_OBSERVED")
                    {
                        if (response.Status != 200) throw Error("DELIVERY_RECEIPT_INVALID", 409);
                        response = await transport.SendAsync(lease.Route, lease.Envelope.EventId, lease.Original.Payload, false, ct);
                    }
                }
                if (response.Status is 200 or 202)
                {
                    receipt = Receipt(response.Body, lease.Envelope);
                    if (receipt.Kind == "NOT_OBSERVED" || receipt.Kind == "PENDING_VALIDATION" && response.Status != 202)
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
    internal static async Task<ApprovalDecisionDeliveryViewV2?> ReadAsync(CP6Context db, Guid environment, Guid eventId, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<DeliveryRow>($"""
            SELECT d.state AS "State",d.epoch AS "Attempts",d.error_code AS "Error",d.ack_json AS "Ack",o.payload AS "Payload"
            FROM cp6_approval_delivery_v2.delivery d JOIN cp6_approval_decision_v2.outbox o
              ON o.environment_id=d.environment_id AND o.tenant_id=d.tenant_id AND o.id=d.outbox_id
            WHERE d.environment_id={environment} AND d.tenant_id={db.CurrentTenantId} AND d.outbox_id={eventId}
            """).SingleOrDefaultAsync(ct);
        return row == null ? null : new(eventId, row.State, row.Attempts, row.Error, row.Ack == null ? null
            : JsonSerializer.Deserialize<JsonElement>(Receipt(Encoding.UTF8.GetBytes(row.Ack), ApprovalDecisionWireV2.Parse(row.Payload).Value).Json));
    }
    private sealed record DeliveryRow(string State, long Attempts, string? Error, string? Ack, byte[] Payload);
}
