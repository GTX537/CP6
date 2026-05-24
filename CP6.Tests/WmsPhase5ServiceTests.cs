using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

/// <summary>
/// WMS Phase 5 (MSBBWM100/150/170) 単体テスト
///
/// テスト観点：
/// FEFO 期限管理：
///   1. GetExpiringStocks — 残日数昇順、閾値内のみ
///   2. Dispose — ADJ -PhysicalQty で在庫除去
/// QC 入荷検品：
///   3. CreateFromInbound — 入庫予定の明細スナップショット
///   4. SaveItems — status 0→1 自動遷移
///   5. Judge PASS — InboundReceipt 自動生成、在庫反映
///   6. Judge FAIL — 在庫反映なし
/// RMA 返品：
///   7. Create — status=1 自動発行
///   8. Receive — 各明細 IN 発行、status 1→2
///   9. JudgeAndDispose RESELL — MOVE で振分先へ、status 2→4
///  10. JudgeAndDispose SCRAP — ADJ で除去
///  11. Close — status 4→5
/// </summary>
public class WmsPhase5ServiceTests
{
    private static (CP6.Core.EFDbContext.CP6Context db, WmsSequenceService seq, StockMovementService stock)
        CreateContext(bool allowNegative = false)
    {
        var db = TestHelper.CreateInMemoryContext();
        db.Warehouses.Add(new Warehouse
        {
            WarehouseCd = "W01", WarehouseName = "メイン", WarehouseType = WarehouseType.RawMaterial,
            AllowNegative = allowNegative,
        });
        db.SaveChanges();
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        return (db, seq, stock);
    }

    // ═════════ FEFO ═════════

    [Fact]
    public async Task Expiry_GetExpiring_ShouldReturnInWindowSortedByDays()
    {
        var (db, _, stock) = CreateContext();
        // 3 ロット：5日後、20日後、40日後
        await SeedStock(stock, "P1", "LOT_FAR", "L01", 100m, expiry: DateTime.Today.AddDays(40));
        await SeedStock(stock, "P1", "LOT_SOON", "L02", 50m, expiry: DateTime.Today.AddDays(5));
        await SeedStock(stock, "P1", "LOT_MID", "L03", 30m, expiry: DateTime.Today.AddDays(20));

        var svc = new ExpiryService(db, stock);
        var result = await svc.GetExpiringStocksAsync(days: 30);

        Assert.Equal(2, result.Count); // 40日後は閾値外
        Assert.Equal("LOT_SOON", result[0].LotNo); // 残日数昇順
        Assert.Equal("LOT_MID", result[1].LotNo);
        Assert.Equal(5, result[0].DaysUntilExpiry);
    }

    [Fact]
    public async Task Expiry_Dispose_ShouldRemoveStock()
    {
        var (db, _, stock) = CreateContext();
        await SeedStock(stock, "P1", "LOT_OLD", "L01", 100m, expiry: DateTime.Today.AddDays(-5));
        var s = await db.Stocks.SingleAsync();

        var svc = new ExpiryService(db, stock);
        var n = await svc.DisposeAsync(new DisposeRequest { StockIds = new() { s.Id } }, "u");

        Assert.Equal(1, n);
        var afterStock = await db.Stocks.SingleAsync();
        Assert.Equal(0m, afterStock.PhysicalQty);
        // ADJ トランザクションが追加されている
        Assert.True(db.StockTransactions.Any(t => t.TxnType == WmsTxnType.ADJ && t.Qty == -100m));
    }

    // ═════════ QC 検品 ═════════

    [Fact]
    public async Task Qc_CreateFromInbound_ShouldSnapshotDetails()
    {
        var (db, seq, stock) = CreateContext();
        var inboundSvc = new InboundService(db, seq, stock);
        var inNo = await inboundSvc.CreateOrderAsync(new InboundOrderDto
        {
            WarehouseCd = "W01", InboundType = 1,
            SupplierCd = "SUP1", SupplierName = "テスト",
            ExpectedArrivalDate = DateTime.Today,
            Details = new()
            {
                new() { ProductCd = "P1", ExpectedQty = 100m, UnitCd = "EA" },
                new() { ProductCd = "P2", ExpectedQty = 50m, UnitCd = "EA" },
            }
        }, "u");
        await inboundSvc.ConfirmOrderAsync(inNo, "u");

        var qcSvc = new QcInspectionService(db, seq, inboundSvc);
        var qcNo = await qcSvc.CreateFromInboundAsync(inNo, "u");

        Assert.StartsWith("QC", qcNo);
        var inspection = await qcSvc.GetAsync(qcNo);
        Assert.NotNull(inspection);
        Assert.Equal(QcInspectionStatus.Created, inspection!.Status);
        Assert.Equal(2, inspection.Items.Count);
        Assert.Equal(100m, inspection.Items[0].ExpectedQty);
        Assert.Equal("SUP1", inspection.SupplierCd);
    }

