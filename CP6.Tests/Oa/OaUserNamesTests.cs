using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class OaUserNamesTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    [Fact]
    public async Task ResolveAsync_PrefersNickName_FallsBackToUserName()
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        db.Sys_Users.AddRange(
            new Sys_User { Id = a, UserName = "alice", NickName = "Alice 王", Password = "x" },
            new Sys_User { Id = b, UserName = "bob", NickName = null, Password = "x" });
        await db.SaveChangesAsync();

        var names = await OaUserNames.ResolveAsync(db, new[] { a, b, Guid.Empty });
        Assert.Equal("Alice 王", names[a]);
        Assert.Equal("bob", names[b]);
        Assert.False(names.ContainsKey(Guid.Empty));   // 空 Guid 不查
    }
}
