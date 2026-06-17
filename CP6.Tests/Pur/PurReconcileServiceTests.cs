using CP6.Core.EFDbContext;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Pur;
using CP6.Core.Services.Pur.Contracts;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Pub;
using CP6.Entity.DomainModels.Pur;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Pur;

/// <summary>
/// 采购对账单测（采购 章08/09 完整性收口）。PO↔GR↔AP 三方核对 + 堵三个漏：
/// 虚开发票（Invoiced&gt;Accepted）/ 超量·重复收货（Received&gt;Ordered+容差）/ 外协吞料（IssuedQty 对账）。
/// </summary>
public class PurReconcileServiceTests
{
    private const string Sup = "SUPRC";
    private const string Item = "ITEM-RC";
    private const string Finished = "BOX-RC";
    private const string Paper = "PAPER-RC";

    private static PurReconcileService NewSvc(CP6Context db) =>
        new(db, new SubcontractService(db, new StubWmsIssueService(), new StubFinCostService()));

    private static PurchaseOrderService NewPoSvc(CP6Context db) =>
        new(db, new SupplierPriceService(db), new FxRateService(db), new SeqService(db), new StubApprovalService());

    private static async Task SeedAsync(CP6Context db)
    {
        db.BusinessPartners.Add(new BusinessPartner { BpCd = Sup, BpName = "供应商RC", SupplierFlg = true, CurrencyCd = null, PurchasePostingDiv = "2" });
        db.Pub_DocSequences.Add(new Pub_DocSequence { BizKey = "PO", Prefix = "PO", DateFormat = "yyyyMMdd", SeqLength = 4, ResetCycle = 0 });
        await db.SaveChangesAsync();
    }

    /// <summary>建标准 PO（数量 100，单价 10），按给定三累计锚回填，返回 PoNo。</summary>
    private static async Task<string> StdPoAsync(CP6Context db, decimal received, decimal accepted, decimal invoiced, decimal qty = 100m)
    {
        var po = await NewPoSvc(db).CreateAsync(new PoCreateDto
        {
            SupplierId = Sup, Type = 1, OrderDate = new DateTime(2026, 6, 1),
            Lines = { new PoLineCreateDto { ItemId = Item, Qty = qty, UnitPrice = 10m } },
        }, "u1");
        var line = await db.PurchaseOrderLines.FirstAsync(l => l.PoNo == po.PoNo);
        line.ReceivedQty = received; line.AcceptedQty = accepted; line.InvoicedQty = invoiced;
        await db.SaveChangesAsync();
        return po.PoNo;
    }

    // ───── 正常诊断口径 ─────

    [Fact]
    public async Task Reconcile_FullyMatched_StatusCompleted_NoIssue()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var poNo = await StdPoAsync(db, received: 100m, accepted: 100m, invoiced: 100m);

        var rep = await NewSvc(db).ReconcilePoAsync(poNo);

