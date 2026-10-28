using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Entity.Contracts.Wire;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Oa.ApprovalSubmissionWireV2;

namespace CP6.Core.Services.Oa;

public interface IApprovalReceiptChainTransportV2
{
    ApprovalDecisionDeliveryRouteV2? Route(Guid tenant);
    Task<ApprovalDecisionTransportResponseV2> ReadAsync(ApprovalDecisionDeliveryRouteV2 route, Guid stream,
        string afterRevision, int limit, string? cursor, CancellationToken ct);
}
public sealed record ApprovalReceiptRecoveryRunV2(string State, Guid? ReceiptStreamId = null);

/// <summary>Recover Owner receipts, never Owner effects. Every source HTTP request is outside TxR;
/// each verified prefix survives later network failure, expiration, revocation or lease takeover.</summary>
public sealed class ApprovalReceiptRecoveryV2(CP6Context db, ScopeAuthorizationFenceV1 scope, IApprovalReceiptChainTransportV2 transport,string businessOwner=ApprovalBusinessProfileV2.ToolOwner)
{
    private readonly ApprovalBusinessProfileV2 profile=ApprovalBusinessProfileV2.For(businessOwner);
    internal int PageLimit { get; init; } = 20;
    internal sealed record Work(Guid Id, Guid Submission, Guid Instance, Guid Decision, string SubjectRevision, string Unit,
        long Highest, Guid? Latest, long Target, long Epoch, string? Route);
    internal sealed record Proof(Guid Registration, Guid Grant, Guid SourceRegistration, Guid SourceGrant);
    internal sealed record Lease(Work Work, ApprovalApplicationReceiptV2 Anchor, ApprovalDecisionDeliveryRouteV2 Route,
        Proof Authority, long Epoch, Guid Token);
    private static string Json(object value) => Encoding.UTF8.GetString(ApprovalCanonicalV2.Bytes(value));
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private void Clean()
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction != null || db.ChangeTracker.HasChanges()
            || System.Transactions.Transaction.Current != null) throw Error("RECEIPT_RECOVERY_TRANSACTION_REQUIRED", 503);
    }
    private async Task Ready(CancellationToken ct)
    {
        await scope.Root(ct);
        if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_approval_receipt_v2.recovery_ready() AS \"Value\"").SingleAsync(ct))
            throw Error("RECEIPT_RECOVERY_UNAVAILABLE", 503);
    }
    private async Task<Proof> Authority(ApprovalDecisionDeliveryRouteV2 r, Guid submission, CancellationToken ct)
    {
        if (r.Environment != scope.EnvironmentId || r.Tenant != db.CurrentTenantId || r.ConsumerOwner != profile.Owner
            || transport.Route(r.Tenant) != r || !await db.Sys_Tenants.AsNoTracking().AnyAsync(t => t.Id == r.Tenant && t.Enable && (t.ExpireDate == null || t.ExpireDate > DateTime.Now), ct))
            throw Error("RECEIPT_RECOVERY_AUTHORITY_UNAVAILABLE", 403);
        return await db.Database.SqlQuery<Proof>(profile.FactSql($"""
            SELECT r.revision AS "Registration",g.revision AS "Grant",sr.revision AS "SourceRegistration",sg.revision AS "SourceGrant"
            FROM cp6_approval_receipt_v2.registrations r
            JOIN cp6_approval_receipt_v2.subject_grants g ON g.environment_id=r.environment_id AND g.tenant_id=r.tenant_id AND g.principal_id=r.principal_id
            JOIN cp6_tool_approval_fact_v2.registrations sr ON sr.environment_id=r.environment_id AND sr.tenant_id=r.tenant_id AND sr.principal_id=r.principal_id
            JOIN cp6_tool_approval_fact_v2.subject_grants sg ON sg.environment_id=sr.environment_id AND sg.tenant_id=sr.tenant_id AND sg.principal_id=sr.principal_id AND sg.submission_id=g.submission_id
            WHERE r.environment_id={r.Environment} AND r.tenant_id={r.Tenant} AND r.principal_id={r.Principal} AND g.submission_id={submission}
              AND r.owner={profile.Owner} AND g.owner=r.owner AND r.enabled AND r.can_query AND r.can_receive AND r.can_recover
              AND g.enabled AND g.can_query AND g.can_receive AND g.can_recover AND r.evidence_mode={r.EvidenceMode}
              AND sr.enabled AND sr.can_query AND sr.evidence_mode=r.evidence_mode AND sg.enabled AND sg.can_query
            """)).SingleOrDefaultAsync(ct) ?? throw Error("RECEIPT_RECOVERY_AUTHORITY_UNAVAILABLE", 403);
    }
    private async Task LockRows(Work w, CancellationToken ct)
    {
        _ = await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM cp6_approval_intake_v2.submissions WHERE tenant_id={db.CurrentTenantId}
              AND environment_id={scope.EnvironmentId} AND owner={profile.Owner} AND id={w.Submission} FOR UPDATE
            """).ToArrayAsync(ct);
        _ = await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM cp6_approval_receipt_v2.streams WHERE environment_id={scope.EnvironmentId} AND tenant_id={db.CurrentTenantId} AND id={w.Id} FOR UPDATE
            """).ToArrayAsync(ct);
    }
    internal async Task<Lease?> ClaimAsync(Guid tenant, CancellationToken ct = default)
    {
        Clean(); if (tenant != db.CurrentTenantId) throw Error("RECEIPT_RECOVERY_SCOPE_MISMATCH", 403);
        var route = transport.Route(tenant); if (route == null) return null;
        await using var tx = await db.Database.BeginTransactionAsync(ct); var fence = await scope.AcquireAsync(ct); await Ready(ct);
        var work = await db.Database.SqlQuery<Work>($"""
            SELECT s.id AS "Id",s.submission_id AS "Submission",s.instance_id AS "Instance",s.decision_id AS "Decision",s.subject_revision AS "SubjectRevision",
              s.application_unit_id AS "Unit",s.highest_revision AS "Highest",s.latest_receipt_id AS "Latest",g.target_revision AS "Target",g.epoch AS "Epoch",g.route_json AS "Route"
            FROM cp6_approval_receipt_v2.gaps g JOIN cp6_approval_receipt_v2.streams s ON s.environment_id=g.environment_id AND s.tenant_id=g.tenant_id AND s.id=g.stream_id
            JOIN cp6_approval_receipt_v2.subject_grants a ON a.environment_id=s.environment_id AND a.tenant_id=s.tenant_id AND a.submission_id=s.submission_id
              AND a.principal_id={route.Principal} AND a.enabled AND a.can_query AND a.can_receive AND a.can_recover
            WHERE g.environment_id={scope.EnvironmentId} AND g.tenant_id={tenant} AND NOT g.completed AND s.owner={profile.Owner}
              AND (g.lease_until IS NULL OR g.lease_until<=clock_timestamp()) AND g.next_attempt<=clock_timestamp()
              AND (g.error_code IS DISTINCT FROM 'RECEIPT_CHAIN_CONFLICT' OR g.route_json::jsonb->>'version'<>{route.Version})
            ORDER BY g.next_attempt,g.stream_id LIMIT 1
            """).SingleOrDefaultAsync(ct);
        if (work == null) return null;
        var proof = await Authority(route, work.Submission, ct); await LockRows(work, ct);
        if (work.Route != null)
        {
            var old = JsonSerializer.Deserialize<ApprovalDecisionDeliveryRouteV2>(work.Route, ApprovalBindingOwnerV2.Json)!;
            if (old != route with { Version = old.Version }) throw Error("RECEIPT_RECOVERY_ROUTE_CHANGED", 409);
        }
        var payload = await db.Database.SqlQuery<byte[]>($"""
            SELECT payload AS "Value" FROM cp6_approval_receipt_v2.inbox WHERE environment_id={scope.EnvironmentId} AND tenant_id={tenant} AND stream_id={work.Id} ORDER BY revision LIMIT 1
            """).SingleAsync(ct);
        var anchor = ApprovalApplicationReceiptWireV2.Parse(payload); var token = Guid.NewGuid(); var epoch = checked(work.Epoch + 1);
        var routeJson = Json(route);
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_approval_receipt_v2.gaps SET epoch={epoch},lease_token={token},lease_until=clock_timestamp()+interval '120 seconds',
              attempts=attempts+1,route_json={routeJson},error_code=NULL
            WHERE environment_id={scope.EnvironmentId} AND tenant_id={tenant} AND stream_id={work.Id} AND epoch={work.Epoch} AND NOT completed
              AND (lease_until IS NULL OR lease_until<=clock_timestamp())
            """, ct);
        if (changed != 1) throw Error("RECEIPT_RECOVERY_LEASE_LOST", 409);
        await scope.ValidateAsync(fence, ct);
        if (await Authority(route, work.Submission, ct) != proof) throw Error("RECEIPT_RECOVERY_AUTHORITY_CHANGED", 403);
        await tx.CommitAsync(ct); return new(work, anchor, route, proof, epoch, token);
    }
    private async Task RequireLease(Lease lease, CancellationToken ct)
    {
        await Ready(ct);
        if (await Authority(lease.Route, lease.Work.Submission, ct) != lease.Authority) throw Error("RECEIPT_RECOVERY_AUTHORITY_CHANGED", 403);
        var valid = await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT FROM cp6_approval_receipt_v2.gaps WHERE environment_id={scope.EnvironmentId} AND tenant_id={db.CurrentTenantId}
              AND stream_id={lease.Work.Id} AND epoch={lease.Epoch} AND lease_token={lease.Token} AND lease_until>clock_timestamp()) AS "Value"
            """).SingleAsync(ct);
        if (!valid) throw Error("RECEIPT_RECOVERY_LEASE_LOST", 409);
    }
    private sealed class RecoveryIdentity(ApprovalReceiptRecoveryV2 owner, Lease lease) : IApprovalServiceAuthorityV2
    {
        public async Task<ApprovalServiceIdentityV2> CurrentAsync(CancellationToken ct)
        {
            await owner.RequireLease(lease, ct); var r = lease.Route;
            // This is a local trusted worker identity, authorized by explicit can_recover grants.
            // It does not manufacture a remote bearer token or impersonate the original human actor.
            return new(r.Environment, r.Tenant, r.Principal, r.Client, "CP6.Services", r.Version, true, r.EvidenceMode);
        }
    }
    private async Task<ApprovalReceiptProjectionV2> Current(Lease lease, CancellationToken ct)
        => await new ApprovalReceiptIngestionV2(db, scope, new RecoveryIdentity(this, lease)).CurrentAsync(lease.Work.Id, ct);
    private async Task Observe(Lease lease, ApprovalDecisionTransportResponseV2 response, CancellationToken ct)
    {
        Clean(); if (response.Body.Length > 1048576) throw Error("RECEIPT_CHAIN_RESPONSE_TOO_LARGE", 409);
        await using var tx = await db.Database.BeginTransactionAsync(ct); var fence = await scope.AcquireAsync(ct); await RequireLease(lease, ct);
        var route = Json(lease.Route); var authority = Json(lease.Authority); var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_approval_receipt_v2.gap_observations(environment_id,tenant_id,stream_id,id,epoch,principal_id,route_json,authority_json,status,raw,raw_hash,recorded_at)
            VALUES({scope.EnvironmentId},{db.CurrentTenantId},{lease.Work.Id},{id},{lease.Epoch},{lease.Route.Principal},{route},{authority},{response.Status},
              {response.Body},{ApprovalCanonicalV2.Hash(response.Body)},clock_timestamp())
            """, ct);
        await scope.ValidateAsync(fence, ct); await RequireLease(lease, ct); await tx.CommitAsync(ct);
    }
    private static long ParseRevision(string? raw)
    {
        if (!long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0 || raw != Number(value)) throw new JsonException();
        return value;
    }
    private async Task ExtendTarget(Lease lease, long maximum, CancellationToken ct)
    {
        Clean(); await using var tx = await db.Database.BeginTransactionAsync(ct); var fence = await scope.AcquireAsync(ct); await RequireLease(lease, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_approval_receipt_v2.gaps g SET target_revision=greatest(g.target_revision,{maximum}),
              completed=(greatest(g.target_revision,{maximum})<=s.highest_revision)
            FROM cp6_approval_receipt_v2.streams s WHERE s.environment_id=g.environment_id AND s.tenant_id=g.tenant_id AND s.id=g.stream_id
              AND g.environment_id={scope.EnvironmentId} AND g.tenant_id={db.CurrentTenantId} AND g.stream_id={lease.Work.Id}
              AND g.epoch={lease.Epoch} AND g.lease_token={lease.Token}
            """, ct);
        await scope.ValidateAsync(fence, ct); await RequireLease(lease, ct); await tx.CommitAsync(ct);
    }
    private ApprovalReceiptChainPageV2 Page(byte[] bytes, Lease lease, long after, Guid? previous, string? previousState, string? snapshot, long? maximum)
    {
        try
        {
            using var doc = WorkflowCommandJsonV1.Parse(bytes); var root = doc.RootElement;
            Shape(root, "kind", "receiptStreamId", "instanceId", "subjectRevision", "applicationUnitId", "highestRevisionAtSnapshot", "items", "nextCursor", "snapshotToken");
            var page = root.Deserialize<ApprovalReceiptChainPageV2>(ApprovalBindingOwnerV2.Json)!;
            var high = ParseRevision(page.HighestRevisionAtSnapshot);
            if (page.Kind != "CHAIN" || page.ReceiptStreamId != lease.Work.Id || page.InstanceId != lease.Work.Instance
                || page.SubjectRevision != lease.Work.SubjectRevision || page.ApplicationUnitId != lease.Work.Unit
                || high < after || high < lease.Work.Target || page.SnapshotToken is not { Length: > 0 and <= 16384 }
                || snapshot != null && (page.SnapshotToken != snapshot || maximum != high) || page.Items.Length > PageLimit
                || page.NextCursor is { Length: 0 or > 8192 }) throw new JsonException();
            var position = after;
            var items = root.GetProperty("items").EnumerateArray().Select(x => ApprovalApplicationReceiptWireV2.Parse(Encoding.UTF8.GetBytes(x.GetRawText()))).ToArray();
            foreach (var e in items)
            {
                if (position == long.MaxValue || e.ReceiptRevision != Number(++position) || position > high || e.PreviousReceiptId != previous
                    || e.Scope != lease.Anchor.Scope || e.ReceiptStreamId != lease.Work.Id || e.SubmissionId != lease.Anchor.SubmissionId
                    || e.InstanceId != lease.Work.Instance || e.DecisionId != lease.Work.Decision || e.DecisionPayloadDigest != lease.Anchor.DecisionPayloadDigest
                    || e.Subject != lease.Anchor.Subject || e.ProducerOwner != lease.Anchor.ProducerOwner || e.Action != lease.Anchor.Action
                    || previousState != null && previousState != "BLOCKED") throw new JsonException();
                previous = e.ReceiptId; previousState = e.Application;
            }
            if ((position < high) != (page.NextCursor != null) || position < high && items.Length == 0) throw new JsonException();
            return page with { Items = items };
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException or FormatException or NullReferenceException or KeyNotFoundException)
        { throw Error("RECEIPT_CHAIN_CONFLICT", 409); }
    }
    private static bool CursorExpired(ApprovalDecisionTransportResponseV2 response)
    {
        if (response.Status != 409) return false;
        try
        {
            using var doc = WorkflowCommandJsonV1.Parse(response.Body);
            return doc.RootElement.TryGetProperty("code", out var code) && code.GetString() == "CURSOR_EXPIRED";
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { return false; }
    }
    internal async Task FinishAsync(Lease lease, string? error, CancellationToken ct)
    {
        Clean(); await using var tx = await db.Database.BeginTransactionAsync(ct); var fence = await scope.AcquireAsync(ct); await RequireLease(lease, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_approval_receipt_v2.gaps SET lease_until=clock_timestamp(),next_attempt=clock_timestamp()+interval '5 seconds',error_code={error}
            WHERE environment_id={scope.EnvironmentId} AND tenant_id={db.CurrentTenantId} AND stream_id={lease.Work.Id} AND epoch={lease.Epoch} AND lease_token={lease.Token}
            """, ct);
        await scope.ValidateAsync(fence, ct);
        if (await Authority(lease.Route, lease.Work.Submission, ct) != lease.Authority) throw Error("RECEIPT_RECOVERY_AUTHORITY_CHANGED", 403);
        await tx.CommitAsync(ct);
    }
    public async Task<ApprovalReceiptRecoveryRunV2> RunAsync(Guid tenant, CancellationToken ct = default)
    {
        Lease? lease = null;
        try
        {
            lease = await ClaimAsync(tenant, ct); if (lease == null) return new("IDLE_OR_DISABLED");
            var current = await Current(lease, ct); var start = ParseRevision(current.HighestContinuousRevision); var last = start;
            var previous = current.Receipt?.ReceiptId; var previousState = current.Receipt?.Application;
            string? cursor = null, snapshot = null; long? maximum = null;
            for (var pageNumber = 0; pageNumber < 3; pageNumber++)
            {
                _ = await Current(lease, ct); // Current authority/lease rechecked immediately before network I/O.
                var response = await transport.ReadAsync(lease.Route, lease.Work.Id, Number(start), PageLimit, cursor, ct);
                await Observe(lease, response, ct);
                if (response.Status != 200)
                {
                    var expired = CursorExpired(response);
                    var state = expired ? "CURSOR_RESTART" : response.Status is 403 or 404 ? "SOURCE_FORBIDDEN" : "SOURCE_UNAVAILABLE";
                    await FinishAsync(lease, state, ct); return new(state, lease.Work.Id);
                }
                using (var kind = WorkflowCommandJsonV1.Parse(response.Body))
                    if (kind.RootElement.GetProperty("kind").GetString() == "NOT_OBSERVED")
                    {
                        Shape(kind.RootElement, "kind", "receiptStreamId", "observedAt");
                        if (kind.RootElement.GetProperty("receiptStreamId").GetGuid() != lease.Work.Id) throw Error("RECEIPT_CHAIN_CONFLICT", 409);
                        _ = kind.RootElement.GetProperty("observedAt").GetDateTimeOffset();
                        await FinishAsync(lease, "SOURCE_NOT_OBSERVED", ct); return new("SOURCE_NOT_OBSERVED", lease.Work.Id);
                    }
                var page = Page(response.Body, lease, last, previous, previousState, snapshot, maximum);
                if (snapshot == null) await ExtendTarget(lease, ParseRevision(page.HighestRevisionAtSnapshot), ct);
                snapshot ??= page.SnapshotToken; maximum ??= ParseRevision(page.HighestRevisionAtSnapshot);
                var receiver = new ApprovalReceiptIngestionV2(db, scope, new RecoveryIdentity(this, lease));
                foreach (var item in page.Items)
                {
                    var result = await receiver.ReceiveAsync(ApprovalCanonicalV2.Bytes(item), ct);
                    if (result.Status != 200) throw Error("RECEIPT_CHAIN_CONFLICT", 409);
                    last = ParseRevision(item.ReceiptRevision); previous = item.ReceiptId; previousState = item.Application;
                }
                cursor = page.NextCursor;
                if (cursor == null)
                {
                    current = await Current(lease, ct);
                    // Concurrent authentic callbacks may already have advanced beyond this fixed snapshot.
                    // The immutable continuous history still proves its entire prefix, including snapshotMax.
                    if (last != maximum || ParseRevision(current.HighestContinuousRevision) < maximum) throw Error("RECEIPT_CHAIN_CONFLICT", 409);
                    await FinishAsync(lease, null, ct); return new(current.PendingGap ? "PREFIX_RECORDED" : "RECOVERED", lease.Work.Id);
                }
            }
            await FinishAsync(lease, null, ct); return new("PREFIX_RECORDED", lease.Work.Id);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is System.Data.Common.DbException or DbUpdateException or ScopeAuthorizationExceptionV1
            or HttpRequestException or TimeoutException or IOException or OperationCanceledException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        {
            var code = e is ScopeAuthorizationExceptionV1 scoped ? scoped.Message : "RECEIPT_RECOVERY_OUTCOME_UNKNOWN";
            if (lease != null)
                try { await FinishAsync(lease, code, ct); }
                catch (Exception failure) when (failure is System.Data.Common.DbException or DbUpdateException or ScopeAuthorizationExceptionV1 or TimeoutException or IOException) { }
            return new(code, lease?.Work.Id);
        }
    }
}
