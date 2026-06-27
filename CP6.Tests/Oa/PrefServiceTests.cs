using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class PrefServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static IPrefService Svc(CP6Context db) => new PrefService(db);

    [Fact]
    public async Task Get_DefaultsEmpty_Save_Upserts()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        Assert.Equal("{}", await Svc(db).GetAsync(me));            // 无则默认 {}
        await Svc(db).SaveAsync(me, """{"pageSize":50}""");
        Assert.Equal("""{"pageSize":50}""", await Svc(db).GetAsync(me));
        await Svc(db).SaveAsync(me, """{"pageSize":20}""");        // upsert 覆盖（不重复行）
        Assert.Equal("""{"pageSize":20}""", await Svc(db).GetAsync(me));
        Assert.Equal(1, await db.Wf_InboxPrefs.CountAsync(p => p.UserId == me));
    }
}
