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
    internal static async Task RevokeAsync(CP6Context db, HttpContext http, string? reason = null)
    {
        // Authorization already validated this signed principal. Exact logout can also
        // retire a restricted or impersonation alias without closing its refresh origin.
        var identity = http.User.Identities.Single(identity => identity.IsAuthenticated);
        var jti = identity.FindFirst("jti")!.Value;
        var actor = Guid.Parse(identity.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? identity.FindFirst("sub")!.Value);
        var audit = SecurityAuditService.CreateRecord(SecurityEventType.Logout, actor, identity.Name,
            null, http.Connection.RemoteIpAddress?.ToString(), http.Request.Headers.UserAgent.ToString(), reason);
        try
        {
            await new AuthenticationInteractionStoreV1(db).RevokeAccessAsync(jti, audit, http.RequestAborted);
        }
        catch (RefreshCompletionUnknownException)
        {
            throw new BizException("ACCESS_LOGOUT_OUTCOME_UNKNOWN", 503);
        }
        catch (Exception error) when (error is DbException or DbUpdateException)
        {
            throw new BizException("AUTH_SESSION_AUTHORITY_UNAVAILABLE", 503);
        }
    }
}
