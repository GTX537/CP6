using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
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

/// <summary>收货服务单测（采购 章03）：双基准（着荷/检收）+ 回写 PO 三累计锚 + 超收挡。</summary>
public class GoodsReceiptServiceTests
{
    private const string Sup = "SUP001";
    private const string Item = "ITEM001";

    private static PurchaseOrderService NewPoSvc(CP6Context db) =>
        new(db, new SupplierPriceService(db), new FxRateService(db), new SeqService(db), new StubApprovalService());

    private static GoodsReceiptService NewGrSvc(CP6Context db) =>
        new(db, new StubWmsReceiveService(), new StubWmsQcQuery(), NewPoSvc(db), new SeqService(db));

    /// <summary>种子：发注先供应商（postingDiv 决定双基准）+ 税码 + PO/GR 采番 + 采购价。</summary>
    private static async Task SeedAsync(CP6Context db, string postingDiv)
    {
        db.BusinessPartners.Add(new BusinessPartner
        {
            BpCd = Sup, BpName = "外协甲", SupplierFlg = true,
            CurrencyCd = null, PurchasePostingDiv = postingDiv, PurchaseTaxCd = "P010",
        });
        db.TaxCodes.Add(new TaxCode { Code = "P010", Name = "进项10%", Rate = 0.10m, Direction = TaxDirection.Input });
        db.Pub_DocSequences.AddRange(
            new Pub_DocSequence { BizKey = "PO", Prefix = "PO", DateFormat = "yyyyMMdd", SeqLength = 4, ResetCycle = 0 },
            new Pub_DocSequence { BizKey = "GR", Prefix = "GR", DateFormat = "yyyyMMdd", SeqLength = 4, ResetCycle = 0 });
        db.SupplierPrices.Add(new SupplierPrice
        {
            SupplierId = Sup, ItemId = Item, Price = 10m, MinQty = 1m, ValidFrom = new DateTime(2026, 1, 1),
        });
        await db.SaveChangesAsync();
    }

    /// <summary>建并确认一张 100 数量的 PO，返回 PoNo。</summary>
    private static async Task<string> ConfirmedPoAsync(CP6Context db)
    {
        var po = NewPoSvc(db);
        var dto = new PoCreateDto
        {
            SupplierId = Sup, OrderDate = new DateTime(2026, 6, 1),
            Lines = { new PoLineCreateDto { ItemId = Item, Qty = 100m } },
        };
        var created = await po.CreateAsync(dto, "u1");
        await po.SubmitForApprovalAsync(created.PoNo, "u1");   // → Confirmed
        return created.PoNo;
    }

    private static GrCreateDto Gr(string poNo, decimal qty) => new()
    {
        PoNo = poNo,
        Lines = { new GrLineCreateDto { PoLineNo = 1, ReceivedQty = qty } },
    };

    [Fact]
    public async Task Confirm_AccrualBasis_AcceptsImmediately_AndPoReceived()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db, postingDiv: "1");   // 着荷基准
        var poNo = await ConfirmedPoAsync(db);
        var grSvc = NewGrSvc(db);

        var gr = await grSvc.ConfirmReceiveAsync(Gr(poNo, 100m), "u1");

        Assert.Equal(GrStatus.Completed, gr.Status);          // 着荷 → 直接完成
        Assert.Equal("NONE", gr.Lines[0].QcStatus);            // 免检
        Assert.Equal(100m, gr.Lines[0].AcceptedQty);
        Assert.NotNull(gr.WmsInboundNo);                       // 委托 WMS 入库回填

        var poLine = await db.PurchaseOrderLines.FirstAsync(l => l.PoNo == poNo);
        Assert.Equal(100m, poLine.ReceivedQty);                // 锚回写
        Assert.Equal(100m, poLine.AcceptedQty);                // 着荷同步累加验收
        var po = await db.PurchaseOrders.FirstAsync(p => p.PoNo == poNo);
        Assert.Equal(PoStatus.Received, po.Status);            // 派生：收齐
    }

    [Fact]
    public async Task Confirm_InspectionBasis_AcceptsOnlyAfterQcPass()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db, postingDiv: "2");   // 检收基准
        var poNo = await ConfirmedPoAsync(db);
        var grSvc = NewGrSvc(db);

        var gr = await grSvc.ConfirmReceiveAsync(Gr(poNo, 100m), "u1");
        Assert.Equal(GrStatus.Inspecting, gr.Status);          // 检收 → 待检
        Assert.Equal("PENDING", gr.Lines[0].QcStatus);

        var poLine = await db.PurchaseOrderLines.FirstAsync(l => l.PoNo == poNo);
        Assert.Equal(100m, poLine.ReceivedQty);                // 已收
        Assert.Equal(0m, poLine.AcceptedQty);                  // 未验收

        // QC 应用（桩=全合格）→ 验收累加
        await grSvc.ApplyQcResultAsync(gr.GrNo, "u1");
        poLine = await db.PurchaseOrderLines.FirstAsync(l => l.PoNo == poNo);
        Assert.Equal(100m, poLine.AcceptedQty);                // QC 通过 → 验收
        var grAfter = await grSvc.GetAsync(gr.GrNo);
        Assert.Equal(GrStatus.Completed, grAfter!.Status);
        Assert.Equal("PASS", grAfter.Lines[0].QcStatus);
    }

    [Fact]
    public async Task Confirm_OverReceipt_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db, postingDiv: "1");
        var poNo = await ConfirmedPoAsync(db);
        var grSvc = NewGrSvc(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => grSvc.ConfirmReceiveAsync(Gr(poNo, 101m), "u1"));
        Assert.Equal("E-PUR-031", ex.Message);
    }

    [Fact]
    public async Task Confirm_PartialReceive_DerivesPartiallyReceived()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db, postingDiv: "1");
        var poNo = await ConfirmedPoAsync(db);
        var grSvc = NewGrSvc(db);

        await grSvc.ConfirmReceiveAsync(Gr(poNo, 40m), "u1");
        var po = await db.PurchaseOrders.FirstAsync(p => p.PoNo == poNo);
        Assert.Equal(PoStatus.PartiallyReceived, po.Status);

        await grSvc.ConfirmReceiveAsync(Gr(poNo, 60m), "u1");   // 累计 100
        po = await db.PurchaseOrders.FirstAsync(p => p.PoNo == poNo);
        Assert.Equal(PoStatus.Received, po.Status);
    }

    [Fact]
    public async Task Confirm_DraftPo_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db, postingDiv: "1");
        var poSvc = NewPoSvc(db);
        var created = await poSvc.CreateAsync(new PoCreateDto
        {
            SupplierId = Sup, OrderDate = new DateTime(2026, 6, 1),
            Lines = { new PoLineCreateDto { ItemId = Item, Qty = 100m } },
        }, "u1");   // 未送审，仍 Draft
        var grSvc = NewGrSvc(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => grSvc.ConfirmReceiveAsync(Gr(created.PoNo, 10m), "u1"));
        Assert.Equal("E-PUR-032", ex.Message);
    }
}
