using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;

namespace CP6.Tests.Fin;

public class DepreciationCalculatorTests
{
    private static readonly IDepreciationCalculator Calc = new DepreciationCalculator();

    private static DepreciationCalcInput Sl(int done, decimal accum) => new()
    {
        Method = DepreciationMethod.StraightLine,
        OriginalValue = 12000m, SalvageValue = 0m, UsefulLifeMonths = 12,
        DepreciatedPeriods = done, AccumulatedBefore = accum,
    };

    [Fact] // §13.1 直线法均摊 + 末期补足残差
    public void StraightLine_EvenSpread_LastPeriodTopsUp()
    {
        Assert.Equal(1000m, Calc.PeriodAmount(Sl(0, 0m)));
        Assert.Equal(1000m, Calc.PeriodAmount(Sl(5, 5000m)));
        Assert.Equal(1000m, Calc.PeriodAmount(Sl(11, 11000m)));
    }

    [Fact] // §13.2 双倍余额递减：年率×年初净值、年内12期月额恒定、末两年切直线、Y=5
    public void DoubleDeclining_YearConstant_SwitchSLLastTwoYears()
    {
        DepreciationCalcInput In(int done, decimal accum, decimal nbvYearStart) => new()
        {
            Method = DepreciationMethod.DoubleDeclining,
            OriginalValue = 100000m, SalvageValue = 5000m, UsefulLifeMonths = 60,
            DepreciatedPeriods = done, AccumulatedBefore = accum, NetBookValueAtYearStart = nbvYearStart,
        };
        Assert.Equal(3333.33m, Calc.PeriodAmount(In(0, 0m, 100000m)));
        Assert.Equal(2000m, Calc.PeriodAmount(In(12, 40000m, 60000m)));
        Assert.Equal(691.67m, Calc.PeriodAmount(In(36, 78400m, 21600m)));
    }

    [Fact] // §13.2 边界：Y=2（24月）无 DDB 阶段，直接直线
    public void DoubleDeclining_TwoYears_FallsBackToStraightLine()
    {
        var input = new DepreciationCalcInput
        {
            Method = DepreciationMethod.DoubleDeclining,
            OriginalValue = 24000m, SalvageValue = 0m, UsefulLifeMonths = 24,
            DepreciatedPeriods = 0, AccumulatedBefore = 0m, NetBookValueAtYearStart = 24000m,
        };
        Assert.Equal(1000m, Calc.PeriodAmount(input));
    }

    [Fact] // §13.3 年数总和：年序加权、跨年月额
    public void SumOfYears_YearWeighted()
    {
        DepreciationCalcInput In(int done, decimal accum) => new()
        {
            Method = DepreciationMethod.SumOfYears,
            OriginalValue = 12000m, SalvageValue = 0m, UsefulLifeMonths = 36,
            DepreciatedPeriods = done, AccumulatedBefore = accum,
        };
        Assert.Equal(500m, Calc.PeriodAmount(In(0, 0m)));
        Assert.Equal(166.67m, Calc.PeriodAmount(In(24, 10000m)));
    }

    [Fact] // §13.4 工作量法：本期/总量比例；§13.5 残值封顶
    public void UnitsOfProduction_ProRata_AndSalvageCap()
    {
        var input = new DepreciationCalcInput
        {
            Method = DepreciationMethod.UnitsOfProduction,
            OriginalValue = 11000m, SalvageValue = 1000m, UsefulLifeMonths = 0,
            DepreciatedPeriods = 0, AccumulatedBefore = 0m,
            TotalWorkload = 10000m, WorkloadThisPeriod = 500m,
        };
        Assert.Equal(500m, Calc.PeriodAmount(input));
        input.DepreciatedPeriods = 1; input.AccumulatedBefore = 9800m; input.WorkloadThisPeriod = 500m;
        Assert.Equal(200m, Calc.PeriodAmount(input));
    }

    [Fact] // §13.4 工作量法缺总量 → 抛 E-FA-008
    public void UnitsOfProduction_MissingTotal_Throws()
    {
        var input = new DepreciationCalcInput
        {
            Method = DepreciationMethod.UnitsOfProduction,
            OriginalValue = 10000m, SalvageValue = 0m, UsefulLifeMonths = 0,
            DepreciatedPeriods = 0, AccumulatedBefore = 0m,
            TotalWorkload = null, WorkloadThisPeriod = 500m,
        };
        var ex = Assert.Throws<InvalidOperationException>(() => Calc.PeriodAmount(input));
        Assert.Equal("E-FA-008", ex.Message);
    }
}
