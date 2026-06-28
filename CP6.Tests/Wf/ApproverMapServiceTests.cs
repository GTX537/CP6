using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

public class ApproverMapServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    [Fact]
    public async Task ApproverMap_Persists_AndQueriesByKeyAndValue()
    {
        using var db = NewDb();
        db.Wf_ApproverMaps.Add(new Wf_ApproverMap
        {
            Id = Guid.NewGuid(), MapKey = "cc", MatchValue = "A100",
            ApproverUserId = Guid.NewGuid(), Enable = true,
        });
        await db.SaveChangesAsync();

        var row = await db.Wf_ApproverMaps.FirstOrDefaultAsync(m => m.MapKey == "cc" && m.MatchValue == "A100" && m.Enable);
        Assert.NotNull(row);
        Assert.NotNull(row!.ApproverUserId);
    }

    [Fact]
    public async Task Create_Then_List_ByKey()
    {
        using var db = NewDb();
        var svc = new ApproverMapService(db);
        await svc.CreateAsync("cc", "A100", Guid.NewGuid(), null);
        var rows = await svc.ListAsync("cc");
        Assert.Single(rows);
    }

    [Fact]
    public async Task Create_DuplicateSameTarget_Throws_EWF015()
    {
        using var db = NewDb();
        var svc = new ApproverMapService(db);
        var uid = Guid.NewGuid();
        await svc.CreateAsync("cc", "A100", uid, null);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateAsync("cc", "A100", uid, null));
        Assert.Contains("E-WF-015", ex.Message);
    }

    [Fact]
    public async Task Create_BothTargetsNull_Throws_EWF015()
    {
        using var db = NewDb();
        var svc = new ApproverMapService(db);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateAsync("cc", "A100", null, null));
        Assert.Contains("E-WF-015", ex.Message);
    }
}
