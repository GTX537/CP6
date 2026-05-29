using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

/// <summary>
/// MobileService 単体テスト（MSBBWM300 RFハンディ・モバイルWMS）
///
/// テスト観点：
/// Create/Start:
///   1. TaskType 必須バリデーション
///   2. MTK 採番 + Priority 既定(0→2) + Status=未着手
///   3. 開始 0→1（StartedAt 記録）／完了・取消後の開始は拒否
/// GetTasks:
///   4. AssignedTo 指定時は「本人割当 + 未割当プール」を両方返す
///   5. OpenOnly は完了・取消を除外
/// Scan:
///   6. ロケーションCD / バーコード一致 → LOCATION
///   7. 製品CD（在庫）一致 → PRODUCT
///   8. 解決不能 → UNKNOWN / 空スキャン
///   9. taskNo 照合（一致 / 不一致）
/// Complete/Cancel:
///   10. MOVE 完了で MOVE トランザクション一対発行 + 実在庫移動
///   11. 非MOVE 完了は状態のみ更新（在庫不動）
///   12. 完了・取消後の再操作は拒否
/// </summary>
public class WmsMobileServiceTests
{
    private static MobileService Create(out CP6.Core.EFDbContext.CP6Context db)
    {
        db = TestHelper.CreateInMemoryContext();
        db.Warehouses.Add(new Warehouse
        {
            WarehouseCd = "W01",
            WarehouseName = "メイン倉庫",
            WarehouseType = WarehouseType.RawMaterial,
            AllowNegative = false,
        });
        db.SaveChanges();

        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        return new MobileService(db, seq, stock);
    }

    // ───────── Create / Start ─────────

