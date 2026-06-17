using Microsoft.EntityFrameworkCore;
using CP6.Entity.DomainModels.Wms;

namespace CP6.Tests;

/// <summary>
/// MRP 引擎核心测试（MRP P1 · spec §5.2/§5.3）★★。
///
/// 低层码逐层、每 Item×日桶只 net 一次：独立需求(受注)→展开 BOM→扣供给+安全库存→净需求→计划订单+Pegging。
/// 纸箱原纸按 ProductMaterial 实际行展开、用量经共享内核（面积×段成率）；自制半成品再展开下层；
/// 共用原纸跨产品需求合并、只 net 一次；已确认计划订单保留并当供给抵扣。
/// </summary>
public class MrpEngineTests
{
    private static MrpEngine CreateEngine(out CP6.Core.EFDbContext.CP6Context db)
    {
        db = TestHelper.CreateInMemoryContext();
        var usage = new MaterialUsageCalculator();
        var lowLevel = new LowLevelCodeService();
        var supply = new SupplyService(db);
        var policy = new ItemPlanningPolicyService(db);
        return new MrpEngine(db, usage, lowLevel, supply, policy);
    }

    private static readonly DateTime Need = new(2026, 7, 1);

    // ── 主数据 seed 帮手 ──
    private static void AddProductSpec(CP6.Core.EFDbContext.CP6Context db, string productCd, decimal w, decimal f, string flute)
        => db.ProductMasters.Add(new ProductMaster { ProductCd = productCd, SheetDimW = w, SheetDimF = f, SheetFlute = flute });

    private static void AddYield(CP6.Core.EFDbContext.CP6Context db, string flute, decimal rate)
        => db.MasterGenericCodes.Add(new MasterGenericCode { GroupCode = "M067", Code = flute, Name = flute, Num1 = rate });

    private static void AddPaperRow(CP6.Core.EFDbContext.CP6Context db, string productCd, string paperCd)
        => db.ProductMaterials.Add(new ProductMaterial
        { ProductCd = productCd, ProcessCd = "P1", MaterialCd = paperCd, MaterialTypeDiv = "4", UsageType = 1 });

    private static void AddFixedRow(CP6.Core.EFDbContext.CP6Context db, string productCd, string childCd, decimal unitUsage, string typeDiv = "3")
        => db.ProductMaterials.Add(new ProductMaterial
        { ProductCd = productCd, ProcessCd = "P1", MaterialCd = childCd, MaterialTypeDiv = typeDiv, UsageType = 2, UnitUsage = unitUsage });

    private static void AddPolicy(CP6.Core.EFDbContext.CP6Context db, string itemCd, PlanMakeOrBuy mob,
        decimal safety = 0, decimal purchaseLead = 0, PlanLotRule lot = PlanLotRule.LotForLot, decimal? moq = null, decimal? mult = null)
        => db.ItemPlanningPolicies.Add(new Plan_ItemPlanningPolicy
        {
            ItemCd = itemCd, MakeOrBuy = (int)mob, SafetyStock = safety, PurchaseLeadDays = purchaseLead,
            LotRule = (int)lot, MoqQty = moq, MultipleQty = mult,
        });

    private static MrpRunRequest Req(params MrpDemand[] demands) => new(demands);

    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Run_Paper_ExpandsByProductMaterialRows()
    {
        var eng = CreateEngine(out var db);
        AddProductSpec(db, "X", 1000, 800, "A");   // 面积 0.8 m²
        AddYield(db, "A", 1.0m);
        AddPaperRow(db, "X", "PAPER-F");
        AddPaperRow(db, "X", "PAPER-C");
        AddPaperRow(db, "X", "PAPER-B");
        AddPolicy(db, "X", PlanMakeOrBuy.Make);
        AddPolicy(db, "PAPER-F", PlanMakeOrBuy.Buy);
        AddPolicy(db, "PAPER-C", PlanMakeOrBuy.Buy);
        AddPolicy(db, "PAPER-B", PlanMakeOrBuy.Buy);
        await db.SaveChangesAsync();

        var run = await eng.RunAsync(Req(new MrpDemand("X", 5000, Need, "SO-1")));

        var orders = await db.PlannedOrders.Where(o => o.MrpRunId == run.Id && !o.IsDeleted).ToListAsync();
        // 3 条原纸（按 ProductMaterial 实际行展开），各用量 = 0.8×1.0×5000 = 4000
        Assert.Equal(4000m, orders.Single(o => o.ItemCd == "PAPER-F").Qty);
        Assert.Equal(4000m, orders.Single(o => o.ItemCd == "PAPER-C").Qty);
        Assert.Equal(4000m, orders.Single(o => o.ItemCd == "PAPER-B").Qty);
        Assert.Equal(3, orders.Count(o => o.ItemCd.StartsWith("PAPER")));
    }