        Assert.False(rep.HasIssue);
        var l = Assert.Single(rep.Lines);
        Assert.Equal("完成", l.Status);
        Assert.False(l.IsIssue);
        Assert.Equal(0m, l.OpenToReceive);
        Assert.Equal(0m, l.OpenToInvoice);
    }

    [Fact]
    public async Task Reconcile_PendingReceive()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var poNo = await StdPoAsync(db, received: 60m, accepted: 60m, invoiced: 60m);

        var l = Assert.Single((await NewSvc(db).ReconcilePoAsync(poNo)).Lines);
        Assert.Equal("待收", l.Status);
        Assert.Equal(40m, l.OpenToReceive);
        Assert.False(l.IsIssue);
    }

    [Fact]
    public async Task Reconcile_PendingQc()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var poNo = await StdPoAsync(db, received: 100m, accepted: 60m, invoiced: 60m);

        var l = Assert.Single((await NewSvc(db).ReconcilePoAsync(poNo)).Lines);
        Assert.Equal("待检", l.Status);
        Assert.False(l.IsIssue);
    }

    [Fact]
    public async Task Reconcile_PendingInvoice()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var poNo = await StdPoAsync(db, received: 100m, accepted: 100m, invoiced: 40m);

        var l = Assert.Single((await NewSvc(db).ReconcilePoAsync(poNo)).Lines);
        Assert.Equal("待开票", l.Status);
        Assert.Equal(60m, l.OpenToInvoice);
        Assert.False(l.IsIssue);
    }

    // ───── ① 防虚开/防重复开票：Invoiced > Accepted ─────

    [Fact]
    public async Task Reconcile_OverInvoice_FlagsFraud_HasIssue()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        // 合格 100，却开票 120 → 虚开嫌疑（没合格收货就开票套现 / 重复开票）
        var poNo = await StdPoAsync(db, received: 100m, accepted: 100m, invoiced: 120m);

        var rep = await NewSvc(db).ReconcilePoAsync(poNo);

        Assert.True(rep.HasIssue);
        var l = Assert.Single(rep.Lines);
        Assert.Equal("虚开嫌疑", l.Status);
        Assert.True(l.IsIssue);
    }

    [Fact]
    public async Task Reconcile_InvoiceWithoutAcceptedGoods_FlagsFraud()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        // 没有合格收货（accepted 0）却开票 50 → 虚开
        var poNo = await StdPoAsync(db, received: 0m, accepted: 0m, invoiced: 50m);

        var l = Assert.Single((await NewSvc(db).ReconcilePoAsync(poNo)).Lines);
        Assert.Equal("虚开嫌疑", l.Status);
        Assert.True(l.IsIssue);
    }

    // ───── ② 防超量/重复收货：Received > Ordered + 容差 ─────

    [Fact]
    public async Task Reconcile_OverReceipt_NoTolerance_FlagsIssue()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        // 订 100 收 130（无容差）→ 超量收货嫌疑
        var poNo = await StdPoAsync(db, received: 130m, accepted: 130m, invoiced: 0m);

        var rep = await NewSvc(db).ReconcilePoAsync(poNo);

        Assert.True(rep.HasIssue);
        var l = Assert.Single(rep.Lines);
        Assert.Equal("超量收货", l.Status);
        Assert.True(l.IsIssue);
        Assert.Equal(-30m, l.OpenToReceive);  // 负=超收
    }

    [Fact]
    public async Task Reconcile_OverReceipt_WithinTolerance_NoIssue()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        db.MatchTolerances.Add(new MatchTolerance { SupplierId = null, QtyTolPct = 0.05m, PriceTolPct = 0m, AmountTolAbs = 0m }); // 全局 5%
        await db.SaveChangesAsync();
        // 订 100 收 103（容差 5% → 上限 105）→ 正常
        var poNo = await StdPoAsync(db, received: 103m, accepted: 103m, invoiced: 0m);

        var rep = await NewSvc(db).ReconcilePoAsync(poNo);

        Assert.False(rep.HasIssue);
        Assert.False(Assert.Single(rep.Lines).IsIssue);
    }

    // ───── ③ 外协吞料：外注 PO 并入支給材防吞料对账 ─────

    /// <summary>建外注 PO（成品 100，加工费 3）+ 登记支給材（单耗 10）+ 发料 issued，回填合格成品 accepted。</summary>
    private static async Task<string> SubPoAsync(CP6Context db, decimal accepted, decimal issued, decimal consignQty = 1000m)
    {
        var po = await NewPoSvc(db).CreateAsync(new PoCreateDto
        {
            SupplierId = Sup, Type = 2, OrderDate = new DateTime(2026, 6, 1),
            Lines = { new PoLineCreateDto { ItemId = Finished, Qty = 100m, UnitPrice = 3m } },
        }, "u1");
        var sub = new SubcontractService(db, new StubWmsIssueService(), new StubFinCostService());
        await sub.AddConsignAsync(po.PoNo, 1, new[] { new ConsignMaterialDto { ConsignItemId = Paper, ConsignQty = consignQty, ConsignUnitCost = 0.5m } }, "u1");
        await sub.IssueConsignAsync(po.PoNo, 1, new[] { new ConsignIssueDto { ConsignItemId = Paper, Qty = issued } }, "u1");
        var line = await db.PurchaseOrderLines.FirstAsync(l => l.PoNo == po.PoNo);
        line.ReceivedQty = accepted; line.AcceptedQty = accepted;
        await db.SaveChangesAsync();
        return po.PoNo;
    }

    [Fact]
    public async Task Reconcile_Subcontract_ConsignAnomaly_FlagsPilferage()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        // 合格成品 80 → 应耗 = 单耗 10 × 80 = 800；实发 1000 → 超容差吞料
        var poNo = await SubPoAsync(db, accepted: 80m, issued: 1000m);

        var rep = await NewSvc(db).ReconcilePoAsync(poNo);

        Assert.True(rep.HasIssue);
        var cr = Assert.Single(rep.ConsignReconciles);
        Assert.True(cr.HasAnomaly);
        Assert.Equal(800m, cr.Lines[0].ExpectedQty);
        Assert.Equal(1000m, cr.Lines[0].IssuedQty);
    }

    [Fact]
    public async Task Reconcile_Subcontract_ConsignWithinTolerance_NoIssue()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        // 合格成品 100 → 应耗 1000 = 实发 1000 → 正常
        var poNo = await SubPoAsync(db, accepted: 100m, issued: 1000m);

        var rep = await NewSvc(db).ReconcilePoAsync(poNo);

        Assert.False(rep.HasIssue);
        Assert.Equal(2, rep.Type);
        Assert.False(Assert.Single(rep.ConsignReconciles).HasAnomaly);
    }

    [Fact]
    public async Task Reconcile_PoNotFound_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewSvc(db).ReconcilePoAsync("PO-NOPE"));
        Assert.Equal("E-PUR-027", ex.Message);
    }
}
