using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

/// <summary>
/// InboundService 単体テスト（MSBBWM Phase 2）
///
/// テスト観点：
/// 1. 入庫予定 CRUD（採番、ステータス遷移、削除制約）
/// 2. 入庫予定 確定（明細 0 件拒否、二重確定拒否）
/// 3. 入庫実績 確定 → StockMovementService.IN 連動
/// 4. 部分入庫 → 予定 Status=2、全数入庫 → Status=3
/// 5. 直入モード（InboundNo=null、関連予定なし）
/// 6. 予定明細の ReceivedQty 累計更新
/// </summary>
public class InboundServiceTests
{
    // ─────── 工場 ───────

    private static InboundService CreateService(out CP6.Core.EFDbContext.CP6Context db)
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
        return new InboundService(db, seq, stock);
    }

    private static InboundOrderDto NewOrderDto(int lines = 1)
    {
        var dto = new InboundOrderDto
        {
            InboundType = 1,
            SupplierCd = "S001",
            SupplierName = "テスト仕入先",
            ExpectedArrivalDate = DateTime.Today.AddDays(3),
            WarehouseCd = "W01",
            Remarks = "テスト",
        };
        for (int i = 0; i < lines; i++)
        {
            dto.Details.Add(new InboundOrderDetailDto
            {
                ProductCd = $"P00{i + 1}",
                ProductName = $"製品{i + 1}",
                ExpectedQty = 100m,
                UnitCd = "EA",
            });
        }
        return dto;
    }

    // ═════════ 入庫予定 ═════════

    [Fact]
    public async Task CreateOrder_ShouldAssignSequentialInboundNo()
    {
        var svc = CreateService(out var db);

        var no1 = await svc.CreateOrderAsync(NewOrderDto(), "u1");
        var no2 = await svc.CreateOrderAsync(NewOrderDto(), "u1");

        Assert.StartsWith("IN", no1);
        Assert.StartsWith("IN", no2);
        Assert.NotEqual(no1, no2);
        Assert.EndsWith("-00001", no1);
        Assert.EndsWith("-00002", no2);
        Assert.Equal(2, db.InboundOrders.Count());
    }

    [Fact]
    public async Task CreateOrder_WithNoDetails_ShouldThrow()
    {
        var svc = CreateService(out _);
        var dto = NewOrderDto(0);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateOrderAsync(dto, "u1"));
    }

    [Fact]
    public async Task ConfirmOrder_ShouldMoveStatusToConfirmed()
    {
        var svc = CreateService(out var db);
        var no = await svc.CreateOrderAsync(NewOrderDto(), "u1");

        await svc.ConfirmOrderAsync(no, "u1");

        var h = await db.InboundOrders.SingleAsync();
        Assert.Equal(InboundOrderStatus.Confirmed, h.Status);
    }

    [Fact]
    public async Task ConfirmOrder_TwiceShouldThrow()
    {
        var svc = CreateService(out _);
        var no = await svc.CreateOrderAsync(NewOrderDto(), "u1");
        await svc.ConfirmOrderAsync(no, "u1");

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ConfirmOrderAsync(no, "u1"));
    }

    [Fact]
    public async Task DeleteOrder_ConfirmedShouldThrow()
    {
        var svc = CreateService(out _);
        var no = await svc.CreateOrderAsync(NewOrderDto(), "u1");
        await svc.ConfirmOrderAsync(no, "u1");

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DeleteOrderAsync(no, "u1"));
    }

    [Fact]
    public async Task CancelOrder_ShouldMoveStatusTo9()
    {
        var svc = CreateService(out var db);
        var no = await svc.CreateOrderAsync(NewOrderDto(), "u1");
        await svc.ConfirmOrderAsync(no, "u1");

        await svc.CancelOrderAsync(no, "u1");

        var h = await db.InboundOrders.SingleAsync();
        Assert.Equal(InboundOrderStatus.Cancelled, h.Status);
    }

    // ═════════ 入庫実績 ═════════

    [Fact]
    public async Task ConfirmReceipt_ShouldCreateStockAndIssueTxn()
    {
        var svc = CreateService(out var db);
        var inNo = await svc.CreateOrderAsync(NewOrderDto(), "u1");
        await svc.ConfirmOrderAsync(inNo, "u1");

        var receiptDto = new InboundReceiptDto
        {
            InboundNo = inNo,
            SourceType = InboundSourceType.Purchase,
            WarehouseCd = "W01",
            Details = new List<InboundReceiptDetailDto>
            {
                new() {
                    RefOrderLineNo = 1,
                    ProductCd = "P001",
                    LotNo = "LOT01",
                    ReceivedQty = 100m,
                    LocationCd = "L01",
                }
            }
        };

        var rcNo = await svc.ConfirmReceiptAsync(receiptDto, "u1");

        Assert.StartsWith("RC", rcNo);
        // 在庫が作成された
        var stock = await db.Stocks.SingleAsync(s =>
            s.WarehouseCd == "W01" && s.LocationCd == "L01" && s.ProductCd == "P001");
        Assert.Equal(100m, stock.PhysicalQty);
        // StockTxn が発行された
        var txn = await db.StockTransactions.SingleAsync();
        Assert.Equal(WmsTxnType.IN, txn.TxnType);
        Assert.Equal(rcNo, txn.RelatedNo);
        // Receipt 明細に StockTxnNo がリンクされた
        var rcDetail = await db.InboundReceiptDetails.SingleAsync();
        Assert.Equal(txn.TxnNo, rcDetail.StockTxnNo);
    }

    [Fact]
    public async Task ConfirmReceipt_PartialShouldMoveOrderToStatus2()
    {
        var svc = CreateService(out var db);
        var inNo = await svc.CreateOrderAsync(NewOrderDto(), "u1");
        await svc.ConfirmOrderAsync(inNo, "u1");

        await svc.ConfirmReceiptAsync(new InboundReceiptDto
        {
            InboundNo = inNo,
            WarehouseCd = "W01",
            Details = new List<InboundReceiptDetailDto>
            {
                new() { RefOrderLineNo = 1, ProductCd = "P001", LotNo = "LOT01", ReceivedQty = 40m, LocationCd = "L01" }
            }
        }, "u1");

        var h = await db.InboundOrders.SingleAsync();
        Assert.Equal(InboundOrderStatus.PartialReceived, h.Status);

        var od = await db.InboundOrderDetails.SingleAsync();
        Assert.Equal(40m, od.ReceivedQty);
    }

    [Fact]
    public async Task ConfirmReceipt_FullShouldMoveOrderToStatus3()
    {
        var svc = CreateService(out var db);
        var inNo = await svc.CreateOrderAsync(NewOrderDto(), "u1");
        await svc.ConfirmOrderAsync(inNo, "u1");

        // 全数受領
        await svc.ConfirmReceiptAsync(new InboundReceiptDto
        {
            InboundNo = inNo,
            WarehouseCd = "W01",
            Details = new List<InboundReceiptDetailDto>
            {
                new() { RefOrderLineNo = 1, ProductCd = "P001", LotNo = "LOT01", ReceivedQty = 100m, LocationCd = "L01" }
            }
        }, "u1");

        var h = await db.InboundOrders.SingleAsync();
        Assert.Equal(InboundOrderStatus.Completed, h.Status);
    }

    [Fact]
    public async Task ConfirmReceipt_SplitTwoReceiptsShouldAccumulateAndComplete()
    {
        var svc = CreateService(out var db);
        var inNo = await svc.CreateOrderAsync(NewOrderDto(), "u1");
        await svc.ConfirmOrderAsync(inNo, "u1");

        // 1 回目 40 → 部分入庫
        await svc.ConfirmReceiptAsync(new InboundReceiptDto
        {
            InboundNo = inNo, WarehouseCd = "W01",
            Details = new List<InboundReceiptDetailDto>
            {
                new() { RefOrderLineNo = 1, ProductCd = "P001", LotNo = "LOT01", ReceivedQty = 40m, LocationCd = "L01" }
            }
        }, "u1");

        // 2 回目 60 → 累計 100 = 全数 → 完了
        await svc.ConfirmReceiptAsync(new InboundReceiptDto
        {
            InboundNo = inNo, WarehouseCd = "W01",
            Details = new List<InboundReceiptDetailDto>
            {
                new() { RefOrderLineNo = 1, ProductCd = "P001", LotNo = "LOT01", ReceivedQty = 60m, LocationCd = "L01" }
            }
        }, "u1");

        var h = await db.InboundOrders.SingleAsync();
        Assert.Equal(InboundOrderStatus.Completed, h.Status);
        var stock = await db.Stocks.SingleAsync();
        Assert.Equal(100m, stock.PhysicalQty);
        Assert.Equal(2, db.StockTransactions.Count());
        Assert.Equal(2, db.InboundReceipts.Count());
    }

    [Fact]
    public async Task ConfirmReceipt_DirectModeShouldWorkWithoutOrder()
    {
        var svc = CreateService(out var db);

        var rcNo = await svc.ConfirmReceiptAsync(new InboundReceiptDto
        {
            InboundNo = null, // 直入
            SourceType = InboundSourceType.Manual,
            WarehouseCd = "W01",
            Details = new List<InboundReceiptDetailDto>
            {
                new() { ProductCd = "P999", LotNo = "DIRECT", ReceivedQty = 50m, LocationCd = "L99" }
            }
        }, "u1");

        Assert.StartsWith("RC", rcNo);
        Assert.Equal(50m, (await db.Stocks.SingleAsync()).PhysicalQty);
        Assert.Empty(db.InboundOrders); // 予定は作られない
    }

    [Fact]
    public async Task ConfirmReceipt_OrderInWrongStatusShouldThrow()
    {
        var svc = CreateService(out _);
        var inNo = await svc.CreateOrderAsync(NewOrderDto(), "u1");
        // ConfirmOrder せず Draft のまま

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ConfirmReceiptAsync(new InboundReceiptDto
            {
                InboundNo = inNo,
                WarehouseCd = "W01",
                Details = new List<InboundReceiptDetailDto>
                {
                    new() { RefOrderLineNo = 1, ProductCd = "P001", LotNo = "LOT", ReceivedQty = 10, LocationCd = "L01" }
                }
            }, "u1"));
    }
}
