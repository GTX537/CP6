using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Pur;
using CP6.Core.Services.Pur.Contracts;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Pub;
using CP6.Entity.DomainModels.Pur;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Pur;

/// <summary>
/// 采购↔财务 全链路集成测试（无 fake，用财务真实 ApInvoiceService/自动凭证引擎）。
/// 证明跨模块接缝：PO→收货→三单匹配→财务真建并过账 ApInvoice（填 PurchaseOrderId + 生成借贷平衡凭证）。
/// </summary>
public class PurchaseToApIntegrationTests
{
    private const string Sup = "SUP1";
    private const string Item = "ITEM001";

    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    [Fact]
    public async Task FullChain_Po_To_Receipt_To_Match_BuildsRealPostedApInvoice_WithPoId()
    {
        using var db = NewDb();

        // ── 财务真实环境：科目表(含 1401 原材料 INVENTORY / AP_CONTROL / TAX_INPUT) + AP 记账规则 + 引擎 ──
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "t");
        PostingRuleSeed.EnsureSeeded(db);
        var engine = new AutoVoucherEngine(db,
            new JournalEntryService(db, new FiscalPeriodService(db, 1), new FinSequenceService(db)));
        var apSvc = new ApInvoiceService(db, engine, new FinSequenceService(db));   // 同时实现 IFinAp

        // ── 采购主数据 ──
        db.BusinessPartners.Add(new BusinessPartner
        {
            BpCd = Sup, BpName = "原纸供应商", SupplierFlg = true,
            CurrencyCd = null, PurchasePostingDiv = "1", PurchaseTaxCd = "P010",   // 着荷基准
        });
        db.TaxCodes.Add(new TaxCode { Code = "P010", Name = "进项10%", Rate = 0.10m, Direction = TaxDirection.Input, Recoverable = true });
        db.Pub_DocSequences.AddRange(
            new Pub_DocSequence { BizKey = "PO", Prefix = "PO", DateFormat = "yyyyMMdd", SeqLength = 4, ResetCycle = 0 },
            new Pub_DocSequence { BizKey = "GR", Prefix = "GR", DateFormat = "yyyyMMdd", SeqLength = 4, ResetCycle = 0 },
            new Pub_DocSequence { BizKey = "TWM", Prefix = "TWM", DateFormat = "yyyyMMdd", SeqLength = 4, ResetCycle = 0 });
        db.SupplierPrices.Add(new SupplierPrice { SupplierId = Sup, ItemId = Item, Price = 100m, MinQty = 1m, ValidFrom = new DateTime(2026, 1, 1) });
        await db.SaveChangesAsync();

        // ── 采购服务（接财务真实 IFinAp 适配器） ──
        var poSvc = new PurchaseOrderService(db, new SupplierPriceService(db), new FxRateService(db), new SeqService(db), new StubApprovalService());
        var grSvc = new GoodsReceiptService(db, new StubWmsReceiveService(), new StubWmsQcQuery(), poSvc, new SeqService(db));
        var adapter = new FinApServiceAdapter(db, apSvc);
        var matchSvc = new ThreeWayMatchService(db, adapter, poSvc, new SeqService(db));

        // 1) 建 PO（10 × 100）→ 送审 → 确认
        var po = await poSvc.CreateAsync(new PoCreateDto
        {
            SupplierId = Sup, OrderDate = new DateTime(2026, 6, 10),
            Lines = { new PoLineCreateDto { ItemId = Item, Qty = 10m } },
        }, "buyer");
        Assert.Equal(1000m, po.NetAmount);
        await poSvc.SubmitForApprovalAsync(po.PoNo, "buyer");

        // 2) 收货（着荷即验收 10）
        await grSvc.ConfirmReceiveAsync(new GrCreateDto
        {
            PoNo = po.PoNo, Lines = { new GrLineCreateDto { PoLineNo = 1, ReceivedQty = 10m } },
        }, "wh");

        // 3) 三单匹配（发票 10 × 100，同价）→ 财务真建并过账 AP
        var m = await matchSvc.MatchInvoiceAsync(new MatchInvoiceDto
        {
            PoNo = po.PoNo, SupplierInvoiceNo = "SI-INTEG-1", InvoiceDate = new DateTime(2026, 6, 12),
            Lines = { new MatchInvoiceLineDto { PoLineNo = 1, Qty = 10m, UnitPrice = 100m } },
        }, "ap-clerk");

        Assert.Equal(MatchStatus.Passed, m.Match.Status);
        Assert.True(m.ApCreated);

        // ── 验证：财务真生成了一张 已过账 ApInvoice，且填了 PurchaseOrderId ──
        var ap = await db.ApInvoices.Include(x => x.Lines).SingleAsync();
        Assert.Equal(po.Id, ap.PurchaseOrderId);                 // ★ 钩子兑现：采购填、财务存
        Assert.Equal(ApInvoiceStatus.Posted, ap.Status);
        Assert.NotNull(ap.JournalEntryId);                       // 自动凭证已过账
        Assert.Equal(1000m, ap.NetAmount);
        Assert.Equal(100m, ap.TaxAmount);
        Assert.Equal(1100m, ap.GrossAmount);

