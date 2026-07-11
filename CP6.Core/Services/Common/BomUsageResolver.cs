using CP6.Entity.DomainModels.Erp;

namespace CP6.Core.Services.Common;

/// <summary>
/// BOM 行 → 用量の共通解決器。
///
/// MRP（<c>MrpEngine.ComputeUsage</c>）と 完工反冲（<c>BackflushService</c>）が
/// 同一の「尺寸驱动 / 静态定额」判定・算法を共用し、口径一致を保証する（公式非重複）。
/// 実際の面積・定额公式は共享内核 <see cref="IMaterialUsageCalculator"/> に一元化されており、
/// 本解決器はその周辺オーケストレーション（尺寸料の規格取得・段成率解決・层系数）のみを担う。
/// </summary>
public static class BomUsageResolver
{
    /// <summary>
    /// BOM 行 1 件の展開用量を算出する。
    /// UsageType=1（または材料区分=4 印刷原紙）＝尺寸驱动：規格面積 × 段成率 × 数量 × 层系数。
    /// それ以外＝静态定额：単耗 × 数量。
    /// </summary>
    /// <param name="calc">共享用量内核</param>
    /// <param name="parentItem">親品目CD（規格・段の引き当てキー）</param>
    /// <param name="row">BOM 行（ProductMaterial）</param>
    /// <param name="parentQty">親産出数量</param>
    /// <param name="specByCd">品目CD → 規格（尺寸料の展開尺寸・段用）</param>
    /// <param name="yieldByFlute">段CD → 段成率（M067）</param>
    public static decimal ComputeUsage(
        IMaterialUsageCalculator calc,
        string parentItem, ProductMaterial row, decimal parentQty,
        IReadOnlyDictionary<string, ProductMaster> specByCd,
        IReadOnlyDictionary<string, decimal> yieldByFlute)
    {
        bool dimensional = row.UsageType == 1 || row.MaterialTypeDiv == "4";
        if (dimensional)
        {
            if (!specByCd.TryGetValue(parentItem, out var pm)) return 0m;
            var w = pm.SheetDimW ?? 0m;
            var f = pm.SheetDimF ?? 0m;
            var yield = pm.SheetFlute != null && yieldByFlute.TryGetValue(pm.SheetFlute, out var y) && y > 0 ? y : 1.0m;
            var coeff = row.UnitUsage ?? 1m;   // per-层差异系数（中芯波纹取り都）refinement、缺省 1
            return calc.CalcDimensional(w, f, yield, parentQty) * coeff;
        }
        return calc.CalcFixed(row.UnitUsage ?? 0m, parentQty);
    }
}
