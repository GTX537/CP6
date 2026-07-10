using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Wms;

/// <summary>
/// 棚卸 承認→在庫調整 の「账实一致（帳簿=実在庫）」回帰テスト（M-WMS 横切 T7 補網）。
///
/// 既存 <see cref="CP6.Tests.StockTakeServiceTests"/> は マイナス差異 1 行のみ検証済み。
/// 本ファイルは未カバーの不変式を補う：
///  ① 盘盈（実盘 > 帳簿）で 正の ADJ が発行され Stock が増える。
///  ② 複数明細（過不足混在）で 各 Stock が正確に CountedQty へ収束し、
///     ADJ 台帳の符号付き合計 = 総差異（保存則）。
/// 棚卸の本質＝承認後は必ず PhysicalQty == CountedQty になること。
/// </summary>
public class StockTakeReconcileTests
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

    // ═════════ ① 盘盈（正の差異） ═════════

    [Fact]
    public async Task Approve_PositiveDiff_ShouldRaiseStockAndIssuePositiveAdj()
    {
        var svc = CreateService(out var db);
        // 帳簿 100、実盘 103 → +3 の盘盈
        await SeedStockAsync(db, "P001", "L1", "LOC1", 100m, 5m);
        var no = await svc.CreatePlanAsync(new StockTakePlanDto { TargetWarehouseCd = "W01" }, "u1");
        await svc.StartCountAsync(no, "u1");
        await svc.UpdateCountsAsync(no, new()
        {
            new() { LineNo = 1, CountedQty = 103m, DiffReasonCd = "FOUND" }
        }, "u1");
        await svc.SubmitForReviewAsync(no, "u1");
        await svc.ApproveAndApplyAsync(no, "u1");

        // 帳簿=実在庫：承認後 PhysicalQty は実盘 103 に一致
        var stock = await db.Stocks.SingleAsync(s => s.ProductCd == "P001");
        Assert.Equal(103m, stock.PhysicalQty);
        Assert.Equal(103m, stock.AvailableQty); // 引当なし

        // 正の ADJ 台帳が発行される（盘盈 = IN 効果）
        var adj = await db.StockTransactions.SingleAsync(t => t.TxnType == WmsTxnType.ADJ);
        Assert.Equal(3m, adj.Qty);
        Assert.Equal(no, adj.RelatedNo);
        Assert.Equal("STOCKTAKE", adj.RelatedType);

        var h = await db.StockTakes.SingleAsync();
        Assert.Equal(StockTakeStatus.Completed, h.Status);
    }

    // ═════════ ② 複数明細（過不足混在）の保存則 ═════════

    [Fact]
    public async Task Approve_MixedMultiLine_ShouldReconcileEachStockToCountedQty()
    {
        var svc = CreateService(out var db);
        // P001: 帳簿 100 → 実盘 97（-3 の棚卸減）
        // P002: 帳簿 50  → 実盘 55（+5 の棚卸増）
        await SeedStockAsync(db, "P001", "L1", "LOC1", 100m, 10m);
        await SeedStockAsync(db, "P002", "L1", "LOC2", 50m, 20m);

        var no = await svc.CreatePlanAsync(new StockTakePlanDto { TargetWarehouseCd = "W01" }, "u1");
        await svc.StartCountAsync(no, "u1");
        await svc.UpdateCountsAsync(no, new()
        {
            new() { LineNo = 1, CountedQty = 97m, DiffReasonCd = "SHRINK" },
            new() { LineNo = 2, CountedQty = 55m, DiffReasonCd = "FOUND" },
        }, "u1");
        await svc.SubmitForReviewAsync(no, "u1");
        await svc.ApproveAndApplyAsync(no, "u1");

        // 各 Stock が正確に実盘値へ収束（账实一致）
        var p1 = await db.Stocks.SingleAsync(s => s.ProductCd == "P001");
        var p2 = await db.Stocks.SingleAsync(s => s.ProductCd == "P002");
        Assert.Equal(97m, p1.PhysicalQty);
        Assert.Equal(55m, p2.PhysicalQty);

        // ADJ 台帳：符号付きで 2 件、合計 = 総差異（-3 + 5 = +2）保存則
        var adjs = await db.StockTransactions
            .Where(t => t.TxnType == WmsTxnType.ADJ && t.RelatedNo == no)
            .ToListAsync();
        Assert.Equal(2, adjs.Count);
        Assert.Equal(2m, adjs.Sum(t => t.Qty));
        Assert.Contains(adjs, t => t.ProductCd == "P001" && t.Qty == -3m);
        Assert.Contains(adjs, t => t.ProductCd == "P002" && t.Qty == 5m);

        // 全明細が承認済 + ADJ TxnNo リンク
        var details = await db.StockTakeDetails.OrderBy(d => d.LineNo).ToListAsync();
        Assert.All(details, d => Assert.Equal(StockTakeDetailApproval.Approved, d.ApprovalStatus));
        Assert.All(details, d => Assert.False(string.IsNullOrEmpty(d.AdjustTxnNo)));
    }
}
