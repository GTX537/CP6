using CP6.Entity.DomainModels.Plan;

namespace CP6.Core.Services.Plan;

/// <summary>独立需求项（受注/MPS/预测 净化后喂入 MRP 的一条成品需求）。</summary>
public record MrpDemand(string ItemCd, decimal Qty, DateTime RequiredDate, string? SourceRefNo = null);

/// <summary>MRP 运算请求。</summary>
public record MrpRunRequest(IReadOnlyList<MrpDemand> Demands, string? ScopeJson = null);

/// <summary>
/// MRP 净需求引擎（MRP P1 · spec §5.2/§5.3）。低层码逐层、每 Item×日桶只 net 一次：
/// 展开 BOM → 扣供给+安全库存 → 净需求 → 计划订单 + Pegging。regenerative 复算。
/// </summary>
public interface IMrpEngine
{
    /// <summary>执行一次 regenerative MRP 运算，落 计划订单/净需求/Pegging，返回运算批次。</summary>
    Task<Plan_MrpRun> RunAsync(MrpRunRequest request);
}
