using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public sealed class WmsAccessScopeProvider(
    CP6Context db,
    ICurrentPermissionContext permissions) : IWmsAccessScopeProvider
{
    public async Task<WmsAccessScope> GetCurrentAsync(
        CancellationToken ct = default)
    {
        var context = await permissions.GetAsync();
        if (context.RoleIds.Contains(1)) return WmsAccessScope.All;
        if (context.RoleIds.Count == 0) return WmsAccessScope.None;

        var grants = await db.WmsRoleScopes.AsNoTracking()
            .Where(x => context.RoleIds.Contains(x.RoleId))
            .Select(x => new WmsScopeGrant(x.WarehouseCd, x.AreaCd))
            .ToListAsync(ct);
        if (grants.Any(x => x.WarehouseCd == "*"
                            && (x.AreaCd == null || x.AreaCd == "*")))
            return WmsAccessScope.All;
        return grants.Count == 0
            ? WmsAccessScope.None
            : new WmsAccessScope(false, grants);
    }
}
