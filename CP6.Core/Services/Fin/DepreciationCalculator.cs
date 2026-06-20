using CP6.Entity.DomainModels.Fin;

namespace CP6.Core.Services.Fin;

/// <summary>四法折旧纯函数实现（spec §3.1）。统一兜底：封顶残值 + 末期补足残差 + 负数归零。</summary>
public sealed class DepreciationCalculator : IDepreciationCalculator
{
    public decimal PeriodAmount(DepreciationCalcInput x)
    {
        decimal depreciable = x.OriginalValue - x.SalvageValue;
        int remain = x.UsefulLifeMonths - x.DepreciatedPeriods;
        decimal raw;

        switch (x.Method)
        {
            case DepreciationMethod.StraightLine:
                raw = x.UsefulLifeMonths <= 0 ? 0m : depreciable / x.UsefulLifeMonths;
                break;

            case DepreciationMethod.DoubleDeclining:
            {
                int Y = (int)Math.Ceiling(x.UsefulLifeMonths / 12.0);
                if (Y <= 2)
                {
                    raw = x.UsefulLifeMonths <= 0 ? 0m : depreciable / x.UsefulLifeMonths;
                    break;
                }
                int y = x.DepreciatedPeriods / 12 + 1;
                decimal r = 2m / Y;
                if (y <= Y - 2)
                    raw = x.NetBookValueAtYearStart * r / 12m;
                else
                {
                    decimal entryNbv = x.OriginalValue * (decimal)Math.Pow((double)(1m - r), Y - 2);
                    raw = (entryNbv - x.SalvageValue) / 2m / 12m;
                }
                break;
            }

            case DepreciationMethod.SumOfYears:
            {
                int Y = (int)Math.Ceiling(x.UsefulLifeMonths / 12.0);
                int y = (int)Math.Ceiling((x.DepreciatedPeriods + 1) / 12.0);
                decimal sum = Y * (Y + 1) / 2m;
                decimal annual = depreciable * (Y - y + 1) / sum;
                raw = annual / 12m;
                break;
            }

            case DepreciationMethod.UnitsOfProduction:
                if (x.TotalWorkload is null or <= 0m || x.WorkloadThisPeriod is null)
                    throw new InvalidOperationException("E-FA-008");
                raw = depreciable * x.WorkloadThisPeriod.Value / x.TotalWorkload.Value;
                break;

            default:
                raw = 0m;
                break;
        }

        decimal amount = Math.Round(raw, 2, MidpointRounding.AwayFromZero);
        decimal cap = depreciable - x.AccumulatedBefore;
        if (amount > cap) amount = cap;
        if (x.Method != DepreciationMethod.UnitsOfProduction && remain <= 1) amount = cap;
        if (amount < 0m) amount = 0m;
        return amount;
    }
}
