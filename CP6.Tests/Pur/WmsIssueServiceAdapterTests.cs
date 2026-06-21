using CP6.Core.EFDbContext;
using CP6.Core.Services.Pur.Contracts;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Pur;

/// <summary>
/// 外注 WMS 出库适配器单测（采购 章07，P2-D3 接桩→真实）。
/// 把 <see cref="IWmsIssueService"/> 桩换成委托真实 <see cref="StockMovementService"/> 的适配器：
/// 按物料选库存源 → OUT 扣减 → 返回真实 TxnNo；库存不足 → E-PUR-080。
/// </summary>
public class WmsIssueServiceAdapterTests
{
    private const string Paper = "PAPER-1";

    /// <summary>建一仓 + 真实 StockMovementService；返回 (db, 库存变动服务)。</summary>
    private static (CP6Context db, StockMovementService move) NewMove(bool allowNegative = false)
    {
        var db = TestHelper.CreateInMemoryContext();
        db.Warehouses.Add(new Warehouse
        {
            WarehouseCd = "W01",
            WarehouseName = "原料仓",
            WarehouseType = WarehouseType.RawMaterial,
            AllowNegative = allowNegative,
        });
        db.SaveChanges();
        return (db, new StockMovementService(db, new WmsSequenceService(db)));
    }

    /// <summary>入库铺底库存。</summary>
    private static Task SeedStockAsync(StockMovementService move, string product, decimal qty,
        string warehouse = "W01", string location = "L01", string lot = "")
        => move.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN,
            WarehouseCd = warehouse, LocationCd = location, ProductCd = product, LotNo = lot, Qty = qty,
        });

    [Fact]
    public async Task Issue_DeductsStock_ReturnsRealTxnNo()
    {
        var (db, move) = NewMove();
        await SeedStockAsync(move, Paper, 1000m);
        var adapter = new WmsIssueServiceAdapter(move, db);

        var result = await adapter.IssueAsync(new WmsIssueRequest
        {
            ItemId = Paper, Qty = 600m, Purpose = "subcontract", RefNo = "PO1-1",
        }, "u1");

        Assert.StartsWith("TXN", result.IssueNo);          // 真实 WMS 出库流水号
        Assert.Equal(600m, result.IssuedQty);
        var stock = await db.Stocks.SingleAsync(s => s.ProductCd == Paper);
        Assert.Equal(400m, stock.PhysicalQty);             // 1000 − 600
    }

    [Fact]
    public async Task Issue_InsufficientStock_ThrowsE080()
    {
        var (db, move) = NewMove(allowNegative: false);
        await SeedStockAsync(move, Paper, 100m);            // 只有 100，发 500 → 不足
        var adapter = new WmsIssueServiceAdapter(move, db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => adapter.IssueAsync(new WmsIssueRequest { ItemId = Paper, Qty = 500m, RefNo = "PO1-1" }, "u1"));
        Assert.Equal("E-PUR-080", ex.Message);             // WMS 不足异常 → 转采购本地化错误码
    }

    [Fact]
    public async Task Issue_NoStockForItem_ThrowsE080()
    {
        var (db, move) = NewMove();                         // 该物料无任何库存行
        var adapter = new WmsIssueServiceAdapter(move, db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => adapter.IssueAsync(new WmsIssueRequest { ItemId = Paper, Qty = 10m, RefNo = "PO1-1" }, "u1"));
        Assert.Equal("E-PUR-080", ex.Message);
    }

    [Fact]
    public async Task Issue_RespectsWarehouseFilter()
    {
        var (db, move) = NewMove();
        db.Warehouses.Add(new Warehouse { WarehouseCd = "W02", WarehouseName = "二号仓", WarehouseType = WarehouseType.RawMaterial });
        await db.SaveChangesAsync();
        await SeedStockAsync(move, Paper, 1000m, warehouse: "W02");   // 库存只在 W02

        var adapter = new WmsIssueServiceAdapter(move, db);

        // 指定 W01（无此料）→ 不足
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => adapter.IssueAsync(new WmsIssueRequest { ItemId = Paper, Qty = 100m, WarehouseCd = "W01", RefNo = "PO1-1" }, "u1"));
        Assert.Equal("E-PUR-080", ex.Message);

        // 不指定仓库 → 从 W02 发成功
        var ok = await adapter.IssueAsync(new WmsIssueRequest { ItemId = Paper, Qty = 100m, RefNo = "PO1-1" }, "u1");
        Assert.Equal(100m, ok.IssuedQty);
        Assert.Equal(900m, (await db.Stocks.SingleAsync(s => s.WarehouseCd == "W02")).PhysicalQty);
    }

    [Fact]
    public async Task Issue_RecordsSubcontractTxn_WithRefNo()
    {
        var (db, move) = NewMove();
        await SeedStockAsync(move, Paper, 1000m);
        var adapter = new WmsIssueServiceAdapter(move, db);

        await adapter.IssueAsync(new WmsIssueRequest { ItemId = Paper, Qty = 600m, RefNo = "PO1-1" }, "u1");

        var txn = await db.StockTransactions.SingleAsync(t => t.TxnType == WmsTxnType.OUT);
        Assert.Equal("SUBCONTRACT", txn.RelatedType);      // ★区分外注支給，非销售/生产领料
        Assert.Equal("PO1-1", txn.RelatedNo);
        Assert.Equal(Paper, txn.ProductCd);
        Assert.Equal(600m, txn.Qty);
    }
}
