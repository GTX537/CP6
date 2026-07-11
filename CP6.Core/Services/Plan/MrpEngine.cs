using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Plan;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Plan;

/// <inheritdoc/>
public class MrpEngine : IMrpEngine
{
    private readonly CP6Context _db;
    private readonly IMaterialUsageCalculator _usage;
    private readonly ILowLevelCodeService _lowLevel;
    private readonly ISupplyService _supply;
    private readonly IItemPlanningPolicyService _policy;

    public MrpEngine(
        CP6Context db,
        IMaterialUsageCalculator usage,
        ILowLevelCodeService lowLevel,
        ISupplyService supply,
        IItemPlanningPolicyService policy)
    {
        _db = db;
        _usage = usage;
        _lowLevel = lowLevel;
        _supply = supply;
        _policy = policy;
    }

    /// <summary>毛需求累加器：逐层 net 前把各上层贡献汇齐到此（防共用料重复 netting）。</summary>
    private sealed class DemandAccum
    {
        public decimal Gross;
        public DateTime EarliestRequired = DateTime.MaxValue;
        public readonly List<(int SourceType, string? RefNo, decimal Qty)> Peggings = new();
    }

    public async Task<Plan_MrpRun> RunAsync(MrpRunRequest request)
    {
        // ── 0. 建运算批次（采番） ──
        var (runNo, _) = await DocNumber.NextAsync(_db, "MRP");
        var run = new Plan_MrpRun
        {
            RunNo = runNo,
            RunAt = DateTime.Now,
            ScopeJson = request.ScopeJson,
            Status = (int)MrpRunStatus.Running,
        };
        _db.MrpRuns.Add(run);

        // ── 1. 复算存活：建议态计划订单作废重生（已确认/已转单保留并当供给）──
        var staleSuggested = await _db.PlannedOrders
            .Where(o => !o.IsDeleted && o.Status == (int)PlannedOrderStatus.Suggested)
            .ToListAsync();
        if (staleSuggested.Count > 0)
        {
            var staleIds = staleSuggested.Select(o => o.Id).ToHashSet();
            foreach (var o in staleSuggested) o.IsDeleted = true;
            var stalePegs = await _db.Peggings
                .Where(p => !p.IsDeleted && staleIds.Contains(p.PlannedOrderId))
                .ToListAsync();
            foreach (var p in stalePegs) p.IsDeleted = true;
        }
        await _db.SaveChangesAsync();   // 落运算批次（取 Id）+ 旧建议作废

        // ── 2. 载入主数据：BOM / 规格 / 段成率，算低层码 ──
        var allBom = await _db.ProductMaterials.AsNoTracking().Where(m => !m.IsDeleted).ToListAsync();
        var bomByParent = allBom.GroupBy(m => m.ProductCd)
            .ToDictionary(g => g.Key, g => g.ToList());
        var edges = allBom.Select(m => new BomEdge(m.ProductCd, m.MaterialCd));
        var levels = _lowLevel.Compute(edges);   // 成环 → 抛 E-PLAN-循环BOM

        var specs = await _db.ProductMasters.AsNoTracking().Where(p => !p.IsDeleted).ToListAsync();
        var specByCd = specs.GroupBy(p => p.ProductCd).ToDictionary(g => g.Key, g => g.First());
        var yields = await _db.MasterGenericCodes.AsNoTracking()
            .Where(c => c.GroupCode == "M067")
            .ToListAsync();
        var yieldByFlute = yields.GroupBy(c => c.Code).ToDictionary(g => g.Key, g => g.First().Num1 ?? 1.0m);

        int LevelOf(string item) => levels.TryGetValue(item, out var l) ? l : 0;

        // ── 3. 汇总独立需求 → 成品毛需求累加器 ──
        var accum = new Dictionary<string, DemandAccum>();
        DemandAccum Get(string item)
        {
            if (!accum.TryGetValue(item, out var a)) accum[item] = a = new DemandAccum();
            return a;
        }
        foreach (var d in request.Demands)
        {
            var a = Get(d.ItemCd);
            a.Gross += d.Qty;
            if (d.RequiredDate < a.EarliestRequired) a.EarliestRequired = d.RequiredDate;
            a.Peggings.Add(((int)PeggingSourceType.Order, d.SourceRefNo, d.Qty));
        }

        int maxLevel = levels.Count > 0 ? levels.Values.Max() : 0;

        // 计划订单 → 其 Pegging 来源（po.Id 在 SaveChanges 后才有，故先收集后建）
        var poPeggings = new List<(Plan_PlannedOrder Po, List<(int, string?, decimal)> Pegs)>();

        // ── 4. 按低层码 0→N 逐层 net ──
        for (int level = 0; level <= maxLevel; level++)
        {
            var itemsAtLevel = accum
                .Where(kv => LevelOf(kv.Key) == level && kv.Value.Gross > 0)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var item in itemsAtLevel)
            {
                var a = accum[item];
                var bucket = a.EarliestRequired == DateTime.MaxValue ? DateTime.Today : a.EarliestRequired;
                var pol = await _policy.GetPolicyAsync(item);
                var sup = await _supply.GetSupplyBreakdownAsync(item, bucket);

                var net = a.Gross - sup.Total - pol.SafetyStock;
                if (net < 0) net = 0;

                _db.NetRequirements.Add(new Plan_NetRequirement
                {
                    MrpRunId = run.Id,
                    ItemCd = item,
                    Bucket = bucket,
                    Gross = a.Gross,
                    OnHand = sup.OnHand,
                    InTransit = sup.InTransit,
                    InWip = sup.InWip,
                    FirmPlanned = sup.FirmPlanned,
                    SafetyStock = pol.SafetyStock,
                    Net = net,
                });

                if (net <= 0) continue;

                var qty = ApplyLotRule(net, pol);
                var leadDays = await _policy.GetLeadDaysAsync(item);
                var releaseDate = bucket.AddDays(-(double)leadDays);
                var isMake = pol.MakeOrBuy == (int)PlanMakeOrBuy.Make;

                var po = new Plan_PlannedOrder
                {
                    MrpRunId = run.Id,
                    Type = (int)(isMake ? PlannedOrderType.Production : PlannedOrderType.Purchase),
                    ItemCd = item,
                    Qty = qty,
                    RequiredDate = bucket,
                    ReleaseDate = releaseDate,
                    Status = (int)PlannedOrderStatus.Suggested,
                };
                _db.PlannedOrders.Add(po);
                poPeggings.Add((po, new List<(int, string?, decimal)>(a.Peggings)));

                // ── 展开下层：仅自制件且有 BOM 行（采购件买入不展开）──
                if (isMake && bomByParent.TryGetValue(item, out var rows))
                {
                    foreach (var row in rows)
                    {
                        var childUsage = ComputeUsage(item, row, qty, specByCd, yieldByFlute);
                        if (childUsage <= 0) continue;

                        var ca = Get(row.MaterialCd);
                        ca.Gross += childUsage;
                        if (releaseDate < ca.EarliestRequired) ca.EarliestRequired = releaseDate;
                        ca.Peggings.Add(((int)PeggingSourceType.ParentPlannedOrder, item, childUsage));
                    }
                }
            }
        }

