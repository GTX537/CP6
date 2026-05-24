using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

/// <summary>
/// MSBBWM140 キッティング 単体テスト
///
/// テスト観点：
/// 1. CreateMaster — BOM 定義、二重登録拒否
/// 2. CreateOrder + Execute ASSEMBLE — 部品 OUT × N + キット品 IN × 1
/// 3. Execute 在庫不足 — InsufficientStockException
/// 4. Execute DISASSEMBLE — キット OUT + 部品 IN（ロット新規）
/// 5. Cancel Draft — 状態遷移
/// 6. Cancel Executed — 拒否
/// </summary>
public class KittingServiceTests
{
    private static (CP6.Core.EFDbContext.CP6Context db, KittingService svc, StockMovementService stock) Create()
    {
        var db = TestHelper.CreateInMemoryContext();
        db.Warehouses.Add(new Warehouse { WarehouseCd = "W01", WarehouseName = "M" });
        db.SaveChanges();
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        return (db, new KittingService(db, seq, stock), stock);
    }

    [Fact]
    public async Task CreateMaster_ShouldStoreBom()
    {
        var (db, svc, _) = Create();
        await svc.CreateMasterAsync(new KitMasterDto
        {
            KitSku = "BOX_SET_A", KitName = "ギフトボックスA",
            DefaultWarehouseCd = "W01",
            Components = new()
            {
                new() { ComponentProductCd = "BOX_BODY", RequiredQty = 1m, UnitCd = "EA" },
                new() { ComponentProductCd = "CUSHION",  RequiredQty = 2m, UnitCd = "EA" },
                new() { ComponentProductCd = "LABEL",    RequiredQty = 1m, UnitCd = "EA" },
            }
        }, "u");

        var m = await db.KitMasters.SingleAsync();
        Assert.Equal("BOX_SET_A", m.KitSku);
        Assert.Equal(3, db.KitMasterComponents.Count());
    }

