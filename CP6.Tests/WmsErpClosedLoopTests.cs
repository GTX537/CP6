using CP6.Core.EFDbContext;
using CP6.Core.Services;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Mes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace CP6.Tests;

/// <summary>
/// ERP ⇄ MES ⇄ WMS 跨模块闭环 結合テスト（実書き込み・モック無し）
///
/// 検証する 2 本のフック：
///  Phase 2（MES完了 → WMS完成品入庫）：
///    ProductionResultService.CompleteAsync が全工程完了を検知 → 実 WmsBridgeHook
///    → 実 InboundService.CreateFinishedGoodsFromWorkOrderAsync で W01 完成品在庫を生成。
///  Phase 4（WMS出荷確定 → ERP受注回写）：
///    OutboundService.ShipAsync（出荷区分）→ 実 ErpBridgeHook
///    → 受注明細の ShippedQty / ShipStatus と受注ヘッダを更新。
///
/// いずれも InMemory DB を 1 インスタンス共有し、本番 DI と同じ実装を直結して、
/// 実際に在庫・受注テーブルへ書き込まれることを断言する。
/// </summary>
public class WmsErpClosedLoopTests
{
    private const string Wh = "W01";

    private static CP6Context NewDb()
    {
        // ProductionResultService.WriteAsync は明示トランザクションを使うため、
        // InMemory の TransactionIgnoredWarning を抑止（no-op トランザクション扱い）。
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new CP6Context(options);
        db.Warehouses.Add(new Warehouse
        {
            WarehouseCd = Wh,
            WarehouseName = "メイン倉庫",
            WarehouseType = WarehouseType.RawMaterial,
            AllowNegative = false,
        });
        db.SaveChanges();
        return db;
    }

    // ════════════════════════════════════════════════════════════
    //  Phase 2 — MES完了 → WMS完成品入庫（実書き込み）
    // ════════════════════════════════════════════════════════════

    [Fact]
    public async Task Phase2_ProductionComplete_AutoCreatesFinishedGoodsInbound()
    {
        var db = NewDb();

        // 発行済（Status=2）の指図 1 工程
        db.WorkOrders.Add(new WorkOrder
        {
            WorkOrderNo = "WO_FG01", Status = 2,
            ProductCd = "PROD-FG", ProductName = "完成品A",
            ProductionQty = 100m, CompletedQty = 0m,
        });
        db.WorkOrderProcesses.Add(new WorkOrderProcess
        {
            WorkOrderNo = "WO_FG01", ProcessCd = "P01", TaskCd = "T01",
            SortOrder = 1, ProcessStatus = 0,
        });
        await db.SaveChangesAsync();

        // 実サービスを直結（モック無し）：完了 → WmsBridge → InboundService
        var wmsSeq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, wmsSeq);
        var inbound = new InboundService(db, wmsSeq, stock);
        var outbound = new OutboundService(db, wmsSeq, stock);
        var wmsBridge = new WmsBridgeHook(outbound, inbound, NullLogger<WmsBridgeHook>.Instance);

        var mesSeq = new MesSequenceService(db);
        var woService = new WorkOrderService(db, mesSeq, new NoOpWmsBridgeHook());
        var prService = new ProductionResultService(db, mesSeq, woService, new NoOpMesNotifier(), wmsBridge);

        // 着手 → 完了（良品 95 / 不良 5）
        var req = new ProductionResultRequest
        {
            WorkOrderNo = "WO_FG01", ProcessCd = "P01", OperatorCd = "OP01",
        };
        await prService.StartAsync(req, "tester");
        await prService.CompleteAsync(new ProductionResultRequest
        {
            WorkOrderNo = "WO_FG01", ProcessCd = "P01", OperatorCd = "OP01",
            GoodQty = 95m, DefectQty = 5m, DefectReasonCd = "D01",
        }, "tester");

        // 指図は完了に遷移し累計良品 95
        var wo = await db.WorkOrders.AsNoTracking().SingleAsync(x => x.WorkOrderNo == "WO_FG01");
        Assert.Equal(4, wo.Status);
        Assert.Equal(95m, wo.CompletedQty);

        // PRODUCTION 区分の入庫実績が自動生成された
        var receipt = await db.InboundReceipts.AsNoTracking()
            .SingleAsync(x => x.WorkOrderNo == "WO_FG01");
        Assert.Equal(InboundSourceType.Production, receipt.SourceType);
        Assert.Equal(Wh, receipt.WarehouseCd);

