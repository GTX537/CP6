using System.Data;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;

namespace CP6.WebApi.Services;

/// <summary>Service-only Main point-in-time permission producer. The frozen registration is the
/// authority for client-to-principal membership; actual IAM rows supply its current grants.
/// CRM still needs its own current identity/resource fence inside the business transaction.</summary>
public sealed class InquiryServicePermissionIssuerV1(CP6Context db, ITenantContext tenant,
    InquiryServicePrincipalRegistryV1 registry, InquiryPermissionSigningKeyV1 signing,
    Func<AuthorizationCatalogSnapshotV1>? captureCatalog = null)
{
    public bool Enabled => registry.Enabled && signing.Enabled;
    public async Task<string> IssueAsync(InquiryPermissionRequestV1 request, StringValues authorization, CancellationToken ct)
    {
        if (request is null || request.SiteId == Guid.Empty || request.FormId == Guid.Empty)
            throw new InquiryPermissionExceptionV1(400, "invalid_request");
        if (request.Purpose != "intake:release")
            throw new InquiryPermissionExceptionV1(400, "unsupported_projection_purpose");
        if (!Enabled || !db.Database.IsNpgsql() || db.Database.CurrentTransaction is not null
            || System.Transactions.Transaction.Current is not null || db.ChangeTracker.Entries().Any())
            throw new InquiryPermissionExceptionV1(503, "inquiry_authority_unavailable");
        var registration = registry.Authenticate(new StringValues(authorization.ToArray()))
            ?? throw new InquiryPermissionExceptionV1(401, "invalid_client");
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
            // Do not alias even a disabled human identity in the same organization.
            if (await db.Sys_Users.AsNoTracking().AnyAsync(x => x.Id == registration.Subject, ct))
                throw new InquiryPermissionExceptionV1(503, "inquiry_service_identity_collision");
            var facts = await InquiryServicePermissionSourceV1.ReadAsync(db, registration, request,
                (captureCatalog ?? AuthorizationCatalogV1.Capture)(), ct);
            var issued = await RequireCurrentTenant();
            var expiry = issued.AddMinutes(5);
            var tenantExpiry = await db.Sys_Tenants.Where(x => x.Id == registration.Tenant).Select(x => x.ExpireDate).SingleAsync(ct);
            if (tenantExpiry is { } end && new DateTimeOffset(DateTime.SpecifyKind(end, DateTimeKind.Local)) < expiry)
                expiry = new DateTimeOffset(DateTime.SpecifyKind(end, DateTimeKind.Local));
            // SessionId is deliberately absent; the frozen projection contains no browser-session field.
            var result = signing.Sign(new(registration.Tenant, registration.Subject, Guid.Empty, expiry), request, facts, issued, expiry);
            ct.ThrowIfCancellationRequested(); await tx.CommitAsync(ct);
            return result;
        }
        finally { tenant.CurrentTenantId = previousTenant; }
    }
}