    [Fact]
    public async Task CreateMaster_Duplicate_ShouldThrow()
    {
        var (_, svc, _) = Create();
        var dto = new KitMasterDto
        {
            KitSku = "K1", KitName = "K1",
            Components = new() { new() { ComponentProductCd = "C1", RequiredQty = 1 } }
        };
        await svc.CreateMasterAsync(dto, "u");
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateMasterAsync(dto, "u"));
    }

    [Fact]
    public async Task ExecuteAssemble_ShouldOutComponentsAndInKit()
    {
        var (db, svc, stock) = Create();
        // BOM: 1 ボックス = 1 ボディ + 2 クッション
        await svc.CreateMasterAsync(new KitMasterDto
        {
            KitSku = "BOX_SET", KitName = "ボックスセット",
            Components = new()
            {
                new() { ComponentProductCd = "BODY",  RequiredQty = 1 },
                new() { ComponentProductCd = "CUSHION", RequiredQty = 2 },
            }
        }, "u");

        // 部品在庫を積む
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01", LocationCd = "L01",
            ProductCd = "BODY", LotNo = "BL1", Qty = 10
        });
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01", LocationCd = "L02",
            ProductCd = "CUSHION", LotNo = "CL1", Qty = 20
        });

        // 10 セット組立
        var no = await svc.CreateOrderAsync(new KitOrderDto
        {
            KitSku = "BOX_SET", Qty = 10, Direction = KitOrderDirection.Assemble,
            WarehouseCd = "W01", KitLocationCd = "L_KIT", KitLotNo = "KIT_LOT_001",
        }, "u");
        await svc.ExecuteAsync(no, "u");

        // 部品在庫が消費された
        var body = await db.Stocks.SingleAsync(s => s.ProductCd == "BODY");
        Assert.Equal(0m, body.PhysicalQty); // 10 - 10
        var cushion = await db.Stocks.SingleAsync(s => s.ProductCd == "CUSHION");
        Assert.Equal(0m, cushion.PhysicalQty); // 20 - 20

        // キット品在庫が生成された
        var kit = await db.Stocks.SingleAsync(s => s.ProductCd == "BOX_SET");
        Assert.Equal(10m, kit.PhysicalQty);
        Assert.Equal("L_KIT", kit.LocationCd);
        Assert.Equal("KIT_LOT_001", kit.LotNo);

        // 指示が実行済
        var order = await db.KitOrders.SingleAsync();
        Assert.Equal(KitOrderStatus.Executed, order.Status);
        Assert.NotNull(order.ExecutedAt);
        Assert.NotNull(order.ExecutedTxnNos);
        // 3 個の TXN（2 部品 OUT + 1 キット IN）
        Assert.Equal(3, order.ExecutedTxnNos!.Split(';').Length);
    }

    [Fact]
    public async Task ExecuteAssemble_InsufficientStock_ShouldThrow()
    {
        var (_, svc, stock) = Create();
        await svc.CreateMasterAsync(new KitMasterDto
        {
            KitSku = "K", KitName = "K",
            Components = new() { new() { ComponentProductCd = "P1", RequiredQty = 5 } }
        }, "u");
        // 部品在庫不足
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01", LocationCd = "L01",
            ProductCd = "P1", LotNo = "L", Qty = 3
        });
        var no = await svc.CreateOrderAsync(new KitOrderDto
        {
            KitSku = "K", Qty = 1, Direction = KitOrderDirection.Assemble,
            WarehouseCd = "W01", KitLocationCd = "L_KIT",
        }, "u");
        await Assert.ThrowsAsync<InsufficientStockException>(() => svc.ExecuteAsync(no, "u"));
    }

    [Fact]
    public async Task ExecuteDisassemble_ShouldOutKitAndInComponents()
    {
        var (db, svc, stock) = Create();
        await svc.CreateMasterAsync(new KitMasterDto
        {
            KitSku = "K2", KitName = "K2",
            Components = new()
            {
                new() { ComponentProductCd = "C1", RequiredQty = 1 },
                new() { ComponentProductCd = "C2", RequiredQty = 3 },
            }
        }, "u");
        // キット品の在庫を積む
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01", LocationCd = "L_KIT",
            ProductCd = "K2", LotNo = "KIT_LOT", Qty = 5
        });

        var no = await svc.CreateOrderAsync(new KitOrderDto
        {
            KitSku = "K2", Qty = 5, Direction = KitOrderDirection.Disassemble,
            WarehouseCd = "W01", KitLocationCd = "L_KIT", KitLotNo = "KIT_LOT",
        }, "u");
        await svc.ExecuteAsync(no, "u");

        // キット品 0、部品が出現
        Assert.Equal(0m, (await db.Stocks.SingleAsync(s => s.ProductCd == "K2")).PhysicalQty);
        Assert.Equal(5m, (await db.Stocks.SingleAsync(s => s.ProductCd == "C1")).PhysicalQty);
        Assert.Equal(15m, (await db.Stocks.SingleAsync(s => s.ProductCd == "C2")).PhysicalQty);
    }

    [Fact]
    public async Task CancelDraft_ShouldChangeStatus()
    {
        var (db, svc, _) = Create();
        await svc.CreateMasterAsync(new KitMasterDto
        {
            KitSku = "K", KitName = "K",
            Components = new() { new() { ComponentProductCd = "P1", RequiredQty = 1 } }
        }, "u");
        var no = await svc.CreateOrderAsync(new KitOrderDto
        {
            KitSku = "K", Qty = 1, Direction = KitOrderDirection.Assemble,
            WarehouseCd = "W01", KitLocationCd = "L_KIT",
        }, "u");
        await svc.CancelAsync(no, "u");
        Assert.Equal(KitOrderStatus.Cancelled, (await db.KitOrders.SingleAsync()).Status);
    }

    [Fact]
    public async Task CancelExecuted_ShouldThrow()
    {
        var (_, svc, stock) = Create();
        await svc.CreateMasterAsync(new KitMasterDto
        {
            KitSku = "K", KitName = "K",
            Components = new() { new() { ComponentProductCd = "P1", RequiredQty = 1 } }
        }, "u");
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01", LocationCd = "L01",
            ProductCd = "P1", LotNo = "L", Qty = 10
        });
        var no = await svc.CreateOrderAsync(new KitOrderDto
        {
            KitSku = "K", Qty = 1, Direction = KitOrderDirection.Assemble,
            WarehouseCd = "W01", KitLocationCd = "L_KIT",
        }, "u");
        await svc.ExecuteAsync(no, "u");
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CancelAsync(no, "u"));
    }
}
