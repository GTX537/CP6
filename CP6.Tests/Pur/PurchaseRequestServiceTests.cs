using CP6.Core.EFDbContext;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Pur;
using CP6.Core.Services.Pur.Contracts;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Pub;
using CP6.Entity.DomainModels.Pur;
using CP6.Core.Services.Sys;
using Xunit;

namespace CP6.Tests.Pur;

/// <summary>采购申请服务单测（采购 章05 §4/§5）：手工建单 / 送审（桩即批）/ PR→PO 按建议供应商分组转单。</summary>
public class PurchaseRequestServiceTests
{
    private static readonly Guid ActorId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private const string SupA = "SUPA";
    private const string SupB = "SUPB";
    private const string Item1 = "ITEM1";
    private const string Item2 = "ITEM2";
    private const string Item3 = "ITEM3";

    private static PurchaseOrderService NewPoSvc(CP6Context db) =>
        new(db, new SupplierPriceService(db), new FxRateService(db), new SeqService(db), new StubApprovalService());

    private static PurchaseRequestService NewSvc(CP6Context db) =>
        new(db, new SeqService(db), new StubApprovalService(), NewPoSvc(db));

    private static UserPermissionContext Permission(string userName = "u1") => new()
    {
        UserId = ActorId,
        UserName = userName,
        DataScopes = { ["pur-pr"] = 5 },
    };

    private static Task<PurchaseRequest> Submit(PurchaseRequestService service, string prNo) =>
        service.SubmitForApprovalAsync(prNo, ActorId, "u1", Permission());

    /// <summary>种子：两发注先供应商 + PR/PO 采番配置。</summary>
    private static async Task SeedAsync(CP6Context db)
    {
        db.BusinessPartners.Add(new BusinessPartner { BpCd = SupA, BpName = "供应甲", SupplierFlg = true, CurrencyCd = null, PurchasePostingDiv = "2" });
        db.BusinessPartners.Add(new BusinessPartner { BpCd = SupB, BpName = "供应乙", SupplierFlg = true, CurrencyCd = null, PurchasePostingDiv = "2" });
        db.Pub_DocSequences.Add(new Pub_DocSequence { BizKey = "PR", Prefix = "PR", DateFormat = "yyyyMMdd", SeqLength = 4, ResetCycle = 0 });
        db.Pub_DocSequences.Add(new Pub_DocSequence { BizKey = "PO", Prefix = "PO", DateFormat = "yyyyMMdd", SeqLength = 4, ResetCycle = 0 });
        await db.SaveChangesAsync();
    }

    private static PrCreateDto NewPrDto(params PrLineCreateDto[] lines) => new()
    {
        RequestDate = new DateTime(2026, 6, 1),
        Lines = new List<PrLineCreateDto>(lines),
    };

    private static PrLineCreateDto Line(string itemId, decimal qty, decimal? estPrice, string? suggest) => new()
    {
        ItemId = itemId, Qty = qty, EstPrice = estPrice, SuggestSupplierId = suggest,
    };

