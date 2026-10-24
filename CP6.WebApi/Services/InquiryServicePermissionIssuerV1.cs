using System.Data;
using System.Text;
using CP6.Entity.DomainModels.Sys;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;

namespace CP6.WebApi.Services;

/// <summary>Service-only Main point-in-time permission producer. The frozen registration is the
/// authority for client-to-principal membership; actual IAM rows supply its current grants.
/// CRM still needs its own current identity/resource fence inside the business transaction.</summary>
public sealed class InquiryServicePermissionIssuerV1(CP6Context db, ITenantContext tenant,
    InquiryServicePrincipalRegistryV1 registry, InquiryPermissionSigningKeyV1 signing,
    Func<AuthorizationCatalogSnapshotV1>? captureCatalog = null, CrmIdentityRuntime? identityRuntime = null)
{
    public bool Enabled => registry.Enabled && signing.Enabled;
    public bool ContextEnabled => Enabled && registry.IdentityPublicationEnabled && identityRuntime is not null;
    public Task<string> IssueAsync(InquiryPermissionRequestV1 request, StringValues authorization, CancellationToken ct)
        => IssueCoreAsync(request, authorization, null, ct);
    public Task<string> IssueContextAsync(InquiryServiceContextRequestV1 request, StringValues authorization, CancellationToken ct)
    {
        if (request is null || !InquiryPermissionSigningKeyV1.ValidServiceNonce(request.RequestNonce))
            throw new InquiryPermissionExceptionV1(400, "invalid_request");
        if (!ContextEnabled) throw new InquiryPermissionExceptionV1(503, "inquiry_service_context_unavailable");
        return IssueCoreAsync(new(request.Purpose, request.SiteId, request.FormId), authorization, request.RequestNonce, ct);
    }
    private async Task<string> IssueCoreAsync(InquiryPermissionRequestV1 request, StringValues authorization, string? nonce, CancellationToken ct)
    {
        if (request is null || request.SiteId == Guid.Empty || request.FormId == Guid.Empty)
            throw new InquiryPermissionExceptionV1(400, "invalid_request");
        if (request.Purpose != "intake:release" && !(nonce is not null && request.Purpose == "public:receive"))
            throw new InquiryPermissionExceptionV1(400, "unsupported_projection_purpose");
        if (!Enabled || !db.Database.IsNpgsql() || db.Database.CurrentTransaction is not null
            || System.Transactions.Transaction.Current is not null || db.ChangeTracker.Entries().Any())
            throw new InquiryPermissionExceptionV1(503, "inquiry_authority_unavailable");
        var registration = registry.Authenticate(new StringValues(authorization.ToArray()))
            ?? throw new InquiryPermissionExceptionV1(401, "invalid_client");
        if (!registry.IsPublished(registration.Tenant))
            throw new InquiryPermissionExceptionV1(503, "inquiry_service_identity_not_published");
        var previousTenant = tenant.CurrentTenantId;
        try
        {
            tenant.CurrentTenantId = registration.Tenant;
            if (db.CurrentTenantId != registration.Tenant) throw new CurrentPermissionUnavailableException();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
            async Task<DateTimeOffset> RequireCurrentTenant()
            {
                if (db.CurrentTenantId != registration.Tenant) throw new CurrentPermissionUnavailableException();
                var now = await db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
                var local = now.LocalDateTime;
                if (!await db.Sys_Tenants.AsNoTracking().AnyAsync(x => x.Id == registration.Tenant && x.Enable && x.AuthenticationPolicyRevision != Guid.Empty
                    && (x.ExpireDate == null || x.ExpireDate > local), ct))
                    throw new InquiryPermissionExceptionV1(403, "insufficient_scope");
                return now;
            }
            await RequireCurrentTenant();
            CrmIdentitySnapshot? serviceSource = null;
            if (registry.IdentityPublicationEnabled)
            {
                var aggregate = "service-principal:" + registration.Subject.ToString("D");
                var row = await db.CrmIdentitySnapshots.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == registration.Tenant && x.AggregateId == aggregate, ct);
                serviceSource = row;
                var source = row is null ? null : System.Text.Json.JsonSerializer.Deserialize<ServicePrincipalIdentityData>(row.PayloadJson, IdentityEventContracts.Json);
                if (row is null || row.IsDeleted || row.EventType != IdentityEventContracts.ServicePrincipalChanged || source is null
                    || source.Version != row.Version || row.PayloadSha256 != IdentityEventContracts.Hash(Encoding.UTF8.GetBytes(row.PayloadJson))
                    || source.TenantId != registration.Tenant || source.PrincipalId != registration.Subject || source.ClientId != registration.Client
                    || source.DepartmentId != registration.Department || !source.Enabled || source.AuthenticationEpoch != registration.AuthenticationEpoch
                    || source.RegistrationVersion != registration.Version || source.RegistrationRevision != registration.Revision
                    || source.SourceProfile != "cp6-inquiry-service-v1" || !source.Roles.SequenceEqual(registration.Roles.Select(IdentityEventContracts.RoleSubject)))
                    throw new InquiryPermissionExceptionV1(503, "inquiry_service_identity_not_current");
            }
            if (nonce is not null && (identityRuntime is null || serviceSource is null
                || identityRuntime.Options.Issuer != signing.Issuer || !identityRuntime.Options.Tenants.ContainsKey(registration.Tenant)
                || !await db.CrmIdentityBootstrapStates.AsNoTracking().AnyAsync(x => x.TenantId == registration.Tenant
                    && x.CompletedAtUtc != null && x.ContractBundleSha256 == identityRuntime.ContractBundleSha256, ct)))
                throw new InquiryPermissionExceptionV1(503, "inquiry_service_context_unavailable");
            // Do not alias even a disabled human identity in the same organization.
            if (await db.Sys_Users.AsNoTracking().AnyAsync(x => x.Id == registration.Subject, ct))
                throw new InquiryPermissionExceptionV1(503, "inquiry_service_identity_collision");
            var facts = request.Purpose == "public:receive" ? null : await InquiryServicePermissionSourceV1.ReadAsync(db, registration, request,
                (captureCatalog ?? AuthorizationCatalogV1.Capture)(), ct);
            var issued = await RequireCurrentTenant();
            var expiry = issued.AddMinutes(5);
            var tenantExpiry = await db.Sys_Tenants.Where(x => x.Id == registration.Tenant).Select(x => x.ExpireDate).SingleAsync(ct);
            if (tenantExpiry is { } end && new DateTimeOffset(DateTime.SpecifyKind(end, DateTimeKind.Local)) < expiry)
                expiry = new DateTimeOffset(DateTime.SpecifyKind(end, DateTimeKind.Local));
            // SessionId is deliberately absent; the frozen projection contains no browser-session field.
            string? result = facts is null ? null : signing.Sign(new(registration.Tenant, registration.Subject, Guid.Empty, expiry), request, facts, issued, expiry);
            if (nonce is not null)
            {
                var aggregates = registration.Roles.Select(x => "permission:" + IdentityEventContracts.RoleSubject(x)).ToArray();
                var roles = await db.CrmIdentitySnapshots.AsNoTracking().Where(x => x.TenantId == registration.Tenant && aggregates.Contains(x.AggregateId)).ToArrayAsync(ct);
                if (roles.Length != aggregates.Length || roles.Any(x => x.Version < 1
                    || x.PayloadSha256 != IdentityEventContracts.Hash(Encoding.UTF8.GetBytes(x.PayloadJson))
                    || x.EventType is not (IdentityEventContracts.PermissionChanged or IdentityEventContracts.PermissionChangedV2 or IdentityEventContracts.PermissionChangedV3)))
                    throw new InquiryPermissionExceptionV1(503, "inquiry_service_role_source_unavailable");
                result = signing.SignServiceContext(result, nonce, registration, serviceSource!, roles, identityRuntime!.ContractBundleSha256, request, issued, expiry);
            }
            ct.ThrowIfCancellationRequested(); await tx.CommitAsync(ct);
            return result ?? throw new InquiryPermissionExceptionV1(503,"inquiry_authority_unavailable");
        }
        finally { tenant.CurrentTenantId = previousTenant; }
    }
}
