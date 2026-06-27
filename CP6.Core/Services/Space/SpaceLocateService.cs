using CP6.Core.EFDbContext;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Space;

/// <summary>按编码定位/搜索/详情查询（ch06 §8，viewer 定位 D8 P1 半的后端）。</summary>
public interface ISpaceLocateService
{
    /// <summary>按库位编码精确定位（未命中返 null → 控制器转 E-SPACE-601）。</summary>
    Task<LocateResult?> LocateAsync(string code);
    /// <summary>按编码前缀模糊搜索候选（可选限本楼层）。</summary>
    Task<List<LocationSearchItem>> SearchAsync(string prefix, Guid? floorId, int limit = 50);
    /// <summary>库位详情（含变长层级路径）。</summary>
    Task<LocationDetail?> DetailAsync(Guid locationId);
}

/// <summary>
/// 定位查询实现（v1.1：构造只注入 CP6Context，租户隔离由全局过滤自动施加）。
/// 纯只读，不改任何数据。
/// </summary>
public class SpaceLocateService : ISpaceLocateService
{
    private readonly CP6Context _db;
    public SpaceLocateService(CP6Context db) => _db = db;

    /// <inheritdoc/>
    public async Task<LocateResult?> LocateAsync(string code) =>
        await _db.Space_Locations
            .Where(l => l.LocationCode == code)
            .Select(l => new LocateResult
            {
                LocationId = l.Id, FloorId = l.FloorId, LocationCode = l.LocationCode!,
                AbsX = l.AbsX ?? 0, AbsY = l.AbsY ?? 0, AbsZ = l.AbsZ ?? 0,
                Placed = l.Placed, Status = l.Status
            })
            .FirstOrDefaultAsync();

    /// <inheritdoc/>
    public async Task<List<LocationSearchItem>> SearchAsync(string prefix, Guid? floorId, int limit = 50) =>
        await _db.Space_Locations
            .Where(l => l.LocationCode != null && l.LocationCode.StartsWith(prefix)
                     && (floorId == null || l.FloorId == floorId))
            .OrderBy(l => l.LocationCode)
            .Take(limit)
            .Select(l => new LocationSearchItem
            {
                LocationId = l.Id, LocationCode = l.LocationCode!, FloorId = l.FloorId,
                Placed = l.Placed, Status = l.Status
            })
            .ToListAsync();

    /// <inheritdoc/>
    public async Task<LocationDetail?> DetailAsync(Guid locationId)
    {
        var l = await _db.Space_Locations.FirstOrDefaultAsync(x => x.Id == locationId);
        if (l == null) return null;

        // 组变长路径（Rack→Zone→Floor→Site，跳 null Aisle），仿 LocationPublishService.BuildItemAsync
        var path = new LocationPath { Col = l.Col ?? 0, Level = l.Level ?? 0, Depth = l.Depth ?? 0 };
        if (l.RackId != null)
        {
            var rack = await _db.Space_Racks.FirstOrDefaultAsync(r => r.Id == l.RackId);
            if (rack != null)
            {
                path.RackCode = rack.RackCode;
                if (rack.AisleId != null)
                    path.AisleCode = (await _db.Space_Aisles.FirstOrDefaultAsync(a => a.Id == rack.AisleId))?.AisleCode;
                var zone = await _db.Space_Zones.FirstOrDefaultAsync(z => z.Id == rack.ZoneId);
                if (zone != null)
                {
                    path.ZoneCode = zone.ZoneCode;
                    var floor = await _db.Space_Floors.FirstOrDefaultAsync(f => f.Id == zone.FloorId);
                    if (floor != null)
                    {
                        path.FloorLevel = floor.Level;
                        path.SiteCode = (await _db.Space_Sites.FirstOrDefaultAsync(s => s.Id == floor.SiteId))?.SiteCode;
                    }
                }
            }
        }

        return new LocationDetail
        {
            LocationId = l.Id, LocationCode = l.LocationCode, Path = path,
            Status = l.Status, CodeOrigin = l.CodeOrigin,
            AbsX = l.AbsX ?? 0, AbsY = l.AbsY ?? 0, AbsZ = l.AbsZ ?? 0
        };
    }
}
