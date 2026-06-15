using CP6.Entity.DomainModels.Fin;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 成本归集（章06 §2/§3）。★差异化卖点：料吃 MES 真实消耗（WorkOrderMaterial.ActualQty）× BOM 供给单价，
/// 工费按标准估算（F3-D1）。按工单一单一成本单，可重复归集（覆盖，已结转的拒绝）。
/// </summary>
public interface ICostCollectService
{
    /// <summary>
    /// 归集某工单的料工费 → 成本单：料 = Σ 实际用量×BOM供给单价（标准 = 计划用量×单价，差额即用量差异）；
    /// 工/费 = 传入标准估算额。完工数取工单累计良品数。幂等（重复调用覆盖，已结转则 E-FIN-402）。
    /// </summary>
    Task<FinResult> CollectAsync(string workOrderNo, decimal laborStd, decimal overheadStd, string user);

    /// <summary>取某工单的成本单（含明细）。</summary>
    Task<CostSheet?> GetByWorkOrderAsync(string workOrderNo);

    /// <summary>成本单列表（按状态可选过滤，不含明细，按创建倒序）。</summary>
    Task<List<CostSheet>> ListAsync(CostSheetStatus? status = null);
}