        // 凭证借贷平衡，应付科目带供应商
        var entry = await db.JournalEntries.Include(x => x.Lines).SingleAsync();
        Assert.Equal(entry.Lines.Sum(l => l.Debit), entry.Lines.Sum(l => l.Credit));
        var apCtrl = await gl.GetByRoleAsync("AP_CONTROL");
        Assert.Equal(Sup, entry.Lines.Single(l => l.AccountId == apCtrl!.Id).PartnerId);

        // ── 验证：PO 锚累加 + 派生关闭 ──
        var poLine = await db.PurchaseOrderLines.SingleAsync(l => l.PoNo == po.PoNo);
        Assert.Equal(10m, poLine.ReceivedQty);
        Assert.Equal(10m, poLine.AcceptedQty);
        Assert.Equal(10m, poLine.InvoicedQty);
        Assert.Equal(2, poLine.MatchStatus);
        var poAfter = await db.PurchaseOrders.SingleAsync(p => p.PoNo == po.PoNo);
        Assert.Equal(PoStatus.Closed, poAfter.Status);           // 收齐+开票齐+匹配完 → 关闭
    }

    [Fact]
    public async Task FullChain_Idempotent_SameSupplierInvoice_NotDoubleBilled()
    {
        using var db = NewDb();
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "t");
        PostingRuleSeed.EnsureSeeded(db);
        var engine = new AutoVoucherEngine(db,
            new JournalEntryService(db, new FiscalPeriodService(db, 1), new FinSequenceService(db)));
        var apSvc = new ApInvoiceService(db, engine, new FinSequenceService(db));

        db.BusinessPartners.Add(new BusinessPartner { BpCd = Sup, BpName = "原纸供应商", SupplierFlg = true, CurrencyCd = null, PurchasePostingDiv = "1", PurchaseTaxCd = "P010" });
        db.TaxCodes.Add(new TaxCode { Code = "P010", Name = "进项10%", Rate = 0.10m, Direction = TaxDirection.Input, Recoverable = true });
        db.Pub_DocSequences.AddRange(
            new Pub_DocSequence { BizKey = "PO", Prefix = "PO", DateFormat = "yyyyMMdd", SeqLength = 4, ResetCycle = 0 },
            new Pub_DocSequence { BizKey = "GR", Prefix = "GR", DateFormat = "yyyyMMdd", SeqLength = 4, ResetCycle = 0 },
            new Pub_DocSequence { BizKey = "TWM", Prefix = "TWM", DateFormat = "yyyyMMdd", SeqLength = 4, ResetCycle = 0 });
        db.SupplierPrices.Add(new SupplierPrice { SupplierId = Sup, ItemId = Item, Price = 100m, MinQty = 1m, ValidFrom = new DateTime(2026, 1, 1) });
        await db.SaveChangesAsync();

        var poSvc = new PurchaseOrderService(db, new SupplierPriceService(db), new FxRateService(db), new SeqService(db), new StubApprovalService());
        var grSvc = new GoodsReceiptService(db, new StubWmsReceiveService(), new StubWmsQcQuery(), poSvc, new SeqService(db));
        var matchSvc = new ThreeWayMatchService(db, new FinApServiceAdapter(db, apSvc), poSvc, new SeqService(db));

        var po = await poSvc.CreateAsync(new PoCreateDto { SupplierId = Sup, OrderDate = new DateTime(2026, 6, 10), Lines = { new PoLineCreateDto { ItemId = Item, Qty = 10m } } }, "buyer");
        await poSvc.SubmitForApprovalAsync(po.PoNo, "buyer");
        await grSvc.ConfirmReceiveAsync(new GrCreateDto { PoNo = po.PoNo, Lines = { new GrLineCreateDto { PoLineNo = 1, ReceivedQty = 10m } } }, "wh");

        var dto = new MatchInvoiceDto { PoNo = po.PoNo, SupplierInvoiceNo = "SI-DUP", InvoiceDate = new DateTime(2026, 6, 12), Lines = { new MatchInvoiceLineDto { PoLineNo = 1, Qty = 5m, UnitPrice = 100m } } };
        await matchSvc.MatchInvoiceAsync(dto, "ap-clerk");

        // 同一供应商发票号再匹配 → 采购侧幂等挡（不会进财务重复建票）
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => matchSvc.MatchInvoiceAsync(dto, "ap-clerk"));
        Assert.Equal("E-PUR-043", ex.Message);
        Assert.Single(await db.ApInvoices.ToListAsync());        // 仍只有一张 AP
    }
}