    [Fact]
    public async Task Run_NetsOncePerItem_SharedPaperMerged()
    {
        var eng = CreateEngine(out var db);
        AddProductSpec(db, "X", 1000, 800, "A");   // 0.8 m²/枚
        AddProductSpec(db, "Y", 500, 400, "A");    // 0.2 m²/枚
        AddYield(db, "A", 1.0m);
        AddPaperRow(db, "X", "PAPER-1");
        AddPaperRow(db, "Y", "PAPER-1");           // 共用同一原纸
        AddPolicy(db, "X", PlanMakeOrBuy.Make);
        AddPolicy(db, "Y", PlanMakeOrBuy.Make);
        AddPolicy(db, "PAPER-1", PlanMakeOrBuy.Buy);
        await db.SaveChangesAsync();

        var run = await eng.RunAsync(Req(
            new MrpDemand("X", 5000, Need, "SO-X"),
            new MrpDemand("Y", 3000, Need, "SO-Y")));

        var paper = await db.PlannedOrders.Where(o => o.MrpRunId == run.Id && o.ItemCd == "PAPER-1" && !o.IsDeleted).ToListAsync();
        // 共用原纸只 net 一次、需求合并：X 0.8×5000=4000 + Y 0.2×3000=600 = 4600，单条
        Assert.Single(paper);
        Assert.Equal(4600m, paper[0].Qty);
    }

    [Fact]
    public async Task Run_NetRequirement_SubtractsAllSupply()
    {
        var eng = CreateEngine(out var db);
        AddPolicy(db, "FG", PlanMakeOrBuy.Buy, safety: 200);
        db.Stocks.Add(new Stock { WarehouseCd = "W1", LocationCd = "L1", ProductCd = "FG", LotNo = "", AvailableQty = 300 });
        await db.SaveChangesAsync();

        var run = await eng.RunAsync(Req(new MrpDemand("FG", 1000, Need, "SO-1")));

        var nr = await db.NetRequirements.SingleAsync(x => x.MrpRunId == run.Id && x.ItemCd == "FG" && !x.IsDeleted);
        Assert.Equal(1000m, nr.Gross);
        Assert.Equal(300m, nr.OnHand);
        Assert.Equal(200m, nr.SafetyStock);
        Assert.Equal(500m, nr.Net);    // 1000 − 300 − 200
        var po = await db.PlannedOrders.SingleAsync(o => o.MrpRunId == run.Id && o.ItemCd == "FG" && !o.IsDeleted);
        Assert.Equal(500m, po.Qty);
    }

