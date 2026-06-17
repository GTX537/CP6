namespace CP6.Tests;

/// <summary>
/// 共享用量内核 IMaterialUsageCalculator 单元测试（MRP P1 · spec §4）
///
/// 内核出"用量量"（非成本）：
/// - 尺寸驱动料：面积(mm→m²) × 段成率 × 数量
/// - 静态定额料：单耗 × 数量
/// 見積(EstimateCalc)与 MRP 共消费同一核心公式，保证两处用量算法一致。
/// </summary>
public class MaterialUsageCalculatorTests
{
    [Fact]
    public void CalcDimensional_AreaTimesYieldTimesQty()
    {
        var calc = new MaterialUsageCalculator();
        // 面积 = 1000×800/1e6 = 0.8 m²/枚; 段成率 1.05; 数量 5000 → 用量 = 0.8×1.05×5000 = 4200 m²
        var usage = calc.CalcDimensional(sheetDimW: 1000, sheetDimF: 800, yieldRate: 1.05m, outputQty: 5000);
        Assert.Equal(4200m, usage);
    }

    [Fact]
    public void CalcFixed_UnitUsageTimesQty()
    {
        // 定额料：单耗 0.03 × 数量 5000 = 150
        Assert.Equal(150m, new MaterialUsageCalculator().CalcFixed(unitUsage: 0.03m, outputQty: 5000));
    }

    [Fact]
    public void CalcDimensional_PerSheet_MatchesEstimateCalcUsage()
    {
        // 見積回归口径：500×400/1e6 × 1.35 × 1 = 0.27 m²/枚（見積 PaperCost = 单价 × 此用量）
        var calc = new MaterialUsageCalculator();
        Assert.Equal(0.27m, calc.CalcDimensional(sheetDimW: 500, sheetDimF: 400, yieldRate: 1.35m, outputQty: 1));
    }
}
