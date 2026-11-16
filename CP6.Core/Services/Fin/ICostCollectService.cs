using CP6.Entity.DomainModels.Fin;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 成本归集（章06 §2/§3）。★差异化卖点：料吃 MES 真实消耗（WorkOrderMaterial.ActualQty）× BOM 供给单价，
/// PostgreSQL 工费采用当前授权的工序费率。按工单一单一成本单，可重算未结转成本单。
/// </summary>
public interface ICostCollectService
{
    /// <summary>
    /// 归集某工单的料工费 → 成本单：料 = Σ 实际用量×BOM供给单价（标准 = 计划用量×单价，差额即用量差异）；
    /// 人工和制造费用按工时乘以费率。传入估算额和操作者文本供历史迁移路径使用。
    /// </summary>
    Task<FinResult> CollectAsync(string workOrderNo, decimal laborStd, decimal overheadStd, string user);

    /// <summary>取某工单的成本单（含明细）。</summary>
    Task<CostSheet?> GetByWorkOrderAsync(string workOrderNo);

    /// <summary>成本单列表（按状态可选过滤，不含明细，按创建倒序）。</summary>
    Task<List<CostSheet>> ListAsync(CostSheetStatus? status = null);
}
