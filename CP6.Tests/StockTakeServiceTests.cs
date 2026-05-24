using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

/// <summary>
/// StockTakeService 単体テスト（MSBBWM Phase 4）
///
/// テスト観点：
/// 1. 計画スナップショット（範囲フィルタ、空フィルタ拒否）
/// 2. カウント入力 → DiffQty/DiffAmount 自動計算
/// 3. 未カウントあり → SubmitForReview 拒否
/// 4. 差異理由未入力（差異≠0 で必須）→ SubmitForReview 拒否
/// 5. 差異 0 → 自動承認
/// 6. 閾値超 → AwaitingApproval、超えない → DiffReview
/// 7. 承認 → ADJ トランザクション発行、Stock.PhysicalQty 更新
/// 8. 取消、完了済の取消拒否
/// </summary>
public class StockTakeServiceTests
{
    private static StockTakeService CreateService(out CP6.Core.EFDbContext.CP6Context db)
    {
        db = TestHelper.CreateInMemoryContext();
        db.Warehouses.Add(new Warehouse
        {
            WarehouseCd = "W01", WarehouseName = "メイン倉庫",
            WarehouseType = WarehouseType.RawMaterial, AllowNegative = false,
        });
        db.SaveChanges();
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        return new StockTakeService(db, seq, stock);
    }

