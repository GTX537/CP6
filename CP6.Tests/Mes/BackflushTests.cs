using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Common;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CP6.Tests.Mes;

/// <summary>
/// F1 財務油路 波C.1 — 完工点 原料反冲（OUT/ISSUE）＋実績消費回写＋負在庫許可（拍板①）。
///
/// 検証不変式：
///  ① 定额料（UsageType=2）：反冲量 = UnitUsage × CompletedQty、OUT 発行 + ActualQty 回写。
///  ② 在庫不足でも負在庫記帳（AllowNegativeOverride）＋告警、例外を投げない。
///  ③ 尺寸料（UsageType=1）：BomUsageResolver（MRP と共用）の面積算法で数量算出。
///  ④ 冪等：同一指図の重複反冲は既存 ISSUE 移動で防止（二重扣庫・二重 ActualQty なし）。
/// </summary>
public class BackflushTests
{
    private static BackflushService Create(CP6Context db, IMaterialShortageNotifier? notifier = null)
    {
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        return new BackflushService(db, stock, new MaterialUsageCalculator(), notifier);
    }

    private static void SeedWarehouse(CP6Context db, string wh = "W01")
        => db.Warehouses.Add(new Warehouse
        {
            WarehouseCd = wh, WarehouseName = "原材料倉", WarehouseType = WarehouseType.RawMaterial,
            AllowNegative = false, // 倉庫は負在庫を許可しない → override が効くことを証明
        });

    private static void SeedStock(CP6Context db, string productCd, decimal qty, string wh = "W01", string loc = "L1")
        => db.Stocks.Add(new Stock
        {
            WarehouseCd = wh, LocationCd = loc, ProductCd = productCd, LotNo = "",
            PhysicalQty = qty, AllocatedQty = 0m, AvailableQty = qty, OwnerType = StockOwnerType.Self,
        });

    // ── ① 定额料：OUT 20 + ActualQty 20（WorkOrderMaterial 行は未存在 → 補行） ──
    [Fact]
    public async Task FixedUsage_GeneratesOutMovement_AndWritesActualQty()
    {
        var db = TestHelper.CreateInMemoryContext();
        SeedWarehouse(db);
        db.WorkOrders.Add(new WorkOrder { WorkOrderNo = "WO1", ProductCd = "PRODA", Status = 4, CompletedQty = 10m });
        db.Set<ProductMaterial>().Add(new ProductMaterial
        { ProductCd = "PRODA", ProcessCd = "P1", MaterialCd = "MAT1", MaterialTypeDiv = "3", UsageType = 2, UnitUsage = 2m });
        SeedStock(db, "MAT1", 100m);
        await db.SaveChangesAsync();

        await Create(db).BackflushAsync("WO1", "tester");

        var txn = await db.StockTransactions.AsNoTracking()
            .SingleAsync(t => t.RelatedType == "ISSUE" && t.RelatedNo == "WO1" && t.ProductCd == "MAT1");
        Assert.Equal(WmsTxnType.OUT, txn.TxnType);
        Assert.Equal(20m, txn.Qty);

        var stock = await db.Stocks.AsNoTracking().SingleAsync(s => s.ProductCd == "MAT1");
        Assert.Equal(80m, stock.PhysicalQty);

        var wom = await db.WorkOrderMaterials.AsNoTracking()
            .SingleAsync(m => m.WorkOrderNo == "WO1" && m.ProcessCd == "P1" && m.MaterialCd == "MAT1");
        Assert.Equal(20m, wom.ActualQty);
    }

    // ── ② 在庫不足：負在庫 -15 + 告警 + 例外を投げない ──
    [Fact]
    public async Task InsufficientStock_AllowsNegative_Warns_AndDoesNotThrow()
    {
        var db = TestHelper.CreateInMemoryContext();
        SeedWarehouse(db);
        db.WorkOrders.Add(new WorkOrder { WorkOrderNo = "WO2", ProductCd = "PRODB", Status = 4, CompletedQty = 10m });
        db.Set<ProductMaterial>().Add(new ProductMaterial
        { ProductCd = "PRODB", ProcessCd = "P1", MaterialCd = "MAT2", MaterialTypeDiv = "3", UsageType = 2, UnitUsage = 2m });
        SeedStock(db, "MAT2", 5m); // 需要 20、在庫 5 → 15 不足
        await db.SaveChangesAsync();

        var notifier = new Mock<IMaterialShortageNotifier>();

        var ex = await Record.ExceptionAsync(() => Create(db, notifier.Object).BackflushAsync("WO2", "tester"));
        Assert.Null(ex); // 拍板①：報工を止めない

        var stock = await db.Stocks.AsNoTracking().SingleAsync(s => s.ProductCd == "MAT2");
        Assert.Equal(-15m, stock.PhysicalQty); // 負在庫記帳

        notifier.Verify(n => n.NotifyAsync("WO2", "MAT2", 15m), Times.Once); // 不足分の告警

        var wom = await db.WorkOrderMaterials.AsNoTracking()
            .SingleAsync(m => m.WorkOrderNo == "WO2" && m.MaterialCd == "MAT2");
        Assert.Equal(20m, wom.ActualQty); // 実消費は反冲全量（不足に関わらず）
    }