    [Fact]
    public async Task Qc_SaveItems_ShouldAutoAdvanceToInspecting()
    {
        var (db, seq, stock) = CreateContext();
        var inboundSvc = new InboundService(db, seq, stock);
        var inNo = await CreateInbound(inboundSvc, "P1", 100m);

        var qcSvc = new QcInspectionService(db, seq, inboundSvc);
        var qcNo = await qcSvc.CreateFromInboundAsync(inNo, "u");

        await qcSvc.SaveItemsAsync(qcNo, new List<QcInspectionItemDto>
        {
            new() { LineNo = 1, ProductCd = "P1", ExpectedQty = 100m, ReceivedQty = 100m, AcceptedQty = 95m, RejectedQty = 5m }
        }, "u");

        var h = await db.QcInspections.SingleAsync();
        Assert.Equal(QcInspectionStatus.Inspecting, h.Status); // 0→1 自動
        var item = await db.QcInspectionItems.SingleAsync();
        Assert.Equal(95m, item.AcceptedQty);
    }

    [Fact]
    public async Task Qc_JudgePass_ShouldAutoCreateReceiptAndApplyStock()
    {
        var (db, seq, stock) = CreateContext();
        var inboundSvc = new InboundService(db, seq, stock);
        var inNo = await CreateInbound(inboundSvc, "P1", 100m);

        var qcSvc = new QcInspectionService(db, seq, inboundSvc);
        var qcNo = await qcSvc.CreateFromInboundAsync(inNo, "u");
        await qcSvc.SaveItemsAsync(qcNo, new List<QcInspectionItemDto>
        {
            new() { LineNo = 1, ProductCd = "P1", ExpectedQty = 100m, ReceivedQty = 100m, AcceptedQty = 90m, RejectedQty = 10m }
        }, "u");

        var result = await qcSvc.JudgeAsync(qcNo, new JudgeRequest
        {
            FinalJudgement = QcInspectionJudgement.Pass,
            Reason = "外見良好",
            AcceptLocations = new() { "L01" },
        }, "u");

        Assert.Equal(QcInspectionJudgement.Pass, result.FinalJudgement);
        Assert.NotNull(result.GeneratedReceiptNo);
        Assert.StartsWith("RC", result.GeneratedReceiptNo);

        // 在庫が 90 計上された
        var s = await db.Stocks.SingleAsync(x => x.ProductCd == "P1");
        Assert.Equal(90m, s.PhysicalQty);
        // 検品 status=2 + GeneratedReceiptNo セット
        var h = await db.QcInspections.SingleAsync();
        Assert.Equal(QcInspectionStatus.Judged, h.Status);
        Assert.Equal(result.GeneratedReceiptNo, h.GeneratedReceiptNo);
    }

    [Fact]
    public async Task Qc_JudgeFail_ShouldNotCreateReceipt()
    {
        var (db, seq, stock) = CreateContext();
        var inboundSvc = new InboundService(db, seq, stock);
        var inNo = await CreateInbound(inboundSvc, "P1", 100m);

        var qcSvc = new QcInspectionService(db, seq, inboundSvc);
        var qcNo = await qcSvc.CreateFromInboundAsync(inNo, "u");
        await qcSvc.SaveItemsAsync(qcNo, new List<QcInspectionItemDto>
        {
            new() { LineNo = 1, ProductCd = "P1", ExpectedQty = 100m, ReceivedQty = 100m, AcceptedQty = 0m, RejectedQty = 100m }
        }, "u");

        var result = await qcSvc.JudgeAsync(qcNo, new JudgeRequest
        {
            FinalJudgement = QcInspectionJudgement.Fail,
            Reason = "全数不良",
        }, "u");

        Assert.Null(result.GeneratedReceiptNo);
        Assert.False(db.Stocks.Any(x => x.ProductCd == "P1"));
    }

    // ═════════ RMA ═════════

    [Fact]
    public async Task Rma_Create_ShouldAutoAuthorize()
    {
        var (db, seq, stock) = CreateContext();
        var svc = new RmaService(db, seq, stock);

        var no = await svc.CreateAsync(new RmaDto
        {
            CustomerCd = "C1", CustomerName = "客先A",
            WarehouseCd = "W01", ReturnReason = "外見不良",
            Details = new() { new() { ProductCd = "P1", Qty = 5m, ConditionLevel = RmaCondition.Open } }
        }, "u");

        Assert.StartsWith("RMA", no);
        var h = await db.RmaHeaders.SingleAsync();
        Assert.Equal(RmaStatus.Authorized, h.Status);
    }

