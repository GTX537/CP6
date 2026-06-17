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

    public async Task<List<Plan_ItemPlanningPolicy>> ListAsync(string? keyword)
    {
        var q = _db.ItemPlanningPolicies.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(keyword))
            q = q.Where(x => x.ItemCd.Contains(keyword));
        return await q.OrderBy(x => x.ItemCd).ToListAsync();
    }

    public async Task UpsertAsync(Plan_ItemPlanningPolicy dto, string? user)
    {
        if (string.IsNullOrWhiteSpace(dto.ItemCd))
            throw new InvalidOperationException("E-PLAN-品目必填: ItemCd 不能为空");

        var existing = await _db.ItemPlanningPolicies
            .FirstOrDefaultAsync(x => x.ItemCd == dto.ItemCd && !x.IsDeleted);

        if (existing == null)
        {
            dto.Creator = user;
            dto.CreateDate = DateTime.Now;
            _db.ItemPlanningPolicies.Add(dto);
        }
        else
        {
            existing.SafetyStock = dto.SafetyStock;
            existing.PurchaseLeadDays = dto.PurchaseLeadDays;
            existing.LotRule = dto.LotRule;
            existing.MoqQty = dto.MoqQty;
            existing.MultipleQty = dto.MultipleQty;
            existing.MakeOrBuy = dto.MakeOrBuy;
            existing.Modifier = user;
            existing.ModifyDate = DateTime.Now;
        }
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(string itemCd, string? user)
    {
        var row = await _db.ItemPlanningPolicies
            .FirstOrDefaultAsync(x => x.ItemCd == itemCd && !x.IsDeleted)
            ?? throw new InvalidOperationException("E-PLAN-策略不存在");
        row.IsDeleted = true;
        row.Modifier = user;
        row.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }
}
