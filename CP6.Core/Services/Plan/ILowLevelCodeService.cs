namespace CP6.Core.Services.Plan;

/// <summary>BOM 父子边（父件 → 直接子料/下级件）。低层码计算的抽象输入，与 EF 解耦。</summary>
public readonly record struct BomEdge(string Parent, string Child);

/// <summary>
/// 低层码计算服务（MRP P1 · spec §5.3 step0 / §10）。
/// </summary>
public interface ILowLevelCodeService
{
    /// <summary>
    /// 计算各物料低层码 = 其在 BOM 中出现的最深层级（顶层成品=0）。
    /// 共用料取所有路径中的最深值，保证 MRP 逐层 net 时需求汇齐才净算。
    /// </summary>
    /// <exception cref="InvalidOperationException">BOM 存在循环依赖（E-PLAN-循环BOM）。</exception>
    IReadOnlyDictionary<string, int> Compute(IEnumerable<BomEdge> edges);
}
