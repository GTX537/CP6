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
/// 外注加工服务单测（采购 章07）。C-1：登记支給材（校验外注 PO Type=2 + 幂等 upsert）/ 发料（委托 WMS 出库、
/// 累加 IssuedQty 防吞料、分批补发、Purpose=subcontract 区分非销售/消耗）。
/// </summary>
public class SubcontractServiceTests
{
    private const string Sub = "SUBA";          // 外协（发注先）
    private const string Finished = "BOX-A";    // 外注成品（纸箱）
    private const string Paper = "PAPER-1";     // 支給材：原纸
    private const string Ink = "INK-1";         // 支給材：油墨

    private static SubcontractService NewSvc(CP6Context db, IWmsIssueService? wms = null, IFinCostService? finCost = null)
        => new(db, wms ?? new StubWmsIssueService(), finCost ?? new StubFinCostService());

    /// <summary>种子：一外协发注先 + PO 采番配置。</summary>
    private static async Task SeedAsync(CP6Context db)
    {
        db.BusinessPartners.Add(new BusinessPartner { BpCd = Sub, BpName = "外协甲", SupplierFlg = true, CurrencyCd = null, PurchasePostingDiv = "2" });
        db.Pub_DocSequences.Add(new Pub_DocSequence { BizKey = "PO", Prefix = "PO", DateFormat = "yyyyMMdd", SeqLength = 4, ResetCycle = 0 });
        await db.SaveChangesAsync();
    }

    /// <summary>建一张外注 PO（Type=2，成品行 UnitPrice=加工费），返回 PoNo。</summary>
    private static async Task<string> CreateSubcontractPoAsync(CP6Context db, int type = 2, decimal processingFee = 3m, decimal qty = 100m)
    {
        var poSvc = new PurchaseOrderService(db, new SupplierPriceService(db), new FxRateService(db),
            new SeqService(db), new StubApprovalService());
        var po = await poSvc.CreateAsync(new PoCreateDto
        {
            SupplierId = Sub,
            Type = type,
            OrderDate = new DateTime(2026, 6, 1),
            Lines = { new PoLineCreateDto { ItemId = Finished, Qty = qty, UnitPrice = processingFee } },
        }, "u1");
        return po.PoNo;
    }

    /// <summary>报价捕获桩：记录最后一次 IssueAsync 请求，返回全额实出。</summary>
    private sealed class CapturingWmsIssue : IWmsIssueService
    {
        public WmsIssueRequest? Last;
        public int Calls;
        public Task<WmsIssueResult> IssueAsync(WmsIssueRequest request, string? userName)
        {
            Last = request; Calls++;
            return Task.FromResult(new WmsIssueResult { IssueNo = $"OUT{Calls}", IssuedQty = request.Qty });
        }
    }

    // ───── AddConsign：校验外注 PO + 幂等 upsert ─────

    [Fact]
    public async Task AddConsign_RegistersMaterials_OnSubcontractPo()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var poNo = await CreateSubcontractPoAsync(db);

        var consigns = await NewSvc(db).AddConsignAsync(poNo, 1, new[]
        {
            new ConsignMaterialDto { ConsignItemId = Paper, ConsignQty = 1000m, ConsignUnitCost = 0.5m },
            new ConsignMaterialDto { ConsignItemId = Ink, ConsignQty = 5m, ConsignUnitCost = 20m },
        }, "u1");

