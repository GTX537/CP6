using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public sealed class WmsRoleScopeService(CP6Context db) : IWmsRoleScopeService
{
    private const int MaxScopesPerRole = 200;

    public async Task<IReadOnlyList<WmsRoleScopeDto>> GetAsync(
        int roleId,
        CancellationToken ct = default)
    {
        await EnsureRoleExistsAsync(roleId, ct);
        if (roleId == 1)
            return [new WmsRoleScopeDto { RoleId = 1, WarehouseCd = "*" }];
        return await ReadAsync(roleId, ct);
    }

    public async Task<IReadOnlyList<WmsRoleScopeDto>> ReplaceAsync(
        int roleId,
        ReplaceWmsRoleScopesRequest request,
        string? userName,
        CancellationToken ct = default)
    {
        await EnsureRoleExistsAsync(roleId, ct);
        if (roleId == 1)
            throw new InvalidOperationException("WM-WMS-SCOPE-ADMIN-IMMUTABLE");

        var normalized = (request.Scopes ?? Array.Empty<WmsRoleScopeItem>())
            .Select(x => new WmsRoleScopeItem
            {
                WarehouseCd = x.WarehouseCd?.Trim().ToUpperInvariant()
                              ?? string.Empty,
                AreaCd = string.IsNullOrWhiteSpace(x.AreaCd)
                    ? "*"
                    : x.AreaCd.Trim().ToUpperInvariant()
            })
            .DistinctBy(x => $"{x.WarehouseCd}\u001f{x.AreaCd}")
            .ToList();
        if (normalized.Count > MaxScopesPerRole)
            throw new ArgumentException("WM-WMS-SCOPE-LIMIT");
        foreach (var scope in normalized)
            await ValidateAsync(scope, ct);

        var existing = await db.WmsRoleScopes
            .Where(x => x.RoleId == roleId)
            .ToListAsync(ct);
        db.WmsRoleScopes.RemoveRange(existing);
        db.WmsRoleScopes.AddRange(normalized.Select(x => new WmsRoleScope
        {
            TenantId = db.CurrentTenantId,
            RoleId = roleId,
            WarehouseCd = x.WarehouseCd,
            AreaCd = x.AreaCd ?? "*",
            Creator = userName
        }));
        await db.SaveChangesAsync(ct);
        return await ReadAsync(roleId, ct);
    }

    private Task<bool> RoleExistsAsync(int roleId, CancellationToken ct)
        => db.Sys_Roles.AnyAsync(x => x.RoleId == roleId && x.Enable, ct);

    private async Task EnsureRoleExistsAsync(int roleId, CancellationToken ct)
    {
        if (!await RoleExistsAsync(roleId, ct))
            throw new KeyNotFoundException("WM-WMS-SCOPE-ROLE-NOT-FOUND");
    }

    private async Task ValidateAsync(
        WmsRoleScopeItem scope,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(scope.WarehouseCd)
            || scope.WarehouseCd.Length > 10
            || scope.AreaCd?.Length > 20)
            throw new ArgumentException("WM-WMS-SCOPE-DATA");
        if (scope.WarehouseCd == "*")
        {
            if (scope.AreaCd is not null and not "*")
                throw new ArgumentException("WM-WMS-SCOPE-WILDCARD");
            scope.AreaCd = "*";
            return;
        }
        if (!await db.Warehouses.AsNoTracking()
                .AnyAsync(x => !x.IsDeleted
                               && x.WarehouseCd == scope.WarehouseCd, ct))
            throw new ArgumentException("WM-WMS-SCOPE-WAREHOUSE-NOT-FOUND");
        if (scope.AreaCd is not null and not "*"
            && !await db.Locations.AsNoTracking()
                .AnyAsync(x => !x.IsDeleted
                               && x.WarehouseCd == scope.WarehouseCd
                               && x.AreaCd == scope.AreaCd, ct))
            throw new ArgumentException("WM-WMS-SCOPE-AREA-NOT-FOUND");
    }

    private Task<List<WmsRoleScopeDto>> ReadAsync(
        int roleId,
        CancellationToken ct)
        => db.WmsRoleScopes.AsNoTracking()
            .Where(x => x.RoleId == roleId)
            .OrderBy(x => x.WarehouseCd)
            .ThenBy(x => x.AreaCd)
            .Select(x => new WmsRoleScopeDto
            {
                RoleId = x.RoleId,
                WarehouseCd = x.WarehouseCd,
                AreaCd = x.AreaCd == "*" ? null : x.AreaCd
            })
            .ToListAsync(ct);
}
