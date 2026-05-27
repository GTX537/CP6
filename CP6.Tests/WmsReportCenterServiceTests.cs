using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

/// <summary>
/// 帳票センター（MSBBWM900）単体テスト
///
/// テスト観点：
/// 1. MonthlyStockReport 月末集計（IN - OUT）
/// 2. AbcAnalysis 80/15/5 ランク付け
/// 3. DeadStock 90日無動の抽出
/// 4. InboundHistory / OutboundHistory 期間フィルタ
/// 5. CSV エクスポート（UTF-8 BOM + ヘッダ + データ + エスケープ）
/// 6. yearMonth フォーマット異常検出
/// </summary>
public class WmsReportCenterServiceTests
{
    private static (CP6.Core.EFDbContext.CP6Context db, StockMovementService stock) Create()
    {
        var db = TestHelper.CreateInMemoryContext();
        db.Warehouses.Add(new Warehouse { WarehouseCd = "W01", WarehouseName = "M" });
        db.SaveChanges();
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        return (db, stock);
    }

    [Fact]
    public async Task MonthlyStockReport_ShouldAggregateInOutToEndOfMonth()
    {
        var (db, stock) = Create();
        // 2026/05/15 IN 100
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01", LocationCd = "L01",
            ProductCd = "P1", LotNo = "L", Qty = 100, OperatorCd = "U",
        });
        // 強制的に TxnDateTime を月初に書換（in-memory）
        var t1 = await db.StockTransactions.SingleAsync();
        t1.TxnDateTime = new DateTime(2026, 5, 15);
        // 2026/05/20 OUT 30
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.OUT, WarehouseCd = "W01", LocationCd = "L01",
            ProductCd = "P1", LotNo = "L", Qty = 30, OperatorCd = "U",
        });
        var t2 = await db.StockTransactions.Skip(1).FirstAsync();
        t2.TxnDateTime = new DateTime(2026, 5, 20);
        await db.SaveChangesAsync();

        var svc = new ReportCenterService(db);
        var rows = await svc.MonthlyStockReportAsync("2026-05");
        Assert.Single(rows);
        Assert.Equal("P1", rows[0].ProductCd);
        Assert.Equal(70m, rows[0].PhysicalQty); // 100 - 30
        Assert.Equal(1, rows[0].LotCount);
    }

    [Fact]
    public async Task MonthlyStockReport_InvalidYearMonth_ShouldThrow()
    {
        var (db, _) = Create();
        var svc = new ReportCenterService(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.MonthlyStockReportAsync("bad"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.MonthlyStockReportAsync("2026-13"));
    }

    [Fact]
    public async Task AbcAnalysis_ShouldRankByCumulativeRatio()
    {
        var (db, stock) = Create();
        // 受入：3 SKU
        foreach (var p in new[] { "PA", "PB", "PC" })
        {
            await stock.ApplyAsync(new StockMovementRequest
            {
                TxnType = WmsTxnType.IN, WarehouseCd = "W01", LocationCd = "L",
                ProductCd = p, LotNo = "L", Qty = 1000, OperatorCd = "U",
            });
        }
        // 出庫：PA 80, PB 15, PC 5（％）
        await stock.ApplyAsync(new StockMovementRequest { TxnType = WmsTxnType.OUT, WarehouseCd = "W01", LocationCd = "L", ProductCd = "PA", LotNo = "L", Qty = 800, OperatorCd = "U" });
        await stock.ApplyAsync(new StockMovementRequest { TxnType = WmsTxnType.OUT, WarehouseCd = "W01", LocationCd = "L", ProductCd = "PB", LotNo = "L", Qty = 150, OperatorCd = "U" });
        await stock.ApplyAsync(new StockMovementRequest { TxnType = WmsTxnType.OUT, WarehouseCd = "W01", LocationCd = "L", ProductCd = "PC", LotNo = "L", Qty = 50, OperatorCd = "U" });

        var svc = new ReportCenterService(db);
        var rows = await svc.AbcAnalysisAsync(30);
        Assert.Equal(3, rows.Count);
        Assert.Equal("PA", rows[0].ProductCd); Assert.Equal("A", rows[0].AbcRank);
        Assert.Equal("PB", rows[1].ProductCd); Assert.Equal("B", rows[1].AbcRank);
        Assert.Equal("PC", rows[2].ProductCd); Assert.Equal("C", rows[2].AbcRank);
    }

    [Fact]
    public async Task AbcAnalysis_NoData_ReturnsEmpty()
    {
        var (db, _) = Create();
        var svc = new ReportCenterService(db);
        var rows = await svc.AbcAnalysisAsync(30);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task DeadStock_ShouldDetectIdleStock()
    {
        var (db, stock) = Create();
        // 古い在庫
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01", LocationCd = "L01",
            ProductCd = "OLD", LotNo = "L", Qty = 50, OperatorCd = "U",
        });
        var oldStock = await db.Stocks.SingleAsync(s => s.ProductCd == "OLD");
        oldStock.ReceiveDate = DateTime.Today.AddDays(-120);
        // 新しい在庫
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01", LocationCd = "L02",
            ProductCd = "NEW", LotNo = "L", Qty = 50, OperatorCd = "U",
        });
        var newStock = await db.Stocks.SingleAsync(s => s.ProductCd == "NEW");
        newStock.ReceiveDate = DateTime.Today.AddDays(-10);
        await db.SaveChangesAsync();

        var svc = new ReportCenterService(db);
        var rows = await svc.DeadStockAsync(90);
        Assert.Single(rows);
        Assert.Equal("OLD", rows[0].ProductCd);
        Assert.True(rows[0].IdleDays >= 90);
    }

    [Fact]
    public async Task InboundOutboundHistory_ShouldFilterByDate()
    {
        var (db, stock) = Create();
        await stock.ApplyAsync(new StockMovementRequest { TxnType = WmsTxnType.IN, WarehouseCd = "W01", LocationCd = "L", ProductCd = "P1", LotNo = "L", Qty = 100, OperatorCd = "U" });
        var t = await db.StockTransactions.SingleAsync();
        t.TxnDateTime = new DateTime(2026, 5, 10);
        await db.SaveChangesAsync();

        var svc = new ReportCenterService(db);
        var inHist = await svc.InboundHistoryAsync(new DateTime(2026, 5, 1), new DateTime(2026, 5, 31));
        Assert.Single(inHist);
        Assert.Equal("P1", inHist[0].ProductCd);

        // 範囲外
        var inHist2 = await svc.InboundHistoryAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 30));
        Assert.Empty(inHist2);

        // OUT 履歴は空
        var outHist = await svc.OutboundHistoryAsync(new DateTime(2026, 5, 1), new DateTime(2026, 5, 31));
        Assert.Empty(outHist);
    }

    [Fact]
    public void ExportCsv_ShouldProduceUtf8BomHeaderAndRows()
    {
        var db = TestHelper.CreateInMemoryContext();
        var svc = new ReportCenterService(db);
        var rows = new[]
        {
            new AbcAnalysisRow { ProductCd = "PA", OutCount = 5, OutQty = 100m, CumulativeRatio = 80m, AbcRank = "A" },
            new AbcAnalysisRow { ProductCd = "PB,X", OutCount = 2, OutQty = 25m, CumulativeRatio = 100m, AbcRank = "B" }, // カンマエスケープ
        };
        var bytes = svc.ExportCsv(rows);
        Assert.Equal(0xEF, bytes[0]); Assert.Equal(0xBB, bytes[1]); Assert.Equal(0xBF, bytes[2]); // BOM
        var text = System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.Contains("ProductCd,OutCount,OutQty,CumulativeRatio,AbcRank", text);
        Assert.Contains("PA,5,100,80,A", text);
        Assert.Contains("\"PB,X\",2,25,100,B", text);
    }
}