        Assert.Equal(2, consigns.Count);
        var paper = consigns.First(c => c.ConsignItemId == Paper);
        Assert.Equal(poNo, paper.PoNo);
        Assert.Equal(1, paper.LineNo);
        Assert.Equal(1000m, paper.ConsignQty);
        Assert.Equal(0.5m, paper.ConsignUnitCost);
        Assert.Equal(0m, paper.IssuedQty);  // 登记时未发
    }

    [Fact]
    public async Task AddConsign_PoNotFound_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewSvc(db).AddConsignAsync("PO-NOPE", 1, new[] { new ConsignMaterialDto { ConsignItemId = Paper, ConsignQty = 10m } }, "u1"));
        Assert.Equal("E-PUR-071", ex.Message);
    }

    [Fact]
    public async Task AddConsign_NonSubcontractPo_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var poNo = await CreateSubcontractPoAsync(db, type: 1);  // 标准采购，非外注

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewSvc(db).AddConsignAsync(poNo, 1, new[] { new ConsignMaterialDto { ConsignItemId = Paper, ConsignQty = 10m } }, "u1"));
        Assert.Equal("E-PUR-072", ex.Message);
    }

    [Fact]
    public async Task AddConsign_LineNotFound_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var poNo = await CreateSubcontractPoAsync(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewSvc(db).AddConsignAsync(poNo, 99, new[] { new ConsignMaterialDto { ConsignItemId = Paper, ConsignQty = 10m } }, "u1"));
        Assert.Equal("E-PUR-073", ex.Message);
    }

    [Fact]
    public async Task AddConsign_NonPositiveQty_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var poNo = await CreateSubcontractPoAsync(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewSvc(db).AddConsignAsync(poNo, 1, new[] { new ConsignMaterialDto { ConsignItemId = Paper, ConsignQty = 0m } }, "u1"));
        Assert.Equal("E-PUR-074", ex.Message);
    }

    [Fact]
    public async Task AddConsign_Upsert_SameMaterial_NoDuplicate()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var poNo = await CreateSubcontractPoAsync(db);
        var svc = NewSvc(db);

        await svc.AddConsignAsync(poNo, 1, new[] { new ConsignMaterialDto { ConsignItemId = Paper, ConsignQty = 1000m, ConsignUnitCost = 0.5m } }, "u1");
        // 重登记同料 → upsert：数量/成本更新，不新增行
        var consigns = await svc.AddConsignAsync(poNo, 1, new[] { new ConsignMaterialDto { ConsignItemId = Paper, ConsignQty = 1200m, ConsignUnitCost = 0.6m } }, "u1");

        var paper = Assert.Single(consigns, c => c.ConsignItemId == Paper);
        Assert.Equal(1200m, paper.ConsignQty);
        Assert.Equal(0.6m, paper.ConsignUnitCost);
    }

    // ───── IssueConsign：委托 WMS 出库 + 累加 IssuedQty + 分批 + Purpose ─────

    [Fact]
    public async Task Issue_FullRemaining_AccumulatesIssuedQty_RecordsWmsNo()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var poNo = await CreateSubcontractPoAsync(db);
        var svc = NewSvc(db);
        await svc.AddConsignAsync(poNo, 1, new[]
        {
            new ConsignMaterialDto { ConsignItemId = Paper, ConsignQty = 1000m, ConsignUnitCost = 0.5m },
            new ConsignMaterialDto { ConsignItemId = Ink, ConsignQty = 5m, ConsignUnitCost = 20m },
        }, "u1");

        // issuances=null → 各支給材一次发齐剩余
        var result = await svc.IssueConsignAsync(poNo, 1, null, "u1");

        Assert.Equal(1000m, result.First(c => c.ConsignItemId == Paper).IssuedQty);
        Assert.Equal(5m, result.First(c => c.ConsignItemId == Ink).IssuedQty);
        Assert.All(result, c => Assert.False(string.IsNullOrEmpty(c.WmsIssueNo)));
    }

    [Fact]
    public async Task Issue_Batch_AccumulatesAcrossCalls()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var poNo = await CreateSubcontractPoAsync(db);
        var svc = NewSvc(db);
        await svc.AddConsignAsync(poNo, 1, new[] { new ConsignMaterialDto { ConsignItemId = Paper, ConsignQty = 1000m, ConsignUnitCost = 0.5m } }, "u1");

        // 分两批发：600 + 400 → 累加 1000
        await svc.IssueConsignAsync(poNo, 1, new[] { new ConsignIssueDto { ConsignItemId = Paper, Qty = 600m } }, "u1");
        var result = await svc.IssueConsignAsync(poNo, 1, new[] { new ConsignIssueDto { ConsignItemId = Paper, Qty = 400m } }, "u1");

        Assert.Equal(1000m, result.First(c => c.ConsignItemId == Paper).IssuedQty);
    }

    [Fact]
    public async Task Issue_PassesSubcontractPurpose_AndRefNo_ToWms()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var poNo = await CreateSubcontractPoAsync(db);
        var cap = new CapturingWmsIssue();
        var svc = NewSvc(db, cap);
        await svc.AddConsignAsync(poNo, 1, new[] { new ConsignMaterialDto { ConsignItemId = Paper, ConsignQty = 1000m, ConsignUnitCost = 0.5m } }, "u1");

        await svc.IssueConsignAsync(poNo, 1, null, "u1");

        Assert.NotNull(cap.Last);
        Assert.Equal("subcontract", cap.Last!.Purpose);   // ★非销售/消耗
        Assert.Equal($"{poNo}-1", cap.Last.RefNo);
        Assert.Equal(Paper, cap.Last.ItemId);
        Assert.Equal(1000m, cap.Last.Qty);                // 一次发齐剩余
    }

    [Fact]
    public async Task Issue_NoConsign_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var poNo = await CreateSubcontractPoAsync(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewSvc(db).IssueConsignAsync(poNo, 1, null, "u1"));
        Assert.Equal("E-PUR-075", ex.Message);
    }

    [Fact]
    public async Task Issue_NonPositiveBatchQty_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var poNo = await CreateSubcontractPoAsync(db);
        var svc = NewSvc(db);
        await svc.AddConsignAsync(poNo, 1, new[] { new ConsignMaterialDto { ConsignItemId = Paper, ConsignQty = 1000m, ConsignUnitCost = 0.5m } }, "u1");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.IssueConsignAsync(poNo, 1, new[] { new ConsignIssueDto { ConsignItemId = Paper, Qty = 0m } }, "u1"));
        Assert.Equal("E-PUR-076", ex.Message);
    }

    [Fact]
    public async Task GetConsign_FiltersByLine()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var poNo = await CreateSubcontractPoAsync(db);
        // 第二行成品（再建一张含两行的 PO 较繁；此处直接补一行 PO 行）
        db.PurchaseOrderLines.Add(new PurchaseOrderLine { PoNo = poNo, LineNo = 2, ItemId = "BOX-B", Qty = 50m, UnitPrice = 4m });
        await db.SaveChangesAsync();
        var svc = NewSvc(db);
        await svc.AddConsignAsync(poNo, 1, new[] { new ConsignMaterialDto { ConsignItemId = Paper, ConsignQty = 1000m, ConsignUnitCost = 0.5m } }, "u1");
        await svc.AddConsignAsync(poNo, 2, new[] { new ConsignMaterialDto { ConsignItemId = Ink, ConsignQty = 3m, ConsignUnitCost = 20m } }, "u1");

        var line1 = await svc.GetConsignAsync(poNo, 1);
        var all = await svc.GetConsignAsync(poNo);

        Assert.Equal(Paper, Assert.Single(line1).ConsignItemId);
        Assert.Equal(2, all.Count);
    }

    // ───── CalcFinishedCost（章07 §5）：成品成本=加工费+支給材成本并入；接财务 06 ─────

    /// <summary>成本入账捕获桩：记录请求，返回成功 + 假凭证号。</summary>
    private sealed class CapturingFinCost : IFinCostService
    {
        public SubcontractCostDto? Last;
        public Task<FinCostResult> PostSubcontractCostAsync(SubcontractCostDto dto, string? operatorId)
        {
            Last = dto;
            return Task.FromResult(FinCostResult.Pass("CV-001"));
        }
    }

    /// <summary>成本入账失败桩。</summary>
    private sealed class FailingFinCost : IFinCostService
    {
        public Task<FinCostResult> PostSubcontractCostAsync(SubcontractCostDto dto, string? operatorId)
            => Task.FromResult(FinCostResult.Fail("E-FIN-XXX"));
    }

    [Fact]
    public async Task CalcFinishedCost_ProcessingFeePlusConsignCost_Merged()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        // 加工费单价 3，成品 100 → 加工费 300
        var poNo = await CreateSubcontractPoAsync(db, processingFee: 3m, qty: 100m);
        var cap = new CapturingFinCost();
        var svc = NewSvc(db, finCost: cap);
        await svc.AddConsignAsync(poNo, 1, new[]
        {
            new ConsignMaterialDto { ConsignItemId = Paper, ConsignQty = 1000m, ConsignUnitCost = 0.5m }, // 500
            new ConsignMaterialDto { ConsignItemId = Ink, ConsignQty = 5m, ConsignUnitCost = 20m },        // 100
        }, "u1");

        var cost = await svc.CalcFinishedCostAsync(poNo, 1, finishedQty: 100m, "u1");

        Assert.Equal(300m, cost.ProcessingFee);                 // 加工费 = 3 × 100
        Assert.Equal(600m, cost.ConsignCost);                   // 支給材 = 500 + 100
        Assert.Equal(900m, cost.FinishedCost);                  // 并入 = 加工费 + 支給材
        Assert.Equal("CV-001", cost.CostVoucherNo);             // 接财务回执
        // 支給材成本"并入"非"另付"：成品成本含支給材
        Assert.Equal(cost.ProcessingFee + cost.ConsignCost, cost.FinishedCost);
    }

    [Fact]
    public async Task CalcFinishedCost_PostsBreakdownToFinance()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var poNo = await CreateSubcontractPoAsync(db, processingFee: 3m, qty: 100m);
        var cap = new CapturingFinCost();
        var svc = NewSvc(db, finCost: cap);
        await svc.AddConsignAsync(poNo, 1, new[] { new ConsignMaterialDto { ConsignItemId = Paper, ConsignQty = 1000m, ConsignUnitCost = 0.5m } }, "u1");

        await svc.CalcFinishedCostAsync(poNo, 1, finishedQty: 100m, "u1");

        Assert.NotNull(cap.Last);
        Assert.Equal(poNo, cap.Last!.PoNo);
        Assert.Equal(Finished, cap.Last.FinishedItemId);
        Assert.Equal(100m, cap.Last.FinishedQty);
        Assert.Equal(300m, cap.Last.ProcessingFee);
        Assert.Equal(500m, cap.Last.ConsignCost);
        Assert.Equal(800m, cap.Last.FinishedCost);
    }

    [Fact]
    public async Task CalcFinishedCost_NonPositiveQty_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var poNo = await CreateSubcontractPoAsync(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewSvc(db).CalcFinishedCostAsync(poNo, 1, finishedQty: 0m, "u1"));
        Assert.Equal("E-PUR-077", ex.Message);
    }

    [Fact]
    public async Task CalcFinishedCost_FinancePostFails_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var poNo = await CreateSubcontractPoAsync(db);
        var svc = NewSvc(db, finCost: new FailingFinCost());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CalcFinishedCostAsync(poNo, 1, finishedQty: 100m, "u1"));
        Assert.Equal("E-PUR-078", ex.Message);
    }

    [Fact]
    public async Task CalcFinishedCost_NonSubcontractPo_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var poNo = await CreateSubcontractPoAsync(db, type: 1);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewSvc(db).CalcFinishedCostAsync(poNo, 1, finishedQty: 100m, "u1"));
        Assert.Equal("E-PUR-072", ex.Message);
    }

    // ───── 只匹加工费（章07 §7）：三单匹配匹的是加工费，支給材不进匹配 ─────

    [Fact]
    public async Task ThreeWayMatch_SubcontractPo_MatchesProcessingFeeOnly_ConsignNotInAp()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        db.Pub_DocSequences.Add(new Pub_DocSequence { BizKey = "TWM", Prefix = "TWM", DateFormat = "yyyyMMdd", SeqLength = 4, ResetCycle = 0 });
        await db.SaveChangesAsync();

        // 外注 PO：加工费单价 3，成品 100 → PO 行净额仅含加工费 300
        var poSvc = new PurchaseOrderService(db, new SupplierPriceService(db), new FxRateService(db), new SeqService(db), new StubApprovalService());
        var po = await poSvc.CreateAsync(new PoCreateDto
        {
            SupplierId = Sub, Type = 2, OrderDate = new DateTime(2026, 6, 1),
            Lines = { new PoLineCreateDto { ItemId = Finished, Qty = 100m, UnitPrice = 3m } },
        }, "u1");
        await poSvc.SubmitForApprovalAsync(po.PoNo, "u1");

        // 登记一大笔支給材成本（500），收成品后验收锚就绪
        await NewSvc(db).AddConsignAsync(po.PoNo, 1, new[] { new ConsignMaterialDto { ConsignItemId = Paper, ConsignQty = 1000m, ConsignUnitCost = 0.5m } }, "u1");
        var line = await db.PurchaseOrderLines.FirstAsync(l => l.PoNo == po.PoNo);
        line.ReceivedQty = 100m; line.AcceptedQty = 100m;
        await db.SaveChangesAsync();

        // 外协开来的发票仅含加工费（PoLineNo=1, qty=100, price=3）
        var fakeAp = new ThreeWayMatchServiceTests_FakeFinAp();
        var matchSvc = new ThreeWayMatchService(db, fakeAp, poSvc, new SeqService(db));
        var r = await matchSvc.MatchInvoiceAsync(new MatchInvoiceDto
        {
            PoNo = po.PoNo, SupplierInvoiceNo = "SI-SUB-1", InvoiceDate = new DateTime(2026, 6, 10),
            Lines = { new MatchInvoiceLineDto { PoLineNo = 1, Qty = 100m, UnitPrice = 3m } },
        }, "u1");

        Assert.True(r.ApCreated);
        var ap = Assert.Single(fakeAp.Created);
        var apLine = Assert.Single(ap.Lines);
        Assert.Equal(3m, apLine.UnitPrice);     // ★只匹加工费（PO 行价=加工费）
        Assert.Equal(100m, apLine.Qty);
        // 支給材（500）不进 AP：AP 仅一行加工费，无支給材物料行
        Assert.DoesNotContain(ap.Lines, l => l.ItemId == Paper);
    }
}

/// <summary>本测试用建应付桩（记录请求，返回成功）。</summary>
file sealed class ThreeWayMatchServiceTests_FakeFinAp : CP6.Core.Services.Pur.Contracts.IFinApService
{
    public List<CP6.Core.Services.Pur.Contracts.PurApInvoiceDto> Created { get; } = new();
    public Task<CP6.Core.Services.Pur.Contracts.PurApResult> CreateApInvoiceAsync(CP6.Core.Services.Pur.Contracts.PurApInvoiceDto dto, string? operatorId)
    {
        Created.Add(dto);
        return Task.FromResult(CP6.Core.Services.Pur.Contracts.PurApResult.Pass(Guid.NewGuid(), $"AP-{Created.Count:D4}"));
    }
}