    [Fact]
    public async Task Run_FirmPlannedOrder_NotRegenerated_CountedAsSupply()
    {
        var eng = CreateEngine(out var db);
        AddPolicy(db, "ITEM-A", PlanMakeOrBuy.Buy);
        // 已确认计划订单 200（到货日落桶）→ 保留 + 当供给
        var confirmed = new Plan_PlannedOrder { ItemCd = "ITEM-A", Qty = 200, RequiredDate = Need, Status = (int)PlannedOrderStatus.Confirmed };
        // 上次运算遗留的建议计划订单 999 → 复算作废重生
        var stale = new Plan_PlannedOrder { ItemCd = "ITEM-A", Qty = 999, RequiredDate = Need, Status = (int)PlannedOrderStatus.Suggested };
        db.PlannedOrders.AddRange(confirmed, stale);
        await db.SaveChangesAsync();
        var confirmedId = confirmed.Id;
        var staleId = stale.Id;

        var run = await eng.RunAsync(Req(new MrpDemand("ITEM-A", 1000, Need, "SO-1")));

        // 已确认保留
        var keep = await db.PlannedOrders.FirstAsync(o => o.Id == confirmedId);
        Assert.False(keep.IsDeleted);
        // 旧建议作废
        var voided = await db.PlannedOrders.FirstAsync(o => o.Id == staleId);
        Assert.True(voided.IsDeleted);
        // 新建议 = 1000 − 200(已确认当供给) = 800
        var fresh = await db.PlannedOrders.SingleAsync(o => o.MrpRunId == run.Id && o.ItemCd == "ITEM-A"
            && o.Status == (int)PlannedOrderStatus.Suggested && !o.IsDeleted);
        Assert.Equal(800m, fresh.Qty);
    }

    [Fact]
    public async Task Run_LotRule_MOQ_RoundsUp()
    {
        var eng = CreateEngine(out var db);
        AddPolicy(db, "RAW", PlanMakeOrBuy.Buy, lot: PlanLotRule.Moq, moq: 100);
        await db.SaveChangesAsync();

        var run = await eng.RunAsync(Req(new MrpDemand("RAW", 80, Need, "SO-1")));

        var po = await db.PlannedOrders.SingleAsync(o => o.MrpRunId == run.Id && o.ItemCd == "RAW" && !o.IsDeleted);
        Assert.Equal(100m, po.Qty);    // 净需求 80 < MOQ 100 → 向上取至 100
    }

    [Fact]
    public async Task Run_ReleaseDate_EqualsRequiredMinusLeadTime()
    {
        var eng = CreateEngine(out var db);
        AddPolicy(db, "RAW", PlanMakeOrBuy.Buy, purchaseLead: 7);
        await db.SaveChangesAsync();

        var run = await eng.RunAsync(Req(new MrpDemand("RAW", 50, Need, "SO-1")));

        var po = await db.PlannedOrders.SingleAsync(o => o.MrpRunId == run.Id && o.ItemCd == "RAW" && !o.IsDeleted);
        Assert.Equal(Need, po.RequiredDate);
        Assert.Equal(Need.AddDays(-7), po.ReleaseDate);   // 下达日 = 需求日 − 提前期
    }

    [Fact]
    public async Task Run_SelfMadeWip_ExplodesToLowerLevel()
    {
        var eng = CreateEngine(out var db);
        // X(成品) → 板(自制仕掛品,定额1) → 原纸(尺寸料)
        AddProductSpec(db, "板", 1200, 900, "A");   // 面积 1.08 m²
        AddYield(db, "A", 1.0m);
        AddFixedRow(db, "X", "板", unitUsage: 1, typeDiv: "1");   // 每个成品 1 块板（仕掛品）
        AddPaperRow(db, "板", "原纸");
        AddPolicy(db, "X", PlanMakeOrBuy.Make);
        AddPolicy(db, "板", PlanMakeOrBuy.Make);
        AddPolicy(db, "原纸", PlanMakeOrBuy.Buy);
        await db.SaveChangesAsync();

        var run = await eng.RunAsync(Req(new MrpDemand("X", 1000, Need, "SO-1")));

        var orders = await db.PlannedOrders.Where(o => o.MrpRunId == run.Id && !o.IsDeleted).ToListAsync();
        Assert.Equal(1000m, orders.Single(o => o.ItemCd == "X").Qty);
        Assert.Equal(1000m, orders.Single(o => o.ItemCd == "板").Qty);     // 1×1000
        Assert.Equal(1080m, orders.Single(o => o.ItemCd == "原纸").Qty);   // 1.08×1.0×1000，展开到第2层
        // 板 是生产类、原纸是采购类
        Assert.Equal((int)PlannedOrderType.Production, orders.Single(o => o.ItemCd == "板").Type);
        Assert.Equal((int)PlannedOrderType.Purchase, orders.Single(o => o.ItemCd == "原纸").Type);
    }
}
