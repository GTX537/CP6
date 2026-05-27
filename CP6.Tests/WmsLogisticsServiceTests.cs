using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

/// <summary>
/// WMS Logistics (CrossDock + Replenish + Slotting) 単体テスト
///
/// テスト観点：
/// CrossDock:
///   1. Create + Execute → IN + OUT 一対発行、滞留時間≒0
///   2. 取消 + 二重実行拒否
/// Replenish:
///   3. Create + Execute → MOVE ペア（OUT + IN）
///   4. GenerateBatchAsync → MinQty 割れ + RES- 在庫あり で一括生成
///   5. 重複防止（既存 Pending 指示は再生成しない）
/// Slotting:
///   6. Analyze → 過去 N 日 OUT 集計 → ABC ランク + 推奨ロケ算出
///   7. Approve → status 1→2
/// </summary>
public class WmsLogisticsServiceTests
{
    private static (CP6.Core.EFDbContext.CP6Context db, WmsSequenceService seq, StockMovementService stock) Create()
    {
        var db = TestHelper.CreateInMemoryContext();
        db.Warehouses.Add(new Warehouse { WarehouseCd = "W01", WarehouseName = "M", AllowNegative = false });
        db.SaveChanges();
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        return (db, seq, stock);
    }

    // ═════════ CrossDock ═════════

    [Fact]
    public async Task XDock_CreateAndExecute_ShouldEmitInAndOut()
    {
        var (db, seq, stock) = Create();
        var svc = new CrossDockService(db, seq, stock);

        var no = await svc.CreateAsync(new CrossDockOrderDto
        {
            ProductCd = "P1", Qty = 50,
            SupplierCd = "S1", CustomerCd = "C1",
            FromDock = "DOCK-IN-1", ToDock = "DOCK-OUT-2",
            WarehouseCd = "W01", TempLocationCd = "XDOCK-TEMP",
            LotNo = "X1",
        }, "u");
        await svc.ExecuteAsync(no, "u");

        var order = await db.CrossDockOrders.SingleAsync();
        Assert.Equal(CrossDockStatus.Executed, order.Status);
        Assert.NotNull(order.InTxnNo);
        Assert.NotNull(order.OutTxnNo);
        Assert.NotNull(order.ExecutedAt);

        // IN + OUT が完了したので Stock の物理は 0（仮置きを経由してすぐ出ていく）
        var s = await db.Stocks.SingleAsync();
        Assert.Equal(0m, s.PhysicalQty);
        Assert.Equal(2, db.StockTransactions.Count(t => t.RelatedNo == no));
    }

