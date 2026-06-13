using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

/// <summary>
/// OA 章01 审批人解析器（A-1）。覆盖 4 策略 + 缺位兜底。消费 PUB Sys_Dept/Sys_User。
/// </summary>
public class ApproverResolverTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    [Fact]
    public async Task DirectManager_ChainShorterThanN_ReturnsTop()
    {
        using var db = NewDb();
        var top = Guid.NewGuid(); var mid = Guid.NewGuid(); var low = Guid.NewGuid();
        db.Sys_Users.AddRange(
            new Sys_User { Id = top, UserName = "top", Password = "x", Enable = true },
            new Sys_User { Id = mid, UserName = "mid", Password = "x", ManagerId = top, Enable = true },
            new Sys_User { Id = low, UserName = "low", Password = "x", ManagerId = mid, Enable = true });
        await db.SaveChangesAsync();

        var res = await new ApproverResolver(db).ResolveAsync(
            new ApproverRule(ApproverStrategy.DirectManager, 5, null, null),
            new ApproverResolveContext { StarterUserId = low }); // 想上溯 5 级，链仅 2 级 → 取链顶 top

        Assert.True(res.Resolved);
        Assert.Equal(top, res.ApproverIds.Single());
    }

    [Fact]
    public async Task DirectManager_NoManager_Unresolved()
    {
        using var db = NewDb();
        var u = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User { Id = u, UserName = "u", Password = "x", Enable = true });
        await db.SaveChangesAsync();

        var res = await new ApproverResolver(db).ResolveAsync(
            new ApproverRule(ApproverStrategy.DirectManager, 1, null, null),
            new ApproverResolveContext { StarterUserId = u });

        Assert.False(res.Resolved);
        Assert.NotNull(res.UnresolvedReason);
    }

    [Fact]
    public async Task DeptLeader_WalksUpToFirstLeader()
    {
        using var db = NewDb();
        var leader = Guid.NewGuid(); var parent = Guid.NewGuid(); var child = Guid.NewGuid(); var u = Guid.NewGuid();
        db.Sys_Depts.AddRange(
            new Sys_Dept { Id = parent, DeptCode = "P", DeptName = "P", LeaderId = leader, Enable = true, Path = $"/{parent}/" },
            new Sys_Dept { Id = child, DeptCode = "C", DeptName = "C", ParentId = parent, Enable = true, Path = $"/{parent}/{child}/" }); // 子部门无 leader
        db.Sys_Users.AddRange(
            new Sys_User { Id = leader, UserName = "L", Password = "x", Enable = true },
            new Sys_User { Id = u, UserName = "u", Password = "x", DeptId = child, Enable = true });
        await db.SaveChangesAsync();

        var res = await new ApproverResolver(db).ResolveAsync(
            new ApproverRule(ApproverStrategy.DeptLeader, null, null, null),
            new ApproverResolveContext { StarterUserId = u });

        Assert.Equal(leader, res.ApproverIds.Single()); // 沿父链找到 P 的负责人
    }

    [Fact]
    public async Task Role_ExcludesDisabledUsers()
    {
        using var db = NewDb();
        db.Sys_Users.AddRange(
            new Sys_User { Id = Guid.NewGuid(), UserName = "a", Password = "x", RoleId = 5, Enable = true },
            new Sys_User { Id = Guid.NewGuid(), UserName = "b", Password = "x", RoleId = 5, Enable = false }); // 停用排除
        await db.SaveChangesAsync();

        var res = await new ApproverResolver(db).ResolveAsync(
            new ApproverRule(ApproverStrategy.Role, null, 5, null),
            new ApproverResolveContext { StarterUserId = Guid.NewGuid() });

        Assert.Single(res.ApproverIds);
    }

    [Fact]
    public async Task Specified_ReturnsTheUser_OrUnresolvedWhenMissing()
    {
        using var db = NewDb();
        var target = Guid.NewGuid();

        var ok = await new ApproverResolver(db).ResolveAsync(
            new ApproverRule(ApproverStrategy.Specified, null, null, target), new ApproverResolveContext());
        Assert.Equal(target, ok.ApproverIds.Single());

        var miss = await new ApproverResolver(db).ResolveAsync(
            new ApproverRule(ApproverStrategy.Specified, null, null, null), new ApproverResolveContext());
        Assert.False(miss.Resolved);
    }

    [Fact]
    public async Task Starter_ReturnsStarterSelf()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid();
        var res = await new ApproverResolver(db).ResolveAsync(
            new ApproverRule(ApproverStrategy.Starter, null, null, null),
            new ApproverResolveContext { StarterUserId = starter });
        Assert.Equal(starter, res.ApproverIds.Single());
    }
}
