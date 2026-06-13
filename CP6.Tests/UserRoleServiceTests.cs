using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class UserRoleServiceTests
{
    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    [Fact]
    public async Task UserRole_RoundTrips_WithIntRoleId()
    {
        using var db = NewDb();
        var uid = Guid.NewGuid();
        db.Sys_UserRoles.Add(new Sys_UserRole { Id = Guid.NewGuid(), UserId = uid, RoleId = 7 });
        await db.SaveChangesAsync();

        var r = await db.Sys_UserRoles.SingleAsync();
        Assert.Equal(7, r.RoleId);
        Assert.Equal(uid, r.UserId);
    }

    [Fact]
    public async Task Menu_MenuKey_RoundTrips()
    {
        using var db = NewDb();
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 10, MenuName = "受注", MenuKey = "order" });
        await db.SaveChangesAsync();

        var m = await db.Sys_Menus.SingleAsync();
        Assert.Equal("order", m.MenuKey);
    }

    // ───── A-4 UserRoleService 分配/主角色/迁移 ─────

    private sealed class SpyCurrent : ICurrentPermissionContext
    {
        public List<Guid> Invalidated { get; } = new();
        public Task<UserPermissionContext> GetAsync() => throw new NotImplementedException();
        public Task<UserPermissionContext> PrewarmAsync(Guid userId) => GetAsync();
        public void Invalidate(Guid userId) => Invalidated.Add(userId);
        public void InvalidateByRole(int roleId) { }
    }

    private static (CP6Context db, UserRoleService svc, SpyCurrent spy) Make()
    {
        var db = NewDb();
        var spy = new SpyCurrent();
        return (db, new UserRoleService(db, spy), spy);
    }

    [Fact]
    public async Task Save_DiffsAddsAndRemoves_AndWritesPrimary_AndInvalidates()
    {
        var (db, svc, spy) = Make();
        var uid = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User { Id = uid, UserName = "u", Password = "x", RoleId = 1 });
        db.Sys_UserRoles.Add(new Sys_UserRole { Id = Guid.NewGuid(), UserId = uid, RoleId = 1 });
        db.Sys_UserRoles.Add(new Sys_UserRole { Id = Guid.NewGuid(), UserId = uid, RoleId = 2 });
        await db.SaveChangesAsync();

        // 目标 {2,3}：删 1、留 2、增 3；主角色 2
        await svc.SaveAsync(uid, new List<int> { 2, 3 }, 2, "tester");

        var rows = await db.Sys_UserRoles.Where(ur => ur.UserId == uid).Select(ur => ur.RoleId).OrderBy(x => x).ToListAsync();
        Assert.Equal(new[] { 2, 3 }, rows);
        Assert.Equal(2, (await db.Sys_Users.FindAsync(uid))!.RoleId);   // 主角色已写
        Assert.Contains(uid, spy.Invalidated);                          // 缓存失效
    }

    [Fact]
    public async Task Save_PrimaryNotInRoleIds_Throws_E011()
    {
        var (db, svc, _) = Make();
        var uid = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User { Id = uid, UserName = "u", Password = "x" });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SaveAsync(uid, new List<int> { 2, 3 }, 9, "tester"));   // 主角色 9 ∉ {2,3}
        Assert.Equal("E-PUB-011", ex.Message);
    }

    [Fact]
    public async Task Get_MergesPrimaryIntoRoleIds()
    {
        var (db, svc, _) = Make();
        var uid = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User { Id = uid, UserName = "u", Password = "x", RoleId = 5 });
        db.Sys_UserRoles.Add(new Sys_UserRole { Id = Guid.NewGuid(), UserId = uid, RoleId = 2 });
        await db.SaveChangesAsync();

        var dto = await svc.GetAsync(uid);
        Assert.Equal(new[] { 2, 5 }, dto.RoleIds);   // 附加 2 + 主 5 合并
        Assert.Equal(5, dto.PrimaryRoleId);
    }

    [Fact]
    public async Task Migrate_IsIdempotent()
    {
        var (db, svc, _) = Make();
        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User { Id = u1, UserName = "a", Password = "x", RoleId = 1 });
        db.Sys_Users.Add(new Sys_User { Id = u2, UserName = "b", Password = "x", RoleId = 2 });
        db.Sys_UserRoles.Add(new Sys_UserRole { Id = Guid.NewGuid(), UserId = u1, RoleId = 1 });   // u1 已迁
        await db.SaveChangesAsync();

        var first = await svc.MigrateAsync();   // 只需补 u2
        Assert.Equal(1, first);

        var second = await svc.MigrateAsync();  // 再跑无新增
        Assert.Equal(0, second);

        var total = await db.Sys_UserRoles.CountAsync();
        Assert.Equal(2, total);
    }
}
