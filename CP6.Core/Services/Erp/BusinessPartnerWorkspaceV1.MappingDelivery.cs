using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Erp;

public sealed partial class BusinessPartnerWorkspaceV1
{
    private static readonly int[] RetryMappingSeconds = [1, 5, 30, 120, 600];
    private sealed record DeliveryClaim(MappingEventRow Event, Guid ClaimId, int Attempt, BusinessPartnerMappingAuthorityBindingV1 Binding);

    public async Task RunMappingDeliveryOnce(CancellationToken ct)
    {
        if (mappingAuthority is null) return;
        var claim = await ClaimMappingDelivery(ct);
        if (claim is null) return;
        var result = await mappingAuthority.DeliverAsync(claim.Binding, claim.Event.Body, claim.Event.BodyHash, ct);
        await FinishMappingDelivery(claim, result, ct);
    }

    private async Task<DeliveryClaim?> ClaimMappingDelivery(CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = PgTime(Now);
        var row = (await db.Database.SqlQuery<MappingEventRow>($"""
            SELECT e.event_id AS "EventId",e.legal_entity_key AS "Legal",e.account_id AS "Account",e.revision AS "Revision",
                   e.body AS "Body",e.body_hash AS "BodyHash",e.source_as_of AS "SourceAsOf"
              FROM main_bp.mapping_events e JOIN main_bp.mapping_deliveries d USING(tenant_id,event_id)
              JOIN main_bp.mapping_authorities a USING(tenant_id,legal_entity_key)
             WHERE e.tenant_id={Tenant} AND a.purpose={ProjectionPurpose} AND a.enabled
               AND ((d.state IN ('READY','SENT_UNKNOWN') AND (d.next_attempt_at IS NULL OR d.next_attempt_at<={now}))
                     OR (d.state='LEASED' AND d.lease_until<={now}))
               AND NOT EXISTS (
                 SELECT FROM main_bp.mapping_events prior JOIN main_bp.mapping_deliveries transport USING(tenant_id,event_id)
                  WHERE prior.tenant_id=e.tenant_id AND prior.legal_entity_key=e.legal_entity_key AND prior.account_id=e.account_id
                    AND (prior.revision,prior.observation_sequence)<(e.revision,e.observation_sequence)
                    AND transport.state IN ('READY','SENT_UNKNOWN','LEASED'))
             ORDER BY e.source_as_of,e.event_id LIMIT 1 FOR UPDATE OF d SKIP LOCKED
            """).ToListAsync(ct)).SingleOrDefault();
        if (row is null) return null;
        var authority = await MappingAuthority(row.Legal, ProjectionPurpose, ct);
        if (authority is null) return null;
        RequireMappingEvent(row);
        var attempt = await db.Database.SqlQuery<int>($"""
            SELECT attempt AS "Value" FROM main_bp.mapping_deliveries WHERE tenant_id={Tenant} AND event_id={row.EventId}
            """).SingleAsync(ct);
        if (attempt >= 20)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE main_bp.mapping_deliveries SET state='DEAD',claim_id=NULL,lease_until=NULL,last_error='DELIVERY_OUTCOME_UNKNOWN'
                 WHERE tenant_id={Tenant} AND event_id={row.EventId}
                """, ct);
            await CompleteDeliveryJobs(row.EventId, "FAILED", "DELIVERY_OUTCOME_UNKNOWN", now, ct);
            await tx.CommitAsync(ct);
            return null;
        }
        var claim = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE main_bp.mapping_deliveries SET state='LEASED',attempt=attempt+1,claim_id={claim},lease_until={now.AddSeconds(30)}
             WHERE tenant_id={Tenant} AND event_id={row.EventId}
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO main_bp.mapping_delivery_attempts(tenant_id,event_id,claim_id,started_at)
            VALUES({Tenant},{row.EventId},{claim},{now})
            """, ct);
        await tx.CommitAsync(ct);
        return new(row, claim, attempt + 1, authority);
    }

    private async Task FinishMappingDelivery(DeliveryClaim claim, BusinessPartnerMappingDeliveryResultV1? result, CancellationToken ct)
    {
        var row = claim.Event;
        var id = row.EventId;
        var now = PgTime(Now);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await BpUseScopeGate.LockAsync(db, row.Legal, true, ct);
        var slot = await ReadMappingSlot(row.Legal, row.Account, true, ct);
        var authority = await MappingAuthority(row.Legal, ProjectionPurpose, ct);
        var status = result?.Status;
        var responseHash = result?.ResponseHash;
        var protectedBody = result?.ResponseBody is { } raw ? Protect("mapping-delivery-response", claim.ClaimId).Protect(raw) : null;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE main_bp.mapping_delivery_attempts SET response_status={status},response_hash={responseHash},protected_response={protectedBody},finished_at={now}
             WHERE tenant_id={Tenant} AND event_id={id} AND claim_id={claim.ClaimId} AND finished_at IS NULL
            """, ct);

        var trustCurrent = authority is not null && MappingAuthorityHash(authority) == MappingAuthorityHash(claim.Binding);
        var accepted = trustCurrent && status is >= 200 and < 300;
        var terminalError = trustCurrent ? status switch
        {
            401 or 403 => "OWNER_AUTHORIZATION_REQUIRED", 404 => "OWNER_REVIEW",
            409 => "OWNER_STREAM_CONFLICT", >= 400 and < 500 when status is not (408 or 429) => "DELIVERY_REJECTED",
            _ => null
        } : null;
        var state = accepted ? "ACKED_PROCESSING" : terminalError is not null || claim.Attempt >= 20 ? "DEAD" : "SENT_UNKNOWN";
        var failure = accepted ? null : terminalError ?? "DELIVERY_OUTCOME_UNKNOWN";
        DateTimeOffset? next = state == "SENT_UNKNOWN" ? now.AddSeconds(RetryMappingSeconds[Math.Min(RetryMappingSeconds.Length, claim.Attempt) - 1]) : null;
        var applied = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE main_bp.mapping_deliveries SET state={state},claim_id=NULL,lease_until=NULL,last_error={failure},next_attempt_at={next}
             WHERE tenant_id={Tenant} AND event_id={id} AND claim_id={claim.ClaimId} AND lease_until>{now}
            """, ct) == 1;
        if (applied && (accepted || state == "DEAD")) await CompleteDeliveryJobs(id, accepted ? "COMPLETE" : "FAILED", failure, now, ct);
        if (applied && trustCurrent && status == 409 && slot?.Revision == row.Revision)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE main_bp.mapping_slots SET generation={NextMappingNumber(slot.Generation)},current_projection_id=NULL,owner_conflict=true
                 WHERE tenant_id={Tenant} AND legal_entity_key={row.Legal} AND account_id={row.Account}
                """, ct);
        await tx.CommitAsync(ct);
    }

    private Task CompleteDeliveryJobs(Guid id, string state, string? failure, DateTimeOffset now, CancellationToken ct)
        => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE main_bp.mapping_jobs SET state={state},last_error={failure},updated_at={now}
             WHERE tenant_id={Tenant} AND event_id={id} AND mode='RETRY_ORIGINAL_EVENT' AND state='WAITING'
            """, ct);
}