    [Fact]
    public async Task Create_WithoutTaskType_ShouldThrow()
    {
        var svc = Create(out _);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateAsync(new MobileTaskDto { TaskType = "" }, "u"));
    }

    [Fact]
    public async Task Create_DefaultsPriorityAndPendingStatus()
    {
        var svc = Create(out var db);
        var no = await svc.CreateAsync(new MobileTaskDto
        {
            TaskType = MobileTaskType.Pick,
            Priority = 0,            // 0 は既定 2 に正規化される
            ProductCd = "P001", Qty = 5,
        }, "alice");

        Assert.StartsWith("MTK", no);
        var t = await db.MobileTasks.SingleAsync();
        Assert.Equal(2, t.Priority);
        Assert.Equal(MobileTaskStatus.Pending, t.Status);
        Assert.Equal("alice", t.Creator);
    }

    [Fact]
    public async Task Start_PendingToInProgress_SetsStartedAt()
    {
        var svc = Create(out var db);
        var no = await svc.CreateAsync(new MobileTaskDto { TaskType = MobileTaskType.Pick }, "u");

        await svc.StartAsync(no, "u");

        var t = await db.MobileTasks.SingleAsync();
        Assert.Equal(MobileTaskStatus.InProgress, t.Status);
        Assert.NotNull(t.StartedAt);
    }

    [Fact]
    public async Task Start_OnCompleted_ShouldThrow()
    {
        var svc = Create(out _);
        var no = await svc.CreateAsync(new MobileTaskDto { TaskType = MobileTaskType.Pick, Qty = 1 }, "u");
        await svc.CompleteAsync(no, new MobileCompleteRequest(), "u");

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.StartAsync(no, "u"));
    }

    // ───────── GetTasks ─────────

    [Fact]
    public async Task GetTasks_ByAssignedTo_IncludesOwnAndUnassignedPool()
    {
        var svc = Create(out _);
        await svc.CreateAsync(new MobileTaskDto { TaskType = MobileTaskType.Pick, AssignedTo = "alice" }, "u");
        await svc.CreateAsync(new MobileTaskDto { TaskType = MobileTaskType.Pick, AssignedTo = "bob" }, "u");
        await svc.CreateAsync(new MobileTaskDto { TaskType = MobileTaskType.Pick, AssignedTo = null }, "u"); // 未割当プール

        var list = await svc.GetTasksAsync(new MobileTaskQuery { AssignedTo = "alice" });

        Assert.Equal(2, list.Count); // alice 本人 + 未割当プール
        Assert.All(list, t => Assert.True(t.AssignedTo == "alice" || t.AssignedTo == null));
    }

    [Fact]
    public async Task GetTasks_OpenOnly_ExcludesCompletedAndCancelled()
    {
        var svc = Create(out _);
        var keep = await svc.CreateAsync(new MobileTaskDto { TaskType = MobileTaskType.Pick }, "u");
        var done = await svc.CreateAsync(new MobileTaskDto { TaskType = MobileTaskType.Pick, Qty = 1 }, "u");
        var cancel = await svc.CreateAsync(new MobileTaskDto { TaskType = MobileTaskType.Pick }, "u");
        await svc.CompleteAsync(done, new MobileCompleteRequest(), "u");
        await svc.CancelAsync(cancel, "u");

        var list = await svc.GetTasksAsync(new MobileTaskQuery { OpenOnly = true });

        Assert.Single(list);
        Assert.Equal(keep, list[0].MobileTaskNo);
    }

    // ───────── Scan ─────────

    private static void SeedLocation(CP6.Core.EFDbContext.CP6Context db, string loc, string? barcode = null, bool blocked = false)
    {
        db.Locations.Add(new Location { LocationCd = loc, WarehouseCd = "W01", LocationName = loc + "棚", Barcode = barcode, IsBlocked = blocked });
        db.SaveChanges();
    }

    private static void SeedStock(CP6.Core.EFDbContext.CP6Context db, string loc, string product, decimal qty = 50m)
    {
        db.Stocks.Add(new Stock
        {
            WarehouseCd = "W01", LocationCd = loc, ProductCd = product, LotNo = "LOT01",
            PhysicalQty = qty, AllocatedQty = 0, AvailableQty = qty, UnitCd = "PCS",
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task Scan_LocationByCode_ResolvesLocation()
    {
        var svc = Create(out var db);
        SeedLocation(db, "L01", barcode: "BC-L01");
        SeedStock(db, "L01", "P001");

        var byCode = await svc.ScanAsync(new MobileScanRequest { Barcode = "L01" });
        Assert.Equal(MobileScanKind.Location, byCode.Kind);
        Assert.Equal("L01", byCode.LocationCd);
        Assert.Single(byCode.Stocks);

        var byBarcode = await svc.ScanAsync(new MobileScanRequest { Barcode = "BC-L01" });
        Assert.Equal(MobileScanKind.Location, byBarcode.Kind);
        Assert.Equal("L01", byBarcode.LocationCd);
    }

    [Fact]
    public async Task Scan_ProductByStock_ResolvesProduct()
    {
        var svc = Create(out var db);
        SeedStock(db, "L01", "P001", qty: 25m);

        var res = await svc.ScanAsync(new MobileScanRequest { Barcode = "P001", WarehouseCd = "W01" });

        Assert.Equal(MobileScanKind.Product, res.Kind);
        Assert.Equal("P001", res.ProductCd);
        Assert.Single(res.Stocks);
        Assert.Equal(25m, res.Stocks[0].PhysicalQty);
    }

    [Fact]
    public async Task Scan_Unknown_ReturnsUnknownWithMessage()
    {
        var svc = Create(out _);

        var unknown = await svc.ScanAsync(new MobileScanRequest { Barcode = "ZZZ" });
        Assert.Equal(MobileScanKind.Unknown, unknown.Kind);
        Assert.Equal("WM-MSG-301", unknown.Message);

        var empty = await svc.ScanAsync(new MobileScanRequest { Barcode = "" });
        Assert.Equal(MobileScanKind.Unknown, empty.Kind);
        Assert.Equal("WM-MSG-300", empty.Message);
    }

    [Fact]
    public async Task Scan_WithTaskNo_ReportsMatch()
    {
        var svc = Create(out var db);
        SeedLocation(db, "L01");
        SeedLocation(db, "L02");
        SeedLocation(db, "L09");
        var no = await svc.CreateAsync(new MobileTaskDto
        {
            TaskType = MobileTaskType.Move,
            WarehouseCd = "W01", FromLocationCd = "L01", ToLocationCd = "L02",
            ProductCd = "P001", Qty = 10,
        }, "u");

        var matched = await svc.ScanAsync(new MobileScanRequest { Barcode = "L01", TaskNo = no });
        Assert.True(matched.Matched);

        var mismatch = await svc.ScanAsync(new MobileScanRequest { Barcode = "L09", TaskNo = no });
        Assert.False(mismatch.Matched);
        Assert.Equal("WM-MSG-302", mismatch.Message);
    }

    // ───────── Complete / Cancel ─────────

    [Fact]
    public async Task Complete_Move_EmitsTransactionsAndMovesStock()
    {
        var svc = Create(out var db);
        // 源ロケに在庫 100 を IN で用意
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01", LocationCd = "L01",
            ProductCd = "P001", LotNo = "LOT01", Qty = 100m,
        });

        var no = await svc.CreateAsync(new MobileTaskDto
        {
            TaskType = MobileTaskType.Move,
            WarehouseCd = "W01", FromLocationCd = "L01", ToLocationCd = "L02",
            ProductCd = "P001", LotNo = "LOT01", Qty = 30m,
        }, "u");
        await svc.StartAsync(no, "u");
        await svc.CompleteAsync(no, new MobileCompleteRequest(), "worker");

        var t = await db.MobileTasks.SingleAsync();
        Assert.Equal(MobileTaskStatus.Completed, t.Status);
        Assert.NotNull(t.DoneAt);
        Assert.False(string.IsNullOrEmpty(t.OutTxnNo));
        Assert.False(string.IsNullOrEmpty(t.InTxnNo));
        Assert.Equal(30m, t.ScannedQty);

        var stocks = await db.Stocks.OrderBy(x => x.LocationCd).ToListAsync();
        Assert.Equal(70m, stocks[0].PhysicalQty); // L01
        Assert.Equal(30m, stocks[1].PhysicalQty); // L02
        Assert.Equal(2, db.StockTransactions.Count(x => x.TxnType == WmsTxnType.MOVE));
    }

    [Fact]
    public async Task Complete_NonMove_UpdatesStatusOnly()
    {
        var svc = Create(out var db);
        var no = await svc.CreateAsync(new MobileTaskDto
        {
            TaskType = MobileTaskType.Pick,
            WarehouseCd = "W01", FromLocationCd = "L01", ProductCd = "P001", Qty = 8m,
        }, "u");

        await svc.CompleteAsync(no, new MobileCompleteRequest { ScannedQty = 8m }, "u");

        var t = await db.MobileTasks.SingleAsync();
        Assert.Equal(MobileTaskStatus.Completed, t.Status);
        Assert.Equal(8m, t.ScannedQty);
        Assert.Empty(db.StockTransactions); // PICK は在庫を動かさない
    }

    [Fact]
    public async Task Complete_OnCancelled_ShouldThrow()
    {
        var svc = Create(out _);
        var no = await svc.CreateAsync(new MobileTaskDto { TaskType = MobileTaskType.Pick }, "u");
        await svc.CancelAsync(no, "u");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CompleteAsync(no, new MobileCompleteRequest(), "u"));
    }

    [Fact]
    public async Task Cancel_PendingToCancelled_Then_CancelCompleted_ShouldThrow()
    {
        var svc = Create(out var db);
        var no = await svc.CreateAsync(new MobileTaskDto { TaskType = MobileTaskType.Pick }, "u");
        await svc.CancelAsync(no, "u");
        Assert.Equal(MobileTaskStatus.Cancelled, (await db.MobileTasks.SingleAsync()).Status);

        var done = await svc.CreateAsync(new MobileTaskDto { TaskType = MobileTaskType.Pick, Qty = 1 }, "u");
        await svc.CompleteAsync(done, new MobileCompleteRequest(), "u");
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CancelAsync(done, "u"));
    }
}
