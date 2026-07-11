using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CP6.Tests.Fin;

/// <summary>
/// F1 波0 Task 0.2：WMS→Fin 库存过账桥（StockFinBridge）。
/// 过账归属过滤——仅 {IN+INBOUND→Received} / {ADJ+STOCKTAKE 按符号→AdjustGain/Loss} / {*+SCRAP→Scrapped}
/// 三类生成凭证；OUTBOUND/ISSUE/MOVE 等一律 Skipped（波B/波C 各自过账，避免双记）。
/// 金额=|Qty×UnitPrice|，缺单价或非正额→Skipped（禁零额凭证）。best-effort：引擎异常不抛出。
/// 走真 AutoVoucherEngine + InMemory（同 FinBridgeHookTests 范式），断言凭证科目 Role + 金额。
/// </summary>
public class StockFinBridgeTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static readonly DateTime Biz = new(2026, 6, 15);

    private static async Task<StockFinBridge> SetupAsync(CP6Context db)
    {
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "t");
        PostingRuleSeed.EnsureSeeded(db);
        var journal = new JournalEntryService(db, new FiscalPeriodService(db, 1), new FinSequenceService(db));
        var engine = new AutoVoucherEngine(db, journal);
        return new StockFinBridge(db, engine, NullLogger<StockFinBridge>.Instance);
    }

    private static StockTransaction Txn(string txnNo, string txnType, decimal qty, decimal? unitPrice) => new()
    {
        TxnNo = txnNo, TxnType = txnType, TxnDateTime = Biz, WarehouseCd = "W01", LocationCd = "L01",
        ProductCd = "P1", LotNo = "", Qty = qty, UnitPrice = unitPrice,
    };

    // 凭证某 Role 科目上的借/贷发生额（走真引擎生成后回查）
    private static async Task<(decimal debit, decimal credit)> RoleAmountAsync(CP6Context db, string role)
    {
        var accId = await db.GlAccounts.Where(a => a.Role == role && a.IsActive).Select(a => a.Id).SingleAsync();
        var lines = await db.JournalLines.Where(l => l.AccountId == accId).ToListAsync();
        return (lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task InboundPurchase_Generates_InventoryReceived_DebitInventory_CreditGrni()
    {
        using var db = NewDb();
        var bridge = await SetupAsync(db);

        var r = await bridge.OnStockMovedAsync(Txn("TXN-IN1", "IN", 10m, 30m), "INBOUND", "wms");
        Assert.True(r.Success, r.Message);

        // 借 INVENTORY 300 / 贷 GRNI 300（10×30）
        var inv = await RoleAmountAsync(db, "INVENTORY");
        var grni = await RoleAmountAsync(db, "GRNI");
        Assert.Equal(300m, inv.debit);
        Assert.Equal(0m, inv.credit);
        Assert.Equal(300m, grni.credit);
        Assert.Equal(0m, grni.debit);

        // 单张 Inventory 来源凭证已过账 + Phase6 事件成功
        Assert.Equal(1, await db.JournalEntries.CountAsync(j => j.Source == VoucherSource.Inventory && j.Status == JournalStatus.Posted));
        Assert.True(await db.IntegrationEvents.AnyAsync(e => e.TargetModule == "FIN" && e.SourceNo == "TXN-IN1" && e.Status == "SUCCESS"));
    }

    [Fact]
    public async Task OutboundShipment_Skipped_NoVoucher()
    {
        using var db = NewDb();
        var bridge = await SetupAsync(db);

        var r = await bridge.OnStockMovedAsync(Txn("TXN-OUT1", "OUT", 5m, 30m), "OUTBOUND", "wms");
        Assert.False(r.Success);
        Assert.Contains("SKIPPED", r.Message);

        Assert.Equal(0, await db.JournalEntries.CountAsync());
        Assert.True(await db.IntegrationEvents.AnyAsync(e => e.SourceNo == "TXN-OUT1" && e.Status == "SKIPPED"));
    }

    [Fact]
    public async Task StockTake_PositiveQty_Generates_AdjustGain_DebitInventory_CreditNonOpIncome()
    {
        using var db = NewDb();
        var bridge = await SetupAsync(db);

        var r = await bridge.OnStockMovedAsync(Txn("TXN-ADJ+", "ADJ", 4m, 25m), "STOCKTAKE", "wms");
        Assert.True(r.Success, r.Message);

        var inv = await RoleAmountAsync(db, "INVENTORY");
        var gain = await RoleAmountAsync(db, "NON_OP_INCOME");
        Assert.Equal(100m, inv.debit);        // 4×25
        Assert.Equal(100m, gain.credit);
    }

    [Fact]
    public async Task StockTake_NegativeQty_Generates_AdjustLoss_DebitPendingLoss_CreditInventory_AbsAmount()
    {
        using var db = NewDb();
        var bridge = await SetupAsync(db);

        // 盘亏：数量为负，金额取绝对值，方向由规则借贷两侧表达
        var r = await bridge.OnStockMovedAsync(Txn("TXN-ADJ-", "ADJ", -4m, 25m), "STOCKTAKE", "wms");
        Assert.True(r.Success, r.Message);

        var loss = await RoleAmountAsync(db, "PENDING_PROPERTY_LOSS");
        var inv = await RoleAmountAsync(db, "INVENTORY");
        Assert.Equal(100m, loss.debit);       // |−4|×25
        Assert.Equal(100m, inv.credit);
    }

    [Fact]
    public async Task Scrap_Generates_InventoryScrapped_DebitNonOpExpense_CreditInventory()
    {
        using var db = NewDb();
        var bridge = await SetupAsync(db);

        var r = await bridge.OnStockMovedAsync(Txn("TXN-SCR", "ADJ", -2m, 50m), "SCRAP", "wms");
        Assert.True(r.Success, r.Message);

        var exp = await RoleAmountAsync(db, "NON_OP_EXPENSE");
        var inv = await RoleAmountAsync(db, "INVENTORY");
        Assert.Equal(100m, exp.debit);        // |−2|×50
        Assert.Equal(100m, inv.credit);
    }

    [Fact]
    public async Task MissingUnitPrice_Skipped_NoVoucher()
    {
        using var db = NewDb();
        var bridge = await SetupAsync(db);

        var r = await bridge.OnStockMovedAsync(Txn("TXN-NP", "IN", 10m, null), "INBOUND", "wms");
        Assert.False(r.Success);
        Assert.Contains("SKIPPED", r.Message);
        Assert.Equal(0, await db.JournalEntries.CountAsync());
        Assert.True(await db.IntegrationEvents.AnyAsync(e => e.SourceNo == "TXN-NP" && e.Status == "SKIPPED"));
    }

    [Fact]
    public async Task ZeroAmount_Skipped_NoVoucher()
    {
        using var db = NewDb();
        var bridge = await SetupAsync(db);

        var r = await bridge.OnStockMovedAsync(Txn("TXN-Z", "IN", 0m, 30m), "INBOUND", "wms");
        Assert.False(r.Success);
        Assert.Contains("SKIPPED", r.Message);
        Assert.Equal(0, await db.JournalEntries.CountAsync());
    }

    [Fact]
    public async Task Idempotent_SameTxnNo_NoDoubleVoucher()
    {
        using var db = NewDb();
        var bridge = await SetupAsync(db);

        await bridge.OnStockMovedAsync(Txn("TXN-IDEM", "IN", 10m, 30m), "INBOUND", "wms");
        await bridge.OnStockMovedAsync(Txn("TXN-IDEM", "IN", 10m, 30m), "INBOUND", "wms");   // 重放同 TxnNo

        Assert.Equal(1, await db.JournalEntries.CountAsync(j => j.Source == VoucherSource.Inventory && j.Status == JournalStatus.Posted));
    }

    [Fact]
    public async Task EngineThrows_ReturnsFailed_DoesNotThrow()
    {
        using var db = NewDb();
        var bridge = new StockFinBridge(db, new ThrowingEngine(), NullLogger<StockFinBridge>.Instance);

        // best-effort：引擎异常被握住，返回失败态而非抛出
        var r = await bridge.OnStockMovedAsync(Txn("TXN-ERR", "IN", 10m, 30m), "INBOUND", "wms");
        Assert.False(r.Success);
        Assert.True(await db.IntegrationEvents.AnyAsync(e => e.SourceNo == "TXN-ERR" && e.Status == "FAILED"));
    }

    private sealed class ThrowingEngine : IAutoVoucherEngine
    {
        public Task<FinResult> GenerateAsync(FinBizEvent evt) => throw new InvalidOperationException("boom");
    }
}
