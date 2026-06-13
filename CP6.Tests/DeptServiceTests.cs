using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class DeptServiceTests
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
    public async Task Dept_And_UserOrgFields_RoundTrip()
    {
        using var db = NewDb();
        var deptId = Guid.NewGuid();
        db.Sys_Depts.Add(new Sys_Dept { Id = deptId, DeptCode = "HQ", DeptName = "総本部", Path = $"/{deptId}/" });
        db.Sys_Users.Add(new Sys_User { Id = Guid.NewGuid(), UserName = "u1", Password = "x", DeptId = deptId, Email = "u1@x.com" });
        await db.SaveChangesAsync();

        var dept = await db.Sys_Depts.SingleAsync();
        Assert.Equal("HQ", dept.DeptCode);
        Assert.Equal($"/{deptId}/", dept.Path);

        var user = await db.Sys_Users.SingleAsync();
        Assert.Equal(deptId, user.DeptId);
        Assert.Equal("u1@x.com", user.Email);
    }

    private static (CP6Context db, DeptService svc) Make()
    {
        var db = NewDb();
        return (db, new DeptService(db));
    }

    [Fact]
    public async Task Create_Root_And_Child_BuildsPath()
    {
        var (db, svc) = Make();
        var rootId = await svc.CreateAsync(new DeptDto { DeptCode = "HQ", DeptName = "本部" }, null, "u");
        var childId = await svc.CreateAsync(new DeptDto { DeptCode = "SALES", DeptName = "営業" }, rootId, "u");

        Assert.Equal($"/{rootId}/", (await db.Sys_Depts.FindAsync(rootId))!.Path);
        Assert.Equal($"/{rootId}/{childId}/", (await db.Sys_Depts.FindAsync(childId))!.Path);
    }

    [Fact]
    public async Task Create_DuplicateCode_Throws_E001()
    {
        var (_, svc) = Make();
        await svc.CreateAsync(new DeptDto { DeptCode = "HQ", DeptName = "a" }, null, "u");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CreateAsync(new DeptDto { DeptCode = "HQ", DeptName = "b" }, null, "u"));
        Assert.Equal("E-PUB-001", ex.Message);
    }

    [Fact]
    public async Task Move_RecomputesSubtreePaths()
    {
        var (db, svc) = Make();
        var a = await svc.CreateAsync(new DeptDto { DeptCode = "A", DeptName = "A" }, null, "u");
        var b = await svc.CreateAsync(new DeptDto { DeptCode = "B", DeptName = "B" }, null, "u");
        var a1 = await svc.CreateAsync(new DeptDto { DeptCode = "A1", DeptName = "A1" }, a, "u");   // /a/a1/

        await svc.MoveAsync(a, b, "u");   // A 挂到 B 下

        Assert.Equal($"/{b}/{a}/", (await db.Sys_Depts.FindAsync(a))!.Path);
        Assert.Equal($"/{b}/{a}/{a1}/", (await db.Sys_Depts.FindAsync(a1))!.Path);   // 子孙整体平移
    }

    [Fact]
    public async Task Move_IntoOwnSubtree_Throws_E004()
    {
        var (_, svc) = Make();
        var a = await svc.CreateAsync(new DeptDto { DeptCode = "A", DeptName = "A" }, null, "u");
        var a1 = await svc.CreateAsync(new DeptDto { DeptCode = "A1", DeptName = "A1" }, a, "u");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.MoveAsync(a, a1, "u"));
        Assert.Equal("E-PUB-004", ex.Message);
    }

    [Fact]
    public async Task Delete_WithChildren_E002_WithUsers_E003()
    {
        var (db, svc) = Make();
        var a = await svc.CreateAsync(new DeptDto { DeptCode = "A", DeptName = "A" }, null, "u");
        var a1 = await svc.CreateAsync(new DeptDto { DeptCode = "A1", DeptName = "A1" }, a, "u");

        // 有子部门 → E-002
        Assert.Equal("E-PUB-002",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DeleteAsync(a))).Message);

        // 有在职用户 → E-003
        db.Sys_Users.Add(new Sys_User { Id = Guid.NewGuid(), UserName = "x", Password = "x", DeptId = a1 });
        await db.SaveChangesAsync();
        Assert.Equal("E-PUB-003",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DeleteAsync(a1))).Message);
    }

    [Fact]
    public async Task Tree_NestsChildren_WithLeaderName()
    {
        var (db, svc) = Make();
        var leaderId = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User { Id = leaderId, UserName = "boss", NickName = "部长", Password = "x" });
        await db.SaveChangesAsync();
        var a = await svc.CreateAsync(new DeptDto { DeptCode = "A", DeptName = "A", LeaderId = leaderId }, null, "u");
        await svc.CreateAsync(new DeptDto { DeptCode = "A1", DeptName = "A1" }, a, "u");

        var tree = await svc.TreeAsync();
        Assert.Single(tree);
        Assert.Equal("部长", tree[0].LeaderName);
        Assert.Single(tree[0].Children);
    }
}
