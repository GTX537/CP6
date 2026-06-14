using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Fin;

/// <summary>财务章01 B-1：凭证采番（{key}-{yyyy-MM}-{NNNNN}，按月归零）+ 凭证头/行落库往返。</summary>
public class FinSequenceServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    [Fact]
    public async Task Next_FormatsAndIncrements_WithinMonth()
    {
        using var db = NewDb();
        var svc = new FinSequenceService(db);
        var d = new DateTime(2026, 6, 15);

        Assert.Equal("GL-2026-06-00001", await svc.NextAsync("GL", d));
        Assert.Equal("GL-2026-06-00002", await svc.NextAsync("GL", d));
        Assert.Equal("GL-2026-06-00003", await svc.NextAsync("gl", d));   // 大小写归一
    }

    [Fact]
    public async Task Next_ResetsAcrossMonth()
    {
        using var db = NewDb();
        var svc = new FinSequenceService(db);

        Assert.Equal("GL-2026-06-00001", await svc.NextAsync("GL", new DateTime(2026, 6, 30)));
        Assert.Equal("GL-2026-07-00001", await svc.NextAsync("GL", new DateTime(2026, 7, 1)));   // 跨月归零
        Assert.Equal("GL-2026-06-00002", await svc.NextAsync("GL", new DateTime(2026, 6, 5)));   // 回到6月接着数
    }

    [Fact]
    public async Task JournalEntry_WithLines_RoundTrips()
    {
        using var db = NewDb();
        var entry = new JournalEntry
        {
            No = "GL-2026-06-00001",
            VoucherDate = new DateTime(2026, 6, 15),
            PeriodId = Guid.NewGuid(),
            Source = VoucherSource.Manual,
            Status = JournalStatus.Draft,
            Description = "测试凭证",
            MakerId = "u1",
            MakerAt = DateTime.Now,
            Lines =
            {
                new JournalLine { LineNo = 1, AccountId = Guid.NewGuid(), Debit = 100.55m },
                new JournalLine { LineNo = 2, AccountId = Guid.NewGuid(), Credit = 100.55m },
            },
        };
        db.JournalEntries.Add(entry);
        await db.SaveChangesAsync();

        var loaded = await db.JournalEntries.Include(x => x.Lines).SingleAsync();
        Assert.Equal("GL-2026-06-00001", loaded.No);
        Assert.Equal(JournalStatus.Draft, loaded.Status);
        Assert.Equal(2, loaded.Lines.Count);
        Assert.Equal(100.55m, loaded.Lines.Single(l => l.LineNo == 1).Debit);   // decimal 精度保真
        Assert.All(loaded.Lines, l => Assert.Equal(loaded.Id, l.EntryId));       // FK 关联
    }
}
