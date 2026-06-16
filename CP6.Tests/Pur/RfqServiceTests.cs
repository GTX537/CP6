using CP6.Core.EFDbContext;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Pur;
using CP6.Core.Services.Pur.Contracts;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Pub;
using CP6.Entity.DomainModels.Pur;
using Xunit;

namespace CP6.Tests.Pur;

/// <summary>
/// 询价服务单测（RFQ，采购 章06 §2/§3）：从 PR 发起（只汇未定供应商行 + 行级追溯）/ 邀供应商（校验发注先 + 幂等）/ 收报价（须先被邀 + 建矩阵）。
/// </summary>
public class RfqServiceTests
{
    private const string SupA = "SUPA";
    private const string SupB = "SUPB";
    private const string NotSupplier = "CUST1"; // 非发注先
    private const string Item1 = "ITEM1";
    private const string Item2 = "ITEM2";
    private const string Item3 = "ITEM3";

    private static RfqService NewSvc(CP6Context db) => new(db, new SeqService(db));

    private static PurchaseRequestService NewPrSvc(CP6Context db) =>
        new(db, new SeqService(db), new StubApprovalService(),
            new PurchaseOrderService(db, new SupplierPriceService(db), new FxRateService(db),
                new SeqService(db), new StubApprovalService()));

    /// <summary>种子：两发注先 + 一非发注先（客户）+ RFQ/PR/PO 采番配置。</summary>
    private static async Task SeedAsync(CP6Context db)
    {
        db.BusinessPartners.Add(new BusinessPartner { BpCd = SupA, BpName = "供应甲", SupplierFlg = true, CurrencyCd = null, PurchasePostingDiv = "2" });
        db.BusinessPartners.Add(new BusinessPartner { BpCd = SupB, BpName = "供应乙", SupplierFlg = true, CurrencyCd = null, PurchasePostingDiv = "2" });
        db.BusinessPartners.Add(new BusinessPartner { BpCd = NotSupplier, BpName = "客户丙", SupplierFlg = false, CustomerFlg = true });
        db.Pub_DocSequences.Add(new Pub_DocSequence { BizKey = "RFQ", Prefix = "RFQ", DateFormat = "yyyyMMdd", SeqLength = 4, ResetCycle = 0 });
        db.Pub_DocSequences.Add(new Pub_DocSequence { BizKey = "PR", Prefix = "PR", DateFormat = "yyyyMMdd", SeqLength = 4, ResetCycle = 0 });
        db.Pub_DocSequences.Add(new Pub_DocSequence { BizKey = "PO", Prefix = "PO", DateFormat = "yyyyMMdd", SeqLength = 4, ResetCycle = 0 });
        await db.SaveChangesAsync();
    }

    /// <summary>建一张 PR（混合：有/无建议供应商行），返回 PrNo。</summary>
    private static async Task<string> CreatePrAsync(CP6Context db, params (string item, decimal qty, string? suggest)[] lines)
    {
        var prSvc = NewPrSvc(db);
        var pr = await prSvc.CreateAsync(new PrCreateDto
        {
            RequestDate = new DateTime(2026, 6, 1),
            Lines = lines.Select(l => new PrLineCreateDto
            {
                ItemId = l.item, Qty = l.qty, RequiredDate = new DateTime(2026, 6, 30),
                UnitCd = "EA", SuggestSupplierId = l.suggest,
            }).ToList(),
        }, "u1");
        return pr.PrNo;
    }

    // ───── CreateFromPr：只汇未定供应商行 + 行级追溯 ─────

    [Fact]
    public async Task CreateFromPr_AggregatesOnlyNoSupplierLines_WithTraceability_DraftStatus()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        // 三行：Item1@SupA(有供应商，不询), Item2@null(询), Item3@null(询)
        var prNo = await CreatePrAsync(db,
            (Item1, 10m, SupA),
            (Item2, 20m, null),
            (Item3, 30m, null));

        var rfq = await NewSvc(db).CreateFromPrAsync(prNo, "buyer1");

        Assert.StartsWith("RFQ", rfq.RfqNo);
        Assert.Equal(RfqStatus.Draft, rfq.Status);
        Assert.Equal(prNo, rfq.SourcePrNo);
        Assert.Equal("buyer1", rfq.Buyer);
        Assert.Equal(2, rfq.Lines.Count); // 仅两条无供应商行进 RFQ
        Assert.DoesNotContain(rfq.Lines, l => l.ItemId == Item1);

        // 行级追溯：RfqLine.SourcePrNo + SourcePrLineNo 指回 PR 行（Item2 是 PR 行 2、Item3 是 PR 行 3）
        var rl2 = rfq.Lines.First(l => l.ItemId == Item2);
        Assert.Equal(1, rl2.LineNo);            // RFQ 内重新编号 1..n
        Assert.Equal(prNo, rl2.SourcePrNo);
        Assert.Equal(2, rl2.SourcePrLineNo);    // 源 PR 行号 2
        Assert.Equal(20m, rl2.Qty);
        Assert.Equal("EA", rl2.UnitCd);

