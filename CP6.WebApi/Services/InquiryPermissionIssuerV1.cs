using System.Data;
using System.Text.Json.Serialization;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Core.Services.CrmIdentity;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Services;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InquiryPermissionRequestV1(string Purpose, Guid SiteId, Guid FormId);
public sealed record InquiryPermissionSubjectV1(Guid TenantId, Guid SubjectId, Guid SessionId, DateTimeOffset ExpiresAtUtc);
public sealed class InquiryPermissionExceptionV1(int status, string code) : InvalidOperationException(code)
{ public int Status { get; } = status; }

/// <summary>Actual Main producer for configuration, human intake and privacy purposes. The callback is the server's
/// existing authenticated CRM Bearer reader, rerun inside the Main authentication root.
/// A signed point-in-time projection is not a cross-database business commit fence.</summary>
public sealed partial class InquiryPermissionIssuerV1(CP6Context db, InquiryPermissionSigningKeyV1 signing,
    Func<AuthorizationCatalogSnapshotV1>? captureCatalog = null, CrmIdentityRuntime? identityRuntime = null)
{
    public bool Enabled => signing.Enabled;
    public Task<string> IssueAsync(InquiryPermissionRequestV1 request,
        Func<CP6Context, CancellationToken, Task<InquiryPermissionSubjectV1?>> currentBearer, CancellationToken ct)
        => IssueCoreAsync(request, currentBearer, null, ct);

    private async Task<string> IssueCoreAsync(InquiryPermissionRequestV1 request,
        Func<CP6Context, CancellationToken, Task<InquiryPermissionSubjectV1?>> currentBearer, string? nonce, CancellationToken ct)
    {
        if (request is null || request.SiteId == Guid.Empty || request.FormId == Guid.Empty)
            throw new InquiryPermissionExceptionV1(400, "invalid_request");
        string[] required = request.Purpose switch
        {
            "configuration:read" => ["crm-site:query"],
            "configuration:write" or "configuration:preview" or "configuration:activate" => ["crm-site:query", "crm-site:configure"],
            "intake:list" or "intake:detail" => ["crm-lead:query"],
            "intake:release" => ["crm-lead:query", "crm-lead:view-pii", "crm-lead:add"],
            "intake:reject" => ["crm-lead:query", "crm-lead:view-pii", "crm-lead:edit"],
            "privacy:consent-withdraw" => ["crm-lead:query", "crm-privacy:consent-withdraw"],
            "privacy:export" or "privacy:download" => ["crm-lead:query", "crm-lead:view-pii", "crm-privacy:export"],
            _ => throw new InquiryPermissionExceptionV1(400, "unsupported_projection_purpose")
        };
        if (!Enabled || !db.Database.IsNpgsql() || db.Database.CurrentTransaction is not null
            || System.Transactions.Transaction.Current is not null || db.ChangeTracker.HasChanges())
            throw new InquiryPermissionExceptionV1(503, "inquiry_authority_unavailable");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
        var subject = await currentBearer(db, ct) ?? throw new InquiryPermissionExceptionV1(401, "invalid_token");
        if (subject.TenantId == Guid.Empty || subject.SubjectId == Guid.Empty || subject.SessionId == Guid.Empty || db.CurrentTenantId != subject.TenantId)
            throw new InquiryPermissionExceptionV1(401, "invalid_token");
        var permissions = await new PermissionAggregator(db).BuildAsync(subject.SubjectId, ct);
        if (permissions.UserId != subject.SubjectId || required.Any(x => !permissions.ActionKeys.Contains(x)))
            throw new InquiryPermissionExceptionV1(403, "insufficient_scope");
        var permitted = false;
        foreach (var role in permissions.RoleIds.Distinct())
        {
            var pairs = await IamInquiryScopeAdministrationV1.ReadSourceAsync(db, subject.TenantId, role, ct);
            if (pairs.Any(x => x.SiteId == request.SiteId && x.FormId == request.FormId)) { permitted = true; break; }
        }
        if (!permitted) throw new InquiryPermissionExceptionV1(403, "insufficient_scope");
        var facts = request.Purpose.StartsWith("intake:", StringComparison.Ordinal)
            ? await InquiryIntakePermissionSourceV1.ReadAsync(db, subject.SubjectId, request.Purpose, required, permissions,
                captureCatalog ?? AuthorizationCatalogV1.Capture, ct)
            : request.Purpose == "privacy:consent-withdraw"
                ? await InquiryConsentPermissionSourceV1.ReadAsync(db, subject.SubjectId, required, permissions,
                    captureCatalog ?? AuthorizationCatalogV1.Capture, ct)
                : request.Purpose is "privacy:export" or "privacy:download"
                    ? await InquiryPrivacyPermissionSourceV1.ReadAsync(db, subject.SubjectId, request.Purpose, required, permissions,
                        captureCatalog ?? AuthorizationCatalogV1.Capture, ct)
                    : new InquiryPermissionFactsV1(required, [], [], false);
        var final = await currentBearer(db, ct);
        if (final is null || final.TenantId != subject.TenantId || final.SubjectId != subject.SubjectId || final.SessionId != subject.SessionId
            || db.CurrentTenantId != subject.TenantId)
            throw new InquiryPermissionExceptionV1(401, "invalid_token");
        var source = nonce is null ? null : await ReadHumanSource(subject, ct);
        var now = await db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        var expires = new[] { now.AddMinutes(5), subject.ExpiresAtUtc, final.ExpiresAtUtc }.Min();
        var result = signing.Sign(subject, request, facts, now, expires);
        if (source is not null) result = signing.SignHumanContext(result, nonce!, source, identityRuntime!.ContractBundleSha256, request, now, expires);
        ct.ThrowIfCancellationRequested(); await tx.CommitAsync(ct);
        return result;
    }
}
