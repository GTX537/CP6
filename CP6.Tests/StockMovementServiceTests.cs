using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

/// <summary>
/// StockMovementService 単体テスト（MSBBWM Phase 1）
///
/// テスト観点：
/// 1. IN/OUT/MOVE/ADJ/RSV/UNRSV それぞれの数量増減ロジック
/// 2. Warehouse.AllowNegative=false の場合の在庫不足検出
/// 3. AllowNegative=true 時のマイナス在庫許容
/// 4. MOVE 時の源/先 トランザクション2件発行
/// 5. TxnNo 採番の連番性
/// 6. 引数バリデーション（負数、空文字等）
/// 7. AvailableQty 自動再計算
/// </summary>
public class StockMovementServiceTests
{
    // ───────── 工場メソッド ─────────

    private static StockMovementService CreateService(out CP6.Core.EFDbContext.CP6Context db, bool allowNegative = false)
    {
        db = TestHelper.CreateInMemoryContext();

        // 既定倉庫を1件登録
        db.Warehouses.Add(new Warehouse
        {
            WarehouseCd = "W01",
            WarehouseName = "メイン倉庫",
            WarehouseType = WarehouseType.RawMaterial,
            AllowNegative = allowNegative,
        });
        db.SaveChanges();

        var seq = new WmsSequenceService(db);
        return new StockMovementService(db, seq);
    }

    private static StockMovementRequest InReq(decimal qty = 100m, string product = "P001", string lot = "LOT01")
        => new()
        {
            TxnType = WmsTxnType.IN,
            WarehouseCd = "W01",
            LocationCd = "L01",
            ProductCd = product,
            LotNo = lot,
            Qty = qty,
            OperatorCd = "U001",
        };

    // ───────── IN：入庫 ─────────

    [Fact]
    public async Task ApplyAsync_IN_ShouldIncreasePhysicalAndAvailable()
    {
        var svc = CreateService(out var db);

        var txnNo = await svc.ApplyAsync(InReq(qty: 50m));

        Assert.StartsWith("TXN", txnNo);
        var s = await db.Stocks.SingleAsync();
        Assert.Equal(50m, s.PhysicalQty);
        Assert.Equal(0m, s.AllocatedQty);
        Assert.Equal(50m, s.AvailableQty);
        Assert.Single(db.StockTransactions);
    }

    [Fact]
    public async Task ApplyAsync_IN_TwoCalls_ShouldAccumulateOnSameStockRow()
    {
        var svc = CreateService(out var db);

        await svc.ApplyAsync(InReq(qty: 30m));
        await svc.ApplyAsync(InReq(qty: 70m));

        var s = await db.Stocks.SingleAsync();
        Assert.Equal(100m, s.PhysicalQty);
        Assert.Equal(2, db.StockTransactions.Count());
    }

    // ───────── OUT：出庫 ─────────

