using CP6.Core.EFDbContext;
using CP6.WebApi.Localization;
using CP6.Entity.DomainModels.Space;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Space;

/// <summary>
/// 库位绝对坐标缓存重算（Space 章00 §6.2）。几何可动、码不漂移：只改 AbsX/Y/Z，不动
/// LocationCode/Status/Version。构造只注入 CP6Context —— 租户隔离由全局查询过滤自动施加（v1.1）。
/// </summary>
public class LocationGeometryService
{
    private readonly CP6Context _db;
    public LocationGeometryService(CP6Context db) { _db = db; }

    /// <summary>
    /// 货架局部 → floor 局部绝对坐标（章00 §6.1）。锚点角 (X,Y,Z) + 绕锚点角偏航 RotationZ。
    /// 索引 1..N；格心取 (idx-0.5)*cell。Z-up：localZ 由 level 决定，localY 由 depth 决定。
    /// </summary>
    public static (int x, int y, int z) ComputeAbs(Space_Rack rack, int col, int level, int depth)
    {
        double localX = (col   - 0.5) * rack.CellW;
        double localZ = (level - 0.5) * rack.CellH;
        double localY = (depth - 0.5) * rack.CellD;
        double th = rack.RotationZ * Math.PI / 180.0;
        double cos = Math.Cos(th), sin = Math.Sin(th);
        int absX = rack.X + (int)Math.Round(localX * cos - localY * sin);
        int absY = rack.Y + (int)Math.Round(localX * sin + localY * cos);
        int absZ = rack.Z + (int)Math.Round(localZ);
        return (absX, absY, absZ);
    }

    /// <summary>
    /// 重算某货架下全部「已放置」库位坐标缓存。不触发 LocationPublished（发布载荷无几何，章04）。
    /// </summary>
    public async Task RecalcRackLocationsAsync(Guid rackId)
    {
        var rack = await _db.Space_Racks.FirstOrDefaultAsync(r => r.Id == rackId)
                   ?? throw new BizException("E-SPACE-002");
        var locs = await _db.Space_Locations
            .Where(l => l.RackId == rackId && l.Placed).ToListAsync();
        foreach (var l in locs)
            (l.AbsX, l.AbsY, l.AbsZ) = ComputeAbs(rack, l.Col!.Value, l.Level!.Value, l.Depth!.Value);
        await _db.SaveChangesAsync();
    }
}
