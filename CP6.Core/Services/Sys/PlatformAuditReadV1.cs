using System.Data.Common;
using System.Security.Claims;
using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

public sealed class PlatformAuditReadV1(CP6Context db, AuditBrowseV1 audit)
{
    public async Task<object> ListAsync(ClaimsPrincipal principal, TenantManagementSessionV1 session, string? targetTenantCode,
        AuditFilterV1 filter, int page, int pageSize, string? cursor, CancellationToken ct)
    {
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
            var actor = await PlatformAdminAuthorization.RequireAsync(db, principal, ct, lockActor: true);
            if (actor.Id != await session.CurrentActor(ct)) throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401);
            var result = await audit.PlatformPageAsync(targetTenantCode, filter, page, pageSize, cursor, ct);
            await session.Complete(ct);
            await tx.CommitAsync(ct);
            return result;
        }
        catch (PlatformAdminAuthorizationException e) { throw new OrderPriceCorrectionException(e.Message, 403, e); }
        catch (IamAdministrationExceptionV1 e) { throw new OrderPriceCorrectionException(e.Message, e.Status, e); }
        catch (DbException e) { throw new OrderPriceCorrectionException("PLATFORM_AUDIT_AUTHORITY_UNAVAILABLE", 503, e); }
    }
}
