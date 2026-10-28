using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Entity.Contracts.Wire;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Oa.ApprovalSubmissionWireV2;
namespace CP6.Core.Services.Wms;

// Trusted deployment route, never taken from an outbox body. No credential is persisted here.
public sealed record ToolApprovalDeliveryRouteV1(Guid Environment, Guid Tenant, Guid Principal,
    string Client, string Issuer, string Destination, string Version, string EvidenceMode);
public sealed record ToolApprovalDeliveryResponseV1(int Status, byte[] Body);
public interface IToolApprovalDeliveryTransportV1
{
    ToolApprovalDeliveryRouteV1? Route(Guid tenant);
    Task<ToolApprovalDeliveryResponseV1> SendAsync(ToolApprovalDeliveryRouteV1 route, Guid command,
        byte[] original, bool query, CancellationToken ct);
}
public sealed record ToolApprovalDeliveryViewV1(Guid SubmissionId, Guid OutboxId, string State, long Attempts,
    string? ErrorCode, ApprovalSubmissionAckV2? Receipt);
public sealed record ToolApprovalDeliveryRunV1(string State, Guid? OutboxId = null);

/// <summary>Actual Owner transport. The immutable original and human command result never change.
/// Lease/attempt is committed before networking; receipt/epoch CAS is a separate native transaction.</summary>
public sealed class ToolApprovalDeliveryV1(CP6Context db, ScopeAuthorizationFenceV1 scope, IToolApprovalDeliveryTransportV1 transport,string businessOwner=ApprovalBusinessProfileV2.ToolOwner)
{
    private readonly ApprovalBusinessProfileV2 profile=ApprovalBusinessProfileV2.For(businessOwner);
    private string Owner=>profile.Owner;
    private IQueryable<T> Query<T>(FormattableString sql)=>db.Database.SqlQuery<T>(profile.SubmissionSql(sql));
    private Task<int> Execute(FormattableString sql,CancellationToken ct)=>db.Database.ExecuteSqlInterpolatedAsync(profile.SubmissionSql(sql),ct);
    internal sealed record Original(Guid Id, Guid Submission, byte[] Payload, string Digest);
    internal sealed record Lease(Original Original, ApprovalSubmitCommandV2 Envelope, ToolApprovalDeliveryRouteV1 Route,
        Guid Registration, long Epoch, Guid Token);
    private sealed record Registration(Guid Revision, Guid Principal, string Audience, string EvidenceMode, bool Enabled);
    private static string Encode<T>(T value) => JsonSerializer.Serialize(value, ApprovalBindingOwnerV2.Json);
    private static string Fingerprint(ToolApprovalDeliveryRouteV1 r) => ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(r));
    private async Task Ready(CancellationToken ct)
    {
        await scope.Root(ct);
        await profile.FenceSubmission(db,scope.EnvironmentId,ct);
        if (!await db.Database.SqlQueryRaw<bool>(profile.SubmissionText("SELECT cp6_tool_approval_v1.delivery_ready() AS \"Value\"")).SingleAsync(ct))
            throw Error("DELIVERY_STORE_UNAVAILABLE",503);
    }
    private async Task<Registration> Authority(ToolApprovalDeliveryRouteV1 r, CancellationToken ct)
    {
        if (r.Environment != scope.EnvironmentId || r.Tenant != db.CurrentTenantId || transport.Route(r.Tenant) != r)
            throw Error("DELIVERY_ROUTE_CHANGED",403);
        var registration = await Query<Registration>($"""
            SELECT revision AS "Revision",service_principal AS "Principal",audience AS "Audience",evidence_mode AS "EvidenceMode",enabled AS "Enabled"
            FROM cp6_approval_binding_v2.registrations WHERE tenant_id={r.Tenant} AND environment_id={r.Environment}
              AND biz_type='WMS_TOOL_DISCARD' AND subject_kind='TOOL_DISPOSITION_REQUEST' AND owner={Owner} FOR SHARE
            """).SingleOrDefaultAsync(ct);
        if (registration is not { Enabled:true } || registration.Principal != r.Principal || registration.Audience != "CP6.Services"
            || registration.EvidenceMode != r.EvidenceMode || !await db.Sys_Tenants.AsNoTracking().AnyAsync(t => t.Id==r.Tenant && t.Enable
                && (t.ExpireDate==null || t.ExpireDate>DateTime.Now),ct)) throw Error("DELIVERY_SERVICE_UNAVAILABLE",403);
        return registration;
    }
    private void Clean()
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction!=null || db.ChangeTracker.HasChanges()
            || System.Transactions.Transaction.Current!=null) throw Error("DELIVERY_TRANSACTION_REQUIRED",503);
    }
    internal async Task<Lease?> ClaimAsync(Guid tenant, CancellationToken ct=default)
    {
        Clean(); if(tenant!=db.CurrentTenantId)throw Error("DELIVERY_SCOPE_MISMATCH",403);
        var route=transport.Route(tenant);if(route==null)return null;
        await using var tx=await db.Database.BeginTransactionAsync(ct);await Ready(ct);var registration=await Authority(route,ct);
        var hash=Fingerprint(route);
        // A short per-tenant transaction lock coordinates absent rows as well as existing leases.
        var lockKey="approval-delivery/"+Owner+"/"+route.Environment+"/"+tenant;
        await Execute($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey},0))",ct);
        var original=await Query<Original>($"""
            SELECT o.id AS "Id",o.submission_id AS "Submission",o.payload AS "Payload",o.payload_digest AS "Digest"
            FROM cp6_tool_approval_v1.outbox o LEFT JOIN cp6_tool_approval_v1.delivery d
              ON d.environment_id=o.environment_id AND d.tenant_id=o.tenant_id AND d.outbox_id=o.id
            WHERE o.environment_id={route.Environment} AND o.tenant_id={tenant} AND (d.outbox_id IS NULL OR
              (d.lease_until<=clock_timestamp() AND ((d.state IN('PENDING','OUTCOME_UNKNOWN') AND d.next_attempt<=clock_timestamp())
                OR (d.state='BLOCKED' AND (d.route_hash<>{hash} OR d.registration_revision<>{registration.Revision})))))
            ORDER BY coalesce(d.next_attempt,'-infinity'::timestamptz),o.id LIMIT 1
            """).SingleOrDefaultAsync(ct);
        if(original==null)return null;
        var savedRoute=await Query<string>($"SELECT route_json AS \"Value\" FROM cp6_tool_approval_v1.delivery WHERE environment_id={route.Environment} AND tenant_id={tenant} AND outbox_id={original.Id}").SingleOrDefaultAsync(ct);
        if(savedRoute!=null)
        {
            var prior=JsonSerializer.Deserialize<ToolApprovalDeliveryRouteV1>(savedRoute,ApprovalBindingOwnerV2.Json)??throw Error("DELIVERY_ROUTE_INVALID",503);
            if(prior!=route with{Version=prior.Version})throw Error("DELIVERY_ROUTE_IDENTITY_CHANGED",409);
        }
        var parsed=Parse(original.Payload);var e=parsed.Value;
        if(parsed.Full!=original.Digest || !parsed.Canonical.AsSpan().SequenceEqual(original.Payload) || e.OwnerOutboxId!=original.Id
            || e.SubmissionId!=original.Submission || e.Scope.TenantId!=tenant || e.Scope.EnvironmentId!=route.Environment.ToString()
            || e.Subject.BusinessOwner!=Owner)throw Error("DELIVERY_ORIGINAL_INVALID",503);
        var token=Guid.NewGuid();var routeJson=System.Text.Encoding.UTF8.GetString(ApprovalCanonicalV2.Bytes(route));
        await Execute($"""
            INSERT INTO cp6_tool_approval_v1.delivery(environment_id,tenant_id,outbox_id,state,epoch,lease_token,lease_until,next_attempt,route_hash,route_json,registration_revision)
            VALUES({route.Environment},{tenant},{original.Id},'OUTCOME_UNKNOWN',1,{token},clock_timestamp()+interval '90 seconds',clock_timestamp(),{hash},{routeJson},{registration.Revision})
            ON CONFLICT(environment_id,tenant_id,outbox_id) DO UPDATE SET state='OUTCOME_UNKNOWN',epoch=cp6_tool_approval_v1.delivery.epoch+1,
              lease_token=EXCLUDED.lease_token,lease_until=EXCLUDED.lease_until,route_hash=EXCLUDED.route_hash,route_json=EXCLUDED.route_json,
              registration_revision=EXCLUDED.registration_revision,error_code=NULL
            """,ct);
        var epoch=await Query<long>($"SELECT epoch AS \"Value\" FROM cp6_tool_approval_v1.delivery WHERE environment_id={route.Environment} AND tenant_id={tenant} AND outbox_id={original.Id}").SingleAsync(ct);
        await Execute($"""
            INSERT INTO cp6_tool_approval_v1.delivery_attempts(environment_id,tenant_id,outbox_id,epoch,lease_token,route_json,registration_revision,started_at)
            VALUES({route.Environment},{tenant},{original.Id},{epoch},{token},{routeJson},{registration.Revision},clock_timestamp())
            """,ct);
        if(await Authority(route,ct)!=registration)throw Error("DELIVERY_AUTHORITY_CHANGED",403);
        // If this commit acknowledgement is lost, RunAsync never sends. A later claim queries the original command.
        await tx.CommitAsync(ct);return new(original,e,route,registration.Revision,epoch,token);
    }
    internal static ApprovalSubmissionAckV2 Receipt(byte[] bytes, ApprovalSubmitCommandV2 e)
    {
        try
        {
            using var strict=WorkflowCommandJsonV1.Parse(bytes);
            var root=strict.RootElement;
            var expected=new[]{"submissionId","instanceId","state","bindingRevisionId","bindingTimelineRevisionId","flowDefVersionId","subject","policy","rowVersion","errorCode","alias"};
            if(root.ValueKind!=JsonValueKind.Object || !root.EnumerateObject().Select(p=>p.Name).Order().SequenceEqual(expected.Order()))throw new JsonException();
            var a=JsonSerializer.Deserialize<ApprovalSubmissionAckV2>(bytes,ApprovalBindingOwnerV2.Json)??throw new JsonException();
            if(a.SubmissionId==Guid.Empty || a.InstanceId!=null || a.State!="DISPATCH_PENDING" || a.ErrorCode!=null
                || a.BindingRevisionId==null || a.BindingRevisionId==Guid.Empty
                || a.BindingTimelineRevisionId==null || a.BindingTimelineRevisionId==Guid.Empty || a.FlowDefVersionId==null || a.FlowDefVersionId==Guid.Empty
                || a.Subject!=e.Subject || a.Policy!=e.Policy || Convert.FromBase64String(a.RowVersion).Length!=8
                || Convert.ToBase64String(Convert.FromBase64String(a.RowVersion))!=a.RowVersion)throw new JsonException();
            if(a.Alias==null ? a.SubmissionId!=e.SubmissionId : a.Alias.RequestedSubmissionId!=e.SubmissionId
                || a.Alias.CanonicalSubmissionId!=a.SubmissionId || a.Alias.AliasId==Guid.Empty || a.SubmissionId==e.SubmissionId)throw new JsonException();
            // Exact canonical nested records also reject extra subject/policy properties ignored by a DTO reader.
            using var canonical=JsonDocument.Parse(ApprovalCanonicalV2.Bytes(a));
            if(ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(root))!=ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(canonical.RootElement)))throw new JsonException();
            return a;
        }
        catch(Exception ex)when(ex is JsonException or FormatException or InvalidOperationException or ArgumentException)
        {throw Error("DELIVERY_RECEIPT_INVALID",409);}
    }
    internal async Task<string> FinishAsync(Lease lease, string state, string? error, ApprovalSubmissionAckV2? ack, CancellationToken ct=default)
    {
        Clean();var r=lease.Route;await using var tx=await db.Database.BeginTransactionAsync(ct);await Ready(ct);var registration=await Authority(r,ct);
        if(registration.Revision!=lease.Registration)throw Error("DELIVERY_AUTHORITY_CHANGED",403);
        if(ack!=null)
        {
            ack=Receipt(ApprovalCanonicalV2.Bytes(ack),lease.Envelope);
            if(ack.Alias!=null)
            {
                var canonical=await Query<byte[]>($"SELECT payload AS \"Value\" FROM cp6_tool_approval_v1.submissions WHERE environment_id={r.Environment} AND tenant_id={r.Tenant} AND id={ack.SubmissionId}").SingleOrDefaultAsync(ct);
                // Never manufacture a local canonical Owner intent from a remote alias response.
                if(canonical==null || Parse(canonical).IntentCore!=Parse(lease.Original.Payload).IntentCore)
                {state="CONFLICT";error="DELIVERY_CANONICAL_OWNER_MISSING";ack=null;}
            }
        }
        var json=ack==null?null:Encode(ack);
        var affected=await Execute($"""
            UPDATE cp6_tool_approval_v1.delivery SET state={state},ack_json={json},error_code={error},lease_until=clock_timestamp(),
              next_attempt=clock_timestamp()+interval '30 seconds'
            WHERE environment_id={r.Environment} AND tenant_id={r.Tenant} AND outbox_id={lease.Original.Id}
              AND epoch={lease.Epoch} AND lease_token={lease.Token} AND state='OUTCOME_UNKNOWN' AND lease_until>clock_timestamp()
            """,ct);
        if(affected==0)return "LEASE_LOST";
        await Execute($"""
            INSERT INTO cp6_tool_approval_v1.delivery_results(environment_id,tenant_id,outbox_id,epoch,lease_token,state,error_code,ack_json,recorded_at)
            VALUES({r.Environment},{r.Tenant},{lease.Original.Id},{lease.Epoch},{lease.Token},{state},{error},{json},clock_timestamp())
            """,ct);
        if(await Authority(r,ct)!=registration)throw Error("DELIVERY_AUTHORITY_CHANGED",403);
        await tx.CommitAsync(ct);return state;
    }
    public async Task<ToolApprovalDeliveryRunV1> RunAsync(Guid tenant,CancellationToken ct=default)
    {
        Lease? lease=null;
        try
        {
            lease=await ClaimAsync(tenant,ct);if(lease==null)return new("IDLE_OR_DISABLED");
            var state="OUTCOME_UNKNOWN";string? error=null;ApprovalSubmissionAckV2? ack=null;
            try
            {
                // Query first even on the first local attempt: a lost sender commit is never inferred from HTTP absence.
                var response=await transport.SendAsync(lease.Route,lease.Envelope.CommandId,lease.Original.Payload,true,ct);
                if(response.Status==404)response=await transport.SendAsync(lease.Route,lease.Envelope.CommandId,lease.Original.Payload,false,ct);
                if(response.Status is 200 or 201)
                {ack=Receipt(response.Body,lease.Envelope);state=ack.Alias==null?"RECEIVED":"DUPLICATE_OF";}
                else if(response.Status is 202 or 429 or 503 or 504){error="RECEIVER_PENDING_OR_UNAVAILABLE";}
                else {state=response.Status==409?"CONFLICT":"BLOCKED";error="RECEIVER_HTTP_"+response.Status;}
            }
            catch(ScopeAuthorizationExceptionV1 e)when(e.Status==409){state="CONFLICT";error=e.Message;}
            catch(Exception e)when(e is HttpRequestException or OperationCanceledException or TimeoutException)
            {ct.ThrowIfCancellationRequested();error="TRANSPORT_OUTCOME_UNKNOWN";}
            return new(await FinishAsync(lease,state,error,ack,ct),lease.Original.Id);
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
        catch(Exception e)when(e is System.Data.Common.DbException or DbUpdateException or ScopeAuthorizationExceptionV1 or TimeoutException or IOException)
        {return new("OUTCOME_UNKNOWN",lease?.Original.Id);}
    }
    internal static async Task<ToolApprovalDeliveryViewV1?> ReadAsync(CP6Context db,Guid environment,Guid submission,CancellationToken ct,string businessOwner=ApprovalBusinessProfileV2.ToolOwner)
    {
        var profile=ApprovalBusinessProfileV2.For(businessOwner);
        var row=await db.Database.SqlQuery<DeliveryRow>(profile.SubmissionSql($"""
            SELECT d.outbox_id AS "Outbox",d.state AS "State",d.epoch AS "Attempts",d.error_code AS "Error",d.ack_json AS "Ack",o.payload AS "Payload"
            FROM cp6_tool_approval_v1.delivery d JOIN cp6_tool_approval_v1.outbox o USING(environment_id,tenant_id)
            WHERE o.id=d.outbox_id AND d.environment_id={environment} AND d.tenant_id={db.CurrentTenantId} AND o.submission_id={submission}
            """)).SingleOrDefaultAsync(ct);
        return row==null?null:new(submission,row.Outbox,row.State,row.Attempts,row.Error,row.Ack==null?null:Receipt(System.Text.Encoding.UTF8.GetBytes(row.Ack),Parse(row.Payload).Value));
    }
    private sealed record DeliveryRow(Guid Outbox,string State,long Attempts,string? Error,string? Ack,byte[] Payload);
}
