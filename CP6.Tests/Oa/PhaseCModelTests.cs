using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class PhaseCModelTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    [Fact]
    public async Task Favorite_And_Pref_Persist()
    {
        using var db = NewDb();
        var u = Guid.NewGuid();
        db.Wf_FormFavorites.Add(new Wf_FormFavorite { Id = Guid.NewGuid(), UserId = u, FormKey = "leave" });
        db.Wf_InboxPrefs.Add(new Wf_InboxPref { Id = Guid.NewGuid(), UserId = u, PrefsJson = """{"pageSize":20}""" });
        await db.SaveChangesAsync();

        Assert.Equal("leave", (await db.Wf_FormFavorites.SingleAsync()).FormKey);
        Assert.Equal("""{"pageSize":20}""", (await db.Wf_InboxPrefs.SingleAsync()).PrefsJson);
    }

    [Fact]
    public async Task FormDef_HasCategoryColumns()
    {
        using var db = NewDb();
        db.Wf_FormDefs.Add(new Wf_FormDef { Id = Guid.NewGuid(), FormKey = "leave", FormName = "请假",
            Category = "人事", SubCategory = "假勤" });
        await db.SaveChangesAsync();
        var got = await db.Wf_FormDefs.SingleAsync();
        Assert.Equal("人事", got.Category);
        Assert.Equal("假勤", got.SubCategory);
    }
}
