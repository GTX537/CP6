using System.Data.Common;
using System.Security.Claims;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Localization;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Services;

internal static class AccessLogoutHttpV1
{
    internal static Task RevokeAsync(CP6Context db, HttpContext http, string? reason = null)
        => RevokeAsync(db, http, reason, unknownRefreshHash: null);

    internal static Task<bool> TryRevokeWithUnknownWebRefreshAsync(CP6Context db, HttpContext http, string raw)
        => RevokeAsync(db, http, reason: null, unknownRefreshHash: AuthenticationInteractionStoreV1.Hash(raw));

    private static async Task<bool> RevokeAsync(CP6Context db, HttpContext http, string? reason, string? unknownRefreshHash)
    {
        if (db.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null || db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("AUTH_SESSION_AUTHORITY_UNAVAILABLE");
        // Authorization already validated this signed principal. Exact logout can also
        // retire a restricted or impersonation alias without closing its refresh origin.
        var identity = http.User.Identities.Single(identity => identity.IsAuthenticated);
        var jti = identity.FindFirst("jti")!.Value;
        var actor = Guid.Parse(identity.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? identity.FindFirst("sub")!.Value);
        var audit = SecurityAuditService.CreateRecord(SecurityEventType.Logout, actor, identity.Name,
            null, http.Connection.RemoteIpAddress?.ToString(), http.Request.Headers.UserAgent.ToString(), reason);
        var bestEffort = unknownRefreshHash is not null;
        var store = http.RequestServices.GetService<ICrmOidcGrantStore>() as SqlCrmOidcGrantStore;
        var originalActor = identity.FindFirst("impersonator_id");
        var source = originalActor is null ? actor : Guid.Parse(originalActor.Value);
        var tenant = Guid.Parse(identity.FindFirst(originalActor is null ? "tenant_id" : "impersonator_tenant_id")!.Value);
        var ct = http.RequestAborted;
        var committing = false;
        try
        {
            var local = new AuthenticationInteractionStoreV1(db);
            if (store is null)
            {
                if (bestEffort) return false;
                await local.RevokeAccessAsync(jti, audit, ct);
                return true;
            }

            CrmAuthSourceRetirementV1? accepted;
            await using (var tx = await db.Database.BeginTransactionAsync(ct))
            {
                await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
                if (bestEffort && await db.Sys_RefreshTokens.IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(row => row.TokenHash == unknownRefreshHash, ct)) return false;

                var auditTenant = Guid.Parse(identity.FindFirst("tenant_id")!.Value);
                accepted = await store.TryStageAccessRetirementAsync(db, tenant, source, jti,
                    new(auditTenant, actor, audit.UserName, audit.ClientIp, audit.UserAgent, audit.Reason, bestEffort), ct);
                if (accepted is not null)
                {
                    if (!await AccessTokenAuthentication.CurrentStateAsync(db,
                        http.RequestServices.GetRequiredService<AuthenticationPolicyServiceV1>(), http.User, ct))
                        throw new InvalidOperationException("REAUTH_REQUIRED");
                }
                else if (!bestEffort) await local.RevokeAccessWithinRootAsync(jti, audit, bestEffort: false, ct);

                ct.ThrowIfCancellationRequested();
                committing = true;
                await tx.CommitAsync(ct);
            }
            if (accepted is null) return !bestEffort;
            await store.CompleteSourceRetirementForRequestAsync(accepted, ct);
            return true;
        }
        catch (Exception error) when (error is RefreshCompletionUnknownException or CrmOidcFamilyLogoutUnavailableException)
        {
            throw new BizException("ACCESS_LOGOUT_OUTCOME_UNKNOWN", 503);
        }
        catch (Exception error) when (error is DbException or DbUpdateException)
        {
            throw new BizException(committing ? "ACCESS_LOGOUT_OUTCOME_UNKNOWN" : "AUTH_SESSION_AUTHORITY_UNAVAILABLE", 503);
        }
    }
}
