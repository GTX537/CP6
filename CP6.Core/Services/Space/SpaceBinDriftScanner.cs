using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Space;

/// <summary>
/// Space↔WMS 库位对账漂移扫描（波5，只读）。发布/停用链路乱序可能产生漂移：Space 侧库位仍
/// Status=1（已发布、未软删）而其消费落点 T_WmsBin 已 IsActive=false。两表以主键等值 join
/// （<c>WmsBin.Id == Space_Location.Id</c>，跨系统同一 GUID）。
/// 只读对账、不自愈——与 FinReconciliationWorker 语义一致，漂移逐条告警交人工核查。
/// </summary>
public static class SpaceBinDriftScanner
{
    /// <param name="LocationId">漂移库位主键（= WmsBin.Id）</param>
    /// <param name="LocationCode">库位编码（已发布必非空，草稿态不入本扫描）</param>
    /// <param name="BinVersion">bin 已消费的最新发布版本（溯源用）</param>
    public record SpaceBinDrift(Guid LocationId, string? LocationCode, long BinVersion);

    /// <summary>扫描当前租户作用域内的漂移库位。租户过滤由 CP6Context 全局 query filter 施加，
    /// 调用方（Worker）经 TenantScopeRunner 逐租户设当前租户后调用。</summary>
    public static async Task<List<SpaceBinDrift>> ScanAsync(CP6Context db, CancellationToken ct)
        => await db.Space_Locations
            .Where(l => l.Status == 1 && !l.IsDeleted)
            .Join(db.WmsBins.Where(b => !b.IsActive),
                  l => l.Id, b => b.Id,
                  (l, b) => new SpaceBinDrift(l.Id, l.LocationCode, b.Version))
            .ToListAsync(ct);
}