        await _db.SaveChangesAsync();   // 落计划订单 + 净需求（po.Id 生成）

        // ── 5. 落 Pegging（钉住来源）──
        foreach (var (po, pegs) in poPeggings)
        {
            foreach (var (st, rf, q) in pegs)
            {
                _db.Peggings.Add(new Plan_Pegging
                {
                    PlannedOrderId = po.Id,
                    SourceType = st,
                    SourceRefNo = rf,
                    Qty = q,
                });
            }
        }

        run.Status = (int)MrpRunStatus.Completed;
        await _db.SaveChangesAsync();

        return run;
    }

    /// <summary>
    /// 展开子料用量。尺寸/定额算法は共通解決器 <see cref="BomUsageResolver"/> に一元化し、
    /// 完工反冲（BackflushService）と口径を共用する（公式非重複）。
    /// </summary>
    private decimal ComputeUsage(
        string parentItem, ProductMaterial row, decimal parentQty,
        IReadOnlyDictionary<string, ProductMaster> specByCd,
        IReadOnlyDictionary<string, decimal> yieldByFlute)
        => BomUsageResolver.ComputeUsage(_usage, parentItem, row, parentQty, specByCd, yieldByFlute);

    /// <summary>批量规则定批：lot-for-lot / MOQ 向上取 / 订货倍数·整卷取整。</summary>
    private static decimal ApplyLotRule(decimal net, Plan_ItemPlanningPolicy pol)
    {
        switch ((PlanLotRule)pol.LotRule)
        {
            case PlanLotRule.Moq:
                return Math.Max(net, pol.MoqQty ?? 0m);
            case PlanLotRule.Multiple:
            case PlanLotRule.RoundRoll:
                var m = pol.MultipleQty ?? 0m;
                return m > 0 ? Math.Ceiling(net / m) * m : net;
            default:
                return net;   // LotForLot
        }
    }
}
