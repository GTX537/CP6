using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Mes;

/// <inheritdoc/>
public class BackflushService : IBackflushService
{
    private readonly CP6Context _db;
    private readonly IStockMovementService _stock;
    private readonly IMaterialUsageCalculator _usage;
    private readonly IMaterialShortageNotifier? _shortageNotifier;

    /// <summary>反冲 OUT 移動の関連種別（StockFinBridge 側で GL 生成対象外＝Skipped）。</summary>
    private const string BackflushRelatedType = "ISSUE";

    /// <summary>在庫が見つからない材料の既定原料倉（既存領料経路 <c>OutboundService.CreateFromWorkOrderAsync</c> と同一）。</summary>
    private const string DefaultMaterialWarehouse = "W01";
    private const string DefaultMaterialLocation = "W01-RM";

    public BackflushService(
        CP6Context db,
        IStockMovementService stock,
        IMaterialUsageCalculator usage,
        IMaterialShortageNotifier? shortageNotifier = null)
    {
        _db = db;
        _stock = stock;
        _usage = usage;
        _shortageNotifier = shortageNotifier;
    }

    public async Task BackflushAsync(string workOrderNo, string? userName)
    {
        if (string.IsNullOrWhiteSpace(workOrderNo)) return;

        var wo = await _db.WorkOrders
            .FirstOrDefaultAsync(w => w.WorkOrderNo == workOrderNo && !w.IsDeleted);
        if (wo == null) return;

        // ── 冪等：同一指図に既存の ISSUE 反冲移動があれば二重反冲しない ──
        var already = await _db.StockTransactions
            .AnyAsync(t => t.RelatedType == BackflushRelatedType && t.RelatedNo == workOrderNo);
        if (already) return;

        var completedQty = wo.CompletedQty;
        if (completedQty <= 0) return;

        // ── BOM（ProductMaterial）ロード：親製品の全材料行 ──
        var bom = await _db.Set<ProductMaterial>().AsNoTracking()
            .Where(p => p.ProductCd == wo.ProductCd && !p.IsDeleted)
            .OrderBy(p => p.SortOrder)
            .ToListAsync();
        if (bom.Count == 0) return;

        // ── 尺寸料用の規格・段成率（MRP と同一ソース）。尺寸行が無ければロード省略 ──
        var specByCd = new Dictionary<string, ProductMaster>();
        var yieldByFlute = new Dictionary<string, decimal>();
        if (bom.Any(r => r.UsageType == 1 || r.MaterialTypeDiv == "4"))
        {
            var specs = await _db.Set<ProductMaster>().AsNoTracking()
                .Where(p => p.ProductCd == wo.ProductCd && !p.IsDeleted)
                .ToListAsync();
            specByCd = specs.GroupBy(p => p.ProductCd).ToDictionary(g => g.Key, g => g.First());

            var yields = await _db.Set<Entity.DomainModels.Common.MasterGenericCode>().AsNoTracking()
                .Where(c => c.GroupCode == "M067")
                .ToListAsync();
            yieldByFlute = yields.GroupBy(c => c.Code).ToDictionary(g => g.Key, g => g.First().Num1 ?? 1.0m);
        }

        // ── 実績消費回写用の指図材料行（トラッキング。無ければ後で補行） ──
        var woMats = await _db.WorkOrderMaterials
            .Where(m => m.WorkOrderNo == workOrderNo && !m.IsDeleted)
            .ToListAsync();

        foreach (var row in bom)
        {
            var qty = BomUsageResolver.ComputeUsage(_usage, wo.ProductCd, row, completedQty, specByCd, yieldByFlute);
            if (qty <= 0) continue;

            // ── 原料倉の解決：自社在庫の最多保有ロケを優先（既存の在庫引き当て惯例に倣う）。
            //    在庫行が無ければ既定原料倉へ（新規 Stock 行が負在庫として立つ）。──
            var src = await _db.Stocks
                .Where(s => s.ProductCd == row.MaterialCd && !s.IsDeleted && s.OwnerType == StockOwnerType.Self)
                .OrderByDescending(s => s.PhysicalQty)
                .FirstOrDefaultAsync();

            var wh = src?.WarehouseCd ?? DefaultMaterialWarehouse;
            var loc = src?.LocationCd ?? DefaultMaterialLocation;
            var lot = src?.LotNo ?? "";
            var onHand = src?.PhysicalQty ?? 0m;

            // ── OUT/ISSUE 発行（拍板①：不足でも負在庫を許可し報工を止めない）──
            await _stock.ApplyAsync(new StockMovementRequest
            {
                TxnType = WmsTxnType.OUT,
                WarehouseCd = wh,
                LocationCd = loc,
                ProductCd = row.MaterialCd,
                LotNo = lot,
                Qty = qty,
                UnitCd = row.UsageUnit,
                UnitPrice = src?.UnitPrice ?? row.SupplyPrice,
                RelatedNo = workOrderNo,
                RelatedType = BackflushRelatedType,
                OperatorCd = userName,
                Remark = $"完工反冲 {workOrderNo} 工程 {row.ProcessCd} 材料 {row.MaterialCd}",
                AllowNegativeOverride = true,
            });

            // ── 実績消費回写（ActualQty 累加）。行が無ければ補行 → CostCollect の料成本が実消費へ対齐 ──
            var mat = woMats.FirstOrDefault(m => m.ProcessCd == row.ProcessCd && m.MaterialCd == row.MaterialCd);
            if (mat == null)
            {
                mat = new WorkOrderMaterial
                {
                    WorkOrderNo = workOrderNo,
                    ProcessCd = row.ProcessCd,
                    MaterialCd = row.MaterialCd,
                    MaterialName = null,
                    MaterialTypeDiv = row.MaterialTypeDiv,
                    Unit = row.UsageUnit,
                    ActualQty = 0m,
                    Creator = userName,
                    CreateDate = DateTime.Now,
                };
                _db.WorkOrderMaterials.Add(mat);
                woMats.Add(mat);
            }
            mat.ActualQty += qty;
            mat.Modifier = userName;
            mat.ModifyDate = DateTime.Now;

            // ── 負在庫告警（best-effort）：不足分を既存の材料不足通知チャネルで発報 ──
            if (onHand < qty)
            {
                var shortfall = qty - onHand;
                try { if (_shortageNotifier != null) await _shortageNotifier.NotifyAsync(workOrderNo, row.MaterialCd, shortfall); }
                catch { /* 告警失敗は反冲本体に影響させない */ }
            }
        }

        await _db.SaveChangesAsync();
    }
}
