using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Wms;

/// <summary>
/// 出庫 確定（Ship）→ 在庫扣減/台帳 の「账实一致」回帰テスト（M-WMS 横切 T7 補網）。
///
/// 既存 <see cref="CP6.Tests.OutboundServiceTests"/> は Ship 基本経路を検証済み。
/// 本ファイルは未カバーの不変式を補う：
///  ① 全チェーン（IN→RSV→OUT）で PhysicalQty == 物理影響台帳の符号付き合計（台帳=実在庫の突合）。
///  ② 2 ロット存在下で 出庫は引当ロットのみ減らし 他ロットは不変（ロット別 账实精度）。
///  ③ 引当前（Confirmed）の出庫確定は拒否され、在庫・台帳に一切副作用がない（誤出庫防止）。
/// </summary>
public class OutboundShipStockTests
{
    private static OutboundService CreateService(out CP6.Core.EFDbContext.CP6Context db)
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
        return new OutboundService(db, seq, stock);
    }

    private static async Task SeedStockAsync(CP6.Core.EFDbContext.CP6Context db,
        string product, string lot, string location, decimal qty,
        DateTime? receiveDate = null, DateTime? expiryDate = null)
    {
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01",
            LocationCd = location, ProductCd = product, LotNo = lot,
            Qty = qty, ReceiveDate = receiveDate, ExpiryDate = expiryDate,
        });
    }

    private static OutboundOrderDto MaterialOrder(string productCd = "P001", decimal qty = 30m) => new()
    {
        OutboundType = OutboundType.Material,
        WarehouseCd = "W01",
        PlannedDate = DateTime.Today,
        Details = new List<OutboundOrderDetailDto> { new() { ProductCd = productCd, RequiredQty = qty } }
    };

    /// <summary>物理在庫に影響する台帳（IN/OUT/ADJ/MOVE）の符号付き合計を求める。RSV/UNRSV は物理不変。</summary>
    private static decimal PhysicalLedgerSum(IEnumerable<StockTransaction> txns, string lot) => txns
        .Where(t => t.LotNo == lot)
        .Sum(t => t.TxnType switch
        {
            WmsTxnType.IN => t.Qty,
            WmsTxnType.OUT => -t.Qty,
            WmsTxnType.ADJ => t.Qty,
            WmsTxnType.MOVE => t.Qty,
            _ => 0m, // RSV / UNRSV は物理在庫に影響しない
        });

    // ═════════ ① 台帳=実在庫の突合（保存則） ═════════

    [Fact]
    public async Task Ship_FullChain_PhysicalQtyEqualsLedgerSum()
    {
        var svc = CreateService(out var db);
        await SeedStockAsync(db, "P001", "L1", "L01", 100m);

        var no = await svc.CreateOrderAsync(MaterialOrder("P001", 30m), "u");
        await svc.ConfirmOrderAsync(no, "u");
        await svc.AllocateAsync(no, "u");   // RSV 30
        var pkg = await svc.ShipAsync(no, new ShipRequest(), "u"); // OUT 30

        Assert.Null(pkg); // 材料出庫は梱包なし
        var s = await db.Stocks.SingleAsync(x => x.LotNo == "L1");
        Assert.Equal(70m, s.PhysicalQty);
        Assert.Equal(0m, s.AllocatedQty);   // OUT が引当も消費
        Assert.Equal(70m, s.AvailableQty);

        // 账实突合：PhysicalQty == 物理影響台帳の符号付き合計（IN+100, OUT-30）
        var txns = await db.StockTransactions.ToListAsync();
        Assert.Equal(s.PhysicalQty, PhysicalLedgerSum(txns, "L1"));

        // OUT は 1 件だけ、数量・関連が正しい
        var outs = txns.Where(t => t.TxnType == WmsTxnType.OUT).ToList();
        var o = Assert.Single(outs);
        Assert.Equal(30m, o.Qty);
        Assert.Equal(no, o.RelatedNo);
        Assert.Equal("OUTBOUND", o.RelatedType);
    }

    // ═════════ ② 2 ロット下のロット別精度 ═════════

    [Fact]
    public async Task Ship_WithSecondLotPresent_ShouldReduceOnlyAllocatedLot()
    {
        var svc = CreateService(out var db);
        // LOT_A は期限が近い（FEFO で先に引当される）、LOT_B は遠い
        await SeedStockAsync(db, "P001", "LOT_A", "L01", 100m,
            receiveDate: DateTime.Today.AddDays(-5), expiryDate: DateTime.Today.AddDays(10));
        await SeedStockAsync(db, "P001", "LOT_B", "L02", 100m,
            receiveDate: DateTime.Today, expiryDate: DateTime.Today.AddDays(60));

        var no = await svc.CreateOrderAsync(MaterialOrder("P001", 30m), "u");
        await svc.ConfirmOrderAsync(no, "u");
        await svc.AllocateAsync(no, "u");
        await svc.ShipAsync(no, new ShipRequest(), "u");

        var lotA = await db.Stocks.SingleAsync(s => s.LotNo == "LOT_A");
        var lotB = await db.Stocks.SingleAsync(s => s.LotNo == "LOT_B");

        // 引当された LOT_A のみ 30 減、LOT_B は完全に不変
        Assert.Equal(70m, lotA.PhysicalQty);
        Assert.Equal(0m, lotA.AllocatedQty);
        Assert.Equal(100m, lotB.PhysicalQty);
        Assert.Equal(0m, lotB.AllocatedQty);       // LOT_B は引当されていない
        Assert.Equal(100m, lotB.AvailableQty);     // LOT_B は完全に不変

        // 総在庫の保存：200 - 30 = 170
        Assert.Equal(170m, await db.Stocks.SumAsync(s => s.PhysicalQty));
        // 出庫台帳は LOT_A に対してのみ発行
        var o = Assert.Single(await db.StockTransactions.Where(t => t.TxnType == WmsTxnType.OUT).ToListAsync());
        Assert.Equal("LOT_A", o.LotNo);
    }

    // ═════════ ③ 引当前の出庫確定は拒否＆副作用なし ═════════

    [Fact]
    public async Task Ship_BeforeAllocate_ShouldThrowAndLeaveStockUntouched()
    {
        var svc = CreateService(out var db);
        await SeedStockAsync(db, "P001", "L1", "L01", 100m);
        var txnCountBefore = await db.StockTransactions.CountAsync(); // IN 1 件

        var no = await svc.CreateOrderAsync(MaterialOrder("P001", 30m), "u");
        await svc.ConfirmOrderAsync(no, "u"); // 引当せず Confirmed のまま

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ShipAsync(no, new ShipRequest(), "u"));

        // 在庫・台帳に副作用なし（誤出庫防止）
        var s = await db.Stocks.SingleAsync();
        Assert.Equal(100m, s.PhysicalQty);
        Assert.Equal(0m, s.AllocatedQty);
        Assert.Equal(txnCountBefore, await db.StockTransactions.CountAsync());
        Assert.DoesNotContain(await db.StockTransactions.ToListAsync(), t => t.TxnType == WmsTxnType.OUT);
        // 指示は Confirmed のまま（Completed に進んでいない）
        Assert.Equal(OutboundOrderStatus.Confirmed, (await db.OutboundOrders.SingleAsync()).Status);
    }
}