    private static async Task SeedStockAsync(CP6.Core.EFDbContext.CP6Context db,
        string product, string lot, string location, decimal qty, decimal? unitPrice = null)
    {
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01",
            LocationCd = location, ProductCd = product, LotNo = lot,
            Qty = qty, UnitPrice = unitPrice,
        });
    }

    // ═════════ 計画 ═════════

    [Fact]
    public async Task CreatePlan_ShouldSnapshotStocksAndAssignNo()
    {
        var svc = CreateService(out var db);
        await SeedStockAsync(db, "P001", "L1", "LOC1", 100m, 10m);
        await SeedStockAsync(db, "P002", "L1", "LOC2", 50m, 20m);

        var no = await svc.CreatePlanAsync(new StockTakePlanDto
        {
            StockTakeType = StockTakeType.Full,
            TargetWarehouseCd = "W01",
        }, "u1");

        Assert.StartsWith("ST", no);
        var details = await db.StockTakeDetails.OrderBy(d => d.LineNo).ToListAsync();
        Assert.Equal(2, details.Count);
        Assert.Equal(100m, details[0].BookQty);
        Assert.Equal(50m, details[1].BookQty);
        Assert.Equal(10m, details[0].UnitPrice);
    }

    [Fact]
    public async Task CreatePlan_LocationPrefixFilter_ShouldIncludeOnlyMatching()
    {
        var svc = CreateService(out var db);
        await SeedStockAsync(db, "P001", "L1", "A-01", 100m);
        await SeedStockAsync(db, "P002", "L1", "B-01", 50m);

        var no = await svc.CreatePlanAsync(new StockTakePlanDto
        {
            TargetWarehouseCd = "W01",
            TargetLocationPrefix = "A-",
        }, "u1");

        var details = await db.StockTakeDetails.Where(d => d.StockTakeNo == no).ToListAsync();
        Assert.Single(details);
        Assert.Equal("A-01", details[0].LocationCd);
    }

    [Fact]
    public async Task CreatePlan_NoMatchingStock_ShouldThrow()
    {
        var svc = CreateService(out _);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreatePlanAsync(new StockTakePlanDto { TargetWarehouseCd = "W01" }, "u1"));
    }

    // ═════════ カウント ═════════

    [Fact]
    public async Task UpdateCounts_ShouldCalcDiffAndAmount()
    {
        var svc = CreateService(out var db);
        await SeedStockAsync(db, "P001", "L1", "LOC1", 100m, 5m);
        var no = await svc.CreatePlanAsync(new StockTakePlanDto { TargetWarehouseCd = "W01" }, "u1");
        await svc.StartCountAsync(no, "u1");

        await svc.UpdateCountsAsync(no, new()
        {
            new() { LineNo = 1, CountedQty = 95m, DiffReasonCd = "DAMAGE" }
        }, "u1");

        var d = await db.StockTakeDetails.SingleAsync();
        Assert.Equal(95m, d.CountedQty);
        Assert.Equal(-5m, d.DiffQty);
        Assert.Equal(-25m, d.DiffAmount); // -5 * 5
    }

    [Fact]
    public async Task StartCount_NonPlannedShouldThrow()
    {
        var svc = CreateService(out var db);
        await SeedStockAsync(db, "P001", "L1", "LOC1", 100m);
        var no = await svc.CreatePlanAsync(new StockTakePlanDto { TargetWarehouseCd = "W01" }, "u1");
        await svc.StartCountAsync(no, "u1");
        // 2 回目はエラー
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.StartCountAsync(no, "u1"));
    }

    // ═════════ 差異確認移行 ═════════

    [Fact]
    public async Task Submit_WithUncountedLines_ShouldThrow()
    {
        var svc = CreateService(out var db);
        await SeedStockAsync(db, "P001", "L1", "LOC1", 100m);
        await SeedStockAsync(db, "P002", "L1", "LOC2", 50m);
        var no = await svc.CreatePlanAsync(new StockTakePlanDto { TargetWarehouseCd = "W01" }, "u1");
        await svc.StartCountAsync(no, "u1");

        // 行 1 のみカウント
        await svc.UpdateCountsAsync(no, new()
        {
            new() { LineNo = 1, CountedQty = 100m }
        }, "u1");

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SubmitForReviewAsync(no, "u1"));
    }

    [Fact]
    public async Task Submit_DiffWithoutReason_ShouldThrow()
    {
        var svc = CreateService(out var db);
        await SeedStockAsync(db, "P001", "L1", "LOC1", 100m, 5m);
        var no = await svc.CreatePlanAsync(new StockTakePlanDto { TargetWarehouseCd = "W01" }, "u1");
        await svc.StartCountAsync(no, "u1");

        // 差異あり、理由なし
        await svc.UpdateCountsAsync(no, new()
        {
            new() { LineNo = 1, CountedQty = 90m }
        }, "u1");

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SubmitForReviewAsync(no, "u1"));
    }

    [Fact]
    public async Task Submit_ZeroDiff_ShouldAutoApprove()
    {
        var svc = CreateService(out var db);
        await SeedStockAsync(db, "P001", "L1", "LOC1", 100m);
        var no = await svc.CreatePlanAsync(new StockTakePlanDto { TargetWarehouseCd = "W01" }, "u1");
        await svc.StartCountAsync(no, "u1");
        await svc.UpdateCountsAsync(no, new()
        {
            new() { LineNo = 1, CountedQty = 100m }
        }, "u1");

        await svc.SubmitForReviewAsync(no, "u1");

        var d = await db.StockTakeDetails.SingleAsync();
        Assert.Equal(StockTakeDetailApproval.AutoApproved, d.ApprovalStatus);
        var h = await db.StockTakes.SingleAsync();
        Assert.Equal(StockTakeStatus.DiffReview, h.Status);
    }

    [Fact]
    public async Task Submit_OverThreshold_ShouldGoAwaitingApproval()
    {
        var svc = CreateService(out var db);
        await SeedStockAsync(db, "P001", "L1", "LOC1", 100m, 10m); // 単価 10
        var no = await svc.CreatePlanAsync(new StockTakePlanDto
        {
            TargetWarehouseCd = "W01",
            ApprovalThresholdAmount = 50m, // 閾値 50 円
        }, "u1");
        await svc.StartCountAsync(no, "u1");

        // 差異 -10 × 10 = -100 → 閾値超
        await svc.UpdateCountsAsync(no, new()
        {
            new() { LineNo = 1, CountedQty = 90m, DiffReasonCd = "DAMAGE" }
        }, "u1");

        await svc.SubmitForReviewAsync(no, "u1");
        var h = await db.StockTakes.SingleAsync();
        Assert.Equal(StockTakeStatus.AwaitingApproval, h.Status);
    }

    // ═════════ 承認 + ADJ 反映 ═════════

    [Fact]
    public async Task Approve_ShouldIssueAdjAndUpdateStock()
    {
        var svc = CreateService(out var db);
        await SeedStockAsync(db, "P001", "L1", "LOC1", 100m, 5m);
        var no = await svc.CreatePlanAsync(new StockTakePlanDto { TargetWarehouseCd = "W01" }, "u1");
        await svc.StartCountAsync(no, "u1");
        // 差異 -5
        await svc.UpdateCountsAsync(no, new()
        {
            new() { LineNo = 1, CountedQty = 95m, DiffReasonCd = "BREAK" }
        }, "u1");
        await svc.SubmitForReviewAsync(no, "u1");
        await svc.ApproveAndApplyAsync(no, "u1");

        // Stock 物理在庫が 95 に更新されたか
        var stock = await db.Stocks.SingleAsync(s => s.ProductCd == "P001");
        Assert.Equal(95m, stock.PhysicalQty);
        // ADJ トランザクション発行確認
        var adj = await db.StockTransactions.SingleAsync(t => t.TxnType == WmsTxnType.ADJ);
        Assert.Equal(-5m, adj.Qty);
        Assert.Equal(no, adj.RelatedNo);
        Assert.Equal("STOCKTAKE", adj.RelatedType);
        // 明細に ADJ TxnNo がリンク
        var d = await db.StockTakeDetails.SingleAsync();
        Assert.Equal(adj.TxnNo, d.AdjustTxnNo);
        Assert.Equal(StockTakeDetailApproval.Approved, d.ApprovalStatus);
        // ヘッダ完了
        var h = await db.StockTakes.SingleAsync();
        Assert.Equal(StockTakeStatus.Completed, h.Status);
    }

    [Fact]
    public async Task Approve_NoDiff_ShouldNotIssueAdj()
    {
        var svc = CreateService(out var db);
        await SeedStockAsync(db, "P001", "L1", "LOC1", 100m);
        var no = await svc.CreatePlanAsync(new StockTakePlanDto { TargetWarehouseCd = "W01" }, "u1");
        await svc.StartCountAsync(no, "u1");
        await svc.UpdateCountsAsync(no, new() { new() { LineNo = 1, CountedQty = 100m } }, "u1");
        await svc.SubmitForReviewAsync(no, "u1");
        await svc.ApproveAndApplyAsync(no, "u1");

        // ADJ は発行されない（IN の 1 件のみ）
        Assert.Equal(1, await db.StockTransactions.CountAsync());
        Assert.Equal(100m, (await db.Stocks.SingleAsync()).PhysicalQty);
    }

    // ═════════ 取消 ═════════

    [Fact]
    public async Task Cancel_ShouldSetStatus9()
    {
        var svc = CreateService(out var db);
        await SeedStockAsync(db, "P001", "L1", "LOC1", 100m);
        var no = await svc.CreatePlanAsync(new StockTakePlanDto { TargetWarehouseCd = "W01" }, "u1");
        await svc.CancelAsync(no, "u1");
        Assert.Equal(StockTakeStatus.Cancelled, (await db.StockTakes.SingleAsync()).Status);
    }

    [Fact]
    public async Task Cancel_Completed_ShouldThrow()
    {
        var svc = CreateService(out var db);
        await SeedStockAsync(db, "P001", "L1", "LOC1", 100m);
        var no = await svc.CreatePlanAsync(new StockTakePlanDto { TargetWarehouseCd = "W01" }, "u1");
        await svc.StartCountAsync(no, "u1");
        await svc.UpdateCountsAsync(no, new() { new() { LineNo = 1, CountedQty = 100m } }, "u1");
        await svc.SubmitForReviewAsync(no, "u1");
        await svc.ApproveAndApplyAsync(no, "u1");

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CancelAsync(no, "u1"));
    }
}
