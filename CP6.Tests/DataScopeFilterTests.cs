using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class DataScopeFilterTests
{
    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    private sealed class FakeOrder : IDataScoped
    {
        public string? Creator { get; set; }
        public Guid? DeptId { get; set; }
    }

    [Fact]
    public void Scope1_Self_FiltersByCreator()
    {
        using var db = NewDb();
        var data = new[]
        {
            new FakeOrder { Creator = "alice", DeptId = Guid.NewGuid() },
            new FakeOrder { Creator = "bob", DeptId = Guid.NewGuid() }
        }.AsQueryable();
        var ctx = new UserPermissionContext { UserName = "alice", DataScopes = { ["order"] = 1 } };

        var r = new DataScopeFilter(db).Apply(data, "order", ctx).ToList();
        Assert.Single(r);
        Assert.Equal("alice", r[0].Creator);
    }

    [Fact]
    public void Scope2_OwnDept_FiltersByDeptId()
    {
        using var db = NewDb();
        var mine = Guid.NewGuid();
        var data = new[] { new FakeOrder { DeptId = mine }, new FakeOrder { DeptId = Guid.NewGuid() } }.AsQueryable();
        var ctx = new UserPermissionContext { DeptId = mine, DataScopes = { ["order"] = 2 } };

        var r = new DataScopeFilter(db).Apply(data, "order", ctx).ToList();
        Assert.Single(r);
        Assert.Equal(mine, r[0].DeptId);
    }

    [Fact]
    public void Scope3_Subtree_FiltersByPathPrefix()
    {
        using var db = NewDb();
        var d1 = Guid.NewGuid();
        var d2 = Guid.NewGuid();
        db.Sys_Depts.AddRange(
            new Sys_Dept { Id = d1, DeptCode = "A", DeptName = "A", Path = $"/{d1}/" },
            new Sys_Dept { Id = d2, DeptCode = "A1", DeptName = "A1", Path = $"/{d1}/{d2}/" });
        db.SaveChanges();

        var data = new[] { new FakeOrder { DeptId = d2 }, new FakeOrder { DeptId = Guid.NewGuid() } }.AsQueryable();
        var ctx = new UserPermissionContext { DeptPath = $"/{d1}/", DataScopes = { ["order"] = 3 } };

        var r = new DataScopeFilter(db).Apply(data, "order", ctx).ToList();
        Assert.Single(r);             // 只 d2（在 A 子树内）
        Assert.Equal(d2, r[0].DeptId);
    }

    [Fact]
    public void Scope4_Custom_FiltersByDeptIdList()
    {
        using var db = NewDb();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var data = new[] { new FakeOrder { DeptId = a }, new FakeOrder { DeptId = b }, new FakeOrder { DeptId = Guid.NewGuid() } }.AsQueryable();
        var ctx = new UserPermissionContext { DataScopes = { ["order"] = 4 }, CustomDeptIds = { ["order"] = new List<Guid> { a, b } } };

        var r = new DataScopeFilter(db).Apply(data, "order", ctx).ToList();
        Assert.Equal(2, r.Count);
    }

    [Fact]
    public void Scope5_All_NoFilter()
    {
        using var db = NewDb();
        var data = new[] { new FakeOrder { DeptId = Guid.NewGuid() }, new FakeOrder { DeptId = Guid.NewGuid() } }.AsQueryable();
        var ctx = new UserPermissionContext { DataScopes = { ["order"] = 5 } };

        var r = new DataScopeFilter(db).Apply(data, "order", ctx).ToList();
        Assert.Equal(2, r.Count);
    }

    [Fact]
    public void UnconfiguredResource_DefaultsToSelf()
    {
        using var db = NewDb();
        var data = new[] { new FakeOrder { Creator = "alice" }, new FakeOrder { Creator = "bob" } }.AsQueryable();
        var ctx = new UserPermissionContext { UserName = "alice" };   // DataScopes 无 "order"

        var r = new DataScopeFilter(db).Apply(data, "order", ctx).ToList();
        Assert.Single(r);   // 回落本人
        Assert.Equal("alice", r[0].Creator);
    }
}
