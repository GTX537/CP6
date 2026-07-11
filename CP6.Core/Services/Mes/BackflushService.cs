using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Mes;

/// <inheritdoc/>
public class BackflushService : IBackflushService
{
    private readonly CP6Context _db;
    private readonly IStockMovementService _stock;
    private readonly IMaterialUsageCalculator _usage;
    private readonly IMaterialShortageNotifier? _shortageNotifier;
    private readonly ILogger<BackflushService>? _logger;

    /// <summary>反冲 OUT 移動の関連種別（StockFinBridge 側で GL 生成対象外＝Skipped）。</summary>
    private const string BackflushRelatedType = "ISSUE";

    /// <summary>在庫が見つからない材料の既定原料倉（既存領料経路 <c>OutboundService.CreateFromWorkOrderAsync</c> と同一）。</summary>
    private const string DefaultMaterialWarehouse = "W01";
    private const string DefaultMaterialLocation = "W01-RM";

    public BackflushService(
        CP6Context db,
        IStockMovementService stock,
        IMaterialUsageCalculator usage,
        IMaterialShortageNotifier? shortageNotifier = null,
        ILogger<BackflushService>? logger = null)
    {
        _db = db;
        _stock = stock;
        _usage = usage;
        _shortageNotifier = shortageNotifier;
        _logger = logger;
    }

    public async Task BackflushAsync(string workOrderNo, string? userName)
    {
        if (string.IsNullOrWhiteSpace(workOrderNo)) return;

        var wo = await _db.WorkOrders
            .FirstOrDefaultAsync(w => w.WorkOrderNo == workOrderNo && !w.IsDeleted);
        if (wo == null) return;

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

        // ── 行级冪等の基準（終審 Important#1）：本工单已反冲済みの材料集（既存 ISSUE txn の材料）を
        //    本次運行開始前に採取。多料 BOM で第 N 料が失敗（例：料の最多保有ロケが棚卸凍結中）しても
        //    ①既完了料は本快照に載り再冲されず（首料不加倍）、②未完了料は快照に無く重放で続伝される。
        //    整単一括冪等（旧実装）だと首料 txn を見て全体 return → 残料が永久未反冲になる欠陥を解消。──
        var alreadyIssued = (await _db.StockTransactions
                .Where(t => t.RelatedType == BackflushRelatedType && t.RelatedNo == workOrderNo)
                .Select(t => t.ProductCd)
                .ToListAsync())
            .ToHashSet();

        // 行失敗を蓄積（整単途中で断链せず全料を試行 → 末尾で集約投げ）。
        var failedMaterials = new List<string>();

        foreach (var row in bom)
        {
            var qty = BomUsageResolver.ComputeUsage(_usage, wo.ProductCd, row, completedQty, specByCd, yieldByFlute);
            if (qty <= 0) continue;

            // 行级冪等：本料は既に反冲済み（前回運行で成功）→ 本料をスキップ（二重扣庫・二重 ActualQty なし）。
            if (alreadyIssued.Contains(row.MaterialCd)) continue;

            try
            {
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
                //    ApplyAsync は自身で SaveChanges＋commit するため、成功した時点で本料の txn は確定する。
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

                // ── 行级即時落库（終審 Important#1）：txn は ApplyAsync で確定済み。ここで本料の ActualQty を
                //    即座に落として账实を対齐させる。以降の料が失敗しても本料の回写は失われない。──
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // ── 行级 fail-soft（終審 Important#1）：本料失敗（例：料ロケが棚卸凍結中で ApplyAsync が
                //    WM-MSG-304 を投げる）は告警記録して次料へ進む。整単の途中で断链せず全料を試行する。
                //    txn 未確定・ActualQty 未落 → 本料は快照に載らず、解凍後の重放で続伝される。──
                _logger?.LogWarning(ex,
                    "完工反冲 行失敗 指図={WorkOrderNo} 工程={ProcessCd} 材料={MaterialCd} —— 本料をスキップし次料へ（解凍後に重放で続伝可）",
                    workOrderNo, row.ProcessCd, row.MaterialCd);
                failedMaterials.Add(row.MaterialCd);
            }
        }

        // ── 全料試行後、失敗料が残れば集約して投げる（終審 Important#1）。
        //    途中では断链しない（成功料は各自即時落库済み）が、末尾で投げることで呼出側の C.2 闸1
        //    （ProductionResultService: 反冲失敗→backflushOk=false→本轮成本归集/结转スキップ）を維持する。
        //    ＝料耗未完のまま零/陈旧成本が Settled に固化するのを防ぐ。未完成料は次回重放で続伝される。──
        if (failedMaterials.Count > 0)
        {
            throw new InvalidOperationException(
                $"完工反冲 部分材料失敗 指図={workOrderNo} 材料=[{string.Join(",", failedMaterials)}]：" +
                "成功料は落库済み、未完成料は解凍後の重放で続伝可。");
        }
    }
}
