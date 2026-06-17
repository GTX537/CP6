using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Plan;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Plan;

/// <inheritdoc/>
public class ItemPlanningPolicyService : IItemPlanningPolicyService
{
    private readonly CP6Context _db;

    public ItemPlanningPolicyService(CP6Context db)
    {
        _db = db;
    }

    public async Task<Plan_ItemPlanningPolicy> GetPolicyAsync(string itemCd)
    {
        var policy = await _db.ItemPlanningPolicies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ItemCd == itemCd && !x.IsDeleted);

        if (policy != null) return policy;

        // 缺主数据 → 合成默认策略（lot-for-lot + 0 提前期），IsDefault 供引擎告警。
        return new Plan_ItemPlanningPolicy
        {
            ItemCd = itemCd,
            LotRule = (int)PlanLotRule.LotForLot,
            SafetyStock = 0m,
            PurchaseLeadDays = 0m,
            MakeOrBuy = (int)PlanMakeOrBuy.Buy,
            IsDefault = true,
        };
    }

    public async Task<decimal> GetLeadDaysAsync(string itemCd)
    {
        var policy = await GetPolicyAsync(itemCd);

        // 自制：制造提前期 = 标准路线各工程 LeadTime 之和（ProductProcess by ProductCd）。
        if (policy.MakeOrBuy == (int)PlanMakeOrBuy.Make)
        {
            return await _db.ProductProcesses.AsNoTracking()
                .Where(p => p.ProductCd == itemCd && !p.IsDeleted)
                .SumAsync(p => p.LeadTime ?? 0m);
        }

        // 采购：直接取采购提前期。
        return policy.PurchaseLeadDays;
    }
}
