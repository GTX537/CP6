using CP6.Core.EFDbContext;
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
}
