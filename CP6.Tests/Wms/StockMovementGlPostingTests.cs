using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CP6.Tests.Wms;

/// <summary>
/// F1 財務油路 波A Task A.1：StockMovementService.ApplyAsync 後置点火 StockFinBridge の回帰テスト。
///
/// 検証観点（真 StockMovementService + 真 StockFinBridge + 真 AutoVoucherEngine + InMemory）：
///  ① 採購入庫（IN + RelatedType=INBOUND）→ Inventory.Received 凭证生成（借 INVENTORY / 贷 GRNI）。
///  ② 生産完工入庫（PRODUCTION 源 → RelatedType=INBOUND-FG）→ 凭证なし Skipped（波C 成本結転が担当・帰属泄漏封口）。
///  ③ 出庫（OUT + OUTBOUND）→ 凭证なし Skipped（波B 開票 / 波C 反冲が担当）。
///  ④ 盘盈（ADJ + STOCKTAKE、Qty>0）→ Inventory.AdjustGain 凭证生成（借 INVENTORY / 贷 NON_OP_INCOME）。
///  ⑤ InboundService 経由の PRODUCTION 源受入 → StockTransaction.RelatedType=INBOUND-FG かつ凭证なし（泄漏封口・源头実証）。
/// 桥点火は best-effort：库存移动主处理は桥の成否に依存せず必ず成功する。
/// </summary>
public class StockMovementGlPostingTests
{
    private static readonly DateTime Biz = new(2026, 6, 15);

    /// <summary>W01 倉庫 + Fin COA/PostingRule を播種し、真 StockFinBridge を配線した StockMovementService を返す。</summary>
    private static async Task<StockMovementService> CreateServiceAsync(CP6Context db)
    {
        db.Warehouses.Add(new Warehouse
        {
            WarehouseCd = "W01", WarehouseName = "メイン倉庫",
            WarehouseType = WarehouseType.RawMaterial, AllowNegative = false,
        });
        await db.SaveChangesAsync();

        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "t");
        PostingRuleSeed.EnsureSeeded(db);

        var journal = new JournalEntryService(db, new FiscalPeriodService(db, 1), new FinSequenceService(db));
        var engine = new AutoVoucherEngine(db, journal);
        var bridge = new StockFinBridge(db, engine, NullLogger<StockFinBridge>.Instance);

