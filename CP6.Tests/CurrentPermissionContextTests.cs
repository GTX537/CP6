using System.Security.Claims;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CP6.Tests;

public class CurrentPermissionContextTests
{
    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    /// <summary>调用计数的假聚合器 —— 验证缓存命中/失效是否触发重建。</summary>
    private sealed class CountingAggregator : IPermissionAggregator
    {
        public int Calls;
        public Task<UserPermissionContext> BuildAsync(Guid userId)
        {
            Calls++;
            return Task.FromResult(new UserPermissionContext { UserId = userId });
        }
    }

    private static IHttpContextAccessor HttpAs(string userName) =>
        new HttpContextAccessor { HttpContext = CtxFor(userName) };

    /// <summary>构造带登录名的 HttpContext（ClaimsIdentity.Name = userName）。</summary>
    private static DefaultHttpContext CtxFor(string userName) => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, userName) }, "test"))
    };

    [Fact]
    public async Task GetAsync_CachesAndRebuildsAfterInvalidate()
    {
        using var db = NewDb();
        var uid = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User { Id = uid, UserName = "u1", Password = "x" });
        await db.SaveChangesAsync();

        var agg = new CountingAggregator();
        var cache = Cache();
        var sut = new CurrentPermissionContext(HttpAs("u1"), cache, db, agg);

        var c1 = await sut.GetAsync();
        var c2 = await sut.GetAsync();           // 命中缓存
        Assert.Equal(uid, c1.UserId);
        Assert.Equal(1, agg.Calls);              // 只 build 一次

        sut.Invalidate(uid);
        await sut.GetAsync();                      // 失效后重建
        Assert.Equal(2, agg.Calls);
    }

    [Fact]
    public async Task InvalidateByRole_RemovesPrimaryAndAdditionalRoleUsers()
    {
        using var db = NewDb();
        var u1 = Guid.NewGuid();   // 主角色 3
        var u2 = Guid.NewGuid();   // 附加角色 3（主角色 9）
        db.Sys_Users.Add(new Sys_User { Id = u1, UserName = "u1", Password = "x", RoleId = 3 });
        db.Sys_Users.Add(new Sys_User { Id = u2, UserName = "u2", Password = "x", RoleId = 9 });
        db.Sys_UserRoles.Add(new Sys_UserRole { Id = Guid.NewGuid(), UserId = u2, RoleId = 3 });
        await db.SaveChangesAsync();

        var agg = new CountingAggregator();
        var cache = Cache();
        // 单 accessor 切换 HttpContext（HttpContextAccessor 用静态 AsyncLocal，不能并存多实例）
        var http = new HttpContextAccessor();
        var sut = new CurrentPermissionContext(http, cache, db, agg);

        http.HttpContext = CtxFor("u1"); await sut.GetAsync();   // build u1
        http.HttpContext = CtxFor("u2"); await sut.GetAsync();   // build u2
        Assert.Equal(2, agg.Calls);

        sut.InvalidateByRole(3);   // 应清掉 u1(主) 和 u2(附加)

        http.HttpContext = CtxFor("u1"); await sut.GetAsync();   // 重建 u1
        http.HttpContext = CtxFor("u2"); await sut.GetAsync();   // 重建 u2
        Assert.Equal(4, agg.Calls);
    }

    [Fact]
    public async Task Prewarm_CachesForSubsequentGet()
    {
        using var db = NewDb();
        var uid = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User { Id = uid, UserName = "u1", Password = "x" });
        await db.SaveChangesAsync();

        var agg = new CountingAggregator();
        var cache = Cache();
        var sut = new CurrentPermissionContext(HttpAs("u1"), cache, db, agg);

        await sut.PrewarmAsync(uid);   // 登录预热：build + 缓存
        await sut.GetAsync();          // 同用户请求 → 命中缓存
        Assert.Equal(1, agg.Calls);    // 只 build 一次
    }

    [Fact]
    public async Task TwoInstances_ShareCacheAndCrossInstanceInvalidation()
    {
        using var db = NewDb();
        var uid = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User
        {
            Id = uid,
            UserName = "shared-user",
            Password = "x",
        });
        await db.SaveChangesAsync();

        var sharedCache = Cache();
        var firstAggregator = new CountingAggregator();
        var secondAggregator = new CountingAggregator();
        var first = new CurrentPermissionContext(
            HttpAs("shared-user"), sharedCache, db, firstAggregator);
        var second = new CurrentPermissionContext(
            HttpAs("shared-user"), sharedCache, db, secondAggregator);

        await first.GetAsync();
        await second.GetAsync();
        Assert.Equal(1, firstAggregator.Calls);
        Assert.Equal(0, secondAggregator.Calls);

        first.Invalidate(uid);
        await second.GetAsync();
        Assert.Equal(1, secondAggregator.Calls);
    }

    [Fact]
    public async Task GetAsync_NotLoggedIn_Throws()
    {
        using var db = NewDb();
        var sut = new CurrentPermissionContext(
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },   // 无身份
            Cache(), db, new CountingAggregator());
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetAsync());
    }

    private static IDistributedCache Cache()
        => new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions()));
}
