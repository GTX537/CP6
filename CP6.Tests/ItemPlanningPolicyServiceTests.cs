using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

/// <summary>
/// 计划主数据 Plan_ItemPlanningPolicy 服务测试（MRP P1 · spec §3.2/§5.3）。
///
/// - 取计划参数（安全库存/提前期/批量规则）
/// - 制造提前期 = ProductProcess.LeadTime 沿路线汇总
/// - 缺 policy → 默认 lot-for-lot + 提前期 0 + IsDefault 告警
/// </summary>
public class ItemPlanningPolicyServiceTests
{
    private static ItemPlanningPolicyService CreateService(out CP6.Core.EFDbContext.CP6Context db)
    {
        db = TestHelper.CreateInMemoryContext();
        return new ItemPlanningPolicyService(db);
    }

    [Fact]
    public async Task GetPolicy_Existing_ReturnsStored()
    {
        var svc = CreateService(out var db);
        db.ItemPlanningPolicies.Add(new Plan_ItemPlanningPolicy
        {
            ItemCd = "RAW-01",
            SafetyStock = 100,
            PurchaseLeadDays = 7,
            LotRule = (int)PlanLotRule.Moq,
            MoqQty = 500,
            MakeOrBuy = (int)PlanMakeOrBuy.Buy,
        });
        await db.SaveChangesAsync();

        var p = await svc.GetPolicyAsync("RAW-01");

        Assert.False(p.IsDefault);
        Assert.Equal(100m, p.SafetyStock);
        Assert.Equal(7m, p.PurchaseLeadDays);
        Assert.Equal((int)PlanLotRule.Moq, p.LotRule);
        Assert.Equal(500m, p.MoqQty);
    }

    [Fact]
    public async Task GetPolicy_Missing_ReturnsLotForLotDefault()
    {
        var svc = CreateService(out _);

        var p = await svc.GetPolicyAsync("UNKNOWN");

        Assert.True(p.IsDefault);
        Assert.Equal((int)PlanLotRule.LotForLot, p.LotRule);
        Assert.Equal(0m, p.SafetyStock);
        Assert.Equal(0m, p.PurchaseLeadDays);
        Assert.Equal("UNKNOWN", p.ItemCd);
    }

    [Fact]
    public async Task GetLeadDays_BuyItem_ReturnsPurchaseLeadDays()
    {
        var svc = CreateService(out var db);
        db.ItemPlanningPolicies.Add(new Plan_ItemPlanningPolicy
        { ItemCd = "BUY-01", PurchaseLeadDays = 5, MakeOrBuy = (int)PlanMakeOrBuy.Buy });
        await db.SaveChangesAsync();

        Assert.Equal(5m, await svc.GetLeadDaysAsync("BUY-01"));
    }

    [Fact]
    public async Task GetLeadDays_MakeItem_SumsRoutingLeadTime()
    {
        var svc = CreateService(out var db);
        db.ItemPlanningPolicies.Add(new Plan_ItemPlanningPolicy
        { ItemCd = "MAKE-01", MakeOrBuy = (int)PlanMakeOrBuy.Make });
        db.ProductProcesses.Add(new ProductProcess { ProductCd = "MAKE-01", TaskCd = "T1", ProcessCd = "P1", LeadTime = 2 });
        db.ProductProcesses.Add(new ProductProcess { ProductCd = "MAKE-01", TaskCd = "T2", ProcessCd = "P2", LeadTime = 3 });
        await db.SaveChangesAsync();

        // 制造提前期 = 路线各工程 LeadTime 之和 = 2 + 3 = 5
        Assert.Equal(5m, await svc.GetLeadDaysAsync("MAKE-01"));
    }

    // ── CRUD（主数据维护 UI 支撑）──

    [Fact]
    public async Task Upsert_Insert_ThenUpdate_SameItemCd()
    {
        var svc = CreateService(out var db);

        await svc.UpsertAsync(new Plan_ItemPlanningPolicy { ItemCd = "P-1", SafetyStock = 10, PurchaseLeadDays = 3 }, "admin");
        await svc.UpsertAsync(new Plan_ItemPlanningPolicy { ItemCd = "P-1", SafetyStock = 99, PurchaseLeadDays = 7 }, "admin");

        var rows = await db.ItemPlanningPolicies.Where(x => x.ItemCd == "P-1" && !x.IsDeleted).ToListAsync();
        Assert.Single(rows);               // upsert 不重复插入
        Assert.Equal(99m, rows[0].SafetyStock);
        Assert.Equal(7m, rows[0].PurchaseLeadDays);
    }

    [Fact]
    public async Task List_FiltersByKeyword_ExcludesDeleted()
    {
        var svc = CreateService(out var db);
        await svc.UpsertAsync(new Plan_ItemPlanningPolicy { ItemCd = "RAW-A" }, "admin");
        await svc.UpsertAsync(new Plan_ItemPlanningPolicy { ItemCd = "RAW-B" }, "admin");
        await svc.UpsertAsync(new Plan_ItemPlanningPolicy { ItemCd = "FG-1" }, "admin");
        await svc.DeleteAsync("RAW-B", "admin");

        var raw = await svc.ListAsync("RAW");

        Assert.Single(raw);                // RAW-B 已删 → 仅 RAW-A
        Assert.Equal("RAW-A", raw[0].ItemCd);
    }

    [Fact]
    public async Task Delete_SoftDeletes()
    {
        var svc = CreateService(out var db);
        await svc.UpsertAsync(new Plan_ItemPlanningPolicy { ItemCd = "X" }, "admin");

        await svc.DeleteAsync("X", "admin");

        Assert.True((await db.ItemPlanningPolicies.FirstAsync(x => x.ItemCd == "X")).IsDeleted);
        Assert.True((await svc.GetPolicyAsync("X")).IsDefault);   // 删后取策略回落默认
    }
}