    [Fact]
    public async Task Create_Manual_AssignsPrNo_DraftStatus_ManualSource_WithLines()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);

        var pr = await svc.CreateAsync(NewPrDto(Line(Item1, 10m, 5m, SupA)), "u1");

        Assert.StartsWith("PR", pr.PrNo);
        Assert.Equal(PrStatus.Draft, pr.Status);
        Assert.Equal(PrSource.Manual, pr.Source);
        var line = Assert.Single(pr.Lines);
        Assert.Equal(1, line.LineNo);
        Assert.Equal(Item1, line.ItemId);
        Assert.Equal(SupA, line.SuggestSupplierId);
    }

    [Fact]
    public async Task List_PopulatesLineCounts()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);
        await svc.CreateAsync(NewPrDto(Line(Item1, 10m, 5m, SupA), Line(Item2, 20m, null, null)), "u1"); // 2 行

        db.ChangeTracker.Clear(); // 模拟生产新上下文：禁导航修复，逼 ListAsync 显式载入行数
        var list = await svc.ListAsync();

        var row = Assert.Single(list);
        Assert.Equal(2, row.Lines.Count); // 列表行展示「行数」
    }

    [Fact]
    public async Task Create_NoLines_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateAsync(NewPrDto(), "u1"));
        Assert.Equal("E-PUR-050", ex.Message);
    }

    [Fact]
    public async Task Submit_Draft_AutoApproves_ToApproved_BackfillsRef()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);
        var pr = await svc.CreateAsync(NewPrDto(Line(Item1, 10m, 5m, SupA)), "u1");

        var submitted = await Submit(svc, pr.PrNo);

        Assert.Equal(PrStatus.Approved, submitted.Status);
        Assert.Equal($"AUTO-{pr.PrNo}", submitted.ApprovalRef);
    }

    [Fact]
    public async Task Submit_NonDraft_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);
        var pr = await svc.CreateAsync(NewPrDto(Line(Item1, 10m, 5m, SupA)), "u1");
        await Submit(svc, pr.PrNo); // → Approved

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Submit(svc, pr.PrNo));
        Assert.Equal("E-PUR-052", ex.Message);
    }

    [Fact]
    public async Task Convert_NotApproved_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);
        var pr = await svc.CreateAsync(NewPrDto(Line(Item1, 10m, 5m, SupA)), "u1"); // Draft

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ConvertToPoAsync(pr.PrNo, "u1"));
        Assert.Equal("E-PUR-053", ex.Message);
    }

    [Fact]
    public async Task Convert_GroupsBySupplier_SplitsMultiPo_BackfillsConvertedPoNo_StatusConverted()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);
        // 一 PR 三行两供应商：Item1@SupA, Item2@SupA, Item3@SupB → 拆 2 PO
        var pr = await svc.CreateAsync(NewPrDto(
            Line(Item1, 10m, 5m, SupA),
            Line(Item2, 20m, 7m, SupA),
            Line(Item3, 30m, 9m, SupB)), "u1");
        await Submit(svc, pr.PrNo);

        var poNos = await svc.ConvertToPoAsync(pr.PrNo, "u1");

        Assert.Equal(2, poNos.Count);                            // 两供应商 → 两 PO
        var reloaded = await svc.GetAsync(pr.PrNo);
        Assert.NotNull(reloaded);
        Assert.Equal(PrStatus.Converted, reloaded!.Status);      // 全行转出 → 已转 PO
        Assert.All(reloaded.Lines, l => Assert.False(string.IsNullOrEmpty(l.ConvertedPoNo)));
        // SupA 两行同一 PO；SupB 独立一 PO
        var poA = reloaded.Lines.First(l => l.ItemId == Item1).ConvertedPoNo;
        Assert.Equal(poA, reloaded.Lines.First(l => l.ItemId == Item2).ConvertedPoNo);
        Assert.NotEqual(poA, reloaded.Lines.First(l => l.ItemId == Item3).ConvertedPoNo);

        // 转出的 PO 行携带 EstPrice 作单价（PR→PO 带价）
        var poSvc = NewPoSvc(db);
        var createdA = await poSvc.GetAsync(poA!);
        Assert.NotNull(createdA);
        Assert.Equal(SupA, createdA!.SupplierId);
        Assert.Equal(2, createdA.Lines.Count);
        Assert.Equal(5m, createdA.Lines.First(l => l.ItemId == Item1).UnitPrice);
    }

    [Fact]
    public async Task Convert_LineWithoutSuggestSupplier_LeftUnconverted_StaysApproved()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);
        // 一行有建议供应商可转，一行无建议供应商（走 RFQ，留着）
        var pr = await svc.CreateAsync(NewPrDto(
            Line(Item1, 10m, 5m, SupA),
            Line(Item2, 20m, 7m, null)), "u1");
        await Submit(svc, pr.PrNo);

        var poNos = await svc.ConvertToPoAsync(pr.PrNo, "u1");

        Assert.Single(poNos);
        var reloaded = await svc.GetAsync(pr.PrNo);
        Assert.Equal(PrStatus.Approved, reloaded!.Status);       // 仍有未转行 → 保持已批（部分转）
        Assert.False(string.IsNullOrEmpty(reloaded.Lines.First(l => l.ItemId == Item1).ConvertedPoNo));
        Assert.True(string.IsNullOrEmpty(reloaded.Lines.First(l => l.ItemId == Item2).ConvertedPoNo));
    }

    [Fact]
    public async Task Convert_NothingConvertible_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);
        // 全行无建议供应商 → 无可转
        var pr = await svc.CreateAsync(NewPrDto(Line(Item1, 10m, 5m, null)), "u1");
        await Submit(svc, pr.PrNo);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ConvertToPoAsync(pr.PrNo, "u1"));
        Assert.Equal("E-PUR-054", ex.Message);
    }

    [Fact]
    public async Task Convert_Idempotent_AlreadyConvertedLineNotReconverted()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);
        var pr = await svc.CreateAsync(NewPrDto(
            Line(Item1, 10m, 5m, SupA),
            Line(Item2, 20m, 7m, null)), "u1");
        await Submit(svc, pr.PrNo);

        var first = await svc.ConvertToPoAsync(pr.PrNo, "u1"); // 转 SupA 行，Item2 留下
        Assert.Single(first);

        // 第二次：仅剩无建议供应商行 → 无可转 → 挡单
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ConvertToPoAsync(pr.PrNo, "u1"));
        Assert.Equal("E-PUR-054", ex.Message);
    }

    [Fact]
    public async Task List_FilterByStatus()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);
        var pr1 = await svc.CreateAsync(NewPrDto(Line(Item1, 10m, 5m, SupA)), "u1");
        await svc.CreateAsync(NewPrDto(Line(Item2, 10m, 5m, SupA)), "u1");
        await Submit(svc, pr1.PrNo); // pr1 → Approved

        var approved = await svc.ListAsync(status: PrStatus.Approved);
        var drafts = await svc.ListAsync(status: PrStatus.Draft);

        Assert.Single(approved);
        Assert.Single(drafts);
    }
}
