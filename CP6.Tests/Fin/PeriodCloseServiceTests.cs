using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Fin;

/// <summary>财务章02 C-3：月结锁期（结账前检查清单 / 锁期 / 反结账 / 锁后记不进）。</summary>
public class PeriodCloseServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static FiscalPeriodService Periods(CP6Context db) => new(db, 1);
    private static JournalEntryService Jes(CP6Context db) => new(db, Periods(db), new FinSequenceService(db));
    private static PeriodCloseService Close(CP6Context db) =>
        new(db, Periods(db), new TrialBalanceService(db));

    private static async Task<(Guid ar, Guid rev)> SeedCoa(CP6Context db)
    {
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "seed");
        return ((await gl.GetByCodeAsync("1122"))!.Id, (await gl.GetByCodeAsync("4001"))!.Id);
    }

    /// <summary>过账一张余额相等凭证到指定日期所属期间。</summary>
    private static async Task PostBalanced(CP6Context db, DateTime date, Guid ar, Guid rev, decimal amount = 100m)
    {
        var svc = Jes(db);
        var e = new JournalEntry
        {
            VoucherDate = date,
            Source = VoucherSource.Manual,
            Lines =
            {
                new JournalLine { AccountId = ar, Debit = amount, PartnerId = "C1" },
                new JournalLine { AccountId = rev, Credit = amount },
            },
        };
        var id = await svc.CreateDraftAsync(e, "u1");
        await svc.SubmitForReviewAsync(id);
        await svc.PostAsync(id, "u2");
    }

    [Fact]
    public async Task PreCloseCheck_PendingVoucher_Fails()
    {
        using var db = NewDb();
        var (ar, rev) = await SeedCoa(db);
        // 一张草稿（未过账）赖在 6 月
        var draft = new JournalEntry
        {
            VoucherDate = new DateTime(2026, 6, 10),
            Source = VoucherSource.Manual,
            Lines = { new JournalLine { AccountId = ar, Debit = 50m, PartnerId = "C1" }, new JournalLine { AccountId = rev, Credit = 50m } },
        };
        await Jes(db).CreateDraftAsync(draft, "u1");

        var june = (await Periods(db).ResolveAsync(new DateTime(2026, 6, 1)))!;
        var r = await Close(db).PreCloseCheckAsync(june.Id);
        Assert.False(r.Ok);
        Assert.Equal("E-FIN-143", r.Code);
    }

    [Fact]
    public async Task PreCloseCheck_PrevPeriodOpen_Fails()
    {
        using var db = NewDb();
        var periods = Periods(db);
        await periods.EnsureOpenAsync(new DateTime(2026, 5, 1));   // 5月 Open
        var june = await periods.EnsureOpenAsync(new DateTime(2026, 6, 1));

        var r = await Close(db).PreCloseCheckAsync(june.Id);       // 上期(5月)未结
        Assert.False(r.Ok);
        Assert.Equal("E-FIN-145", r.Code);
    }

    [Fact]
    public async Task Close_Succeeds_LocksAndOpensNext()
    {
        using var db = NewDb();
        var (ar, rev) = await SeedCoa(db);
        await PostBalanced(db, new DateTime(2026, 6, 10), ar, rev);   // 仅建 6 月（无 5 月→上期 null 不拦）

        var june = (await Periods(db).ResolveAsync(new DateTime(2026, 6, 1)))!;
        var r = await Close(db).CloseAsync(june.Id, "boss");
        Assert.True(r.Ok);

        var closed = await db.FiscalPeriods.FindAsync(june.Id);
        Assert.Equal(PeriodStatus.Closed, closed!.Status);
        Assert.Equal("boss", closed.ClosedBy);
        Assert.NotNull(closed.ClosedAt);

        // 下期 7 月自动 Open
        var july = await Periods(db).ResolveAsync(new DateTime(2026, 7, 1));
        Assert.NotNull(july);
        Assert.Equal(PeriodStatus.Open, july!.Status);
    }

    [Fact]
    public async Task Close_ThenPostToClosedPeriod_Blocked()
    {
        using var db = NewDb();
        var (ar, rev) = await SeedCoa(db);
        await PostBalanced(db, new DateTime(2026, 6, 10), ar, rev);
        var june = (await Periods(db).ResolveAsync(new DateTime(2026, 6, 1)))!;
        await Close(db).CloseAsync(june.Id, "boss");

        // 锁后再想往 6 月落一张
        var svc = Jes(db);
        var e = new JournalEntry
        {
            VoucherDate = new DateTime(2026, 6, 20),
            Source = VoucherSource.Manual,
            Lines = { new JournalLine { AccountId = ar, Debit = 10m, PartnerId = "C1" }, new JournalLine { AccountId = rev, Credit = 10m } },
        };
        var id = await svc.CreateDraftAsync(e, "u1");
        await svc.SubmitForReviewAsync(id);
        var r = await svc.PostAsync(id, "u2");
        Assert.False(r.Ok);
        Assert.Equal("E-FIN-112", r.Code);                         // 期间已结，不能过账
    }

    [Fact]
    public async Task Reopen_RestoresOpen()
    {
        using var db = NewDb();
        var (ar, rev) = await SeedCoa(db);
        await PostBalanced(db, new DateTime(2026, 6, 10), ar, rev);
        var june = (await Periods(db).ResolveAsync(new DateTime(2026, 6, 1)))!;
        var close = Close(db);
        await close.CloseAsync(june.Id, "boss");

        var r = await close.ReopenAsync(june.Id, "boss");
        Assert.True(r.Ok);
        var p = await db.FiscalPeriods.FindAsync(june.Id);
        Assert.Equal(PeriodStatus.Open, p!.Status);
        Assert.Null(p.ClosedAt);
        Assert.Null(p.ClosedBy);
    }

    [Fact]
    public async Task Reopen_NotClosed_Fails()
    {
        using var db = NewDb();
        var june = await Periods(db).EnsureOpenAsync(new DateTime(2026, 6, 1));
        var r = await Close(db).ReopenAsync(june.Id, "boss");
        Assert.False(r.Ok);
        Assert.Equal("E-FIN-146", r.Code);
    }
}
