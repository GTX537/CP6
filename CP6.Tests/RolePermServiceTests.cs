using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DTOs.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class RolePermServiceTests
{
    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    private sealed class SpyCurrent : ICurrentPermissionContext
    {
        public List<int> InvalidatedRoles { get; } = new();
        private readonly UserPermissionContext _ctx;
        public SpyCurrent(UserPermissionContext? ctx = null) => _ctx = ctx ?? new UserPermissionContext();
        public Task<UserPermissionContext> GetAsync() => Task.FromResult(_ctx);
        public void Invalidate(Guid userId) { }
        public void InvalidateByRole(int roleId) => InvalidatedRoles.Add(roleId);
    }

    [Fact]
    public async Task SaveRolePerm_ActionNotInGrantedMenu_Throws_E021()
    {
        using var db = NewDb();
        var svc = new RolePermService(db, new SpyCurrent());
        var dto = new RolePermDto
        {
            MenuIds = new() { 10 },
            Actions = new() { new RoleActionItem { MenuId = 99, ActionCode = "export" } }   // 99 ∉ {10}
        };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SaveRolePermAsync(1, dto, "t"));
        Assert.Equal("E-PUB-021", ex.Message);
    }

    [Fact]
    public async Task SaveRolePerm_DiffsMenusAndActions_AndInvalidatesRole()
    {
        using var db = NewDb();
        // 既有：菜单 {10,20}，操作 (10,query)
        db.Sys_RoleMenus.AddRange(new Sys_RoleMenu { RoleId = 1, MenuId = 10 }, new Sys_RoleMenu { RoleId = 1, MenuId = 20 });
        db.Sys_RoleActions.Add(new Sys_RoleAction { Id = Guid.NewGuid(), RoleId = 1, MenuId = 10, ActionCode = "query" });
        await db.SaveChangesAsync();

        var spy = new SpyCurrent();
        var svc = new RolePermService(db, spy);
        // 目标：菜单 {10,30}（删20增30），操作 (10,export)（删query增export）
        var dto = new RolePermDto
        {
            MenuIds = new() { 10, 30 },
            Actions = new() { new RoleActionItem { MenuId = 10, ActionCode = "export" } }
        };
        await svc.SaveRolePermAsync(1, dto, "t");

        var menus = await db.Sys_RoleMenus.Where(m => m.RoleId == 1).Select(m => m.MenuId).OrderBy(x => x).ToListAsync();
        Assert.Equal(new[] { 10, 30 }, menus);
        var acts = await db.Sys_RoleActions.Where(a => a.RoleId == 1).Select(a => a.ActionCode).ToListAsync();
        Assert.Equal(new[] { "export" }, acts);
        Assert.Contains(1, spy.InvalidatedRoles);
    }

    [Fact]
    public async Task SaveMenuActions_DiffsByActionCode()
    {
        using var db = NewDb();
        db.Sys_MenuActions.Add(new Sys_MenuAction { Id = Guid.NewGuid(), MenuId = 10, ActionCode = "query", ActionName = "旧名", Sort = 1 });
        db.Sys_MenuActions.Add(new Sys_MenuAction { Id = Guid.NewGuid(), MenuId = 10, ActionCode = "del", ActionName = "删除", Sort = 2 });
        await db.SaveChangesAsync();

        var svc = new RolePermService(db, new SpyCurrent());
        // 目标：query(改名) + export(新增)，del 被删
        await svc.SaveMenuActionsAsync(10, new()
        {
            new MenuActionDto { ActionCode = "query", ActionName = "查询", Sort = 1 },
            new MenuActionDto { ActionCode = "export", ActionName = "导出", Sort = 3 }
        }, "t");

        var rows = await db.Sys_MenuActions.Where(a => a.MenuId == 10).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal("查询", rows.Single(r => r.ActionCode == "query").ActionName);   // 改名生效
        Assert.Contains(rows, r => r.ActionCode == "export");
        Assert.DoesNotContain(rows, r => r.ActionCode == "del");                       // 已删
    }

    [Fact]
    public async Task MyActions_ReturnsSortedActionKeys()
    {
        using var db = NewDb();
        var ctx = new UserPermissionContext { ActionKeys = { "order:export", "order:add" } };
        var svc = new RolePermService(db, new SpyCurrent(ctx));
        var keys = await svc.MyActionsAsync();
        Assert.Equal(new[] { "order:add", "order:export" }, keys);   // 已排序
    }

    // ───── C-3 数据权限配置 ─────

    [Fact]
    public async Task SaveDataScope_UnsupportedScope_Throws_E031()
    {
        DataScopeRegistry.Register("ds-e031", "测试资源", new[] { 1, 2 }, 1);
        using var db = NewDb();
        var svc = new RolePermService(db, new SpyCurrent());
        var items = new List<RoleDataScopeDto> { new() { ResourceKey = "ds-e031", ScopeType = 5 } };   // 5 ∉ [1,2]
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SaveRoleDataScopesAsync(1, items, "t"));
        Assert.Equal("E-PUB-031", ex.Message);
    }

    [Fact]
    public async Task SaveDataScope_CustomWithoutDepts_Throws_E032()
    {
        using var db = NewDb();
        var svc = new RolePermService(db, new SpyCurrent());
        var items = new List<RoleDataScopeDto> { new() { ResourceKey = "x", ScopeType = 4, CustomDeptIds = new() } };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SaveRoleDataScopesAsync(1, items, "t"));
        Assert.Equal("E-PUB-032", ex.Message);
    }

    [Fact]
    public async Task SaveDataScope_ReplacesExisting_PersistsCsv_AndInvalidates()
    {
        using var db = NewDb();
        db.Sys_RoleDataScopes.Add(new Sys_RoleDataScope { Id = Guid.NewGuid(), RoleId = 1, ResourceKey = "old", ScopeType = 1 });
        await db.SaveChangesAsync();

        var spy = new SpyCurrent();
        var svc = new RolePermService(db, spy);
        var d1 = Guid.NewGuid();
        var d2 = Guid.NewGuid();
        var items = new List<RoleDataScopeDto>
        {
            new() { ResourceKey = "order", ScopeType = 4, CustomDeptIds = new() { d1, d2 } },
            new() { ResourceKey = "ship", ScopeType = 2 }
        };
        await svc.SaveRoleDataScopesAsync(1, items, "t");

        var rows = await db.Sys_RoleDataScopes.Where(d => d.RoleId == 1).ToListAsync();
        Assert.Equal(2, rows.Count);                                  // old 被全量替换
        Assert.DoesNotContain(rows, r => r.ResourceKey == "old");
        Assert.Equal($"{d1},{d2}", rows.Single(r => r.ResourceKey == "order").CustomDeptIds);   // CSV 持久化
        Assert.Null(rows.Single(r => r.ResourceKey == "ship").CustomDeptIds);                    // 非自定义不存
        Assert.Contains(1, spy.InvalidatedRoles);
    }

    // ───── D-3 字段权限配置 ─────

    [Fact]
    public async Task SaveFieldPerm_FieldNotRegistered_Throws_E041()
    {
        FieldRegistry.Register("fp-e041", new FieldRegistry.Field("Known", "已知"));
        using var db = NewDb();
        var svc = new RolePermService(db, new SpyCurrent());
        var items = new List<RoleFieldPermDto> { new() { ResourceKey = "fp-e041", FieldName = "Unknown", Access = 3 } };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SaveRoleFieldPermsAsync(1, items, "t"));
        Assert.Equal("E-PUB-041", ex.Message);
    }

    [Fact]
    public async Task SaveFieldPerm_StoresOnlyNonDefault_PerResource_AndInvalidates()
    {
        FieldRegistry.Register("fp-res", new FieldRegistry.Field("Cost", "成本"), new FieldRegistry.Field("Price", "价格"));
        using var db = NewDb();
        // 既有：别的资源 other 的配置（不应被本次保存波及）
        db.Sys_RoleFieldPerms.Add(new Sys_RoleFieldPerm { Id = Guid.NewGuid(), RoleId = 1, ResourceKey = "other", FieldName = "X", Access = 2 });
        await db.SaveChangesAsync();

        var spy = new SpyCurrent();
        var svc = new RolePermService(db, spy);
        var items = new List<RoleFieldPermDto>
        {
            new() { ResourceKey = "fp-res", FieldName = "Cost", Access = 3 },   // 隐藏 → 存
            new() { ResourceKey = "fp-res", FieldName = "Price", Access = 1 }   // 可读写 → 不存
        };
        await svc.SaveRoleFieldPermsAsync(1, items, "t");

        var fp = await db.Sys_RoleFieldPerms.Where(f => f.RoleId == 1 && f.ResourceKey == "fp-res").ToListAsync();
        Assert.Single(fp);                          // 仅 Cost 落库（Price=1 不存）
        Assert.Equal("Cost", fp[0].FieldName);
        Assert.Equal(3, fp[0].Access);
        Assert.True(await db.Sys_RoleFieldPerms.AnyAsync(f => f.ResourceKey == "other"));   // 其它资源未被误删
        Assert.Contains(1, spy.InvalidatedRoles);
    }

    [Fact]
    public async Task MyReadonly_ReturnsAccess2Fields()
    {
        using var db = NewDb();
        var ctx = new UserPermissionContext
        {
            FieldPerms = { ["order"] = new() { ["Cost"] = 2, ["Memo"] = 3, ["Price"] = 1 } }
        };
        var keys = await new RolePermService(db, new SpyCurrent(ctx)).MyReadonlyAsync("order");
        Assert.Equal(new[] { "Cost" }, keys);   // 只 Access=2（只读）
    }
}
