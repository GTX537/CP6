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

/// <summary>三单匹配单测（采购 章04，★MVP 核心）：容差匹配→建AP / 超容差挂起 / 防重复开票 / 放行拒绝。</summary>
public class ThreeWayMatchServiceTests
{
    private const string Sup = "SUP001";
    private const string Item = "ITEM001";

    /// <summary>建应付桩：记录请求，返回成功 + 假发票号。匹配逻辑不依赖财务 GL 种子。</summary>
    private sealed class FakeFinApService : IFinApService
    {
        public List<PurApInvoiceDto> Created { get; } = new();
        public Task<PurApResult> CreateApInvoiceAsync(PurApInvoiceDto dto, string? operatorId)
        {
            Created.Add(dto);
            return Task.FromResult(PurApResult.Pass(Guid.NewGuid(), $"AP-{Created.Count:D4}"));
        }
    }

    private static PurchaseOrderService NewPoSvc(CP6Context db) =>
        new(db, new SupplierPriceService(db), new FxRateService(db), new SeqService(db), new StubApprovalService());

    private static ThreeWayMatchService NewMatchSvc(CP6Context db, IFinApService finAp) =>
        new(db, finAp, NewPoSvc(db), new SeqService(db));

    private static async Task SeedAsync(CP6Context db)
    {
        db.BusinessPartners.Add(new BusinessPartner
        {
            BpCd = Sup, BpName = "外协甲", SupplierFlg = true,
            CurrencyCd = null, PurchasePostingDiv = "1", PurchaseTaxCd = "P010",
        });
        db.TaxCodes.Add(new TaxCode { Code = "P010", Name = "进项10%", Rate = 0.10m, Direction = TaxDirection.Input });
        db.Pub_DocSequences.AddRange(
            new Pub_DocSequence { BizKey = "PO", Prefix = "PO", DateFormat = "yyyyMMdd", SeqLength = 4, ResetCycle = 0 },
            new Pub_DocSequence { BizKey = "TWM", Prefix = "TWM", DateFormat = "yyyyMMdd", SeqLength = 4, ResetCycle = 0 });
        db.SupplierPrices.Add(new SupplierPrice
        {
            SupplierId = Sup, ItemId = Item, Price = 10m, MinQty = 1m, ValidFrom = new DateTime(2026, 1, 1),
        });
        await db.SaveChangesAsync();
    }

    /// <summary>建确认 PO（数量 100，单价 10）并直接置验收锚 accepted，返回 (poNo, poId)。</summary>
    private static async Task<(string PoNo, Guid PoId)> ConfirmedPoAsync(CP6Context db, decimal accepted)
    {
        var po = NewPoSvc(db);
        var created = await po.CreateAsync(new PoCreateDto
        {
            SupplierId = Sup, OrderDate = new DateTime(2026, 6, 1),
            Lines = { new PoLineCreateDto { ItemId = Item, Qty = 100m } },
        }, "u1");
        await po.SubmitForApprovalAsync(created.PoNo, "u1");

        var line = await db.PurchaseOrderLines.FirstAsync(l => l.PoNo == created.PoNo);
        line.ReceivedQty = accepted;
        line.AcceptedQty = accepted;
        await db.SaveChangesAsync();
        return (created.PoNo, created.Id);
    }

    private static MatchInvoiceDto Inv(string poNo, decimal qty, decimal price, string invNo = "SI-001") => new()
    {
        PoNo = poNo, SupplierInvoiceNo = invNo,
        InvoiceDate = new DateTime(2026, 6, 10),
        Lines = { new MatchInvoiceLineDto { PoLineNo = 1, Qty = qty, UnitPrice = price } },
    };

    [Fact]
    public async Task Match_WithinTolerance_AutoBuildsAp_FillsPoId_AccumulatesInvoiced()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var (poNo, poId) = await ConfirmedPoAsync(db, accepted: 100m);
        var fake = new FakeFinApService();
        var svc = NewMatchSvc(db, fake);

        var r = await svc.MatchInvoiceAsync(Inv(poNo, 100m, 10m), "u1");

