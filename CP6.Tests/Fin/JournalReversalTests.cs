using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Fin;

/// <summary>财务章01 B-3：★红冲（凭证不可改不可删，只能红冲）。借贷对调 + 原凭证冻结 + 互指。</summary>
public class JournalReversalTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static JournalEntryService Svc(CP6Context db) =>
        new(db, new FiscalPeriodService(db, 1), new FinSequenceService(db));

    /// <summary>建并过账一张（银行 借 / 收入 贷）凭证，返回已过账的 entryId。</summary>
    private static async Task<(Guid id, Guid bank, Guid rev)> PostBalanced(CP6Context db, decimal amount = 100m)
    {
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "seed");
        var bank = (await gl.GetByCodeAsync("1002"))!.Id;
        var rev = (await gl.GetByCodeAsync("4001"))!.Id;
        var svc = Svc(db);
        var e = new JournalEntry
        {
            VoucherDate = new DateTime(2026, 6, 15),
            Source = VoucherSource.Manual,
            Lines =
            {
                new JournalLine { AccountId = bank, Debit = amount },
                new JournalLine { AccountId = rev, Credit = amount },
            },
        };
        var id = await svc.CreateDraftAsync(e, "u1");
        await svc.SubmitForReviewAsync(id);
        await svc.PostAsync(id, "u2");
        return (id, bank, rev);
    }

    [Fact]
    public async Task Reverse_CreatesSwappedEntry_AndFreezesOrigin()
    {
        using var db = NewDb();
        var svc = Svc(db);
        var (id, bank, rev) = await PostBalanced(db);

        var r = await svc.ReverseAsync(id, "u1", "记错", autoPost: false);
        Assert.True(r.Ok);

        var origin = await svc.GetAsync(id);
        var reversal = await db.JournalEntries.Include(x => x.Lines).SingleAsync(x => x.ReverseOfId == id);

        Assert.Equal(JournalStatus.Reversed, origin!.Status);
        Assert.Equal(reversal.Id, origin.ReversedById);          // 互指
        Assert.Equal(VoucherSource.Reversal, reversal.Source);
        Assert.Equal(JournalStatus.PendingReview, reversal.Status);  // 手工红冲走复核

        // 借贷对调：原 银行借100 → 红冲 银行贷100；原 收入贷100 → 红冲 收入借100
        Assert.Equal(100m, reversal.Lines.Single(l => l.AccountId == bank).Credit);
        Assert.Equal(0m, reversal.Lines.Single(l => l.AccountId == bank).Debit);
        Assert.Equal(100m, reversal.Lines.Single(l => l.AccountId == rev).Debit);
    }

    [Fact]
    public async Task Reverse_AutoPost_PostsDirectly()
    {
        using var db = NewDb();
        var svc = Svc(db);
        var (id, _, _) = await PostBalanced(db);

        await svc.ReverseAsync(id, "system-trigger", "出货取消", autoPost: true);

        var reversal = await db.JournalEntries.SingleAsync(x => x.ReverseOfId == id);
        Assert.Equal(JournalStatus.Posted, reversal.Status);     // 系统触发直过
        Assert.True(reversal.AutoPosted);
        Assert.Equal("SYSTEM", reversal.CheckerId);
    }

    [Fact]
    public async Task Reverse_NonPosted_Fails()
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
                new JournalLine { AccountId = rev, Credit = 100m },
            },
        };
        var id = await svc.CreateDraftAsync(e, "u1");   // 仍是草稿

        var r = await svc.ReverseAsync(id, "u1", "试图红冲草稿");
        Assert.False(r.Ok);
        Assert.Equal("E-FIN-120", r.Code);
    }
}
