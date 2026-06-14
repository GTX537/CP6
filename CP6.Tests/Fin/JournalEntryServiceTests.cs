using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Fin;

/// <summary>
/// 财务章01 B-2：★借贷恒等校验（铁律1）+ ★maker-checker 状态机（铁律2）。
/// </summary>
public class JournalEntryServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static JournalEntryService Svc(CP6Context db) =>
        new(db, new FiscalPeriodService(db, 1), new FinSequenceService(db));

    // ───────── 纯校验（ValidateBalance，无需 DB）─────────

    private static GlAccount Leaf(Guid id, string code = "X", bool requirePartner = false, bool active = true) =>
        new() { Id = id, Code = code, IsLeaf = true, IsActive = active, RequirePartner = requirePartner };

    private static JournalLine Ln(Guid acc, decimal debit = 0, decimal credit = 0, string? partner = null) =>
        new() { AccountId = acc, Debit = debit, Credit = credit, PartnerId = partner };

    [Fact]
    public void Validate_Unbalanced_Fails()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var e = new JournalEntry { Lines = { Ln(a, debit: 100), Ln(b, credit: 90) } };
        var dict = new Dictionary<Guid, GlAccount> { [a] = Leaf(a), [b] = Leaf(b) };
        var r = JournalEntryService.ValidateBalance(e, dict);
        Assert.False(r.Ok);
        Assert.Equal("E-FIN-107", r.Code);
    }

    [Fact]
    public void Validate_DecimalPrecision_NoFloatError()
    {
        // 借 0.1+0.2 / 贷 0.3 → decimal 相等通过（double 会失败）
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var e = new JournalEntry { Lines = { Ln(a, debit: 0.1m), Ln(a, debit: 0.2m), Ln(b, credit: 0.3m) } };
        var dict = new Dictionary<Guid, GlAccount> { [a] = Leaf(a), [b] = Leaf(b) };
        Assert.True(JournalEntryService.ValidateBalance(e, dict).Ok);
    }

    [Fact]
    public void Validate_NonLeafAccount_Fails()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var e = new JournalEntry { Lines = { Ln(a, debit: 100), Ln(b, credit: 100) } };
        var nonLeaf = new GlAccount { Id = a, Code = "1000", IsLeaf = false, IsActive = true };
        var dict = new Dictionary<Guid, GlAccount> { [a] = nonLeaf, [b] = Leaf(b) };
        var r = JournalEntryService.ValidateBalance(e, dict);
        Assert.False(r.Ok);
        Assert.Equal("E-FIN-104", r.Code);
    }

    [Fact]
    public void Validate_RequirePartner_Missing_Fails()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var e = new JournalEntry { Lines = { Ln(a, debit: 100), Ln(b, credit: 100) } };   // a 缺往来单位
        var dict = new Dictionary<Guid, GlAccount> { [a] = Leaf(a, "1122", requirePartner: true), [b] = Leaf(b) };
        var r = JournalEntryService.ValidateBalance(e, dict);
        Assert.False(r.Ok);
        Assert.Equal("E-FIN-106", r.Code);
    }

    [Fact]
    public void Validate_OneLine_Fails()
    {
        var a = Guid.NewGuid();
        var e = new JournalEntry { Lines = { Ln(a, debit: 100) } };
        var r = JournalEntryService.ValidateBalance(e, new Dictionary<Guid, GlAccount> { [a] = Leaf(a) });
        Assert.Equal("E-FIN-101", r.Code);
    }

    [Fact]
    public void Validate_BothDebitAndCredit_Fails()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var e = new JournalEntry { Lines = { Ln(a, debit: 100, credit: 100), Ln(b, credit: 100) } };
        var dict = new Dictionary<Guid, GlAccount> { [a] = Leaf(a), [b] = Leaf(b) };
        var r = JournalEntryService.ValidateBalance(e, dict);
        Assert.Equal("E-FIN-103", r.Code);
    }

    // ───────── maker-checker 状态机（带 DB）─────────

    /// <summary>建一张余额相等的草稿（银行存款 借 / 主营业务收入 贷），返回 entryId。</summary>
    private static async Task<Guid> BalancedDraft(CP6Context db, string maker, decimal amount = 100m)
    {
        var gl = new GlAccountService(db);
        if (!await db.GlAccounts.AnyAsync()) await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "seed");
        var bank = (await gl.GetByCodeAsync("1002"))!.Id;
        var rev = (await gl.GetByCodeAsync("4001"))!.Id;
        var e = new JournalEntry
        {
            VoucherDate = new DateTime(2026, 6, 15),
            Source = VoucherSource.Manual,
            Description = "测试",
            Lines =
            {
                new JournalLine { AccountId = bank, Debit = amount },
                new JournalLine { AccountId = rev, Credit = amount },
            },
        };
        return await Svc(db).CreateDraftAsync(e, maker);
    }

    [Fact]
    public async Task CreateDraft_AssignsNoAndPeriod()
    {
        using var db = NewDb();
        var id = await BalancedDraft(db, "u1");
        var e = await Svc(db).GetAsync(id);
        Assert.Equal(JournalStatus.Draft, e!.Status);
        Assert.StartsWith("GL-2026-06-", e.No);
        Assert.NotEqual(Guid.Empty, e.PeriodId);
        Assert.All(e.Lines, l => Assert.True(l.LineNo > 0));
    }

    [Fact]
    public async Task Submit_Then_Post_DifferentChecker_Succeeds()
    {
        using var db = NewDb();
        var svc = Svc(db);
        var id = await BalancedDraft(db, "u1");

        Assert.True((await svc.SubmitForReviewAsync(id)).Ok);
        Assert.True((await svc.PostAsync(id, "u2")).Ok);

        var e = await svc.GetAsync(id);
        Assert.Equal(JournalStatus.Posted, e!.Status);
        Assert.Equal("u2", e.CheckerId);
        Assert.NotNull(e.CheckerAt);
    }

    [Fact]
    public async Task Post_MakerEqualsChecker_Fails()
    {
        using var db = NewDb();
        var svc = Svc(db);
        var id = await BalancedDraft(db, "u1");
        await svc.SubmitForReviewAsync(id);

        var r = await svc.PostAsync(id, "u1");        // 制单人==过账人
        Assert.False(r.Ok);
        Assert.Equal("E-FIN-111", r.Code);
        Assert.Equal(JournalStatus.PendingReview, (await svc.GetAsync(id))!.Status);  // 未过账
    }

    [Fact]
    public async Task Post_NotPendingReview_Fails()
    {
        using var db = NewDb();
        var svc = Svc(db);
        var id = await BalancedDraft(db, "u1");        // 仍是 Draft，未提交
        var r = await svc.PostAsync(id, "u2");
        Assert.Equal("E-FIN-110", r.Code);
    }

    [Fact]
    public async Task Submit_Unbalanced_Fails_StaysDraft()
    {
        using var db = NewDb();
        var svc = Svc(db);
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "seed");
        var bank = (await gl.GetByCodeAsync("1002"))!.Id;
        var rev = (await gl.GetByCodeAsync("4001"))!.Id;
        var e = new JournalEntry
        {
            VoucherDate = new DateTime(2026, 6, 15),
            Source = VoucherSource.Manual,
            Lines =
            {
                new JournalLine { AccountId = bank, Debit = 100m },
                new JournalLine { AccountId = rev, Credit = 90m },   // 不平
            },
        };
        var id = await svc.CreateDraftAsync(e, "u1");

        var r = await svc.SubmitForReviewAsync(id);
        Assert.Equal("E-FIN-107", r.Code);
        Assert.Equal(JournalStatus.Draft, (await svc.GetAsync(id))!.Status);
    }

    [Fact]
    public async Task AutoPost_ManualSource_Rejected()
    {
        using var db = NewDb();
        var svc = Svc(db);
        var e = new JournalEntry { Source = VoucherSource.Manual, VoucherDate = new DateTime(2026, 6, 1) };
        var r = await svc.AutoPostAsync(e);
        Assert.Equal("E-FIN-113", r.Code);
    }

    [Fact]
    public async Task AutoPost_NonManual_PostsDirectly()
    {
        using var db = NewDb();
        var svc = Svc(db);
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "seed");
        var bank = (await gl.GetByCodeAsync("1002"))!.Id;
        var rev = (await gl.GetByCodeAsync("4001"))!.Id;
        var e = new JournalEntry
        {
            Source = VoucherSource.AR,                 // 自动凭证
            VoucherDate = new DateTime(2026, 6, 20),
            Lines =
            {
                new JournalLine { AccountId = bank, Debit = 500m },
                new JournalLine { AccountId = rev, Credit = 500m },
            },
        };

        var r = await svc.AutoPostAsync(e);
        Assert.True(r.Ok);

        var saved = await svc.GetAsync(e.Id);
        Assert.Equal(JournalStatus.Posted, saved!.Status);
        Assert.True(saved.AutoPosted);
        Assert.Equal("SYSTEM", saved.CheckerId);
        Assert.StartsWith("GL-2026-06-", saved.No);
    }

    [Fact]
    public async Task Reject_SetsRejectedWithReason()
    {
        using var db = NewDb();
        var svc = Svc(db);
        var id = await BalancedDraft(db, "u1");
        await svc.SubmitForReviewAsync(id);

        Assert.True((await svc.RejectAsync(id, "金额录错")).Ok);
        var e = await svc.GetAsync(id);
        Assert.Equal(JournalStatus.Rejected, e!.Status);
        Assert.Equal("金额录错", e.RejectReason);
    }
}
