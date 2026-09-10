using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.CrmIdentity;

internal static class IdentityPermissionSnapshot
{
    internal static async Task<PermissionIdentityData> ReadAsync(CP6Context db, Guid tenant, int roleId,
        CancellationToken cancellationToken)
    {
        var enabled = await db.Sys_Roles.IgnoreQueryFilters().AnyAsync(
            x => x.TenantId == tenant && x.RoleId == roleId && x.Enable, cancellationToken).ConfigureAwait(false);
        if (!enabled) return new(tenant, $"role:{roleId}", false, 0, []);
        var actions = await (from a in db.Sys_RoleActions.IgnoreQueryFilters().AsNoTracking()
            join m in db.Sys_Menus.AsNoTracking() on a.MenuId equals m.MenuId
            where a.TenantId == tenant && a.RoleId == roleId && m.MenuKey != null && m.MenuKey.StartsWith("crm-")
            select new { Resource = m.MenuKey!, Action = a.ActionCode }).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
        var scopes = await db.Sys_RoleDataScopes.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.RoleId == roleId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var fields = await db.Sys_RoleFieldPerms.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.RoleId == roleId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var grants = actions.OrderBy(x => x.Resource, StringComparer.Ordinal).ThenBy(x => x.Action, StringComparer.Ordinal).Select(action =>
        {
            var selected = scopes.Where(x => x.ResourceKey == action.Resource).ToArray();
            // Same default own-scope and MAX/MIN union as the existing Core permission aggregator.
            var scope = selected.Length == 0 ? 1 : selected.Max(x => x.ScopeType);
            var departments = scope == 4 ? selected.Where(x => x.ScopeType == 4)
                .SelectMany(x => (x.CustomDeptIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Where(x => Guid.TryParse(x, out _)).Select(Guid.Parse).Distinct().Order().ToArray() : [];
            var access = fields.Where(x => x.ResourceKey == action.Resource).GroupBy(x => x.FieldName)
                .OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new IdentityFieldPermission(x.Key, x.Min(f => f.Access))).ToArray();
            return new IdentityGrant(action.Resource, action.Action, scope, departments, access);
        }).ToArray();
        return new(tenant, $"role:{roleId}", true, 0, grants);
    }
}
