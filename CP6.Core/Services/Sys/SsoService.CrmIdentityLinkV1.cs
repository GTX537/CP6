using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.Sys;

public partial class SsoService
{
    internal const string CrmIdentityLinkKind = "sso-identity-link";

    internal static Guid CrmIdentityLinkOperation(Guid tenant, string reference) => new(
        SHA256.HashData(Encoding.UTF8.GetBytes($"{CrmIdentityLinkKind}/{tenant:D}/{reference}")).AsSpan(0, 16));

    private async Task<string?> ReadBrowserOriginalMappingAsync(MappingInput input, byte[] issuer, byte[] subject)
    {
        if (input.Kind != "Web") return null;
        var rows = await _db.Database.SqlQuery<BrowserMapping>($"""
            SELECT i.operation_ref AS "Reference", i.issuer={issuer} AND i.subject={subject} AS "Matches"
            FROM cp6_authentication_v1.sso_mapping_intents i
            LEFT JOIN cp6_authentication_v1.sso_mapping_receipts r ON r.operation_ref=i.operation_ref
            WHERE i.tenant_id={input.Policy.TenantId} AND i.client_kind='Web' AND i.client_binding={input.Binding}
              AND i.operation_ref<>{input.Reference} AND r.state IS DISTINCT FROM 'CLOSED'
            LIMIT 2
            """).ToArrayAsync();
        // Only a verified refresh of this browser's original mapping can use an existing SSO capability.
        if (rows.Length > 1 || rows.Length == 1 && !rows[0].Matches)
            throw new InvalidOperationException("SSO_IDENTITY_CHANGED");
        return rows.SingleOrDefault()?.Reference;
    }

    private async Task<SsoCrmLink?> PrepareSsoCrmLinkAsync(MappingInput input, MappingIntent mapping, Guid store,
        string document, IDbContextTransaction tx)
    {
        var tenant = input.Policy.TenantId;
        if (await IamCrmUserSealV1.ExistsAsync(_db, tenant, mapping.UserId, default))
            throw new InvalidOperationException("SSO_IDENTITY_CHANGED");

        var plan = new SsoCrmLinkPlan(Guid.NewGuid(), input.Deadline, MappingDigest(document));
        const string preview = "sso_original_crm_link";
        await tx.CreateSavepointAsync(preview);
        var (_, advances) = await CaptureSsoLinkAsync(input, mapping, plan.Epoch);
        await tx.RollbackToSavepointAsync(preview);
        await tx.ReleaseSavepointAsync(preview);
        _db.ChangeTracker.Clear();
        if (advances.Length == 0) return null;

        await IamAdministrationV1.RequireSealDurabilityAsync(_db, default);
        var operation = CrmIdentityLinkOperation(tenant, input.Reference);
        await IamCrmUserSealV1.InsertAsync(_db, tenant, mapping.UserId, mapping.UserId, CrmIdentityLinkKind, operation,
            JsonSerializer.Serialize(plan), store, advances, default);
        var accepted = await IamCrmUserSealV1.ReadAsync(_db, tenant, operation, default)
            ?? throw new SsoMappingRecoveryRequiredV1(input.Reference);
        await RequireMappingTenant(tenant, input.Deadline);
        return new(input, mapping, plan, accepted);
    }

    private async Task<SsoCrmLink> ContinueSsoCrmLinkAsync(MappingInput callback, string reference)
    {
        var tenant = callback.Policy.TenantId;
        var original = await _db.Database.SqlQuery<PersistedMapping>($"""
            SELECT client_kind AS "Kind", client_binding AS "Binding", document AS "Document"
            FROM cp6_authentication_v1.sso_mapping_intents WHERE operation_ref={reference} AND tenant_id={tenant}
            """).SingleAsync();
        var accepted = await IamCrmUserSealV1.ReadAsync(_db, tenant, CrmIdentityLinkOperation(tenant, reference), default);
        if (accepted?.Kind != CrmIdentityLinkKind || original.Kind != callback.Kind || original.Binding != callback.Binding)
            throw new SsoMappingRecoveryRequiredV1(reference);
        var plan = JsonSerializer.Deserialize<SsoCrmLinkPlan>(accepted.RequestProtected);
        var mapping = JsonSerializer.Deserialize<MappingIntent>(original.Document);
        if (plan is null || plan.DocumentDigest != MappingDigest(original.Document) || mapping?.Kind != "LINK" ||
            mapping.UserId != accepted.UserId || mapping.UserId != accepted.ActorId)
            throw new SsoMappingRecoveryRequiredV1(reference);

        var input = callback with { Reference = reference, Deadline = plan.Deadline };
        await RequireCurrentSsoLinkAsync(input, mapping);
        return new(input, mapping, plan, accepted);
    }

