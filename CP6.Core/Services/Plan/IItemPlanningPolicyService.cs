using CP6.Entity.DomainModels.Plan;

namespace CP6.Core.Services.Plan;

/// <summary>
/// 计划主数据服务（MRP P1 · spec §3.2/§5.3）：品目计划策略取值 + 提前期汇总。
/// </summary>
public interface IItemPlanningPolicyService
{
    /// <summary>取品目计划策略；缺则返回默认（lot-for-lot + 0 提前期，IsDefault=true 供告警）。</summary>
    Task<Plan_ItemPlanningPolicy> GetPolicyAsync(string itemCd);

    /// <summary>取品目计划提前期（日）：采购类取 PurchaseLeadDays，自制类取路线 LeadTime 汇总。</summary>
    Task<decimal> GetLeadDaysAsync(string itemCd);

    /// <summary>主数据列表（按品目关键字过滤，剔除已删）。</summary>
    Task<List<Plan_ItemPlanningPolicy>> ListAsync(string? keyword);

    /// <summary>按 ItemCd upsert（存在则更新，否则新增）。</summary>
    Task UpsertAsync(Plan_ItemPlanningPolicy dto, string? user);

    /// <summary>软删除品目策略。</summary>
    Task DeleteAsync(string itemCd, string? user);
}
