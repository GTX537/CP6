using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

/// <summary>
/// 停用同步 RPC 真实现（ch04 §6.1② v1.1）：再查一次实时库存做 TOCTOU 权威校验，
/// 无库存 → T_WmsBin.IsActive=false + Version 落定；有库存 → 拒绝 W-SPACE-404。
/// bin 尚未消费落库（如 UPSERT 事件还在重试）→ 无库存即放行，异步兜底事件会幂等收敛（§6.1④）。
/// </summary>
public class WmsBinDeactivator : IWmsBinDeactivator
{
    private readonly CP6Context _db;

    public WmsBinDeactivator(CP6Context db) => _db = db;

    /// <inheritdoc/>
    public async Task<WmsDeactivateResult> DeactivateAsync(WmsDeactivateRequest req, CancellationToken ct = default)
    {
        var bin = await _db.WmsBins.FirstOrDefaultAsync(b => b.Id == req.LocationId, ct);

        // 权威库存判定：优先按 bin 落库的 (WarehouseCd, LocationCode) 锚；bin 未落库退回请求携带的键
        var warehouseCd = bin?.WarehouseCd ?? req.WarehouseCd;
        var code = bin?.LocationCode ?? req.LocationCode;
        var stocks = _db.Stocks.Where(s => s.LocationCd == code);
        if (!string.IsNullOrEmpty(warehouseCd))
            stocks = stocks.Where(s => s.WarehouseCd == warehouseCd);
        var qty = await stocks.SumAsync(s => s.PhysicalQty, ct);
        if (qty > 0)
            return new WmsDeactivateResult { Success = false, Reason = "W-SPACE-404 库存非0" };

        if (bin != null)
        {
            bin.IsActive = false;
            bin.Version = Math.Max(bin.Version, req.Version);   // 版本单调不回退（防陈旧停用重开乱序窗）
            bin.LastPublishedAt = DateTime.Now;
            bin.LastPublishedBy = req.User;
            await _db.SaveChangesAsync(ct);
        }
        else if (!string.IsNullOrEmpty(req.WarehouseCd))
        {
            // H6 乱序防护墓碑：同步 RPC 是权威停用时点。bin 未曾消费（UPSERT 事件可能仍在重试）
            // 也要占住 (Id, Version)，防止迟到的旧版 UPSERT 事后复活该库位。
            _db.WmsBins.Add(new WmsBin
            {
                Id = req.LocationId,
                LocationCode = req.LocationCode,
                WarehouseCd = req.WarehouseCd,
                IsActive = false,
                Version = req.Version,
                LastPublishedAt = DateTime.Now,
                LastPublishedBy = req.User
            });
            await _db.SaveChangesAsync(ct);
        }
        // bin 不存在且无仓维度（如采纳态无楼层归属）→ 无库存即放行，异步兜底事件幂等收敛
        return new WmsDeactivateResult { Success = true };
    }
}