    private async Task<Sys_User> ExecuteSsoCrmLinkAsync(SsoCrmLink original)
    {
        var input = original.Input;
        var tenant = input.Policy.TenantId;
        try
        {
            var fence = _db.InquiryFence ?? throw new SsoMappingRecoveryRequiredV1(input.Reference);
            await using var crm = await fence.OpenSealAsync(tenant, original.Pending.StoreId, default);
            if (await MarkSsoCrmLinkStartedAsync(original) is { } committed) return committed;

            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            await AuthenticationCompletionGateV1.AcquireAsync(_db);
            if (await ReadCommittedSsoLinkAsync(original) is { } existing) return existing;
            var pending = await ReloadSsoCrmLinkAsync(original);
            if (!pending.SealStarted) throw new SsoMappingRecoveryRequiredV1(input.Reference);
            await RequireCurrentSsoLinkAsync(input, original.Mapping);

            var (user, advances) = await CaptureSsoLinkAsync(input, original.Mapping, original.Plan.Epoch);
            var accepted = pending.ReadAdvances().ToDictionary(advance => advance.Aggregate, StringComparer.Ordinal);
            var aggregate = $"user:{user.Id:D}";
            // The original user source stays exact. Independently retired or issued grants are read again under this root.
            if (advances.SingleOrDefault(advance => advance.Aggregate == aggregate) != accepted.GetValueOrDefault(aggregate) ||
                advances.Any(advance => accepted.TryGetValue(advance.Aggregate, out var prior) && advance != prior))
                throw new SsoMappingRecoveryRequiredV1(input.Reference);

            await IamAdministrationV1.RequireSealDurabilityAsync(_db, default);
            var result = await RecordMappingAsync(input, original.Mapping, user);
            await pending.RemoveAsync(_db, tenant, default);
            await RequireMappingTenant(tenant, input.Deadline);
            if (advances.Length > 0)
                await CrmInquiryTenantPauseV1.AdvanceConnectedIdentitiesAsync(crm, tenant, pending.StoreId,
                    advances.Select(advance => (advance.Aggregate, advance.Version)).ToArray(), default);
            await RequireMappingTenant(tenant, input.Deadline);
            await tx.CommitAsync();
            return result;
        }
        catch (Exception error) when (error is not SsoMappingRecoveryRequiredV1 and not InvalidOperationException { Message: "SSO_MAPPING_CLOSED" })
        {
            // A later Main rollback cannot establish the CRM seal's outcome.
            throw new SsoMappingRecoveryRequiredV1(input.Reference, error);
        }
        finally
        {
            _db.ChangeTracker.Clear();
        }
    }

    private async Task<Sys_User?> MarkSsoCrmLinkStartedAsync(SsoCrmLink original)
    {
        var input = original.Input;
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await AuthenticationCompletionGateV1.AcquireAsync(_db);
        if (await ReadCommittedSsoLinkAsync(original) is { } committed) return committed;
        var pending = await ReloadSsoCrmLinkAsync(original);
        await RequireCurrentSsoLinkAsync(input, original.Mapping);
        if (!pending.SealStarted)
        {
            await IamAdministrationV1.RequireSealDurabilityAsync(_db, default);
            await pending.MarkStartedAsync(_db, input.Policy.TenantId, default);
        }
        await RequireMappingTenant(input.Policy.TenantId, input.Deadline);
        await tx.CommitAsync();
        return null;
    }

    private async Task RequireCurrentSsoLinkAsync(MappingInput input, MappingIntent accepted)
    {
        await RequireMappingAuthority(input);
        if (await PrepareMapping(input, accepted.UserId) != accepted)
            throw new InvalidOperationException("SSO_IDENTITY_CHANGED");
    }

    private async Task<Sys_User?> ReadCommittedSsoLinkAsync(SsoCrmLink original)
    {
        var input = original.Input;
        var status = await new SsoMappingRecoveryV1(_db).ResolveOwnedAsync(input.Reference, input.Policy.TenantId, false, null, default);
        if (status.State == "UNKNOWN") return null;
        if (status.State == "CLOSED") throw new InvalidOperationException("SSO_MAPPING_CLOSED");
        if (status.State != "IDENTITY_BOUND") throw new SsoMappingRecoveryRequiredV1(input.Reference);
        var user = await SsoIdentityQueriesV1.ExactAsync(_db, input.Policy.TenantId, input.Issuer, input.Subject);
        if (user?.Id != original.Pending.UserId) throw new InvalidOperationException("SSO_IDENTITY_CHANGED");
        RequireActive(user);
        return user;
    }

    private async Task<IamCrmUserSealV1> ReloadSsoCrmLinkAsync(SsoCrmLink original)
    {
        var before = original.Pending;
        var current = await IamCrmUserSealV1.ReadAsync(_db, original.Input.Policy.TenantId, before.OperationId, default);
        if (current is null || current.Kind != CrmIdentityLinkKind || current.UserId != before.UserId || current.ActorId != before.ActorId ||
            current.StoreId != before.StoreId || current.RequestProtected != before.RequestProtected || current.SourceSeals != before.SourceSeals)
            throw new SsoMappingRecoveryRequiredV1(original.Input.Reference);
        return current;
    }

    private async Task<(Sys_User User, CrmUserSealAdvanceV1[] Advances)> CaptureSsoLinkAsync(MappingInput input, MappingIntent mapping, Guid epoch)
    {
        var advances = new List<CrmUserSealAdvanceV1>();
        _db.CaptureCrmUserSeal = (tenant, subject, advance) =>
        {
            if ((tenant, subject) != (input.Policy.TenantId, mapping.UserId))
                throw new InvalidOperationException("SSO_IDENTITY_WRITER_UNAVAILABLE");
            advances.Add(advance);
        };
        _db.CaptureExactCrmUserSeal = true;
        try
        {
            var user = await ApplyMappingAsync(input, mapping, epoch);
            return (user, advances.ToArray());
        }
        finally
        {
            _db.CaptureExactCrmUserSeal = false;
            _db.CaptureCrmUserSeal = null;
        }
    }

    private static string MappingDigest(string document) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(document)));

    private sealed record SsoCrmLink(MappingInput Input, MappingIntent Mapping, SsoCrmLinkPlan Plan, IamCrmUserSealV1 Pending);
    private sealed record SsoCrmLinkPlan(Guid Epoch, DateTimeOffset Deadline, string DocumentDigest);
    private sealed record PersistedMapping(string Kind, string Binding, string Document);
    private sealed record BrowserMapping(string Reference, bool Matches);
}