        var seq = new WmsSequenceService(db);
        return new StockMovementService(db, seq, notifier: null, finBridge: bridge);
    }

    private static StockMovementRequest Req(string txnType, decimal qty, decimal? unitPrice,
        string? relatedType, string location = "L01", string product = "P1", string lot = "LOT01")
        => new()
        {
            TxnType = txnType, WarehouseCd = "W01", LocationCd = location,
            ProductCd = product, LotNo = lot, Qty = qty, UnitPrice = unitPrice,
            RelatedType = relatedType, RelatedNo = "R1", OperatorCd = "wms",
        };

    /// <summary>指定 Role 科目上の借/贷発生額合計を回査。</summary>
    private static async Task<(decimal debit, decimal credit)> RoleAmountAsync(CP6Context db, string role)
    {
        var accId = await db.GlAccounts.Where(a => a.Role == role && a.IsActive).Select(a => a.Id).SingleAsync();
        var lines = await db.JournalLines.Where(l => l.AccountId == accId).ToListAsync();
        return (lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
    }

    // ───────── ① 採購入庫 IN + INBOUND → Inventory.Received ─────────

    [Fact]
    public async Task ApplyAsync_PurchaseInbound_Generates_InventoryReceived()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var svc = await CreateServiceAsync(db);

        var txnNo = await svc.ApplyAsync(Req(WmsTxnType.IN, 10m, 30m, "INBOUND"));
        Assert.StartsWith("TXN", txnNo);

        // 桥点火 → 借 INVENTORY 300 / 贷 GRNI 300（10×30）
        var inv = await RoleAmountAsync(db, "INVENTORY");
        var grni = await RoleAmountAsync(db, "GRNI");
        Assert.Equal(300m, inv.debit);
        Assert.Equal(300m, grni.credit);
        Assert.Equal(1, await db.JournalEntries.CountAsync(
            j => j.Source == VoucherSource.Inventory && j.Status == JournalStatus.Posted));
    }

    // ───────── ② 生産完工入庫 IN + INBOUND-FG → Skipped（凭证なし） ─────────

    [Fact]
    public async Task ApplyAsync_ProductionFgInbound_NoVoucher_Skipped()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var svc = await CreateServiceAsync(db);

        var txnNo = await svc.ApplyAsync(Req(WmsTxnType.IN, 10m, 30m, "INBOUND-FG"));
        Assert.StartsWith("TXN", txnNo);

        // 完工入庫は波C 成本結転が過账——桥は凭证を生成しない
        Assert.Equal(0, await db.JournalEntries.CountAsync());
        Assert.True(await db.IntegrationEvents.AnyAsync(e => e.SourceNo == txnNo && e.Status == "SKIPPED"));
    }

    // ───────── ③ 出庫 OUT + OUTBOUND → Skipped（凭证なし） ─────────

    [Fact]
    public async Task ApplyAsync_Outbound_NoVoucher_Skipped()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var svc = await CreateServiceAsync(db);

        // 先に在庫を積む（採購入庫）→ 出庫可能に
        await svc.ApplyAsync(Req(WmsTxnType.IN, 20m, 30m, "INBOUND"));
        var invBefore = (await RoleAmountAsync(db, "INVENTORY")).debit;

        var outTxn = await svc.ApplyAsync(Req(WmsTxnType.OUT, 5m, 30m, "OUTBOUND"));

        // 出庫は波B/波C 側で過账——桥は新たな凭证を生成しない（INVENTORY 借方は入庫分のまま不変）
        Assert.Equal(invBefore, (await RoleAmountAsync(db, "INVENTORY")).debit);
        Assert.True(await db.IntegrationEvents.AnyAsync(e => e.SourceNo == outTxn && e.Status == "SKIPPED"));
    }

    // ───────── ④ 盘盈 ADJ + STOCKTAKE（Qty>0）→ Inventory.AdjustGain ─────────

    [Fact]
    public async Task ApplyAsync_StockTakeGain_Generates_AdjustGain()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var svc = await CreateServiceAsync(db);

        var txnNo = await svc.ApplyAsync(Req(WmsTxnType.ADJ, 4m, 25m, "STOCKTAKE"));
        Assert.StartsWith("TXN", txnNo);

        // 借 INVENTORY 100 / 贷 NON_OP_INCOME 100（4×25）
        var inv = await RoleAmountAsync(db, "INVENTORY");
        var gain = await RoleAmountAsync(db, "NON_OP_INCOME");
        Assert.Equal(100m, inv.debit);
        Assert.Equal(100m, gain.credit);
    }

    // ───────── ⑤ InboundService 経由 PRODUCTION 源受入 → RelatedType=INBOUND-FG + 凭证なし（泄漏封口・源头） ─────────

    [Fact]
    public async Task InboundService_ProductionSource_EmitsInboundFg_And_NoVoucher()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var svc = await CreateServiceAsync(db);
        var seq = new WmsSequenceService(db);
        var inbound = new InboundService(db, seq, svc);

        var receiptNo = await inbound.ConfirmReceiptAsync(new InboundReceiptDto
        {
            SourceType = InboundSourceType.Production,
            WorkOrderNo = "WO-1",
            WarehouseCd = "W01",
            OperatorCd = "mes",
            Details = new List<InboundReceiptDetailDto>
            {
                new() { LineNo = 1, ProductCd = "FG1", LotNo = "WO-1", ReceivedQty = 8m, LocationCd = "FG-01", UnitPrice = 55m },
            },
        }, "mes");

        // 発行された StockTransaction は採購と区別する INBOUND-FG である
        var txn = await db.StockTransactions.SingleAsync(t => t.RelatedNo == receiptNo);
        Assert.Equal("INBOUND-FG", txn.RelatedType);

        // 帰属泄漏封口：完工入庫は Inventory.Received を生成しない（波C 成本結転が担当）
        Assert.Equal(0, await db.JournalEntries.CountAsync());
    }
}
