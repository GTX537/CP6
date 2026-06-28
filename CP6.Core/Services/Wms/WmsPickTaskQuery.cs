using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

/// <summary>拣货路径只读实现（读 T_OutboundOrder/T_OutboundOrderDetail；纯读，多租户全局过滤自动隔离）。</summary>
public class WmsPickTaskQuery : IWmsPickTaskQuery
{
    private readonly CP6Context _db;
    public WmsPickTaskQuery(CP6Context db) => _db = db;

    public async Task<PickPathDto> GetPickPathAsync(string taskNo, CancellationToken ct = default)
    {
        var exists = await _db.OutboundOrders.AnyAsync(o => o.OutboundNo == taskNo, ct);
        if (!exists) return new PickPathDto { TaskNo = taskNo };

        var details = await _db.OutboundOrderDetails
            .Where(d => d.OutboundNo == taskNo && d.LocationCd != null)
            .OrderBy(d => d.LineNo)
            .ToListAsync(ct);

        var stops = details.Select(d => new PickStop
        {
            Seq = d.LineNo,
            LocationCode = d.LocationCd!,
            Qty = d.RequiredQty,
            MaterialNo = d.ProductCd,
        }).ToList();

        return new PickPathDto { TaskNo = taskNo, Items = stops };
    }
}