    [Fact]
    public async Task Rma_Receive_ShouldEmitInTransactionAndMoveStatus()
    {
        var (db, seq, stock) = CreateContext();
        var svc = new RmaService(db, seq, stock);
        var no = await svc.CreateAsync(new RmaDto
        {
            CustomerCd = "C1", WarehouseCd = "W01",
            Details = new() { new() { ProductCd = "P1", LotNo = "OLD_LOT", Qty = 5m } }
        }, "u");

        await svc.ReceiveAsync(no, "u");

        var h = await db.RmaHeaders.SingleAsync();
        Assert.Equal(RmaStatus.Received, h.Status);
        // 保留ロケに在庫 5 が IN された
        var s = await db.Stocks.SingleAsync();
        Assert.Equal(5m, s.PhysicalQty);
        Assert.Equal("W01-RMA-HOLD", s.LocationCd);
        // 明細に InboundTxnNo 反映
        var d = await db.RmaDetails.SingleAsync();
        Assert.NotNull(d.InboundTxnNo);
    }

    [Fact]
    public async Task Rma_JudgeResell_ShouldMoveToDestLocation()
    {
        var (db, seq, stock) = CreateContext();
        var svc = new RmaService(db, seq, stock);
        var no = await svc.CreateAsync(new RmaDto
        {
            CustomerCd = "C1", WarehouseCd = "W01",
            Details = new() { new() { ProductCd = "P1", LotNo = "L1", Qty = 5m } }
        }, "u");
        await svc.ReceiveAsync(no, "u");
        await svc.StartInspectionAsync(no, "u");

        await svc.JudgeAndDisposeAsync(no, new List<RmaDispositionInput>
        {
            new() { LineNo = 1, Judgement = RmaJudgement.Resell, DestLocationCd = "RESELL-A1" }
        }, "u");

        var h = await db.RmaHeaders.SingleAsync();
        Assert.Equal(RmaStatus.Judged, h.Status);
        // 保留→RESELL-A1 へ移動：在庫は 2 件
        var stocks = await db.Stocks.OrderBy(s => s.LocationCd).ToListAsync();
        Assert.Equal(2, stocks.Count);
        Assert.Equal(0m, stocks.Single(s => s.LocationCd == "W01-RMA-HOLD").PhysicalQty);
        Assert.Equal(5m, stocks.Single(s => s.LocationCd == "RESELL-A1").PhysicalQty);
    }

    [Fact]
    public async Task Rma_JudgeScrap_ShouldRemoveStock()
    {
        var (db, seq, stock) = CreateContext();
        var svc = new RmaService(db, seq, stock);
        var no = await svc.CreateAsync(new RmaDto
        {
            CustomerCd = "C1", WarehouseCd = "W01",
            Details = new() { new() { ProductCd = "P1", LotNo = "L1", Qty = 3m } }
        }, "u");
        await svc.ReceiveAsync(no, "u");

        await svc.JudgeAndDisposeAsync(no, new List<RmaDispositionInput>
        {
            new() { LineNo = 1, Judgement = RmaJudgement.Scrap }
        }, "u");

        var s = await db.Stocks.SingleAsync();
        Assert.Equal(0m, s.PhysicalQty); // 廃棄で 0
        Assert.True(db.StockTransactions.Any(t => t.TxnType == WmsTxnType.ADJ && t.Qty == -3m));
    }

    [Fact]
    public async Task Rma_Close_ShouldMoveTo5()
    {
        var (db, seq, stock) = CreateContext();
        var svc = new RmaService(db, seq, stock);
        var no = await svc.CreateAsync(new RmaDto
        {
            CustomerCd = "C1", WarehouseCd = "W01",
            Details = new() { new() { ProductCd = "P1", LotNo = "L1", Qty = 2m } }
        }, "u");
        await svc.ReceiveAsync(no, "u");
        await svc.JudgeAndDisposeAsync(no, new List<RmaDispositionInput>
        {
            new() { LineNo = 1, Judgement = RmaJudgement.Scrap }
        }, "u");
        await svc.CloseAsync(no, "u");

        var h = await db.RmaHeaders.SingleAsync();
        Assert.Equal(RmaStatus.Closed, h.Status);
    }

    // ─── helpers ───
    private static async Task SeedStock(StockMovementService stock, string prod, string lot, string loc, decimal qty, DateTime? expiry = null)
    {
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN,
            WarehouseCd = "W01", LocationCd = loc, ProductCd = prod, LotNo = lot, Qty = qty,
            ExpiryDate = expiry,
        });
    }

    private static async Task<string> CreateInbound(InboundService svc, string product, decimal qty)
    {
        var no = await svc.CreateOrderAsync(new InboundOrderDto
        {
            WarehouseCd = "W01", InboundType = 1,
            SupplierCd = "SUP1", SupplierName = "テスト",
            ExpectedArrivalDate = DateTime.Today,
            Details = new() { new() { ProductCd = product, ExpectedQty = qty, UnitCd = "EA" } }
        }, "u");
        await svc.ConfirmOrderAsync(no, "u");
        return no;
    }
}
