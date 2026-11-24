using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

internal static class WorkflowNotificationAuthorityV1
{
    internal static async Task<Guid> ActualUserAsync(CP6Context db, IHttpContextAccessor http, CancellationToken ct)
    {
        var user = CurrentPermissionContext.RequireCurrentUser(db, http);
        if (http.HttpContext!.User.HasClaim(x => x.Type == "impersonator_id") ||
            await ActiveRecipientAsync(db, user, ct) is null)
            throw new ScopeAuthorizationExceptionV1("REAUTH_REQUIRED", 401);
        return user;
    }

    internal static async Task<Sys_User?> ActiveRecipientAsync(CP6Context db, Guid user, CancellationToken ct)
    {
        var now = DateTime.Now;
        if (!await db.Sys_Tenants.AsNoTracking().AnyAsync(t =>
                t.Id == db.CurrentTenantId && t.Enable && (t.ExpireDate == null || t.ExpireDate > now), ct))
            return null;
        return await db.Sys_Users.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == user && x.Enable && (x.LockedUntil == null || x.LockedUntil <= now), ct);
    }

    internal static async Task<bool> HasActionAsync(CP6Context db, Guid user, string menu, string action, CancellationToken ct)
    {
        var current = await PostgreSqlPermissionReader.ReadAsync(db, user, ct);
        return current.MenuKeys.Contains(menu) && current.ActionKeys.Contains(menu + ":" + action)
            && await (from m in db.Sys_Menus join a in db.Sys_MenuActions on m.MenuId equals a.MenuId
                      where m.Enable && m.MenuKey == menu && a.ActionCode == action
                      select a).AnyAsync(ct);
    }

    internal static async Task RequireActionAsync(CP6Context db, Guid user, string menu, string action, CancellationToken ct)
    {
        if (!await HasActionAsync(db, user, menu, action, ct))
            throw new ScopeAuthorizationExceptionV1("ACTION_FORBIDDEN", 403);
    }
}
