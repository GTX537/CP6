using System.Data;
using System.Globalization;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using static CP6.WebApi.Services.LeadMaintenanceContractV1;

namespace CP6.WebApi.Services;

/// <summary>在身份和精确对象的发布锁内签发短期维护凭据，由 CRM 在最终事务中复核并执行业务动作。</summary>
public sealed class LeadMaintenanceContextIssuerV1(CP6Context db, ITenantContext tenant,
    InquiryServicePrincipalRegistryV1 registry, InquiryPermissionSigningKeyV1 signing,
    LeadMaintenanceTrustV1 trust, CrmIdentityRuntime? identityRuntime = null)
{
    public bool Enabled => trust.Enabled && registry.Enabled && registry.IdentityPublicationEnabled
        && signing.Enabled && identityRuntime is not null;

    internal async Task<string> IssueAsync(LeadMaintenanceRequestV1 request, StringValues authorization, CancellationToken ct)
    {
        request.Validate();
        var registration = registry.Authenticate(authorization) ?? throw new InquiryPermissionExceptionV1(401, "invalid_client");
        if (registration.Tenant != request.TenantId) throw Denied();
        if (!Enabled || identityRuntime is not { } identity || !registry.IsPublished(registration.Tenant)
            || !db.Database.IsNpgsql() || db.Database.CurrentTransaction is not null
            || System.Transactions.Transaction.Current is not null || db.ChangeTracker.Entries().Any()
            || identity.Options.Issuer != signing.Issuer) throw Unavailable();

        var previousTenant = tenant.CurrentTenantId;
        try
        {
            tenant.CurrentTenantId = registration.Tenant;
            if (db.CurrentTenantId != registration.Tenant) throw Unavailable();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
            await IdentitySourceAuthorityV1.RequireCurrentAsync(db, identity, registration.Tenant, ct);

            var organization = await db.Sys_Tenants.AsNoTracking().Where(row => row.Id == registration.Tenant && row.Enable
                    && row.AuthenticationPolicyRevision != Guid.Empty)
                .Select(row => new { row.ExpireDate }).SingleOrDefaultAsync(ct);
            if (organization is null || await TenantPendingSuspensionV1.ExistsAsync(db, registration.Tenant, ct)
                || await IamCrmRoleSealV1.BlocksUnscopedIssuanceAsync(db, registration.Tenant, registration.Roles.ToArray(), ct)
                || await IamCrmDepartmentSealV1.BlocksIssuanceAsync(db, registration.Tenant, registration.Department, [], false, null, ct))
                throw Denied();

            if (!await db.CrmIdentityBootstrapStates.AsNoTracking().AnyAsync(row => row.TenantId == registration.Tenant
                && row.CompletedAtUtc != null && row.ContractBundleSha256 == identity.ContractBundleSha256, ct)
                || await db.Sys_Users.IgnoreQueryFilters().AnyAsync(row => row.TenantId == registration.Tenant && row.Id == registration.Subject, ct))
                throw Unavailable();
            var service = await InquiryServicePermissionIssuerV1.ReadPublishedSourceAsync(db, registration, ct);
            if (service is null) throw Unavailable();
            await InquiryRetentionPermissionSourceV1.ReadAsync(db, registration, ct);

            await db.Database.SqlQuery<bool>($"""
                SELECT cp6_lead_maintenance_v1.lock_target({request.TenantId}, {request.TargetKind}, {request.ObjectId}) AS "Value"
                """).SingleAsync(ct);
            var grant = await ReadGrantAsync(request, registration, ct) ?? throw Denied();
            var rows = await db.Database.SqlQuery<LeadMaintenanceEvidenceRowV1>($"""
                SELECT component AS "Component", sequence AS "Sequence", payload AS "Payload", signature AS "Signature",
                    object_authority AS "ObjectAuthority", source_message_id AS "SourceMessageId"
                FROM cp6_lead_maintenance_v1.evidence
                WHERE tenant_id={request.TenantId} AND target_kind={request.TargetKind} AND object_id={request.ObjectId}
                  AND (component <> 'HOLD' OR {request.Purpose} <> 'privacy:policy')
                """).ToDictionaryAsync(row => row.Component, StringComparer.Ordinal, ct);

            var now = await db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
            if (grant.ValidFromUtc > now || grant.ValidUntilUtc <= now) throw Denied();
            if (!IsToken(grant.Version)) throw Unavailable();
            var expires = grant.ValidUntilUtc < now.AddMinutes(1) ? grant.ValidUntilUtc : now.AddMinutes(1);
            if (organization.ExpireDate is { } end)
            {
                var deadline = new DateTimeOffset(DateTime.SpecifyKind(end, DateTimeKind.Local)).ToUniversalTime();
                if (deadline <= now) throw Denied();
                expires = Earlier(expires, deadline);
            }

            var target = request.Target;
            var sourceRow = Required(rows, Source);
            var source = ReadEvidence<LeadMaintenanceSourceV1>(sourceRow, target);
            if (source.ProviderAuthority != request.Authority || !IsToken(source.PolicyAuthority)
                || !IsUtc(source.ReceivedAtUtc) || source.ReceivedAtUtc > now) throw Unavailable();
            var sourceWire = trust.Verify(target, request.Authority, sourceRow);
            if (source.SourceMessageId != request.SourceMessageId) throw Denied();
            if (sourceRow.ObjectAuthority != request.Authority || sourceRow.SourceMessageId != source.SourceMessageId) throw Unavailable();

            var policyRow = Required(rows, Policy);
            var policy = ReadEvidence<LeadMaintenancePolicyV1>(policyRow, target);
            var policyWire = trust.Verify(target, source.PolicyAuthority, policyRow);
            var purgeAt = EvaluatePolicy(request.TargetKind, source, sourceWire.Sha256, policy, now);
            if (request.TargetKind == ManualLead && policy.EffectiveToUtc is { } policyEnd)
                expires = Earlier(expires, policyEnd);

            List<LeadMaintenanceEvidenceWireV1> evidence = [sourceWire];
            if (request.Purpose != GetHold) evidence.Add(policyWire);
            bool? holdActive = null;
            if (request.Purpose != GetPolicy)
            {
                var holdRow = Required(rows, Hold);
                var hold = ReadEvidence<LeadMaintenanceHoldV1>(holdRow, target);
                var holdWire = trust.Verify(target, policy.HoldAuthority, holdRow);
                (holdActive, expires) = EvaluateHold(hold, policy.HoldAuthority, sourceWire.Sha256, policyWire.Sha256, now, expires);
                if (request.Purpose == Maintain)
                {
                    if (holdActive is true) throw new InquiryPermissionExceptionV1(409, "active_hold");
                    if (purgeAt > now) throw new InquiryPermissionExceptionV1(409, "retention_not_due");
                }
                evidence.Add(holdWire);
            }

            expires = signing.LeadMaintenanceExpiry(now, expires);
            var payload = JsonSerializer.SerializeToUtf8Bytes(new
            {
                target, request.SourceMessageId, request.Purpose, request.RequestNonce, scopeDigest = ScopeDigest(request),
                decision = "ALLOW", issuedAtUtc = now, expiresAtUtc = expires, grantVersion = grant.Version,
                service = new
                {
                    principalId = registration.Subject, clientId = registration.Client,
                    authenticationEpoch = registration.AuthenticationEpoch, registrationVersion = registration.Version,
                    registrationRevision = registration.Revision.ToString(CultureInfo.InvariantCulture),
                    sourceVersion = service.Version.ToString(CultureInfo.InvariantCulture),
                    sourceSha256 = service.PayloadSha256, identityBundleSha256 = identity.ContractBundleSha256
                },
                purgeAtUtc = request.Purpose == GetHold ? (DateTimeOffset?)null : purgeAt,
                holdActive, evidence
            }, LeadMaintenanceContractV1.Json);
            var signed = signing.SignLeadMaintenance(payload);
            await tx.CommitAsync(ct);
            return signed;
        }
        finally { tenant.CurrentTenantId = previousTenant; }
    }

    private Task<Grant?> ReadGrantAsync(LeadMaintenanceRequestV1 request, ServicePrincipalRegistryV1.Registration registration,
        CancellationToken ct)
        => db.Database.SqlQuery<Grant>($"""
            SELECT version AS "Version", valid_from_utc AS "ValidFromUtc", valid_until_utc AS "ValidUntilUtc"
            FROM cp6_lead_maintenance_v1.scope_grants
            WHERE tenant_id={request.TenantId} AND target_kind={request.TargetKind} AND object_id={request.ObjectId}
                AND object_authority={request.Authority} AND principal_id={registration.Subject}
                AND authentication_epoch={registration.AuthenticationEpoch} AND purpose={request.Purpose} AND enabled
            """).SingleOrDefaultAsync(ct);

    private static LeadMaintenanceEvidenceRowV1 Required(Dictionary<string, LeadMaintenanceEvidenceRowV1> rows, string component)
        => rows.TryGetValue(component, out var value) ? value : throw Unavailable();

    private static DateTimeOffset EvaluatePolicy(string kind, LeadMaintenanceSourceV1 source, string sourceDigest,
        LeadMaintenancePolicyV1 policy, DateTimeOffset now)
    {
        var inbound = kind == Inbound;
        var effectiveAt = inbound ? source.ReceivedAtUtc : now;
        if (policy.ProviderAuthority != source.PolicyAuthority || policy.SourceSha256 != sourceDigest
            || !IsDigest(policy.ContentDigest) || !IsToken(policy.HoldAuthority) || !IsToken(policy.KeyPolicyVersion)
            || !IsUtc(policy.EffectiveFromUtc) || policy.EffectiveFromUtc > effectiveAt
            || policy.EffectiveToUtc is { } end && (!IsUtc(end) || end <= effectiveAt)
            || policy.PayloadClass != (inbound ? InboundClass : LeadClass)
            || policy.Purpose != (inbound ? InboundPurpose : LeadPurpose)
            || (inbound ? policy.RetainSeconds is not > 0 || policy.RetainCalendarMonths is not null
                : policy.RetainSeconds is not null || policy.RetainCalendarMonths != LeadRetentionMonths))
            throw Unavailable();
        try
        {
            return inbound ? source.ReceivedAtUtc.AddSeconds(policy.RetainSeconds!.Value)
                : source.ReceivedAtUtc.AddMonths(LeadRetentionMonths);
        }
        catch (ArgumentOutOfRangeException) { throw Unavailable(); }
    }

    private static (bool Active, DateTimeOffset Expires) EvaluateHold(LeadMaintenanceHoldV1 hold, string authority,
        string sourceDigest, string policyDigest, DateTimeOffset now, DateTimeOffset expires)
    {
        if (hold.ProviderAuthority != authority || hold.SourceSha256 != sourceDigest || hold.PolicySha256 != policyDigest
            || !IsUtc(hold.ObservedAtUtc) || !IsUtc(hold.ValidUntilUtc) || hold.ObservedAtUtc > now || hold.ValidUntilUtc <= now
            || hold.ValidUntilUtc - hold.ObservedAtUtc > TimeSpan.FromMinutes(5) || hold.Windows is not { Length: <= 64 })
            throw Unavailable();
        expires = Earlier(expires, hold.ValidUntilUtc);
        var active = false;
        foreach (var window in hold.Windows)
        {
            if (window is null || !IsUtc(window.FromUtc) || window.ToUtc is { } end && (!IsUtc(end) || end <= window.FromUtc))
                throw Unavailable();
            if (window.FromUtc > now) expires = Earlier(expires, window.FromUtc);
            else if (window.ToUtc is null || window.ToUtc > now)
            {
                active = true;
                if (window.ToUtc is { } stop) expires = Earlier(expires, stop);
            }
        }
        return (active, expires);
    }

    private static DateTimeOffset Earlier(DateTimeOffset left, DateTimeOffset right) => left < right ? left : right;
    private sealed record Grant(string Version, DateTimeOffset ValidFromUtc, DateTimeOffset ValidUntilUtc);
}
