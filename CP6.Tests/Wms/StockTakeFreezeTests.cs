using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace CP6.Tests.Wms;

/// <summary>
/// F1 財務油路 波F — 棚卸開始で所涉ロケーションを凍結（出入庫拒否）。
///
/// StockTakeService.cs 自認「棚卸中フラグ未実装」の補装。過账基线正确性の前置：
///  ① 進行中の棚卸（Counting/DiffReview/AwaitingApproval）に覆われたロケーションでは
///     物理入出庫（IN/OUT/MOVE）を <c>WM-MSG-304</c> で拒否。
///  ② 棚卸承認自身の差異調整（ADJ）は放行（盘盈亏凭证照旧）。
///  ③ 承認完了 or 取消で自然解凍（活性单クエリが空 → 出庫可）。
///  ④ AllocateAsync は凍結ロケーションを引当候補から除外。
///  ⑤ 完工反冲（OUT/ISSUE）が凍結ロケーションに撞る → 報工成功＋反冲被拒＋成本归集跳过（C.2 闸1）。
///
/// 凍結粒度 = StockTakeDetail（倉庫＋ロケーション）。全て真链（真 StockMovement/StockTake/Outbound/
/// Backflush/ProductionResult/FinBridge + InMemory）。
/// </summary>
public class StockTakeFreezeTests
{
    // ProductionResultService.WriteAsync は明示トランザクションを使うため InMemory 警告を抑止。
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    private static CP6Context NewDbWithWarehouse()
    {
        var db = TestHelper.CreateInMemoryContext();
        db.Warehouses.Add(new Warehouse
        {
            WarehouseCd = "W01", WarehouseName = "メイン倉庫",
            WarehouseType = WarehouseType.RawMaterial, AllowNegative = false,
        });
        db.SaveChanges();
        return db;
    }

