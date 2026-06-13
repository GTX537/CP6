using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class PermissionAggregatorTests
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
    public async Task Build_MergesAllRoles_UnionMenus()
    {
        using var db = NewDb();
        var uid = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User { Id = uid, UserName = "u", Password = "x", RoleId = 1 });   // 主角色 1
        db.Sys_UserRoles.Add(new Sys_UserRole { Id = Guid.NewGuid(), UserId = uid, RoleId = 2 });  // 附加角色 2
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 10, MenuName = "受注", MenuKey = "order" });
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 11, MenuName = "出荷", MenuKey = "ship" });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 10 });   // 角色1 → order
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 2, MenuId = 11 });   // 角色2 → ship
        await db.SaveChangesAsync();

        var agg = new PermissionAggregator(db);
        var ctx = await agg.BuildAsync(uid);

        Assert.Equal(new[] { 1, 2 }, ctx.RoleIds.OrderBy(x => x));   // 主角色并入
        Assert.Contains("order", ctx.MenuKeys);                     // 角色1 的菜单
        Assert.Contains("ship", ctx.MenuKeys);                      // 角色2 的菜单（并集）
    }

    [Fact]
    public async Task Build_NoDuplicateRole_WhenPrimaryAlsoInUserRole()
    {
        using var db = NewDb();
        var uid = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User { Id = uid, UserName = "u", Password = "x", RoleId = 5 });
        db.Sys_UserRoles.Add(new Sys_UserRole { Id = Guid.NewGuid(), UserId = uid, RoleId = 5 });  // 与主角色重复
        await db.SaveChangesAsync();

        var ctx = await new PermissionAggregator(db).BuildAsync(uid);

        Assert.Equal(new[] { 5 }, ctx.RoleIds);   // 去重
    }

    [Fact]
    public async Task Build_FillsDeptPath_FromUserDept()
    {
        using var db = NewDb();
        var uid = Guid.NewGuid();
        var did = Guid.NewGuid();
        db.Sys_Depts.Add(new Sys_Dept { Id = did, DeptCode = "A", DeptName = "A部", Path = $"/{did}/" });
        db.Sys_Users.Add(new Sys_User { Id = uid, UserName = "u", Password = "x", DeptId = did });
        await db.SaveChangesAsync();

        var ctx = await new PermissionAggregator(db).BuildAsync(uid);

        Assert.Equal(did, ctx.DeptId);
        Assert.Equal($"/{did}/", ctx.DeptPath);
    }

    [Fact]
    public async Task Build_UnknownUser_Throws()
    {
        using var db = NewDb();
        var agg = new PermissionAggregator(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => agg.BuildAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Build_FillsFieldPerms_MinAccessAcrossRoles()
    {
        using var db = NewDb();
        var uid = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User { Id = uid, UserName = "u", Password = "x", RoleId = 1 });
        db.Sys_UserRoles.Add(new Sys_UserRole { Id = Guid.NewGuid(), UserId = uid, RoleId = 2 });
        // Cost：role1 隐藏(3) / role2 只读(2) → MIN=2（最可见）
        db.Sys_RoleFieldPerms.Add(new Sys_RoleFieldPerm { Id = Guid.NewGuid(), RoleId = 1, ResourceKey = "order", FieldName = "Cost", Access = 3 });
        db.Sys_RoleFieldPerms.Add(new Sys_RoleFieldPerm { Id = Guid.NewGuid(), RoleId = 2, ResourceKey = "order", FieldName = "Cost", Access = 2 });
        // Price：仅 role1 只读(2)
        db.Sys_RoleFieldPerms.Add(new Sys_RoleFieldPerm { Id = Guid.NewGuid(), RoleId = 1, ResourceKey = "order", FieldName = "Price", Access = 2 });
        await db.SaveChangesAsync();

        var ctx = await new PermissionAggregator(db).BuildAsync(uid);

        Assert.Equal(2, ctx.FieldPerms["order"]["Cost"]);    // MIN(3,2)=2
        Assert.Equal(2, ctx.FieldPerms["order"]["Price"]);
    }

    [Fact]
    public async Task Build_FillsDataScopes_MaxScope_AndCustomDeptUnion()
    {
        using var db = NewDb();
        var uid = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User { Id = uid, UserName = "u", Password = "x", RoleId = 1 });
        db.Sys_UserRoles.Add(new Sys_UserRole { Id = Guid.NewGuid(), UserId = uid, RoleId = 2 });
        var d1 = Guid.NewGuid();
        var d2 = Guid.NewGuid();
        db.Sys_RoleDataScopes.AddRange(
            new Sys_RoleDataScope { Id = Guid.NewGuid(), RoleId = 1, ResourceKey = "order", ScopeType = 4, CustomDeptIds = $"{d1}" },
            new Sys_RoleDataScope { Id = Guid.NewGuid(), RoleId = 2, ResourceKey = "order", ScopeType = 4, CustomDeptIds = $"{d2},{d1}" },  // 并集 + 去重
            new Sys_RoleDataScope { Id = Guid.NewGuid(), RoleId = 1, ResourceKey = "ship", ScopeType = 2 },
            new Sys_RoleDataScope { Id = Guid.NewGuid(), RoleId = 2, ResourceKey = "ship", ScopeType = 5 });   // MAX=5
        await db.SaveChangesAsync();

        var ctx = await new PermissionAggregator(db).BuildAsync(uid);

        Assert.Equal(4, ctx.DataScopes["order"]);            // 同资源多角色 MAX
        Assert.Equal(5, ctx.DataScopes["ship"]);             // MAX(2,5)=5
        Assert.Equal(2, ctx.CustomDeptIds["order"].Count);   // 自定义并集去重
        Assert.Contains(d1, ctx.CustomDeptIds["order"]);
        Assert.Contains(d2, ctx.CustomDeptIds["order"]);
    }

    [Fact]
    public async Task Build_FillsActionKeys_UnionAcrossRoles_FiltersNullMenuKey()
    {
        using var db = NewDb();
        var uid = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User { Id = uid, UserName = "u", Password = "x", RoleId = 1 });
        db.Sys_UserRoles.Add(new Sys_UserRole { Id = Guid.NewGuid(), UserId = uid, RoleId = 2 });
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 10, MenuName = "受注", MenuKey = "order" });
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 99, MenuName = "无键菜单", MenuKey = null });   // 未配 MenuKey
        db.Sys_RoleActions.Add(new Sys_RoleAction { Id = Guid.NewGuid(), RoleId = 1, MenuId = 10, ActionCode = "query" });
        db.Sys_RoleActions.Add(new Sys_RoleAction { Id = Guid.NewGuid(), RoleId = 2, MenuId = 10, ActionCode = "export" });
        db.Sys_RoleActions.Add(new Sys_RoleAction { Id = Guid.NewGuid(), RoleId = 1, MenuId = 99, ActionCode = "delete" });  // 应被过滤
        await db.SaveChangesAsync();

        var ctx = await new PermissionAggregator(db).BuildAsync(uid);

        Assert.Contains("order:query", ctx.ActionKeys);
        Assert.Contains("order:export", ctx.ActionKeys);              // 跨角色并集
        Assert.DoesNotContain(ctx.ActionKeys, k => k.StartsWith(":")); // 无脏键
        Assert.Equal(2, ctx.ActionKeys.Count);
    }
}
