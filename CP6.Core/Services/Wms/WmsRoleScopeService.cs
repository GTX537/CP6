using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public sealed partial class WmsRoleScopeService : IWmsRoleScopeService
{
    private readonly CP6Context db;
    public WmsRoleScopeService(CP6Context db) : this(db, null, null) { }
    private const int MaxScopesPerRole = 200;

    private async Task<IReadOnlyList<WmsRoleScopeDto>> GetCoreAsync(
        int roleId,
        CancellationToken ct = default)
    {
        await EnsureRoleExistsAsync(roleId, ct);
        if (roleId == 1)
            return [new WmsRoleScopeDto { RoleId = 1, WarehouseCd = "*" }];
        return await ReadAsync(roleId, ct);
    }

    private async Task<IReadOnlyList<WmsRoleScopeDto>> ReplaceCoreAsync(
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
        await ValidateAsync(normalized, ct);

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

    private async Task ValidateAsync(IReadOnlyList<WmsRoleScopeItem> scopes, CancellationToken ct)
    {
        foreach (var scope in scopes)
        {
            if (string.IsNullOrWhiteSpace(scope.WarehouseCd)
                || scope.WarehouseCd.Length > 10 || scope.AreaCd?.Length > 20)
                throw new ArgumentException("WM-WMS-SCOPE-DATA");
            if (scope.WarehouseCd == "*" && scope.AreaCd is not null and not "*")
                throw new ArgumentException("WM-WMS-SCOPE-WILDCARD");
        }

        var warehouseCodes = scopes.Where(x => x.WarehouseCd != "*").Select(x => x.WarehouseCd).Distinct().ToArray();
        var warehouses = warehouseCodes.Length == 0 ? new HashSet<string>()
            : (await db.Warehouses.AsNoTracking().Where(x => !x.IsDeleted && warehouseCodes.Contains(x.WarehouseCd))
                .Select(x => x.WarehouseCd).ToListAsync(ct)).ToHashSet();
        var areaCodes = scopes.Where(x => x.WarehouseCd != "*" && x.AreaCd != "*")
            .Select(x => x.AreaCd).Distinct().ToArray();
        var areas = new HashSet<(string Warehouse, string? Area)>();
        if (areaCodes.Length != 0)
        {
            var rows = await db.Locations.AsNoTracking()
                .Where(x => !x.IsDeleted && warehouseCodes.Contains(x.WarehouseCd) && areaCodes.Contains(x.AreaCd))
                .Select(x => new { x.WarehouseCd, x.AreaCd }).Distinct().ToListAsync(ct);
            foreach (var row in rows) areas.Add((row.WarehouseCd, row.AreaCd));
        }
        foreach (var scope in scopes)
        {
            if (scope.WarehouseCd == "*") continue;
            if (!warehouses.Contains(scope.WarehouseCd))
                throw new ArgumentException("WM-WMS-SCOPE-WAREHOUSE-NOT-FOUND");
            if (scope.AreaCd is not null and not "*" && !areas.Contains((scope.WarehouseCd, scope.AreaCd)))
                throw new ArgumentException("WM-WMS-SCOPE-AREA-NOT-FOUND");
        }
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