    private static async Task SeedStockInAsync(StockMovementService stock,
        string product, string lot, string location, decimal qty, decimal? unitPrice = null)
    {
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01",
            LocationCd = location, ProductCd = product, LotNo = lot,
            Qty = qty, UnitPrice = unitPrice,
        });
    }

    /// <summary>W01 全域の棚卸を作成し Counting へ（＝所涉ロケーションを凍結）。棚卸番号を返す。</summary>
    private static async Task<string> FreezeWarehouseAsync(CP6Context db, StockMovementService stock)
    {
        var seq = new WmsSequenceService(db);
        var take = new StockTakeService(db, seq, stock);
        var no = await take.CreatePlanAsync(new StockTakePlanDto { TargetWarehouseCd = "W01" }, "u1");
        await take.StartCountAsync(no, "u1");
        return no;
    }

    // ═════════ ① IN/OUT/MOVE 拒否（WM-MSG-304） ═════════

    [Fact]
    public async Task Frozen_Out_Rejected_WithMsg304()
    {
        using var db = NewDbWithWarehouse();
        var stock = new StockMovementService(db, new WmsSequenceService(db));
        await SeedStockInAsync(stock, "P001", "L1", "LOC1", 100m, 5m);
        await FreezeWarehouseAsync(db, stock);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.OUT, WarehouseCd = "W01", LocationCd = "LOC1",
            ProductCd = "P001", LotNo = "L1", Qty = 10m,
        }));
        Assert.Contains("WM-MSG-304", ex.Message);

        // 出庫は弾かれ在庫は不変（100 のまま）
        var s = await db.Stocks.SingleAsync(x => x.ProductCd == "P001");
        Assert.Equal(100m, s.PhysicalQty);
    }

    [Fact]
    public async Task Frozen_In_Rejected_WithMsg304()
    {
        using var db = NewDbWithWarehouse();
        var stock = new StockMovementService(db, new WmsSequenceService(db));
        await SeedStockInAsync(stock, "P001", "L1", "LOC1", 100m, 5m);
        await FreezeWarehouseAsync(db, stock);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01", LocationCd = "LOC1",
            ProductCd = "P001", LotNo = "L1", Qty = 10m,
        }));
        Assert.Contains("WM-MSG-304", ex.Message);
    }

    [Fact]
    public async Task Frozen_Move_Rejected_WithMsg304()
    {
        using var db = NewDbWithWarehouse();
        var stock = new StockMovementService(db, new WmsSequenceService(db));
        await SeedStockInAsync(stock, "P001", "L1", "LOC1", 100m, 5m);
        await FreezeWarehouseAsync(db, stock);

        // MoveAsync は源 OUT を LOC1（凍結中）で発行 → WM-MSG-304 で弾かれる
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => stock.MoveAsync(new StockMoveRequest
        {
            WarehouseCd = "W01", FromLocationCd = "LOC1", ToLocationCd = "LOC2",
            ProductCd = "P001", LotNo = "L1", Qty = 10m,
        }));
        Assert.Contains("WM-MSG-304", ex.Message);
    }

    // ═════════ ② 棚卸承認の ADJ は放行（盘盈亏凭证照旧） ═════════

    [Fact]
    public async Task Frozen_ApproveApplyAdj_Allowed_StockConverges()
    {
        using var db = NewDbWithWarehouse();
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        var take = new StockTakeService(db, seq, stock);

        // 帳簿 100 → 実盘 97（-3 の棚卸減）。棚卸は全域凍結中に承認 ADJ を発行する。
        await SeedStockInAsync(stock, "P001", "L1", "LOC1", 100m, 5m);
        var no = await take.CreatePlanAsync(new StockTakePlanDto { TargetWarehouseCd = "W01" }, "u1");
        await take.StartCountAsync(no, "u1"); // ← ここで LOC1 凍結
        await take.UpdateCountsAsync(no, new() { new() { LineNo = 1, CountedQty = 97m, DiffReasonCd = "SHRINK" } }, "u1");
        await take.SubmitForReviewAsync(no, "u1"); // → DiffReview（依然凍結中）
        await take.ApproveAndApplyAsync(no, "u1");  // ← ADJ 放行、凍結中でも通る

        // 账实一致：ADJ で 97 に収束（盘亏凭证の基となる ADJ 台帳が発行される）
        var s = await db.Stocks.SingleAsync(x => x.ProductCd == "P001");
        Assert.Equal(97m, s.PhysicalQty);
        var adj = await db.StockTransactions.SingleAsync(t => t.TxnType == WmsTxnType.ADJ);
        Assert.Equal(-3m, adj.Qty);
        Assert.Equal("STOCKTAKE", adj.RelatedType);
    }

    // ── 豁免収窄（審査 Important）：ADJ 放行は RelatedType=="STOCKTAKE" のみ。
    //    効期核銷（EXPIRY_DISPOSE）等の他 ADJ は凍結中の快照 BookQty を狂わせるため同様に拒否。
    //    STOCKTAKE ADJ の放行側は上の Frozen_ApproveApplyAdj_Allowed_StockConverges が既に鎖定。──
    [Fact]
    public async Task Frozen_NonStockTakeAdj_Rejected_WithMsg304()
    {
        using var db = NewDbWithWarehouse();
        var stock = new StockMovementService(db, new WmsSequenceService(db));
        await SeedStockInAsync(stock, "P001", "L1", "LOC1", 100m, 5m);
        await FreezeWarehouseAsync(db, stock);

        // 効期核銷を模した ADJ（ExpiryService.cs と同型：ADJ + RelatedType=EXPIRY_DISPOSE）
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.ADJ, WarehouseCd = "W01", LocationCd = "LOC1",
            ProductCd = "P001", LotNo = "L1", Qty = -5m, RelatedType = "EXPIRY_DISPOSE",
        }));
        Assert.Contains("WM-MSG-304", ex.Message);

        // RelatedType 無指定の生 ADJ も同様に拒否
        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() => stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.ADJ, WarehouseCd = "W01", LocationCd = "LOC1",
            ProductCd = "P001", LotNo = "L1", Qty = -5m,
        }));
        Assert.Contains("WM-MSG-304", ex2.Message);

        // 在庫不変（快照 BookQty=100 が守られる）＋ ADJ 台帳ゼロ
        Assert.Equal(100m, (await db.Stocks.SingleAsync(x => x.ProductCd == "P001")).PhysicalQty);
        Assert.Equal(0, await db.StockTransactions.CountAsync(t => t.TxnType == WmsTxnType.ADJ));
    }

    // ── MoveAsync 双端予検（審査 Minor）：移動先のみ凍結でも腿1（源 OUT）発行前に拒否＝在庫宙吊りを防ぐ ──
    [Fact]
    public async Task Move_ToFrozenDestination_RejectedBeforeLeg1_NoPartialMove()
    {
        using var db = NewDbWithWarehouse();
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        var take = new StockTakeService(db, seq, stock);

        // LOC1（移動元、凍結なし）と LOC2（移動先）に在庫を積み、LOC2 のみ棚卸凍結
        await SeedStockInAsync(stock, "P001", "L1", "LOC1", 100m, 5m);
        await SeedStockInAsync(stock, "P002", "L2", "LOC2", 50m, 5m);
        var no = await take.CreatePlanAsync(new StockTakePlanDto
        {
            TargetWarehouseCd = "W01", TargetLocationPrefix = "LOC2",
        }, "u1");
        await take.StartCountAsync(no, "u1"); // ← LOC2 のみ凍結

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => stock.MoveAsync(new StockMoveRequest
        {
            WarehouseCd = "W01", FromLocationCd = "LOC1", ToLocationCd = "LOC2",
            ProductCd = "P001", LotNo = "L1", Qty = 10m,
        }));
        Assert.Contains("WM-MSG-304", ex.Message);

        // 腿1が発行されていない：源在庫不変 + MOVE 台帳ゼロ（宙吊りなし）
        Assert.Equal(100m, (await db.Stocks.SingleAsync(x => x.ProductCd == "P001")).PhysicalQty);
        Assert.Equal(0, await db.StockTransactions.CountAsync(t => t.TxnType == WmsTxnType.MOVE));
    }

    // ═════════ ③ 承認完了 / 取消で解凍 ═════════

    [Fact]
    public async Task AfterApprove_Unfrozen_OutAllowed()
    {
        using var db = NewDbWithWarehouse();
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        var take = new StockTakeService(db, seq, stock);

        await SeedStockInAsync(stock, "P001", "L1", "LOC1", 100m, 5m);
        var no = await take.CreatePlanAsync(new StockTakePlanDto { TargetWarehouseCd = "W01" }, "u1");
        await take.StartCountAsync(no, "u1");
        // 差異 0 で承認 → Completed（解凍）
        await take.UpdateCountsAsync(no, new() { new() { LineNo = 1, CountedQty = 100m } }, "u1");
        await take.SubmitForReviewAsync(no, "u1");
        await take.ApproveAndApplyAsync(no, "u1");
        Assert.Equal(StockTakeStatus.Completed, (await db.StockTakes.SingleAsync()).Status);

        // 解凍後は OUT 可
        var txnNo = await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.OUT, WarehouseCd = "W01", LocationCd = "LOC1",
            ProductCd = "P001", LotNo = "L1", Qty = 10m,
        });
        Assert.False(string.IsNullOrEmpty(txnNo));
        Assert.Equal(90m, (await db.Stocks.SingleAsync(x => x.ProductCd == "P001")).PhysicalQty);
    }

    [Fact]
    public async Task AfterCancel_Unfrozen_OutAllowed()
    {
        using var db = NewDbWithWarehouse();
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        var take = new StockTakeService(db, seq, stock);

        await SeedStockInAsync(stock, "P001", "L1", "LOC1", 100m, 5m);
        var no = await FreezeWarehouseAsync(db, stock);
        await take.CancelAsync(no, "u1"); // → Cancelled（解凍）
        Assert.Equal(StockTakeStatus.Cancelled, (await db.StockTakes.SingleAsync()).Status);

        var txnNo = await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.OUT, WarehouseCd = "W01", LocationCd = "LOC1",
            ProductCd = "P001", LotNo = "L1", Qty = 10m,
        });
        Assert.False(string.IsNullOrEmpty(txnNo));
    }

    // ═════════ ④ AllocateAsync は凍結ロケーションを引当候補から除外 ═════════

    [Fact]
    public async Task Allocate_ExcludesFrozenLocation_ThenRecoversAfterCancel()
    {
        using var db = NewDbWithWarehouse();
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        var take = new StockTakeService(db, seq, stock);
        var outbound = new OutboundService(db, seq, stock);

        await SeedStockInAsync(stock, "P001", "L1", "LOC1", 100m, 5m);
        var no = await FreezeWarehouseAsync(db, stock); // LOC1 凍結

        var outNo = await outbound.CreateOrderAsync(new OutboundOrderDto
        {
            OutboundType = OutboundType.Material, WarehouseCd = "W01", PlannedDate = DateTime.Today,
            Details = new() { new() { ProductCd = "P001", RequiredQty = 30m } },
        }, "u");
        await outbound.ConfirmOrderAsync(outNo, "u");

        // 凍結中は唯一の在庫が候補から除外 → 引当不能
        await Assert.ThrowsAsync<InsufficientStockException>(() => outbound.AllocateAsync(outNo, "u"));

        // 棚卸取消で解凍 → 同一在庫が引当できる
        await take.CancelAsync(no, "u1");
        await outbound.AllocateAsync(outNo, "u");
        var d = await db.OutboundOrderDetails.SingleAsync(x => x.OutboundNo == outNo);
        Assert.Equal(30m, d.AllocatedQty);
        Assert.Equal("LOC1", d.LocationCd);
    }

    // ═════════ ⑤ 完工反冲が凍結ロケーションに撞る → 報工成功＋反冲被拒＋归集跳过 ═════════

    private static async Task SeedCoaAndPostingRulesAsync(CP6Context db)
    {
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "seed");
        PostingRuleSeed.EnsureSeeded(db);
    }

    [Fact]
    public async Task Backflush_HitsFrozenLocation_ReportSucceeds_NoReversal_CostCollectSkipped()
    {
        using var db = NewDb();
        await SeedCoaAndPostingRulesAsync(db);

        // 発行済指図 1 工程 + 定额料 BOM + 材料在庫（W01/LF）
        db.Set<WorkOrder>().Add(new WorkOrder { Id = Guid.NewGuid(), WorkOrderNo = "WOF", ProductCd = "P1", Status = 2, ProductionQty = 10m, CompletedQty = 0m });
        db.Set<WorkOrderProcess>().Add(new WorkOrderProcess { WorkOrderNo = "WOF", ProcessCd = "OP1", TaskCd = "T1", SortOrder = 1, ProcessStatus = 1 });
        db.Set<ProductMaterial>().Add(new ProductMaterial { Id = Guid.NewGuid(), ProductCd = "P1", ProcessCd = "OP1", MaterialCd = "M1", MaterialTypeDiv = "3", UsageType = 2, UnitUsage = 2m, SupplyPrice = 5m });
        db.Set<Stock>().Add(new Stock { WarehouseCd = "W01", LocationCd = "LF", ProductCd = "M1", LotNo = "", PhysicalQty = 100m, AllocatedQty = 0m, AvailableQty = 100m, OwnerType = StockOwnerType.Self, UnitPrice = 5m });
        await db.SaveChangesAsync();

        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        // 材料在庫のロケーション LF を棚卸で凍結
        await FreezeWarehouseAsync(db, stock);

        // 真链：ProductionResult → Backflush（反冲 OUT が LF に撞る）→ FinBridge 归集
        var journal = new JournalEntryService(db, new FiscalPeriodService(db, 1), new FinSequenceService(db));
        var collect = new CostCollectService(db, new FinSequenceService(db), new ProcessCostRateService(db));
        var settle = new CostSettleService(db, journal);
        var ar = new ArInvoiceService(db, new AutoVoucherEngine(db, journal), journal, new FinSequenceService(db));
        var finHook = new FinBridgeHook(db, ar, collect, settle, NullLogger<FinBridgeHook>.Instance);
        var backflush = new BackflushService(db, stock, new MaterialUsageCalculator());
        var mesSeq = new MesSequenceService(db);
        var woService = new WorkOrderService(db, mesSeq, new NoOpWmsBridgeHook());
        var prService = new ProductionResultService(db, mesSeq, woService, new NoOpMesNotifier(), new NoOpWmsBridgeHook(), backflush, finHook);

        // 報工は成功（反冲失敗は吞まれる）
        var resultNo = await prService.CompleteAsync(new ProductionResultRequest
        {
            WorkOrderNo = "WOF", ProcessCd = "OP1", OperatorCd = "OP", GoodQty = 10m,
        }, "mes");
        Assert.False(string.IsNullOrEmpty(resultNo));
        Assert.Equal(WorkOrderStatus.Completed, (await db.Set<WorkOrder>().AsNoTracking().SingleAsync(w => w.WorkOrderNo == "WOF")).Status);

        // 反冲被拒：ISSUE 移動なし＋材料在庫は不変（100）
        Assert.Equal(0, await db.StockTransactions.CountAsync(t => t.RelatedType == "ISSUE"));
        Assert.Equal(100m, (await db.Stocks.SingleAsync(s => s.ProductCd == "M1")).PhysicalQty);

        // 実績消費未回写（ActualQty 行なし）
        Assert.Equal(0, await db.WorkOrderMaterials.CountAsync(m => m.WorkOrderNo == "WOF"));

        // 成本归集跳过（C.2 闸1）：CostSheet 生成されず、Cost 凭证ゼロ
        Assert.Equal(0, await db.CostSheets.CountAsync(s => s.WorkOrderNo == "WOF"));
        Assert.Equal(0, await db.JournalEntries.CountAsync(j => j.Source == VoucherSource.Cost));
    }
}
