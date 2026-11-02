using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Pub;

/// <summary>Host-owned binding, never request fields. The resolver validates the current original
/// access session against the supplied fresh allocation context. Business object/scope checks
/// remain the invoking Owner's responsibility; this component does not create a business object.</summary>
public sealed class NumberAllocationAuthorityV1(IHttpContextAccessor http,
    Func<CP6Context, CancellationToken, Task<Guid>> currentActor, string resource, string allocateAction, string readAction)
{
    internal async Task<Guid> Require(CP6Context db, string purpose, CancellationToken ct)
    {
        var actor = await currentActor(db, ct);
        if (actor != CurrentPermissionContext.RequireCurrentUser(db, http)
            || http.HttpContext!.User.HasClaim(c => c.Type == "impersonator_id"))
            throw new NumberAllocationExceptionV1("REAUTH_REQUIRED", 401);
        var p = await PostgreSqlPermissionReader.ReadAsync(db, actor, ct);
        if (!await db.Sys_Users.AsNoTracking().AnyAsync(x => x.Id == actor && x.Enable && (x.LockedUntil == null || x.LockedUntil <= DateTime.Now), ct)
            || !await db.Sys_Tenants.AsNoTracking().AnyAsync(x => x.Id == db.CurrentTenantId && x.Enable && (x.ExpireDate == null || x.ExpireDate > DateTime.Now), ct))
            throw new NumberAllocationExceptionV1("REAUTH_REQUIRED", 401);
        var menu = purpose == "register" ? "pub-seq" : resource;
        var action = purpose == "register" ? "add" : purpose == "read" ? readAction : allocateAction;
        if (!p.ActionKeys.Contains(menu + ":" + action) || !await
            (from a in db.Sys_MenuActions.AsNoTracking() join m in db.Sys_Menus.AsNoTracking() on a.MenuId equals m.MenuId
             where m.Enable && m.MenuKey == menu && a.ActionCode == action select a.Id).AnyAsync(ct))
            throw new NumberAllocationExceptionV1("ACTION_FORBIDDEN", 403);
        return actor;
    }
}
