using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

/// <summary>作业热图只读实现（读 Space_Location 取该层 Placed 编码 + T_StockTransaction 计次；纯读，全局过滤隔离）。</summary>
public class WmsWorkloadQuery : IWmsWorkloadQuery
{
    private readonly CP6Context _db;
    public WmsWorkloadQuery(CP6Context db) => _db = db;

    public async Task<IReadOnlyList<WorkloadDto>> GetWorkloadAsync(
        Guid floorId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var codes = await _db.Space_Locations
            .Where(l => l.FloorId == floorId && l.Placed && l.LocationCode != null)
            .Select(l => l.LocationCode!)
            .ToListAsync(ct);
        if (codes.Count == 0) return Array.Empty<WorkloadDto>();

        return await _db.StockTransactions
            .Where(t => t.TxnDateTime >= from && t.TxnDateTime < to && codes.Contains(t.LocationCd))
            .GroupBy(t => t.LocationCd)
            .Select(g => new WorkloadDto { LocationCode = g.Key, OpCount = g.Count() })
            .ToListAsync(ct);
    }
}
