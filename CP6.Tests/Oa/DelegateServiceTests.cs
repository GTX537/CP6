using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class DelegateServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static IDelegateService Svc(CP6Context db) => new DelegateService(db);

    [Fact]
    public async Task MyGrants_ResolvesBothDirections()
    {
        using var db = NewDb();
        var me = Guid.NewGuid(); var x = Guid.NewGuid(); var y = Guid.NewGuid();
        db.Sys_Users.AddRange(
            new Sys_User { Id = x, UserName = "x", NickName = "X 经理", Password = "p" },
            new Sys_User { Id = y, UserName = "y", NickName = "Y 同事", Password = "p" });
        // X 授我；我授 Y
        db.Wf_FlowDelegates.AddRange(
            new Wf_FlowDelegate { Id = Guid.NewGuid(), GrantorId = x, DelegateId = me, Enable = true,
                ValidFrom = DateTime.Now.AddDays(-1), ValidTo = DateTime.Now.AddDays(1) },
            new Wf_FlowDelegate { Id = Guid.NewGuid(), GrantorId = me, DelegateId = y, Enable = true,
                ValidFrom = DateTime.Now.AddDays(-1), ValidTo = DateTime.Now.AddDays(1) });
        await db.SaveChangesAsync();

        var g = await Svc(db).MyGrantsAsync(me);
        Assert.Contains(g.ICanActAs, u => u.UserId == x && u.UserName == "X 经理");
        Assert.Contains(g.CanActForMe, u => u.UserId == y);
    }

    [Fact]
    public async Task AssertActiveGrant_OkWhenActive_ThrowsWhenMissingOrExpired()
    {
        using var db = NewDb();
        var me = Guid.NewGuid(); var x = Guid.NewGuid();
        db.Wf_FlowDelegates.Add(new Wf_FlowDelegate { Id = Guid.NewGuid(), GrantorId = x, DelegateId = me, Enable = true,
            ValidFrom = DateTime.Now.AddDays(-1), ValidTo = DateTime.Now.AddDays(1) });
        await db.SaveChangesAsync();

        await Svc(db).AssertActiveGrantAsync(me, x);   // 不抛

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Svc(db).AssertActiveGrantAsync(me, Guid.NewGuid()));
        Assert.Equal("E-WF-001", ex.Message);
    }

    [Fact]
    public async Task AddAndRemove_MyDelegate()
    {
        using var db = NewDb();
        var me = Guid.NewGuid(); var y = Guid.NewGuid();
        var id = await Svc(db).AddDelegateAsync(me, y, DateTime.Now, DateTime.Now.AddDays(7), null, "休假代理");
        Assert.Single(await Svc(db).ListMyDelegatesAsync(me));
        await Svc(db).RemoveDelegateAsync(me, id);
        Assert.Empty(await Svc(db).ListMyDelegatesAsync(me));
    }
}
