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
        return await ResolveOwnedAsync(reference, db.CurrentTenantId, close, actor, ct);
    }
    public async Task<SsoMappingStatusV1?> ResolveBrowserAsync(string binding, bool close, CancellationToken ct)
    {
        RequireRootTransaction();
        var states = await db.Database.SqlQuery<StateRow>($"SELECT state_hash AS \"Reference\",policy::text AS \"Policy\",client_kind AS \"Kind\",client_binding AS \"Binding\",consumed_at IS NOT NULL AS \"Consumed\" FROM cp6_authentication_v1.sso_states WHERE client_kind='Web' AND client_binding={binding}").ToListAsync(ct);
        var results = new List<SsoMappingStatusV1>();
        foreach (var state in states)
        {
            var tenant = JsonSerializer.Deserialize<AuthenticationPolicyV1>(state.Policy)!.TenantId;
            results.Add(await ResolveOwnedAsync(state.Reference, tenant, close, null, ct));
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
    public Task<StateRow?> StateAsync(string reference, CancellationToken ct) => db.Database.SqlQuery<StateRow>($"SELECT state_hash AS \"Reference\",policy::text AS \"Policy\",client_kind AS \"Kind\",client_binding AS \"Binding\",consumed_at IS NOT NULL AS \"Consumed\" FROM cp6_authentication_v1.sso_states WHERE state_hash={reference}").SingleOrDefaultAsync(ct);
    private void RequireRootTransaction()
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction == null) throw new InvalidOperationException("SSO_IDENTITY_WRITER_UNAVAILABLE");
    }
    public sealed class StateRow
    {
        public string Reference { get; set; } = ""; public string Policy { get; set; } = "";
        public string? Kind { get; set; } public string? Binding { get; set; } public bool Consumed { get; set; }
    }
}