    [Fact]
    public async Task ApplyAsync_OUT_AfterIN_ShouldDecreasePhysical()
    {
        var svc = CreateService(out var db);
        await svc.ApplyAsync(InReq(qty: 100m));

        await svc.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.OUT,
            WarehouseCd = "W01", LocationCd = "L01", ProductCd = "P001", LotNo = "LOT01",
            Qty = 30m,
        });

        var s = await db.Stocks.SingleAsync();
        Assert.Equal(70m, s.PhysicalQty);
        Assert.Equal(70m, s.AvailableQty);
    }

    [Fact]
    public async Task ApplyAsync_OUT_InsufficientStock_ShouldThrow()
    {
        var svc = CreateService(out var db, allowNegative: false);
        await svc.ApplyAsync(InReq(qty: 10m));

        var ex = await Assert.ThrowsAsync<InsufficientStockException>(() =>
            svc.ApplyAsync(new StockMovementRequest
            {
                TxnType = WmsTxnType.OUT,
                WarehouseCd = "W01", LocationCd = "L01", ProductCd = "P001", LotNo = "LOT01",
                Qty = 50m,
            }));

        Assert.Equal("P001", ex.ProductCd);
        Assert.Equal(50m, ex.Requested);
    }

    [Fact]
    public async Task ApplyAsync_OUT_AllowNegativeWarehouse_ShouldAllowNegative()
    {
        var svc = CreateService(out var db, allowNegative: true);
        await svc.ApplyAsync(InReq(qty: 10m));

        await svc.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.OUT,
            WarehouseCd = "W01", LocationCd = "L01", ProductCd = "P001", LotNo = "LOT01",
            Qty = 50m,
        });

        var s = await db.Stocks.SingleAsync();
        Assert.Equal(-40m, s.PhysicalQty);
    }

    // ───────── RSV / UNRSV：引当 ─────────

    [Fact]
    public async Task ApplyAsync_RSV_ShouldIncreaseAllocatedAndReduceAvailable()
    {
        var svc = CreateService(out var db);
        await svc.ApplyAsync(InReq(qty: 100m));

        await svc.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.RSV,
            WarehouseCd = "W01", LocationCd = "L01", ProductCd = "P001", LotNo = "LOT01",
            Qty = 30m,
        });

        var s = await db.Stocks.SingleAsync();
        Assert.Equal(100m, s.PhysicalQty);
        Assert.Equal(30m, s.AllocatedQty);
        Assert.Equal(70m, s.AvailableQty);
    }

    [Fact]
    public async Task ApplyAsync_RSV_OverAvailable_ShouldThrow()
    {
        var svc = CreateService(out var db);
        await svc.ApplyAsync(InReq(qty: 100m));

        await Assert.ThrowsAsync<InsufficientStockException>(() =>
            svc.ApplyAsync(new StockMovementRequest
            {
                TxnType = WmsTxnType.RSV,
                WarehouseCd = "W01", LocationCd = "L01", ProductCd = "P001", LotNo = "LOT01",
                Qty = 150m,
            }));
    }

    [Fact]
    public async Task ApplyAsync_UNRSV_ShouldReduceAllocatedAndRestoreAvailable()
    {
        var svc = CreateService(out var db);
        await svc.ApplyAsync(InReq(qty: 100m));
        await svc.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.RSV,
            WarehouseCd = "W01", LocationCd = "L01", ProductCd = "P001", LotNo = "LOT01", Qty = 60m,
        });

        await svc.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.UNRSV,
            WarehouseCd = "W01", LocationCd = "L01", ProductCd = "P001", LotNo = "LOT01", Qty = 40m,
        });

        var s = await db.Stocks.SingleAsync();
        Assert.Equal(100m, s.PhysicalQty);
        Assert.Equal(20m, s.AllocatedQty);
        Assert.Equal(80m, s.AvailableQty);
    }

    // ───────── ADJ：棚卸調整 ─────────

    [Fact]
    public async Task ApplyAsync_ADJ_NegativeDelta_ShouldDecreasePhysical()
    {
        var svc = CreateService(out var db);
        await svc.ApplyAsync(InReq(qty: 100m));

        await svc.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.ADJ,
            WarehouseCd = "W01", LocationCd = "L01", ProductCd = "P001", LotNo = "LOT01",
            Qty = -10m,
            Remark = "棚卸差異",
        });

        var s = await db.Stocks.SingleAsync();
        Assert.Equal(90m, s.PhysicalQty);
    }

    // ───────── MOVE：棚移動 ─────────

    [Fact]
    public async Task MoveAsync_ShouldEmitTwoTransactionsAndSplitStock()
    {
        var svc = CreateService(out var db);
        await svc.ApplyAsync(InReq(qty: 100m));

        var (outNo, inNo) = await svc.MoveAsync(new StockMoveRequest
        {
            WarehouseCd = "W01",
            FromLocationCd = "L01",
            ToLocationCd = "L02",
            ProductCd = "P001", LotNo = "LOT01",
            Qty = 30m,
        });

        Assert.NotEqual(outNo, inNo);
        var stocks = await db.Stocks.OrderBy(x => x.LocationCd).ToListAsync();
        Assert.Equal(2, stocks.Count);
        Assert.Equal(70m, stocks[0].PhysicalQty); // L01
        Assert.Equal(30m, stocks[1].PhysicalQty); // L02
        Assert.Equal(2, db.StockTransactions.Count(t => t.TxnType == WmsTxnType.MOVE));
    }

    [Fact]
    public async Task MoveAsync_SameLocation_ShouldThrow()
    {
        var svc = CreateService(out _);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.MoveAsync(new StockMoveRequest
            {
                WarehouseCd = "W01",
                FromLocationCd = "L01",
                ToLocationCd = "L01",
                ProductCd = "P001", LotNo = "LOT01",
                Qty = 10m,
            }));
    }

    // ───────── バリデーション ─────────

    [Fact]
    public async Task ApplyAsync_InRequest_WithZeroQty_ShouldThrow()
    {
        var svc = CreateService(out _);

        await Assert.ThrowsAsync<ArgumentException>(() => svc.ApplyAsync(InReq(qty: 0m)));
    }

    [Fact]
    public async Task ApplyAsync_OutRequest_WithNegativeQty_ShouldThrow()
    {
        var svc = CreateService(out _);
        await svc.ApplyAsync(InReq(qty: 100m));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.ApplyAsync(new StockMovementRequest
            {
                TxnType = WmsTxnType.OUT,
                WarehouseCd = "W01", LocationCd = "L01", ProductCd = "P001", LotNo = "LOT01",
                Qty = -10m,
            }));
    }

    // ───────── 採番 ─────────

    [Fact]
    public async Task WmsSequence_SamePrefixSameDay_ShouldBeSequential()
    {
        var db = TestHelper.CreateInMemoryContext();
        var seq = new WmsSequenceService(db);

        var n1 = await seq.NextAsync("TXN");
        var n2 = await seq.NextAsync("TXN");
        var n3 = await seq.NextAsync("TXN");

        // 新形式：{prefix}{yyyyMM}{NNNN} 例 TXN2026050001（永不重置）
        Assert.EndsWith("0001", n1);
        Assert.EndsWith("0002", n2);
        Assert.EndsWith("0003", n3);
        Assert.StartsWith($"TXN{DateTime.Today:yyyyMM}", n1);
    }
}
