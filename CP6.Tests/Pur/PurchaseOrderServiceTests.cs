using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Pur;
using CP6.Core.Services.Pur.Contracts;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Pub;
using CP6.Entity.DomainModels.Pur;
using Xunit;

namespace CP6.Tests.Pur;

/// <summary>采购订单服务单测（采购 章02）：建单带出 / 派生状态机 / 送审 / 取消。</summary>
public class PurchaseOrderServiceTests
{
    private const string Sup = "SUP001";
    private const string Item = "ITEM001";

    private static PurchaseOrderService NewSvc(CP6Context db) =>
        new(db, new SupplierPriceService(db), new FxRateService(db), new SeqService(db), new StubApprovalService());

    /// <summary>种子：发注先供应商 + 进项税码 P010(10%) + PO 采番配置 + 一档采购价。</summary>
    private static async Task SeedAsync(CP6Context db, bool supplierFlg = true)
    {
        db.BusinessPartners.Add(new BusinessPartner
        {
            BpCd = Sup, BpName = "外协甲", SupplierFlg = supplierFlg,
            CurrencyCd = null, PurchasePostingDiv = "2", PurchaseTaxCd = "P010",
        });
        db.TaxCodes.Add(new TaxCode { Code = "P010", Name = "进项10%", Rate = 0.10m, Direction = TaxDirection.Input });
        db.Pub_DocSequences.Add(new Pub_DocSequence
        {
            BizKey = "PO", Prefix = "PO", DateFormat = "yyyyMMdd", SeqLength = 4, ResetCycle = 0,
        });
        db.SupplierPrices.Add(new SupplierPrice
        {
            SupplierId = Sup, ItemId = Item, Price = 10m, MinQty = 1m, ValidFrom = new DateTime(2026, 1, 1),
        });
        await db.SaveChangesAsync();
    }

    private static PoCreateDto NewPoDto(decimal qty = 100m, decimal? price = null) => new()
    {
        SupplierId = Sup,
        OrderDate = new DateTime(2026, 6, 1),
        Lines = { new PoLineCreateDto { ItemId = Item, Qty = qty, UnitPrice = price } },
    };

    [Fact]
    public async Task Create_BringsDefaults_ResolvesPrice_CalcsAmounts_AnchorsZero()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);

        var po = await svc.CreateAsync(NewPoDto(qty: 100m), "u1");

        Assert.StartsWith("PO", po.PoNo);
        Assert.Equal("JPY", po.CurrencyCd);              // null 供应商币种 → 本位币
        Assert.Equal(1m, po.FxRate);                     // 本位币冻结 1
        Assert.Equal("2", po.PostingBasis);              // 供应商检收基准带出
        Assert.Equal(PoStatus.Draft, po.Status);
        var line = Assert.Single(po.Lines);
        Assert.Equal(10m, line.UnitPrice);               // 阶梯价解析
        Assert.Equal(1000m, line.NetAmount);             // 100×10
        Assert.Equal(100m, line.TaxAmount);              // 1000×10%
        Assert.Equal(0m, line.ReceivedQty);
        Assert.Equal(0m, line.AcceptedQty);
        Assert.Equal(0m, line.InvoicedQty);
        Assert.Equal(1000m, po.NetAmount);
        Assert.Equal(100m, po.TaxAmount);
        Assert.Equal(1100m, po.GrossAmount);
    }

    [Fact]
    public async Task Create_NonSupplier_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db, supplierFlg: false);
        var svc = NewSvc(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateAsync(NewPoDto(), "u1"));
        Assert.Equal("E-PUR-021", ex.Message);
    }

    [Fact]
    public async Task Create_NoApplicablePrice_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);

        // 数量 0.5 < 最低档 MinQty=1 → 无适用价且未手填 → 挡单
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateAsync(NewPoDto(qty: 0.5m), "u1"));
        Assert.Equal("E-PUR-022", ex.Message);
    }

    [Fact]
    public async Task Create_ExplicitPrice_OverridesTier()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);

        var po = await svc.CreateAsync(NewPoDto(qty: 10m, price: 12.5m), "u1");
        Assert.Equal(12.5m, po.Lines[0].UnitPrice);
        Assert.Equal(125m, po.Lines[0].NetAmount);
    }

    [Fact]
    public async Task Submit_Draft_AutoApproves_ToConfirmed()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);
        var po = await svc.CreateAsync(NewPoDto(), "u1");

        var submitted = await svc.SubmitForApprovalAsync(po.PoNo, "u1");
        Assert.Equal(PoStatus.Confirmed, submitted.Status);
        Assert.Equal($"AUTO-{po.PoNo}", submitted.ApprovalRef);
    }

    [Fact]
    public async Task Submit_NonDraft_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);
        var po = await svc.CreateAsync(NewPoDto(), "u1");
        await svc.SubmitForApprovalAsync(po.PoNo, "u1");   // → Confirmed

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SubmitForApprovalAsync(po.PoNo, "u1"));
        Assert.Equal("E-PUR-028", ex.Message);
    }

    [Fact]
    public async Task Cancel_Confirmed_Ok_ButReceived_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);
        var po = await svc.CreateAsync(NewPoDto(), "u1");
        await svc.SubmitForApprovalAsync(po.PoNo, "u1");   // Confirmed

        await svc.CancelAsync(po.PoNo, "u1");
        Assert.Equal(PoStatus.Cancelled, (await svc.GetAsync(po.PoNo))!.Status);
    }

    [Fact]
    public async Task RefreshStatus_DerivesFromAnchors()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);
        var po = await svc.CreateAsync(NewPoDto(qty: 100m), "u1");
        await svc.SubmitForApprovalAsync(po.PoNo, "u1");   // Confirmed

        // 模拟收货：行 ReceivedQty=AcceptedQty=100（收齐）
        var line = db.PurchaseOrderLines.First(l => l.PoNo == po.PoNo);
        line.ReceivedQty = 100m;
        line.AcceptedQty = 100m;
        await db.SaveChangesAsync();
        await svc.RefreshStatusAsync(po.PoNo);
        Assert.Equal(PoStatus.Received, (await svc.GetAsync(po.PoNo))!.Status);

        // 再模拟开票收齐 + 匹配完成 → 关闭
        line = db.PurchaseOrderLines.First(l => l.PoNo == po.PoNo);
        line.InvoicedQty = 100m;
        line.MatchStatus = 2;
        await db.SaveChangesAsync();
        await svc.RefreshStatusAsync(po.PoNo);
        Assert.Equal(PoStatus.Closed, (await svc.GetAsync(po.PoNo))!.Status);
    }

    [Theory]
    [InlineData(0, 0, 0, 0, PoStatus.Confirmed)]          // 未动
    [InlineData(50, 50, 0, 0, PoStatus.PartiallyReceived)] // 部分收
    [InlineData(100, 100, 0, 0, PoStatus.Received)]        // 收齐
    [InlineData(100, 100, 50, 1, PoStatus.PartiallyInvoiced)] // 部分开票
    [InlineData(100, 100, 100, 2, PoStatus.Closed)]        // 开票齐+匹配完
    public void DeriveStatus_Matrix(decimal recv, decimal acc, decimal inv, int match, PoStatus expected)
    {
        var lines = new[] { new PurchaseOrderLine { Qty = 100m, ReceivedQty = recv, AcceptedQty = acc, InvoicedQty = inv, MatchStatus = match } };
        Assert.Equal(expected, PurchaseOrderService.DeriveStatus(lines));
    }
}
