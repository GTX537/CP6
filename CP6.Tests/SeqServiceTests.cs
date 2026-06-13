using CP6.Core.EFDbContext;
using CP6.Core.Services.Pub;
using CP6.Entity.DomainModels.Pub;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class SeqServiceTests
{
    private static CP6Context Db()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    [Fact]
    public async Task Next_BuildsNumber_AndResetsAcrossPeriod()
    {
        using var db = Db();
        db.Pub_DocSequences.Add(new Pub_DocSequence
        {
            Id = Guid.NewGuid(),
            BizKey = "PO", Prefix = "PO", DateFormat = "yyyyMMdd", SeqLength = 4, ResetCycle = 1, CurrentValue = 0
        });
        await db.SaveChangesAsync();
        var svc = new SeqService(db);

        var day1 = new DateTime(2026, 6, 13);
        Assert.Equal("PO202606130001", await svc.NextAsync("PO", day1));
        Assert.Equal("PO202606130002", await svc.NextAsync("PO", day1));

        var day2 = new DateTime(2026, 6, 14);
        Assert.Equal("PO202606140001", await svc.NextAsync("PO", day2));   // 跨日重置流水
    }

    [Fact]
    public async Task Next_NoReset_AccumulatesAcrossDays()
    {
        using var db = Db();
        db.Pub_DocSequences.Add(new Pub_DocSequence
        {
            Id = Guid.NewGuid(), BizKey = "G", Prefix = "G", DateFormat = null, SeqLength = 5, ResetCycle = 0, CurrentValue = 0
        });
        await db.SaveChangesAsync();
        var svc = new SeqService(db);

        Assert.Equal("G00001", await svc.NextAsync("G", new DateTime(2026, 6, 13)));
        Assert.Equal("G00002", await svc.NextAsync("G", new DateTime(2026, 6, 14)));   // 不重置，累计
    }

    [Fact]
    public async Task Next_UnknownBizKey_Throws_E051()
    {
        using var db = Db();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new SeqService(db).NextAsync("NOPE"));
        Assert.Equal("E-PUB-051", ex.Message);
    }

    [Fact]
    public async Task Next_MonthlyReset()
    {
        using var db = Db();
        db.Pub_DocSequences.Add(new Pub_DocSequence
        {
            Id = Guid.NewGuid(), BizKey = "M", Prefix = "M", DateFormat = "yyyyMM", SeqLength = 3, ResetCycle = 2, CurrentValue = 0
        });
        await db.SaveChangesAsync();
        var svc = new SeqService(db);

        Assert.Equal("M202606001", await svc.NextAsync("M", new DateTime(2026, 6, 30)));
        Assert.Equal("M202607001", await svc.NextAsync("M", new DateTime(2026, 7, 1)));   // 跨月重置
    }
}