        var rl3 = rfq.Lines.First(l => l.ItemId == Item3);
        Assert.Equal(3, rl3.SourcePrLineNo);
    }

    [Fact]
    public async Task CreateFromPr_NoEligibleLines_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        // 全行都有建议供应商 → 无可询价行
        var prNo = await CreatePrAsync(db, (Item1, 10m, SupA), (Item2, 20m, SupB));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewSvc(db).CreateFromPrAsync(prNo, "buyer1"));
        Assert.Equal("E-PUR-060", ex.Message);
    }

    [Fact]
    public async Task CreateFromPr_PrNotFound_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewSvc(db).CreateFromPrAsync("PR-NOPE", "buyer1"));
        Assert.Equal("E-PUR-067", ex.Message);
    }

    // ───── AddSuppliers / Invite：校验发注先 + 幂等 + 状态推进 ─────

    [Fact]
    public async Task AddSuppliers_ValidSuppliers_Invited_StatusInviting()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);
        var prNo = await CreatePrAsync(db, (Item1, 10m, null), (Item2, 20m, null));
        var rfq = await svc.CreateFromPrAsync(prNo, "buyer1");

        var result = await svc.AddSuppliersAsync(rfq.RfqNo, new[] { SupA, SupB }, "buyer1");

        Assert.Equal(RfqStatus.Inviting, result.Status);
        Assert.Equal(2, result.Suppliers.Count);
        Assert.All(result.Suppliers, s => Assert.Equal(RfqInviteStatus.Invited, s.InviteStatus));
        Assert.Equal("供应甲", result.Suppliers.First(s => s.SupplierId == SupA).SupplierName); // 快照
    }

    [Fact]
    public async Task AddSuppliers_NonSupplierFlag_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);
        var prNo = await CreatePrAsync(db, (Item1, 10m, null));
        var rfq = await svc.CreateFromPrAsync(prNo, "buyer1");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.AddSuppliersAsync(rfq.RfqNo, new[] { NotSupplier }, "buyer1"));
        Assert.Equal("E-PUR-063", ex.Message);
    }

    [Fact]
    public async Task AddSuppliers_UnknownSupplier_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);
        var prNo = await CreatePrAsync(db, (Item1, 10m, null));
        var rfq = await svc.CreateFromPrAsync(prNo, "buyer1");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.AddSuppliersAsync(rfq.RfqNo, new[] { "GHOST" }, "buyer1"));
        Assert.Equal("E-PUR-062", ex.Message);
    }

    [Fact]
    public async Task AddSuppliers_Idempotent_NoDoubleAdd()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);
        var prNo = await CreatePrAsync(db, (Item1, 10m, null));
        var rfq = await svc.CreateFromPrAsync(prNo, "buyer1");

        await svc.AddSuppliersAsync(rfq.RfqNo, new[] { SupA }, "buyer1");
        // 二次邀同一家 + 一新家 → SupA 不重复，仅新增 SupB
        var result = await svc.AddSuppliersAsync(rfq.RfqNo, new[] { SupA, SupB }, "buyer1");

        Assert.Equal(2, result.Suppliers.Count);
        Assert.Equal(1, result.Suppliers.Count(s => s.SupplierId == SupA));
    }

    [Fact]
    public async Task AddSuppliers_RfqNotFound_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewSvc(db).AddSuppliersAsync("RFQ-NOPE", new[] { SupA }, "buyer1"));
        Assert.Equal("E-PUR-061", ex.Message);
    }

    // ───── RecordQuote：须先被邀 + 建报价矩阵 + 状态推进 ─────

    [Fact]
    public async Task RecordQuote_InvitedSupplier_BuildsMatrix_MarksQuoted_StatusQuoting()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);
        var prNo = await CreatePrAsync(db, (Item1, 10m, null), (Item2, 20m, null));
        var rfq = await svc.CreateFromPrAsync(prNo, "buyer1");
        await svc.AddSuppliersAsync(rfq.RfqNo, new[] { SupA, SupB }, "buyer1");

        // SupA 对两行各报一价
        var result = await svc.RecordQuoteAsync(rfq.RfqNo, SupA, new[]
        {
            new RfqQuoteLineDto { LineNo = 1, QuotedPrice = 8m, CurrencyCd = "JPY", LeadDays = 7, ValidUntil = new DateTime(2026, 7, 31) },
            new RfqQuoteLineDto { LineNo = 2, QuotedPrice = 15m, LeadDays = 10, ValidUntil = new DateTime(2026, 7, 31) },
        }, "buyer1");

        Assert.Equal(RfqStatus.Quoting, result.Status);
        Assert.Equal(RfqInviteStatus.Quoted, result.Suppliers.First(s => s.SupplierId == SupA).InviteStatus);
        Assert.Equal(RfqInviteStatus.Invited, result.Suppliers.First(s => s.SupplierId == SupB).InviteStatus); // 未报价的不变
        Assert.Equal(2, result.Quotes.Count(q => q.SupplierId == SupA));
        var q1 = result.Quotes.First(q => q.SupplierId == SupA && q.LineNo == 1);
        Assert.Equal(8m, q1.QuotedPrice);
        Assert.Equal(7, q1.LeadDays);
        Assert.False(q1.IsSelected); // B-1 默认未选中
        Assert.Equal(0, q1.Rank);    // B-1 默认未排名
    }

    [Fact]
    public async Task RecordQuote_Upsert_OverwritesPreviousQuoteSameLine()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);
        var prNo = await CreatePrAsync(db, (Item1, 10m, null));
        var rfq = await svc.CreateFromPrAsync(prNo, "buyer1");
        await svc.AddSuppliersAsync(rfq.RfqNo, new[] { SupA }, "buyer1");

        await svc.RecordQuoteAsync(rfq.RfqNo, SupA, new[]
        {
            new RfqQuoteLineDto { LineNo = 1, QuotedPrice = 10m },
        }, "buyer1");
        // 重报同行 → upsert 不新增，价更新
        var result = await svc.RecordQuoteAsync(rfq.RfqNo, SupA, new[]
        {
            new RfqQuoteLineDto { LineNo = 1, QuotedPrice = 9m },
        }, "buyer1");

        var quotes = result.Quotes.Where(q => q.SupplierId == SupA && q.LineNo == 1).ToList();
        Assert.Single(quotes);
        Assert.Equal(9m, quotes[0].QuotedPrice);
    }

    [Fact]
    public async Task RecordQuote_NotInvitedSupplier_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);
        var prNo = await CreatePrAsync(db, (Item1, 10m, null));
        var rfq = await svc.CreateFromPrAsync(prNo, "buyer1");
        await svc.AddSuppliersAsync(rfq.RfqNo, new[] { SupA }, "buyer1");

        // SupB 未被邀请 → 不能报价
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RecordQuoteAsync(rfq.RfqNo, SupB, new[]
        {
            new RfqQuoteLineDto { LineNo = 1, QuotedPrice = 8m },
        }, "buyer1"));
        Assert.Equal("E-PUR-064", ex.Message);
    }

    [Fact]
    public async Task RecordQuote_QuoteForNonexistentLine_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);
        var prNo = await CreatePrAsync(db, (Item1, 10m, null)); // 仅一行 → LineNo 1
        var rfq = await svc.CreateFromPrAsync(prNo, "buyer1");
        await svc.AddSuppliersAsync(rfq.RfqNo, new[] { SupA }, "buyer1");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RecordQuoteAsync(rfq.RfqNo, SupA, new[]
        {
            new RfqQuoteLineDto { LineNo = 99, QuotedPrice = 8m }, // 不存在的行
        }, "buyer1"));
        Assert.Equal("E-PUR-065", ex.Message);
    }

    [Fact]
    public async Task RecordQuote_NoLines_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);
        var prNo = await CreatePrAsync(db, (Item1, 10m, null));
        var rfq = await svc.CreateFromPrAsync(prNo, "buyer1");
        await svc.AddSuppliersAsync(rfq.RfqNo, new[] { SupA }, "buyer1");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.RecordQuoteAsync(rfq.RfqNo, SupA, Array.Empty<RfqQuoteLineDto>(), "buyer1"));
        Assert.Equal("E-PUR-066", ex.Message);
    }

    // ───── Get / List ─────

    [Fact]
    public async Task Get_LoadsLines_Suppliers_Quotes()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);
        var prNo = await CreatePrAsync(db, (Item1, 10m, null), (Item2, 20m, null));
        var created = await svc.CreateFromPrAsync(prNo, "buyer1");
        await svc.AddSuppliersAsync(created.RfqNo, new[] { SupA }, "buyer1");
        await svc.RecordQuoteAsync(created.RfqNo, SupA, new[]
        {
            new RfqQuoteLineDto { LineNo = 1, QuotedPrice = 8m },
        }, "buyer1");

        var rfq = await svc.GetAsync(created.RfqNo);

        Assert.NotNull(rfq);
        Assert.Equal(2, rfq!.Lines.Count);
        Assert.Single(rfq.Suppliers);
        Assert.Single(rfq.Quotes);
    }

    [Fact]
    public async Task List_FilterByStatus()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);
        var pr1 = await CreatePrAsync(db, (Item1, 10m, null));
        var pr2 = await CreatePrAsync(db, (Item2, 20m, null));
        var rfq1 = await svc.CreateFromPrAsync(pr1, "buyer1"); // Draft
        var rfq2 = await svc.CreateFromPrAsync(pr2, "buyer1");
        await svc.AddSuppliersAsync(rfq2.RfqNo, new[] { SupA }, "buyer1"); // → Inviting

        var drafts = await svc.ListAsync(RfqStatus.Draft);
        var inviting = await svc.ListAsync(RfqStatus.Inviting);

        Assert.Single(drafts);
        Assert.Single(inviting);
        Assert.Equal(rfq1.RfqNo, drafts[0].RfqNo);
    }
}