        // W01 完成品ロケーションに良品 95 が積まれた
        var fgStock = await db.Stocks.AsNoTracking()
            .SingleAsync(s => s.ProductCd == "PROD-FG" && s.WarehouseCd == Wh);
        Assert.Equal("W01-FG", fgStock.LocationCd);
        Assert.Equal(95m, fgStock.PhysicalQty);
        Assert.Equal("WO_FG01", fgStock.LotNo); // LotNo 未設定 → 指図NO を採用
    }

    [Fact]
    public async Task Phase2_ProductionComplete_IsIdempotent_NoDoubleInbound()
    {
        var db = NewDb();
        db.WorkOrders.Add(new WorkOrder
        {
            WorkOrderNo = "WO_FG02", Status = 4, ProductCd = "PROD-FG2",
            ProductName = "完成品B", ProductionQty = 50m, CompletedQty = 50m,
        });
        await db.SaveChangesAsync();

        var wmsSeq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, wmsSeq);
        var inbound = new InboundService(db, wmsSeq, stock);

        // 1 回目：入庫成功
        await inbound.CreateFinishedGoodsFromWorkOrderAsync("WO_FG02", 50m, "tester");
        // 2 回目：二重入庫防止で InvalidOperationException
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            inbound.CreateFinishedGoodsFromWorkOrderAsync("WO_FG02", 50m, "tester"));

        Assert.Equal(1, await db.InboundReceipts.CountAsync(x => x.WorkOrderNo == "WO_FG02"));
    }

    // ════════════════════════════════════════════════════════════
    //  Phase 4 — WMS出荷確定 → ERP受注回写（実書き込み）
    // ════════════════════════════════════════════════════════════

    [Fact]
    public async Task Phase4_ShipConfirm_WritesShipmentBackToOrder()
    {
        var db = NewDb();

        // 受注 1 明細（PROD-X × 1000）
        db.Orders.Add(new Order
        {
            WebOrderNo = "WO_PA_SHIP", CustomerCd = "C001",
            OrderType = "01", OrderDate = DateTime.Today, Status = 1,
        });
        db.OrderDetails.Add(new OrderDetail
        {
            WebOrderNo = "WO_PA_SHIP", WebOrderDetailNo = 1,
            ProductCd = "PROD-X", Quantity = 1000m,
            UnitPriceUnit = "EA", IndividualUnitPrice = 12.5m,
        });
        await db.SaveChangesAsync();

        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        // W01 に出荷対象在庫を積む
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = Wh, LocationCd = "L01",
            ProductCd = "PROD-X", LotNo = "L1", Qty = 1000m,
        });

        // 実 ErpBridgeHook を直結した OutboundService
        var erpBridge = new ErpBridgeHook(db, NullLogger<ErpBridgeHook>.Instance);
        var outbound = new OutboundService(db, seq, stock, null, erpBridge);

        // 受注 → 出荷指示展開 → 確定 → 引当 → 出荷確定
        var no = await outbound.CreateFromOrderAsync("WO_PA_SHIP", "u");
        await outbound.ConfirmOrderAsync(no, "u");
        await outbound.AllocateAsync(no, "u");
        var pkg = await outbound.ShipAsync(no, new ShipRequest(), "u");
        Assert.NotNull(pkg); // 出荷区分 → 梱包採番

        // 受注明細へ出荷実績が回写された
        var od = await db.OrderDetails.AsNoTracking()
            .SingleAsync(d => d.WebOrderNo == "WO_PA_SHIP" && d.WebOrderDetailNo == 1);
        Assert.Equal(1000m, od.ShippedQty);
        Assert.Equal(9, od.ShipStatus);          // 全量出荷 → 出荷済
        Assert.Equal(no, od.LastOutboundNo);
        Assert.NotNull(od.LastShipDate);

        // 受注ヘッダもロールアップ（全明細出荷済 → 9）
        var order = await db.Orders.AsNoTracking().SingleAsync(o => o.WebOrderNo == "WO_PA_SHIP");
        Assert.Equal(9, order.ShipStatus);
        Assert.NotNull(order.ActualShipDate);
    }

    [Fact]
    public async Task Phase4_NoOpErpBridge_LeavesOrderUntouched()
    {
        var db = NewDb();
        db.Orders.Add(new Order
        {
            WebOrderNo = "WO_PA_NOOP", CustomerCd = "C001",
            OrderType = "01", OrderDate = DateTime.Today, Status = 1,
        });
        db.OrderDetails.Add(new OrderDetail
        {
            WebOrderNo = "WO_PA_NOOP", WebOrderDetailNo = 1,
            ProductCd = "PROD-Y", Quantity = 100m,
        });
        await db.SaveChangesAsync();

        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = Wh, LocationCd = "L01",
            ProductCd = "PROD-Y", LotNo = "L1", Qty = 100m,
        });

        // ErpBridge 無効化（NoOp）— 出荷しても受注は更新されない
        var outbound = new OutboundService(db, seq, stock, null, new NoOpErpBridgeHook());
        var no = await outbound.CreateFromOrderAsync("WO_PA_NOOP", "u");
        await outbound.ConfirmOrderAsync(no, "u");
        await outbound.AllocateAsync(no, "u");
        await outbound.ShipAsync(no, new ShipRequest(), "u");

        var od = await db.OrderDetails.AsNoTracking()
            .SingleAsync(d => d.WebOrderNo == "WO_PA_NOOP" && d.WebOrderDetailNo == 1);
        Assert.Null(od.ShippedQty);
        Assert.Equal(0, od.ShipStatus);

        var order = await db.Orders.AsNoTracking().SingleAsync(o => o.WebOrderNo == "WO_PA_NOOP");
        Assert.Equal(0, order.ShipStatus);
    }

    // ════════════════════════════════════════════════════════════
    //  Phase 3 — 指図発行 → 材料出庫 → 原料在庫扣減（実書き込み）
    // ════════════════════════════════════════════════════════════

    [Fact]
    public async Task Phase3_WorkOrderIssue_ExpandsMaterialOutbound_AndShipDeductsStock()
    {
        var db = NewDb();

        // 発行前（Status=1）の指図：1 工程 + 材料 2 種
        db.WorkOrders.Add(new WorkOrder
        {
            WorkOrderNo = "WO_MAT01", Status = 1, ProductCd = "PROD-M",
            ProductName = "製品M", ProductionQty = 100m, Priority = 2,
        });
        db.WorkOrderProcesses.Add(new WorkOrderProcess
        {
            WorkOrderNo = "WO_MAT01", ProcessCd = "P01", TaskCd = "T01",
            SortOrder = 1, ProcessStatus = 0,
        });
        db.WorkOrderMaterials.Add(new WorkOrderMaterial
        {
            WorkOrderNo = "WO_MAT01", ProcessCd = "P01", MaterialCd = "M001",
            MaterialName = "原紙", PlanQty = 500m, Unit = "kg", SortOrder = 1,
        });
        db.WorkOrderMaterials.Add(new WorkOrderMaterial
        {
            WorkOrderNo = "WO_MAT01", ProcessCd = "P01", MaterialCd = "M002",
            MaterialName = "インキ", PlanQty = 10m, Unit = "L", SortOrder = 2,
        });
        await db.SaveChangesAsync();

        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        // 原料在庫を W01 に積む（出庫充足分）
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = Wh, LocationCd = "L01",
            ProductCd = "M001", LotNo = "ML1", Qty = 600m,
        });
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = Wh, LocationCd = "L01",
            ProductCd = "M002", LotNo = "ML2", Qty = 50m,
        });

        // 実サービス直結：発行 → WmsBridge → 材料出庫指示 自動展開
        var inbound = new InboundService(db, seq, stock);
        var outbound = new OutboundService(db, seq, stock);
        var wmsBridge = new WmsBridgeHook(outbound, inbound, NullLogger<WmsBridgeHook>.Instance);
        var woService = new WorkOrderService(db, new MesSequenceService(db), wmsBridge);

        await woService.IssueAsync("WO_MAT01", "tester");

        // 指図は発行済（Status=2）
        Assert.Equal(2, (await db.WorkOrders.AsNoTracking().SingleAsync(x => x.WorkOrderNo == "WO_MAT01")).Status);

        // 材料出庫指示が自動生成された（OutboundType=Material / WorkOrderNo 紐付き）
        var matOutbound = await db.OutboundOrders.AsNoTracking()
            .SingleAsync(x => x.WorkOrderNo == "WO_MAT01");
        Assert.Equal(OutboundType.Material, matOutbound.OutboundType);
        var outboundNo = matOutbound.OutboundNo;

        var matLines = await db.OutboundOrderDetails.AsNoTracking()
            .Where(d => d.OutboundNo == outboundNo).OrderBy(d => d.LineNo).ToListAsync();
        Assert.Equal(2, matLines.Count);
        Assert.Equal(500m, matLines.Single(d => d.ProductCd == "M001").RequiredQty);

        // 出庫を確定 → 引当 → 出荷で原料在庫を実扣減
        await outbound.ConfirmOrderAsync(outboundNo, "u");
        await outbound.AllocateAsync(outboundNo, "u");
        var pkg = await outbound.ShipAsync(outboundNo, new ShipRequest(), "u");
        Assert.Null(pkg); // 材料出庫は梱包なし

        // 原料在庫が出庫数だけ減った
        var m1 = await db.Stocks.AsNoTracking().SingleAsync(s => s.ProductCd == "M001");
        Assert.Equal(100m, m1.PhysicalQty);   // 600 - 500
        Assert.Equal(0m, m1.AllocatedQty);     // OUT が引当も同時消費
        Assert.Equal(100m, m1.AvailableQty);

        var m2 = await db.Stocks.AsNoTracking().SingleAsync(s => s.ProductCd == "M002");
        Assert.Equal(40m, m2.PhysicalQty);     // 50 - 10

        // 出庫指示は完了
        Assert.Equal(OutboundOrderStatus.Completed,
            (await db.OutboundOrders.AsNoTracking().SingleAsync(x => x.OutboundNo == outboundNo)).Status);
        Assert.Equal(500m, matLines.Count == 0 ? 0m :
            (await db.OutboundOrderDetails.AsNoTracking().SingleAsync(d => d.OutboundNo == outboundNo && d.ProductCd == "M001")).ShippedQty);
    }
}
