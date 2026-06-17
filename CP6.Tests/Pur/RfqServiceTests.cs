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

    private static RfqService NewSvc(CP6Context db) => new(db, new SeqService(db),
        new PurchaseOrderService(db, new SupplierPriceService(db), new FxRateService(db),
            new SeqService(db), new StubApprovalService()),
        new SupplierPriceService(db));

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

    // ───── RankQuotes（章06 §4）：按行分组比价；剔除过期；价格优先→同价比交期；Rank 是建议 ─────

    private const string SupC = "SUPC";

    /// <summary>追加第三家发注先 SUPC（默认 Seed 只有 SupA/SupB）。</summary>
    private static void SeedSupC(CP6Context db)
    {
        db.BusinessPartners.Add(new BusinessPartner { BpCd = SupC, BpName = "供应丙", SupplierFlg = true, CurrencyCd = null, PurchasePostingDiv = "2" });
        db.SaveChanges();
    }

    [Fact]
    public async Task Rank_PerLine_ExcludesExpired_PriceFirst()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        SeedSupC(db);
        var svc = NewSvc(db);
        var prNo = await CreatePrAsync(db, (Item1, 10m, null)); // 单行 LineNo 1
        var rfq = await svc.CreateFromPrAsync(prNo, "buyer1");
        await svc.AddSuppliersAsync(rfq.RfqNo, new[] { SupA, SupB, SupC }, "buyer1");

        var asOf = new DateTime(2026, 6, 16);
        // 行1: A 报 10（有效）/ B 报 8（已过期）/ C 报 9（有效）
        await svc.RecordQuoteAsync(rfq.RfqNo, SupA, new[]
        {
            new RfqQuoteLineDto { LineNo = 1, QuotedPrice = 10m, LeadDays = 5, ValidUntil = new DateTime(2026, 7, 31) },
        }, "buyer1");
        await svc.RecordQuoteAsync(rfq.RfqNo, SupB, new[]
        {
            new RfqQuoteLineDto { LineNo = 1, QuotedPrice = 8m, LeadDays = 5, ValidUntil = new DateTime(2026, 6, 1) }, // 过期
        }, "buyer1");
        await svc.RecordQuoteAsync(rfq.RfqNo, SupC, new[]
        {
            new RfqQuoteLineDto { LineNo = 1, QuotedPrice = 9m, LeadDays = 5, ValidUntil = new DateTime(2026, 7, 31) },
        }, "buyer1");

        var result = await svc.RankQuotesAsync(rfq.RfqNo, asOf, "buyer1");

        var b = result.Quotes.First(q => q.SupplierId == SupB && q.LineNo == 1);
        var c = result.Quotes.First(q => q.SupplierId == SupC && q.LineNo == 1);
        var a = result.Quotes.First(q => q.SupplierId == SupA && q.LineNo == 1);
        Assert.Equal(0, b.Rank);   // 过期 → 不参与排名
        Assert.Equal(1, c.Rank);   // 9 最低 → 第一
        Assert.Equal(2, a.Rank);   // 10 → 第二
        // Rank 仅是建议，不自动选中
        Assert.All(result.Quotes, q => Assert.False(q.IsSelected));
    }

    [Fact]
    public async Task Rank_SamePrice_BreaksTieByLeadDays()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        SeedSupC(db);
        var svc = NewSvc(db);
        var prNo = await CreatePrAsync(db, (Item1, 10m, null));
        var rfq = await svc.CreateFromPrAsync(prNo, "buyer1");
        await svc.AddSuppliersAsync(rfq.RfqNo, new[] { SupA, SupB, SupC }, "buyer1");

        var asOf = new DateTime(2026, 6, 16);
        // 同价 10：A 交期 7 / B 交期 3 / C 交期 null（视为最大，垫底）
        await svc.RecordQuoteAsync(rfq.RfqNo, SupA, new[]
        {
            new RfqQuoteLineDto { LineNo = 1, QuotedPrice = 10m, LeadDays = 7, ValidUntil = new DateTime(2026, 7, 31) },
        }, "buyer1");
        await svc.RecordQuoteAsync(rfq.RfqNo, SupB, new[]
        {
            new RfqQuoteLineDto { LineNo = 1, QuotedPrice = 10m, LeadDays = 3, ValidUntil = new DateTime(2026, 7, 31) },
        }, "buyer1");
        await svc.RecordQuoteAsync(rfq.RfqNo, SupC, new[]
        {
            new RfqQuoteLineDto { LineNo = 1, QuotedPrice = 10m, LeadDays = null, ValidUntil = new DateTime(2026, 7, 31) },
        }, "buyer1");

        var result = await svc.RankQuotesAsync(rfq.RfqNo, asOf, "buyer1");

        Assert.Equal(1, result.Quotes.First(q => q.SupplierId == SupB).Rank); // 交期 3 最短
        Assert.Equal(2, result.Quotes.First(q => q.SupplierId == SupA).Rank); // 交期 7
        Assert.Equal(3, result.Quotes.First(q => q.SupplierId == SupC).Rank); // 交期 null 垫底
    }

    [Fact]
    public async Task Rank_PerLine_Independent()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);
        var prNo = await CreatePrAsync(db, (Item1, 10m, null), (Item2, 20m, null)); // 两行
        var rfq = await svc.CreateFromPrAsync(prNo, "buyer1");
        await svc.AddSuppliersAsync(rfq.RfqNo, new[] { SupA, SupB }, "buyer1");

        var asOf = new DateTime(2026, 6, 16);
        // 行1: A 便宜；行2: B 便宜（按行各自排名，不串行）
        await svc.RecordQuoteAsync(rfq.RfqNo, SupA, new[]
        {
            new RfqQuoteLineDto { LineNo = 1, QuotedPrice = 5m, ValidUntil = new DateTime(2026, 7, 31) },
            new RfqQuoteLineDto { LineNo = 2, QuotedPrice = 99m, ValidUntil = new DateTime(2026, 7, 31) },
        }, "buyer1");
        await svc.RecordQuoteAsync(rfq.RfqNo, SupB, new[]
        {
            new RfqQuoteLineDto { LineNo = 1, QuotedPrice = 7m, ValidUntil = new DateTime(2026, 7, 31) },
            new RfqQuoteLineDto { LineNo = 2, QuotedPrice = 12m, ValidUntil = new DateTime(2026, 7, 31) },
        }, "buyer1");

        var result = await svc.RankQuotesAsync(rfq.RfqNo, asOf, "buyer1");

        Assert.Equal(1, result.Quotes.First(q => q.SupplierId == SupA && q.LineNo == 1).Rank);
        Assert.Equal(2, result.Quotes.First(q => q.SupplierId == SupB && q.LineNo == 1).Rank);
        Assert.Equal(1, result.Quotes.First(q => q.SupplierId == SupB && q.LineNo == 2).Rank);
        Assert.Equal(2, result.Quotes.First(q => q.SupplierId == SupA && q.LineNo == 2).Rank);
    }

    [Fact]
    public async Task Rank_RfqNotFound_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewSvc(db).RankQuotesAsync("RFQ-NOPE", null, "buyer1"));
        Assert.Equal("E-PUR-061", ex.Message);
    }

    // ───── Select（章06 §4）：按行选 + 校验未过期 + 一行一选 + 推 Status ─────

    /// <summary>建一张两行 RFQ，邀 A/B，各报有效价，已比价。返回 RfqNo。</summary>
    private static async Task<(RfqService svc, string rfqNo)> BuildQuotedRfqAsync(CP6Context db, DateTime asOf)
    {
        var svc = NewSvc(db);
        var prNo = await CreatePrAsync(db, (Item1, 10m, null), (Item2, 20m, null));
        var rfq = await svc.CreateFromPrAsync(prNo, "buyer1");
        await svc.AddSuppliersAsync(rfq.RfqNo, new[] { SupA, SupB }, "buyer1");
        await svc.RecordQuoteAsync(rfq.RfqNo, SupA, new[]
        {
            new RfqQuoteLineDto { LineNo = 1, QuotedPrice = 5m, ValidUntil = new DateTime(2026, 7, 31) },
            new RfqQuoteLineDto { LineNo = 2, QuotedPrice = 99m, ValidUntil = new DateTime(2026, 7, 31) },
        }, "buyer1");
        await svc.RecordQuoteAsync(rfq.RfqNo, SupB, new[]
        {
            new RfqQuoteLineDto { LineNo = 1, QuotedPrice = 7m, ValidUntil = new DateTime(2026, 7, 31) },
            new RfqQuoteLineDto { LineNo = 2, QuotedPrice = 12m, ValidUntil = new DateTime(2026, 7, 31) },
        }, "buyer1");
        await svc.RankQuotesAsync(rfq.RfqNo, asOf, "buyer1");
        return (svc, rfq.RfqNo);
    }

    [Fact]
    public async Task Select_PerLine_DifferentSuppliers_AllSelected_StatusSelected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var asOf = new DateTime(2026, 6, 16);
        var (svc, rfqNo) = await BuildQuotedRfqAsync(db, asOf);

        // 行1 选 A（便宜），行2 选 B（便宜）——按行拆不同供应商
        var result = await svc.SelectAsync(rfqNo, new[]
        {
            new RfqSelectionDto { LineNo = 1, SupplierId = SupA },
            new RfqSelectionDto { LineNo = 2, SupplierId = SupB },
        }, "buyer1");

        Assert.True(result.Quotes.First(q => q.LineNo == 1 && q.SupplierId == SupA).IsSelected);
        Assert.False(result.Quotes.First(q => q.LineNo == 1 && q.SupplierId == SupB).IsSelected);
        Assert.True(result.Quotes.First(q => q.LineNo == 2 && q.SupplierId == SupB).IsSelected);
        Assert.False(result.Quotes.First(q => q.LineNo == 2 && q.SupplierId == SupA).IsSelected);
        Assert.Equal(RfqStatus.Selected, result.Status); // 两行都选 → 已选定
    }

    [Fact]
    public async Task Select_ReselectLine_ClearsPriorSelectionOnSameLine()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var asOf = new DateTime(2026, 6, 16);
        var (svc, rfqNo) = await BuildQuotedRfqAsync(db, asOf);

        // 行1 先选 A
        await svc.SelectAsync(rfqNo, new[] { new RfqSelectionDto { LineNo = 1, SupplierId = SupA } }, "buyer1");
        // 行1 改选 B → A 应被清掉，仅 B 选中
        var result = await svc.SelectAsync(rfqNo, new[] { new RfqSelectionDto { LineNo = 1, SupplierId = SupB } }, "buyer1");

        Assert.False(result.Quotes.First(q => q.LineNo == 1 && q.SupplierId == SupA).IsSelected);
        Assert.True(result.Quotes.First(q => q.LineNo == 1 && q.SupplierId == SupB).IsSelected);
        Assert.Equal(1, result.Quotes.Count(q => q.LineNo == 1 && q.IsSelected)); // 一行一选
    }

    [Fact]
    public async Task Select_ExpiredQuote_Throws()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);
        var prNo = await CreatePrAsync(db, (Item1, 10m, null));
        var rfq = await svc.CreateFromPrAsync(prNo, "buyer1");
        await svc.AddSuppliersAsync(rfq.RfqNo, new[] { SupA }, "buyer1");
        // A 报价已过期
        await svc.RecordQuoteAsync(rfq.RfqNo, SupA, new[]
        {
            new RfqQuoteLineDto { LineNo = 1, QuotedPrice = 8m, ValidUntil = new DateTime(2026, 6, 1) },
        }, "buyer1");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SelectAsync(rfq.RfqNo, new[] { new RfqSelectionDto { LineNo = 1, SupplierId = SupA } }, "buyer1"));
        Assert.Equal("E-PUR-068", ex.Message);
    }

    [Fact]
    public async Task Select_QuoteNotFound_Throws()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var asOf = new DateTime(2026, 6, 16);
        var (svc, rfqNo) = await BuildQuotedRfqAsync(db, asOf);

        // 行1 没有 SUPC 的报价（SUPC 也没被邀）→ 找不到报价
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SelectAsync(rfqNo, new[] { new RfqSelectionDto { LineNo = 1, SupplierId = SupC } }, "buyer1"));
        Assert.Equal("E-PUR-069", ex.Message);
    }

    [Fact]
    public async Task Select_RfqNotFound_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewSvc(db).SelectAsync("RFQ-NOPE", new[] { new RfqSelectionDto { LineNo = 1, SupplierId = SupA } }, "buyer1"));
        Assert.Equal("E-PUR-061", ex.Message);
    }

    // ───── WriteBackPrices（章06 §5）：选中报价回写价表 Source=rfq；落选不写 ─────

    /// <summary>建两行 RFQ → 报价 → 比价 → 行1选A(5)/行2选B(12)。返回 (svc, rfqNo)。</summary>
    private static async Task<(RfqService svc, string rfqNo)> BuildSelectedRfqAsync(CP6Context db, DateTime asOf)
    {
        var (svc, rfqNo) = await BuildQuotedRfqAsync(db, asOf);
        await svc.SelectAsync(rfqNo, new[]
        {
            new RfqSelectionDto { LineNo = 1, SupplierId = SupA }, // A@5（便宜）
            new RfqSelectionDto { LineNo = 2, SupplierId = SupB }, // B@12（便宜）
        }, "buyer1");
        return (svc, rfqNo);
    }

    [Fact]
    public async Task WriteBack_OnlySelectedQuotes_UpsertPriceTable_SourceRfq()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var asOf = new DateTime(2026, 6, 16);
        var (svc, rfqNo) = await BuildSelectedRfqAsync(db, asOf);

        await svc.WriteBackPricesAsync(rfqNo, "buyer1");

        var prices = new SupplierPriceService(db);
        var aPrices = await prices.ListAsync(SupA);
        var bPrices = await prices.ListAsync(SupB);

        // 选中：A×Item1@5、B×Item2@12 → 各回写一档，Source=rfq，ValidTo=报价有效期
        var a1 = Assert.Single(aPrices);
        Assert.Equal(Item1, a1.ItemId);
        Assert.Equal(5m, a1.Price);
        Assert.Equal("rfq", a1.Source);
        Assert.Equal(new DateTime(2026, 7, 31), a1.ValidTo);

        var b2 = Assert.Single(bPrices);
        Assert.Equal(Item2, b2.ItemId);
        Assert.Equal(12m, b2.Price);
        Assert.Equal("rfq", b2.Source);

        // 落选报价（行1 B@7、行2 A@99）不回写
        Assert.DoesNotContain(bPrices, p => p.ItemId == Item1);
        Assert.DoesNotContain(aPrices, p => p.ItemId == Item2);
    }

    [Fact]
    public async Task WriteBack_Idempotent_RerunNoDuplicate()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var asOf = new DateTime(2026, 6, 16);
        var (svc, rfqNo) = await BuildSelectedRfqAsync(db, asOf);

        await svc.WriteBackPricesAsync(rfqNo, "buyer1");
        await svc.WriteBackPricesAsync(rfqNo, "buyer1"); // 再跑一次（同日同业务键）→ 幂等

        var prices = new SupplierPriceService(db);
        Assert.Single(await prices.ListAsync(SupA));
        Assert.Single(await prices.ListAsync(SupB));
    }

    [Fact]
    public async Task WriteBack_RfqNotFound_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewSvc(db).WriteBackPricesAsync("RFQ-NOPE", "buyer1"));
        Assert.Equal("E-PUR-061", ex.Message);
    }

    // ───── ConvertToPo（章06 §6）：按选中供应商分组、用成交价、推 Converted ─────

    [Fact]
    public async Task Convert_GroupBySelectedSupplier_UsesQuotedPrice_StatusConverted()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var asOf = new DateTime(2026, 6, 16);
        var (svc, rfqNo) = await BuildSelectedRfqAsync(db, asOf);

        var poNos = await svc.ConvertToPoAsync(rfqNo, "buyer1");

        Assert.Equal(2, poNos.Count); // 两供应商各一张 PO

        var poA = db.PurchaseOrders.First(p => poNos.Contains(p.PoNo) && p.SupplierId == SupA);
        var poB = db.PurchaseOrders.First(p => poNos.Contains(p.PoNo) && p.SupplierId == SupB);
        Assert.Equal(rfqNo, poA.SourceRfqNo); // 行级追溯回 RFQ
        Assert.Equal(rfqNo, poB.SourceRfqNo);

        var aLine = db.PurchaseOrderLines.Where(l => l.PoNo == poA.PoNo).ToList();
        var bLine = db.PurchaseOrderLines.Where(l => l.PoNo == poB.PoNo).ToList();
        Assert.Equal(Item1, Assert.Single(aLine).ItemId);
        Assert.Equal(5m, aLine[0].UnitPrice);   // ★成交价=询价价，非价表取价
        Assert.Equal(Item2, Assert.Single(bLine).ItemId);
        Assert.Equal(12m, bLine[0].UnitPrice);

        Assert.Equal(RfqStatus.Converted, (await svc.GetAsync(rfqNo))!.Status);
    }

    [Fact]
    public async Task Convert_SameSupplierMultipleLines_OnePo()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var asOf = new DateTime(2026, 6, 16);
        var (svc, rfqNo) = await BuildQuotedRfqAsync(db, asOf);
        // 两行都选 A（行2 A@99 有效）→ 同供应商合一张 PO
        await svc.SelectAsync(rfqNo, new[]
        {
            new RfqSelectionDto { LineNo = 1, SupplierId = SupA },
            new RfqSelectionDto { LineNo = 2, SupplierId = SupA },
        }, "buyer1");

        var poNos = await svc.ConvertToPoAsync(rfqNo, "buyer1");

        Assert.Single(poNos);
        var lines = db.PurchaseOrderLines.Where(l => l.PoNo == poNos[0]).ToList();
        Assert.Equal(2, lines.Count); // 一张 PO 两行
    }

    [Fact]
    public async Task Convert_NoSelection_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var asOf = new DateTime(2026, 6, 16);
        var (svc, rfqNo) = await BuildQuotedRfqAsync(db, asOf); // 比价但未选定

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ConvertToPoAsync(rfqNo, "buyer1"));
        Assert.Equal("E-PUR-070", ex.Message); // 无选中报价
    }

    [Fact]
    public async Task Convert_SelectedButExpiredQuote_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);
        var prNo = await CreatePrAsync(db, (Item1, 10m, null));
        var rfq = await svc.CreateFromPrAsync(prNo, "buyer1");
        await svc.AddSuppliersAsync(rfq.RfqNo, new[] { SupA }, "buyer1");
        await svc.RecordQuoteAsync(rfq.RfqNo, SupA, new[]
        {
            new RfqQuoteLineDto { LineNo = 1, QuotedPrice = 8m, ValidUntil = new DateTime(2026, 7, 31) },
        }, "buyer1");
        // 模拟"选定后才过期"：直接置选中 + 改成已过期，转 PO 第二道关应拦
        var q = db.RfqQuotes.First(x => x.RfqNo == rfq.RfqNo && x.SupplierId == SupA && x.LineNo == 1);
        q.IsSelected = true;
        q.ValidUntil = new DateTime(2026, 6, 1);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ConvertToPoAsync(rfq.RfqNo, "buyer1"));
        Assert.Equal("E-PUR-068", ex.Message); // 选中报价已过期（转 PO 第二道关）
    }

    [Fact]
    public async Task Convert_RfqNotFound_Rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewSvc(db).ConvertToPoAsync("RFQ-NOPE", "buyer1"));
        Assert.Equal("E-PUR-061", ex.Message);
    }
}
