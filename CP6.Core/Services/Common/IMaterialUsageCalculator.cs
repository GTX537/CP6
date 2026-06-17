namespace CP6.Core.Services.Common;

/// <summary>
/// 共享用量内核（MRP P1 · spec §4）。
///
/// 出"用量量"（非成本）。見積(EstimateCalcService)与 MRP(MrpEngine)共消费同一核心公式，
/// 保证两处单耗算法严格一致——成本 = 单价 × 用量，价由各调用方乘。
///
/// 纸箱特性：原纸是"尺寸料面积驱动单耗"——展开尺寸 × 段成率得每枚面积用量；
/// 辅料是"静态定额"——单耗 × 数量。
/// </summary>
public interface IMaterialUsageCalculator
{
    /// <summary>
    /// 尺寸驱动料用量：面积(mm²→m²) × 段成率 × 数量。
    /// </summary>
    /// <param name="sheetDimW">展开宽 W（mm）</param>
    /// <param name="sheetDimF">展开流向 F（mm）</param>
    /// <param name="yieldRate">段成率（M067 by 段）</param>
    /// <param name="outputQty">产出数量（枚）</param>
    /// <returns>用量（m²）</returns>
    decimal CalcDimensional(decimal sheetDimW, decimal sheetDimF, decimal yieldRate, decimal outputQty);

    /// <summary>
    /// 静态定额料用量：单耗 × 数量。
    /// </summary>
    decimal CalcFixed(decimal unitUsage, decimal outputQty);
}

/// <inheritdoc/>
public class MaterialUsageCalculator : IMaterialUsageCalculator
{
    public decimal CalcDimensional(decimal sheetDimW, decimal sheetDimF, decimal yieldRate, decimal outputQty)
        => (sheetDimW * sheetDimF / 1_000_000m) * yieldRate * outputQty;

    public decimal CalcFixed(decimal unitUsage, decimal outputQty)
        => unitUsage * outputQty;
}
