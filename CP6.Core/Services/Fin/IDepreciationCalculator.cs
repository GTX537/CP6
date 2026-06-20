using CP6.Entity.DomainModels.Fin;

namespace CP6.Core.Services.Fin;

/// <summary>折旧引擎（纯函数·无 DB 依赖，spec §3.1）。给定折旧参数 + 已提期数 + 本期工作量，返回本期折旧额（已封顶残值、末期取整兜底）。</summary>
public interface IDepreciationCalculator
{
    decimal PeriodAmount(DepreciationCalcInput input);
}

/// <summary>折旧计算入参（spec §3.1）。</summary>
public sealed class DepreciationCalcInput
{
    public DepreciationMethod Method;
    public decimal OriginalValue;
    public decimal SalvageValue;
    public int UsefulLifeMonths;
    public int DepreciatedPeriods;
    public decimal AccumulatedBefore;
    public decimal NetBookValueAtYearStart;
    public decimal? TotalWorkload;
    public decimal? WorkloadThisPeriod;
}
