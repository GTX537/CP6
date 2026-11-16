using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

public sealed record SsoMappingStatusV1(string OperationRef, string State);
public sealed class SsoMappingRecoveryRequiredV1(string operationRef, Exception? inner = null)
    : InvalidOperationException("SSO_IDENTITY_MAPPING_UNKNOWN", inner)
{
    public string OperationRef { get; } = operationRef;
}

/// <summary>Safe status/closure only. Neither receipts nor consumed states can resume an IdP code exchange.</summary>
public sealed class SsoMappingRecoveryV1(CP6Context db)
{
    public static void RequireReference(string reference)
    {
        if (reference is not { Length: 64 } || !reference.All(Uri.IsHexDigit)) throw new InvalidOperationException("SSO_MAPPING_REFERENCE_INVALID");
    }
    public async Task<SsoMappingStatusV1> ResolveForTenantAsync(string reference, bool close, Guid actor, CancellationToken ct)
    {
        RequireReference(reference); RequireRootTransaction();
        var row = await StateAsync(reference, ct);
        if (row == null || JsonSerializer.Deserialize<AuthenticationPolicyV1>(row.Policy)!.TenantId != db.CurrentTenantId)
            throw new SsoConfigurationExceptionV1(404, "SSO_MAPPING_UNAVAILABLE");
        return await ResolveOwnedAsync(row.OriginalMappingRef ?? reference, db.CurrentTenantId, close, actor, ct);
    }
    public async Task<SsoMappingStatusV1?> ResolveBrowserAsync(string binding, bool close, CancellationToken ct)
    {
        RequireRootTransaction();
        var states = await db.Database.SqlQuery<BrowserMappingState>($"""
            SELECT DISTINCT original.state_hash AS "Reference", original.policy::text AS "Policy",
                i.operation_ref IS NOT NULL AS "Admitted"
            FROM cp6_authentication_v1.sso_states s
            JOIN cp6_authentication_v1.sso_states original ON original.state_hash=coalesce(s.original_mapping_ref,s.state_hash)
            LEFT JOIN cp6_authentication_v1.sso_mapping_intents i ON i.operation_ref=original.state_hash
            WHERE s.client_kind='Web' AND s.client_binding={binding}
            """).ToListAsync(ct);
        var hasOriginalIntent = states.Any(state => state.Admitted);
        var results = new List<SsoMappingStatusV1>();
        foreach (var state in states)
        {
            // A new IdP round trip may be left in flight. It has no separate mapping until it is admitted.
            // Explicit closure still fences every related state before returning the original outcome.
            var report = state.Admitted || !hasOriginalIntent;
            if (!close && !report) continue;
            var tenant = JsonSerializer.Deserialize<AuthenticationPolicyV1>(state.Policy)!.TenantId;
            var result = await ResolveOwnedAsync(state.Reference, tenant, close, null, ct);
            if (report) results.Add(result);
        }
        // Multiple SSO states cannot be represented as one successful original operation.
        return results.Count switch { 0 => null, 1 => results[0], _ => new("", "MULTIPLE_ORIGINAL_OPERATIONS") };
    }
    public async Task<SsoMappingStatusV1> ResolveOwnedAsync(string reference, Guid tenant, bool close, Guid? actor, CancellationToken ct)
    {
        RequireRootTransaction();
        var receipt = await db.Database.SqlQuery<string>($"SELECT state AS \"Value\" FROM cp6_authentication_v1.sso_mapping_receipts WHERE operation_ref={reference} AND tenant_id={tenant}").SingleOrDefaultAsync(ct);
        if (receipt != null) return new(reference, receipt);
        if (!close) return new(reference, "UNKNOWN");
        var pending = await IamCrmUserSealV1.ReadAsync(db, tenant, SsoService.CrmIdentityLinkOperation(tenant, reference), ct);
        if (pending?.Kind == SsoService.CrmIdentityLinkKind)
        {
            if (pending.SealStarted) return new(reference, "UNKNOWN");
            await IamAdministrationV1.RequireSealDurabilityAsync(db, ct);
            await pending.RemoveAsync(db, tenant, ct);
        }
        // Insert a terminal fence even before an intent arrives; a missing row is not evidence of non-execution.
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_authentication_v1.sso_mapping_receipts(operation_ref,tenant_id,state) VALUES({reference},{tenant},'CLOSED')", ct);
        var audit = new Sys_SecurityLog { TenantId = tenant, UserId = actor,
            EventType = (int)SecurityEventType.SsoIdentityMappingClosed, CreatedAt = DateTime.Now,
            Reason = "Closed original SSO mapping operation", ContextJson = JsonSerializer.Serialize(new { operationRef = reference }) };
        // The anonymous browser's request tenant may be different. Use the proven original owner;
        // StampTenant only fills an empty TenantId and preserves this explicit owner.
        db.Sys_SecurityLogs.Add(audit);
        await db.SaveChangesAsync(ct);
        return new(reference, "CLOSED");
    }
    internal async Task BindContinuationAsync(string continuation, string original, CancellationToken ct)
    {
        RequireRootTransaction();
        if (await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_authentication_v1.sso_states s SET original_mapping_ref={original}
            FROM cp6_authentication_v1.sso_states source
            WHERE s.state_hash={continuation} AND source.state_hash={original} AND s.original_mapping_ref IS NULL
              AND source.original_mapping_ref IS NULL AND s.client_kind=source.client_kind AND s.client_binding=source.client_binding
              AND s.consumed_at IS NOT NULL AND source.consumed_at IS NOT NULL
            """, ct) != 1)
            throw new InvalidOperationException("SSO_IDENTITY_CHANGED");
    }

    public Task<StateRow?> StateAsync(string reference, CancellationToken ct) => db.Database.SqlQuery<StateRow>($"""
        SELECT state_hash AS "Reference", policy::text AS "Policy", client_kind AS "Kind", client_binding AS "Binding",
            consumed_at IS NOT NULL AS "Consumed", original_mapping_ref AS "OriginalMappingRef"
        FROM cp6_authentication_v1.sso_states WHERE state_hash={reference}
        """).SingleOrDefaultAsync(ct);
    private void RequireRootTransaction()
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction == null) throw new InvalidOperationException("SSO_IDENTITY_WRITER_UNAVAILABLE");
    }
    public sealed class StateRow
    {
        public string Reference { get; set; } = ""; public string Policy { get; set; } = "";
        public string? Kind { get; set; } public string? Binding { get; set; } public bool Consumed { get; set; }
        public string? OriginalMappingRef { get; set; }
    }
    private sealed record BrowserMappingState(string Reference, string Policy, bool Admitted);
}