        Assert.Equal(MatchStatus.Passed, r.Match.Status);
        Assert.True(r.ApCreated);
        Assert.Single(fake.Created);
        Assert.Equal(poId, fake.Created[0].PurchaseOrderId);          // ★ 填了 PurchaseOrderId
        Assert.Equal(1m, fake.Created[0].FxRate);                     // PO 冻结汇率透传
        var poLine = await db.PurchaseOrderLines.FirstAsync(l => l.PoNo == poNo);
        Assert.Equal(100m, poLine.InvoicedQty);                       // 锚累加
        Assert.Equal(2, poLine.MatchStatus);                          // 收齐→完成
    }

    [Fact]
    public async Task Match_OverPriceTolerance_Suspends_NoAp()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var (poNo, _) = await ConfirmedPoAsync(db, accepted: 100m);
        var fake = new FakeFinApService();
        var svc = NewMatchSvc(db, fake);

        // PO 价 10，发票价 12（20% 超 → 缺省容差 0 → 挂起）
        var r = await svc.MatchInvoiceAsync(Inv(poNo, 100m, 12m), "u1");

        Assert.Equal(MatchStatus.Suspended, r.Match.Status);
        Assert.False(r.ApCreated);
        Assert.Empty(fake.Created);
        var poLine = await db.PurchaseOrderLines.FirstAsync(l => l.PoNo == poNo);
        Assert.Equal(0m, poLine.InvoicedQty);                         // 锚不动
    }

    [Fact]
    public async Task Match_CannotOverInvoice_BeyondAccepted()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var (poNo, _) = await ConfirmedPoAsync(db, accepted: 90m);     // 仅验收 90
        var fake = new FakeFinApService();
        var svc = NewMatchSvc(db, fake);

        // 先开 90（用尽可开票量）
        await svc.MatchInvoiceAsync(Inv(poNo, 90m, 10m, "SI-A"), "u1");
        // 再开 10 → remainAccepted=0 → 挂起（防重复/超量开票）
        var r = await svc.MatchInvoiceAsync(Inv(poNo, 10m, 10m, "SI-B"), "u1");

        Assert.Equal(MatchStatus.Suspended, r.Match.Status);
        Assert.False(r.ApCreated);
        var poLine = await db.PurchaseOrderLines.FirstAsync(l => l.PoNo == poNo);
        Assert.Equal(90m, poLine.InvoicedQty);                        // 仍 90，未超开
    }

    [Fact]
    public async Task Match_DuplicateSupplierInvoice_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var (poNo, _) = await ConfirmedPoAsync(db, accepted: 100m);
        var svc = NewMatchSvc(db, new FakeFinApService());

        await svc.MatchInvoiceAsync(Inv(poNo, 50m, 10m, "SI-DUP"), "u1");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.MatchInvoiceAsync(Inv(poNo, 50m, 10m, "SI-DUP"), "u1"));
        Assert.Equal("E-PUR-043", ex.Message);
    }

    [Fact]
    public async Task Match_WithinConfiguredTolerance_Passes()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        db.MatchTolerances.Add(new MatchTolerance { SupplierId = null, PriceTolPct = 0.05m, AmountTolAbs = 0m }); // 全局 5%
        await db.SaveChangesAsync();
        var (poNo, _) = await ConfirmedPoAsync(db, accepted: 100m);
        var fake = new FakeFinApService();
        var svc = NewMatchSvc(db, fake);

        // 价 10.3（3% 超，<5% 容差）→ 通过
        var r = await svc.MatchInvoiceAsync(Inv(poNo, 100m, 10.3m), "u1");
        Assert.Equal(MatchStatus.Passed, r.Match.Status);
        Assert.Single(fake.Created);
    }

    [Fact]
    public async Task Release_SuspendedVariance_BuildsAp_Accumulates()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var (poNo, _) = await ConfirmedPoAsync(db, accepted: 100m);
        var fake = new FakeFinApService();
        var svc = NewMatchSvc(db, fake);

        var sus = await svc.MatchInvoiceAsync(Inv(poNo, 100m, 12m), "u1");   // 挂起
        Assert.Equal(MatchStatus.Suspended, sus.Match.Status);

        var rel = await svc.ReleaseAsync(sus.Match.MatchNo, "mgr", "价差经核可");
        Assert.Equal(MatchStatus.Released, rel.Match.Status);
        Assert.True(rel.ApCreated);
        Assert.Single(fake.Created);
        var poLine = await db.PurchaseOrderLines.FirstAsync(l => l.PoNo == poNo);
        Assert.Equal(100m, poLine.InvoicedQty);
        Assert.Equal("mgr", rel.Match.HandledBy);
    }

    [Fact]
    public async Task Reject_Suspended_SetsRejected_NoAp()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var (poNo, _) = await ConfirmedPoAsync(db, accepted: 100m);
        var fake = new FakeFinApService();
        var svc = NewMatchSvc(db, fake);

        var sus = await svc.MatchInvoiceAsync(Inv(poNo, 100m, 12m), "u1");
        var rej = await svc.RejectAsync(sus.Match.MatchNo, "mgr", "价格不符拒收");

        Assert.Equal(MatchStatus.Rejected, rej.Status);
        Assert.Empty(fake.Created);
        var poLine = await db.PurchaseOrderLines.FirstAsync(l => l.PoNo == poNo);
        Assert.Equal(0m, poLine.InvoicedQty);
    }

    // ───── 真实适配器：映射 + 委托财务 IFinAp + 借方按 GL 角色 INVENTORY ─────

    private sealed class FakeFinAp : IFinAp
    {
        public FinApInvoiceRequest? Last { get; private set; }
        public Task<FinApInvoiceResult> CreateInvoiceFromPurchaseAsync(FinApInvoiceRequest request, string operatorId)
        {
            Last = request;
            return Task.FromResult(new FinApInvoiceResult { InvoiceId = Guid.NewGuid(), InvoiceNo = "AP-X" });
        }
    }

    [Fact]
    public async Task Adapter_ResolvesInventoryAccount_MapsAndDelegates()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var inv = new GlAccount { Code = "1401", Name = "原材料", Type = AccountType.Asset, NormalSide = AccountSide.Debit, IsLeaf = true, IsActive = true, Role = "INVENTORY" };
        db.GlAccounts.Add(inv);
        await db.SaveChangesAsync();
        var fakeFinAp = new FakeFinAp();
        var adapter = new FinApServiceAdapter(db, fakeFinAp);
        var poId = Guid.NewGuid();

        var r = await adapter.CreateApInvoiceAsync(new PurApInvoiceDto
        {
            PurchaseOrderId = poId, SupplierId = Sup, SupplierInvoiceNo = "SI-1",
            InvoiceDate = new DateTime(2026, 6, 10), DueDate = new DateTime(2026, 7, 10),
            CurrencyCd = null, FxRate = 1m,
            Lines = { new PurApLineDto { ItemId = Item, Qty = 100m, UnitPrice = 10m } },
        }, "u1");

        Assert.True(r.Ok);
        Assert.Equal("AP-X", r.InvoiceNo);
        Assert.NotNull(fakeFinAp.Last);
        Assert.Equal(poId, fakeFinAp.Last!.PurchaseOrderId);                       // 透传 PurchaseOrderId
        Assert.Equal(inv.Id, fakeFinAp.Last!.Lines[0].ExpenseAccountId);           // 借方解析为 INVENTORY 科目
    }

    [Fact]
    public async Task Adapter_NoGlAccount_Fails()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var adapter = new FinApServiceAdapter(db, new FakeFinAp());

        var r = await adapter.CreateApInvoiceAsync(new PurApInvoiceDto
        {
            SupplierId = Sup, SupplierInvoiceNo = "SI-1",
            InvoiceDate = new DateTime(2026, 6, 10), DueDate = new DateTime(2026, 7, 10),
            Lines = { new PurApLineDto { ItemId = Item, Qty = 1m, UnitPrice = 1m } },
        }, "u1");

        Assert.False(r.Ok);
        Assert.Equal("E-PUR-044", r.Code);
    }
}
