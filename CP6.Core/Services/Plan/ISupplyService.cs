namespace CP6.Core.Services.Plan;

/// <summary>供给分解（各源明细，供净需求 Plan_NetRequirement 钻取）。</summary>
public readonly record struct SupplyBreakdown(decimal OnHand, decimal InTransit, decimal InWip, decimal FirmPlanned)
{
    /// <summary>合计供给。</summary>
    public decimal Total => OnHand + InTransit + InWip + FirmPlanned;
}

/// <summary>
/// MRP 供给汇总服务（MRP P1 · spec §5.1）。
/// 汇总四源：WMS 现库存 + 在途(采购未到) + 在制(已下达工单) + 已确认/已转单计划订单(scheduled receipt)。
/// </summary>
public interface ISupplyService
{
    /// <summary>取某品目截至日桶的供给分解。</summary>
    Task<SupplyBreakdown> GetSupplyBreakdownAsync(string itemCd, DateTime bucket);

    /// <summary>取某品目截至日桶的供给合计。</summary>
    Task<decimal> GetSupplyAsync(string itemCd, DateTime bucket);
}