    // ── ③ 尺寸料（UsageType=1）：面積算法で数量（既存 WorkOrderMaterial に累加） ──
    [Fact]
    public async Task DimensionalUsage_UsesAreaAlgorithm_AccumulatesActualQty()
    {
        var db = TestHelper.CreateInMemoryContext();
        SeedWarehouse(db);
        db.WorkOrders.Add(new WorkOrder { WorkOrderNo = "WO3", ProductCd = "PRODC", Status = 4, CompletedQty = 10m });
        db.Set<ProductMaster>().Add(new ProductMaster
        { ProductCd = "PRODC", SheetDimW = 1000m, SheetDimF = 1000m, SheetFlute = "A" });
        db.Set<MasterGenericCode>().Add(new MasterGenericCode { GroupCode = "M067", Code = "A", Num1 = 1.0m });
        db.Set<ProductMaterial>().Add(new ProductMaterial
        { ProductCd = "PRODC", ProcessCd = "P1", MaterialCd = "PAPER1", MaterialTypeDiv = "4", UsageType = 1 });
        // 既存 WorkOrderMaterial 行（ActualQty=0）→ 累加パスを検証
        db.WorkOrderMaterials.Add(new WorkOrderMaterial
        { WorkOrderNo = "WO3", ProcessCd = "P1", MaterialCd = "PAPER1", ActualQty = 0m });
        SeedStock(db, "PAPER1", 100m);
        await db.SaveChangesAsync();

        await Create(db).BackflushAsync("WO3", "tester");

        // 面積 = 1000×1000/1e6 = 1 m²、段成率 1.0、数量 10 → 用量 10
        var txn = await db.StockTransactions.AsNoTracking()
            .SingleAsync(t => t.RelatedType == "ISSUE" && t.RelatedNo == "WO3" && t.ProductCd == "PAPER1");
        Assert.Equal(10m, txn.Qty);

        var stock = await db.Stocks.AsNoTracking().SingleAsync(s => s.ProductCd == "PAPER1");
        Assert.Equal(90m, stock.PhysicalQty);

        var wom = await db.WorkOrderMaterials.AsNoTracking()
            .SingleAsync(m => m.WorkOrderNo == "WO3" && m.MaterialCd == "PAPER1");
        Assert.Equal(10m, wom.ActualQty);
    }

    // ── ④ 冪等：重複反冲は既存 ISSUE 移動で防止 ──
    [Fact]
    public async Task RepeatedBackflush_IsIdempotent_NoDoubleConsumption()
    {
        var db = TestHelper.CreateInMemoryContext();
        SeedWarehouse(db);
        db.WorkOrders.Add(new WorkOrder { WorkOrderNo = "WO4", ProductCd = "PRODD", Status = 4, CompletedQty = 10m });
        db.Set<ProductMaterial>().Add(new ProductMaterial
        { ProductCd = "PRODD", ProcessCd = "P1", MaterialCd = "MAT4", MaterialTypeDiv = "3", UsageType = 2, UnitUsage = 2m });
        SeedStock(db, "MAT4", 100m);
        await db.SaveChangesAsync();

        var svc = Create(db);
        await svc.BackflushAsync("WO4", "tester");
        await svc.BackflushAsync("WO4", "tester"); // 2 回目は no-op

        var txnCount = await db.StockTransactions.AsNoTracking()
            .CountAsync(t => t.RelatedType == "ISSUE" && t.RelatedNo == "WO4" && t.ProductCd == "MAT4");
        Assert.Equal(1, txnCount);

        var stock = await db.Stocks.AsNoTracking().SingleAsync(s => s.ProductCd == "MAT4");
        Assert.Equal(80m, stock.PhysicalQty); // 1 回のみ扣庫

        var wom = await db.WorkOrderMaterials.AsNoTracking()
            .SingleAsync(m => m.WorkOrderNo == "WO4" && m.MaterialCd == "MAT4");
        Assert.Equal(20m, wom.ActualQty); // 二重 ActualQty なし
    }

