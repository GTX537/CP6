using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

/// <summary>
/// 多倉庫ルーティング（Gap 4.2 / T14）テスト。
///
/// 観点：
/// 1. 候補倉庫の優先順 = ルール（SortOrder）→ Warehouse.OutboundPriority → フォールバック、重複除去
/// 2. ProductCdPrefix 条件のマッチング
/// 3. 引当：ルールで指定倉庫が選ばれる（FEFO や倉庫優先度より優先）
/// 4. 引当：ルール無しでも Warehouse.OutboundPriority 昇順で多倉庫から選ぶ
/// 5. 出庫：引当した実倉庫(d.WarehouseCd)から OUT が発行される
/// </summary>
public class OutboundRoutingTests
{
    private static CP6.Core.EFDbContext.CP6Context NewDb() => TestHelper.CreateInMemoryContext();

    private static void SeedWarehouse(CP6.Core.EFDbContext.CP6Context db, string cd, int outboundPriority)
    {
        db.Warehouses.Add(new Warehouse
        {
            WarehouseCd = cd,
            WarehouseName = cd + " 倉庫",
            WarehouseType = WarehouseType.Finished,
            OutboundPriority = outboundPriority,
        });
    }

    private static async Task SeedStockAsync(StockMovementService stock, string warehouseCd,
        string product, string lot, string location, decimal qty,
        DateTime? receiveDate = null, DateTime? expiryDate = null)
    {
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN,
            WarehouseCd = warehouseCd,
            LocationCd = location,
            ProductCd = product,
            LotNo = lot,
            Qty = qty,
            ReceiveDate = receiveDate,
            ExpiryDate = expiryDate,
        });
    }

    // ═════════ 1. 候補解決の優先順 ═════════

    [Fact]
    public async Task Resolve_RuleFirst_ThenPriority_ThenFallback_Distinct()
    {
        using var db = NewDb();
        SeedWarehouse(db, "W01", outboundPriority: 100);
        SeedWarehouse(db, "W02", outboundPriority: 50);
        SeedWarehouse(db, "W03", outboundPriority: 10);
        await db.SaveChangesAsync();

        var routing = new OutboundRoutingService(db);
        await routing.CreateRuleAsync(new OutboundRoutingRule
        {
            RuleName = "得意先 C001 は W02 優先",
            SortOrder = 10,
            CustomerCd = "C001",
            TargetWarehouseCd = "W02",
        }, "u");

        var candidates = await routing.ResolveCandidateWarehousesAsync("C001", "P001", OutboundType.Shipping, "W01");

        // ルールの W02 が先頭 → 残りを OutboundPriority 昇順(W03=10, W01=100) → フォールバック W01(重複除去)
        Assert.Equal(new[] { "W02", "W03", "W01" }, candidates);
    }

    [Fact]
    public async Task Resolve_ProductPrefixCondition_FiltersNonMatching()
    {
        using var db = NewDb();
        SeedWarehouse(db, "W01", outboundPriority: 100);
        SeedWarehouse(db, "WMAT", outboundPriority: 200);
        await db.SaveChangesAsync();

        var routing = new OutboundRoutingService(db);
        await routing.CreateRuleAsync(new OutboundRoutingRule
        {
            RuleName = "材料(M*)は材料倉庫へ",
            SortOrder = 10,
            ProductCdPrefix = "M",
            TargetWarehouseCd = "WMAT",
        }, "u");

        // "M001" はマッチ → WMAT 先頭
        var matMatch = await routing.ResolveCandidateWarehousesAsync(null, "M001", OutboundType.Material, "W01");
        Assert.Equal("WMAT", matMatch[0]);

        // "P001" は接頭辞 M に非マッチ → ルール無効、優先度順(W01=100, WMAT=200)
        var noMatch = await routing.ResolveCandidateWarehousesAsync(null, "P001", OutboundType.Material, "W01");
        Assert.Equal(new[] { "W01", "WMAT" }, noMatch);
    }

    // ═════════ 2. 引当：ルール優先 ═════════

    [Fact]
    public async Task Allocate_WithRule_PicksTargetWarehouse_OverPriorityAndFefo()
    {
        using var db = NewDb();
        SeedWarehouse(db, "W01", outboundPriority: 100);
        SeedWarehouse(db, "W02", outboundPriority: 50);
        await db.SaveChangesAsync();

        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        // W01 は期限が近い（FEFO なら本来 W01 が魅力的）が、ルールで W02 を優先させる
        await SeedStockAsync(stock, "W01", "P001", "LOT_W01", "L01", 100m, expiryDate: DateTime.Today.AddDays(5));
        await SeedStockAsync(stock, "W02", "P001", "LOT_W02", "L01", 100m, expiryDate: DateTime.Today.AddDays(90));

        var routing = new OutboundRoutingService(db);
        await routing.CreateRuleAsync(new OutboundRoutingRule
        {
            RuleName = "C001 → W02",
            SortOrder = 10,
            CustomerCd = "C001",
            TargetWarehouseCd = "W02",
        }, "u");

        var svc = new OutboundService(db, seq, stock, routing: routing);
        var no = await svc.CreateOrderAsync(new OutboundOrderDto
        {
            OutboundType = OutboundType.Shipping,
            CustomerCd = "C001",
            WarehouseCd = "W01", // ヘッダは W01 だがルールで W02 が選ばれるはず
            Details = new() { new() { ProductCd = "P001", RequiredQty = 30m } },
        }, "u");
        await svc.ConfirmOrderAsync(no, "u");
        await svc.AllocateAsync(no, "u");

        var d = await db.OutboundOrderDetails.SingleAsync(x => x.OutboundNo == no);
        Assert.Equal("W02", d.WarehouseCd);   // ルールで W02 から引当
        Assert.Equal("LOT_W02", d.LotNo);
        Assert.Equal(30m, d.AllocatedQty);

        var sW02 = await db.Stocks.SingleAsync(s => s.WarehouseCd == "W02");
        Assert.Equal(30m, sW02.AllocatedQty);
        var sW01 = await db.Stocks.SingleAsync(s => s.WarehouseCd == "W01");
        Assert.Equal(0m, sW01.AllocatedQty);  // W01 は触らない
    }

    // ═════════ 3. 引当：ルール無し → 倉庫優先度 ═════════

    [Fact]
    public async Task Allocate_NoRule_PicksByWarehousePriority()
    {
        using var db = NewDb();
        SeedWarehouse(db, "W01", outboundPriority: 100); // ヘッダ倉庫だが在庫なし
        SeedWarehouse(db, "W02", outboundPriority: 50);  // 優先度高・在庫あり
        await db.SaveChangesAsync();

        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        await SeedStockAsync(stock, "W02", "P001", "LOT_W02", "L01", 100m);

        var routing = new OutboundRoutingService(db);
        var svc = new OutboundService(db, seq, stock, routing: routing);
        var no = await svc.CreateOrderAsync(new OutboundOrderDto
        {
            OutboundType = OutboundType.Shipping,
            WarehouseCd = "W01",
            Details = new() { new() { ProductCd = "P001", RequiredQty = 40m } },
        }, "u");
        await svc.ConfirmOrderAsync(no, "u");
        await svc.AllocateAsync(no, "u");

        var d = await db.OutboundOrderDetails.SingleAsync(x => x.OutboundNo == no);
        Assert.Equal("W02", d.WarehouseCd);   // 優先度 50 の W02 が在庫を持つので選ばれる
        Assert.Equal(40m, d.AllocatedQty);
    }

    // ═════════ 4. 出庫：実引当倉庫から OUT ═════════

    [Fact]
    public async Task Ship_AfterRoutedAllocation_OutsFromAllocatedWarehouse()
    {
        using var db = NewDb();
        SeedWarehouse(db, "W01", outboundPriority: 100);
        SeedWarehouse(db, "W02", outboundPriority: 50);
        await db.SaveChangesAsync();

        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        await SeedStockAsync(stock, "W02", "P001", "LOT_W02", "L01", 100m);

        var routing = new OutboundRoutingService(db);
        var svc = new OutboundService(db, seq, stock, routing: routing);
        var no = await svc.CreateOrderAsync(new OutboundOrderDto
        {
            OutboundType = OutboundType.Material, // 梱包なしで検証を単純化
            WarehouseCd = "W01",
            Details = new() { new() { ProductCd = "P001", RequiredQty = 30m } },
        }, "u");
        await svc.ConfirmOrderAsync(no, "u");
        await svc.AllocateAsync(no, "u");
        await svc.ShipAsync(no, new ShipRequest(), "u");

        var sW02 = await db.Stocks.SingleAsync(s => s.WarehouseCd == "W02");
        Assert.Equal(70m, sW02.PhysicalQty);  // 100 - 30、W02 から出庫
        Assert.Equal(0m, sW02.AllocatedQty);

        var d = await db.OutboundOrderDetails.SingleAsync(x => x.OutboundNo == no);
        Assert.Equal(30m, d.ShippedQty);
        Assert.Equal("W02", d.WarehouseCd);
        Assert.Equal(OutboundOrderStatus.Completed, (await db.OutboundOrders.SingleAsync()).Status);
    }
}
