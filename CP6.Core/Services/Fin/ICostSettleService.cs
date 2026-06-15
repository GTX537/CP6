namespace CP6.Core.Services.Fin;

/// <summary>
/// 成本完工结转（章06 §5）。把已归集成本单结转：①料工费→WIP 凭证 + ②WIP→FG 凭证，
/// 成本单置 Settled。FG 完工单位成本供 AR 成本结转切真实（F3-D3）。
/// </summary>
public interface ICostSettleService
{
    /// <summary>
    /// 完工结转：①借 WIP / 贷 原材料(INVENTORY)+直接人工(DIRECT_LABOR)+制造费用(MFG_OVERHEAD)；②借 FG / 贷 WIP。
    /// 全按 Role 锚点取科目（模板可移植）。成本单须为 Collected；已结转 E-FIN-402；不存在/未归集 E-FIN-403；角色缺失 E-FIN-141。
    /// </summary>
    Task<FinResult> SettleAsync(string workOrderNo, string user);

    /// <summary>FG 完工单位成本（喂 AR 成本结转切真实，F3-D3）。无成本单返回 0。</summary>
    Task<decimal> FgUnitCostAsync(string workOrderNo);
}