    // ── ⑤ 多料 BOM 部分失敗（終審 Important#1）：第二料が失敗しても首料は反冲＋ActualQty 落库成功
    //    （旧実装だと後料失敗で全断链→首料 ActualQty 消失＋整単冪等で残料永久未反冲）。
    //    失敗料が残るので末尾で投げる（C.2 闸維持）。解凍後の重放で第二料のみ補反冲、首料は加倍しない ──
    [Fact]
    public async Task PartialFailure_FirstMaterialPersists_ReplayResumesSecondOnly()
    {
        var db = TestHelper.CreateInMemoryContext();
        SeedWarehouse(db);
        db.WorkOrders.Add(new WorkOrder { WorkOrderNo = "WO5", ProductCd = "PRODE", Status = 4, CompletedQty = 10m });
        // 二料 BOM：MAT5A（SortOrder 1・成功）/ MAT5B（SortOrder 2・当初失敗）
        db.Set<ProductMaterial>().Add(new ProductMaterial
        { ProductCd = "PRODE", ProcessCd = "P1", MaterialCd = "MAT5A", MaterialTypeDiv = "3", UsageType = 2, UnitUsage = 2m, SortOrder = 1 });
        db.Set<ProductMaterial>().Add(new ProductMaterial
        { ProductCd = "PRODE", ProcessCd = "P1", MaterialCd = "MAT5B", MaterialTypeDiv = "3", UsageType = 2, UnitUsage = 3m, SortOrder = 2 });
        SeedStock(db, "MAT5A", 100m);
        SeedStock(db, "MAT5B", 100m);
        await db.SaveChangesAsync();

        // 第二料 MAT5B の在庫移動を最初は失敗させる（棚卸凍結を模擬：ApplyAsync が投げる）
        var seq = new WmsSequenceService(db);
        var gated = new GatedStock(new StockMovementService(db, seq)) { BlockedProduct = "MAT5B" };
        var svc = new BackflushService(db, gated, new MaterialUsageCalculator());

        // ── 初回：全料を試行（首料成功→即時落库、第二料失敗）→ 末尾で集約投げ（C.2 闸維持） ──
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.BackflushAsync("WO5", "tester"));

        // 首料 MAT5A：反冲成功＋在庫扣＋ActualQty 落库（旧実装だと後料失敗で断链→ ActualQty が失われた）
        Assert.Equal(1, await db.StockTransactions.CountAsync(t => t.RelatedType == "ISSUE" && t.RelatedNo == "WO5" && t.ProductCd == "MAT5A"));
        Assert.Equal(80m, (await db.Stocks.AsNoTracking().SingleAsync(s => s.ProductCd == "MAT5A")).PhysicalQty);
        Assert.Equal(20m, (await db.WorkOrderMaterials.AsNoTracking().SingleAsync(m => m.WorkOrderNo == "WO5" && m.MaterialCd == "MAT5A")).ActualQty);
        // 第二料 MAT5B：まだ反冲されていない
        Assert.Equal(0, await db.StockTransactions.CountAsync(t => t.RelatedType == "ISSUE" && t.RelatedNo == "WO5" && t.ProductCd == "MAT5B"));

        // ── 解凍（阻止解除）→ 重放：第二料のみ補反冲、首料は加倍しない ──
        gated.BlockedProduct = null;
        await svc.BackflushAsync("WO5", "tester");

        // 首料は 1 回のみ（重放で加倍しない）
        Assert.Equal(1, await db.StockTransactions.CountAsync(t => t.RelatedType == "ISSUE" && t.RelatedNo == "WO5" && t.ProductCd == "MAT5A"));
        Assert.Equal(80m, (await db.Stocks.AsNoTracking().SingleAsync(s => s.ProductCd == "MAT5A")).PhysicalQty);
        Assert.Equal(20m, (await db.WorkOrderMaterials.AsNoTracking().SingleAsync(m => m.WorkOrderNo == "WO5" && m.MaterialCd == "MAT5A")).ActualQty);
        // 第二料は今回補反冲（30 = 3×10）＋ActualQty 落库
        Assert.Equal(1, await db.StockTransactions.CountAsync(t => t.RelatedType == "ISSUE" && t.RelatedNo == "WO5" && t.ProductCd == "MAT5B"));
        Assert.Equal(70m, (await db.Stocks.AsNoTracking().SingleAsync(s => s.ProductCd == "MAT5B")).PhysicalQty);
        Assert.Equal(30m, (await db.WorkOrderMaterials.AsNoTracking().SingleAsync(m => m.WorkOrderNo == "WO5" && m.MaterialCd == "MAT5B")).ActualQty);
    }

    /// <summary>指定材料の在庫移動を投げさせるデコレータ（棚卸凍結による ApplyAsync 失敗を模擬）。</summary>
    private sealed class GatedStock : IStockMovementService
    {
        private readonly IStockMovementService _inner;
        public string? BlockedProduct { get; set; }
        public GatedStock(IStockMovementService inner) => _inner = inner;

        public Task<string> ApplyAsync(StockMovementRequest req, CancellationToken ct = default)
        {
            if (BlockedProduct != null && req.ProductCd == BlockedProduct)
                throw new InvalidOperationException("WM-MSG-304: 棚卸進行中のロケーション（模擬）");
            return _inner.ApplyAsync(req, ct);
        }

        public Task<(string OutTxnNo, string InTxnNo)> MoveAsync(StockMoveRequest req, CancellationToken ct = default)
            => _inner.MoveAsync(req, ct);
    }
}