    [Fact]
    public async Task XDock_DoubleExecute_ShouldThrow()
    {
        var (db, seq, stock) = Create();
        var svc = new CrossDockService(db, seq, stock);
        var no = await svc.CreateAsync(new CrossDockOrderDto
        {
            ProductCd = "P1", Qty = 5, WarehouseCd = "W01", TempLocationCd = "T", LotNo = "L",
        }, "u");
        await svc.ExecuteAsync(no, "u");
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ExecuteAsync(no, "u"));
    }

    // ═════════ Replenish ═════════

    [Fact]
    public async Task Replenish_ManualExecute_ShouldMoveStock()
    {
        var (db, seq, stock) = Create();
        // 保管棚 RES-A-01 に 100 個
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01", LocationCd = "RES-A-01",
            ProductCd = "P1", LotNo = "L1", Qty = 100,
        });

        var svc = new ReplenishService(db, seq, stock);
        var no = await svc.CreateAsync(new ReplenishOrderDto
        {
            ProductCd = "P1", WarehouseCd = "W01",
            FromLocationCd = "RES-A-01", ToLocationCd = "PIK-A-01",
            LotNo = "L1", Qty = 20,
        }, "u");
        await svc.ExecuteAsync(no, "u");

        var stocks = await db.Stocks.OrderBy(s => s.LocationCd).ToListAsync();
        Assert.Equal(2, stocks.Count);
        Assert.Equal(20m, stocks.Single(s => s.LocationCd == "PIK-A-01").PhysicalQty);
        Assert.Equal(80m, stocks.Single(s => s.LocationCd == "RES-A-01").PhysicalQty);
    }

    [Fact]
    public async Task Replenish_GenerateBatch_ShouldCreateForLowStock()
    {
        var (db, seq, stock) = Create();
        var svc = new ReplenishService(db, seq, stock);

        // ピッキング棚 PIK-A-01 で MinQty=10 未満（5 個）
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01", LocationCd = "PIK-A-01",
            ProductCd = "P_LOW", LotNo = "L_PIK", Qty = 5,
        });
        // 保管棚に同製品の在庫あり
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01", LocationCd = "RES-A-01",
            ProductCd = "P_LOW", LotNo = "L_RES", Qty = 50,
        });
        // 別製品（在庫充分） — 補充指示は作られない
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01", LocationCd = "PIK-A-02",
            ProductCd = "P_OK", LotNo = "L_OK", Qty = 50,
        });

        var n = await svc.GenerateBatchAsync("W01", minQty: 10m, userName: "u");
        Assert.Equal(1, n);
        var order = await db.ReplenishOrders.SingleAsync();
        Assert.Equal("P_LOW", order.ProductCd);
        Assert.Equal("RES-A-01", order.FromLocationCd);
        Assert.Equal("PIK-A-01", order.ToLocationCd);
        Assert.Equal(5m, order.Qty); // 10 - 5 = 5
        Assert.Equal(ReplenishTrigger.Batch, order.TriggerType);
    }

    [Fact]
    public async Task Replenish_GenerateBatch_NoDuplicateWhenPendingExists()
    {
        var (db, seq, stock) = Create();
        var svc = new ReplenishService(db, seq, stock);
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01", LocationCd = "PIK-A-01",
            ProductCd = "P1", LotNo = "L1", Qty = 3,
        });
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01", LocationCd = "RES-A-01",
            ProductCd = "P1", LotNo = "L2", Qty = 100,
        });

        var n1 = await svc.GenerateBatchAsync("W01", 10m, "u");
        var n2 = await svc.GenerateBatchAsync("W01", 10m, "u");
        Assert.Equal(1, n1);
        Assert.Equal(0, n2); // 重複防止
    }

    // ═════════ Slotting ═════════

    [Fact]
    public async Task Slotting_Analyze_ShouldRankByOutFrequency()
    {
        var (db, seq, stock) = Create();
        // 3 製品で出庫頻度差を作る
        // P_HOT: 10 回 OUT、P_MID: 5 回、P_COLD: 1 回
        await SeedInAndOut(stock, "P_HOT", "RES-1", 1000, 10, 50);
        await SeedInAndOut(stock, "P_MID", "RES-2", 500, 5, 30);
        await SeedInAndOut(stock, "P_COLD", "RES-3", 200, 1, 20);

        var svc = new SlottingService(db, seq);
        var no = await svc.AnalyzeAsync("W01", 30, "u");

        var result = await svc.GetAsync(no);
        Assert.NotNull(result);
        Assert.Equal(SlottingStatus.Recommended, result!.Plan.Status);
        Assert.Equal(16, result.Plan.TxnSampleCount); // 10+5+1

        var recs = result.Recommendations;
        Assert.Equal(3, recs.Count);
        // 一番頻度高い P_HOT は A（パレート 80% 内に入る）
        var hot = recs.Single(r => r.ProductCd == "P_HOT");
        Assert.Equal(AbcRank.A, hot.AbcRank);
        Assert.Equal(10, hot.OutCount);
        // P_HOT の現在ロケは RES-* なので移動候補（PIK-A-* が推奨）
        Assert.True(hot.NeedsRelocation);
    }

    [Fact]
    public async Task Slotting_Approve_ShouldMoveStatusTo2()
    {
        var (db, seq, stock) = Create();
        await SeedInAndOut(stock, "P1", "RES-1", 100, 1, 10);
        var svc = new SlottingService(db, seq);
        var no = await svc.AnalyzeAsync("W01", 30, "u");
        await svc.ApproveAsync(no, "approver");

        var p = await db.SlottingPlans.SingleAsync();
        Assert.Equal(SlottingStatus.Approved, p.Status);
        Assert.Equal("approver", p.ApproverCd);
    }

    // ─── ヘルパー ───
    private static async Task SeedInAndOut(StockMovementService stock, string product, string loc, decimal initQty, int outCount, decimal outQtyEach)
    {
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01", LocationCd = loc,
            ProductCd = product, LotNo = "L", Qty = initQty,
        });
        for (int i = 0; i < outCount; i++)
        {
            await stock.ApplyAsync(new StockMovementRequest
            {
                TxnType = WmsTxnType.OUT, WarehouseCd = "W01", LocationCd = loc,
                ProductCd = product, LotNo = "L", Qty = outQtyEach,
            });
        }
    }
}
