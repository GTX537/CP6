using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

/// <summary>
/// M-MES 横切接线 Task 2：MesMenuSeed 幂等 + MenuKey 锚定断言。
/// 头号命门回归：既有 300 段菜单在 Program.cs 回填块之后 Add 且未设 MenuKey → 洁净首启 null 全 403；
///   且 310 RoutePath=/mes/machine-list 回填得 mes-machine-list ≠ 真相源 mes-machine（machine 键错配）。
/// MesMenuSeed 须在回填前把 10 锚定行显式赋 mes-*，并把已被错误回填的行就地纠回。
/// </summary>
public class MesMenuSeedTests
{
    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    // 真相源 docs/seeds/mes-permission-keys.md §二 的 10 个 menu-key → 锚定 MenuId。
    private static readonly (string Key, int MenuId)[] Anchors =
    {
        ("mes-planning-board", 301),
        ("mes-work-order", 302),
        ("mes-production-result", 304),
        ("mes-quality-inspection", 306),
        ("mes-defect", 308),
        ("mes-machine", 310),
        ("mes-oee", 311),
        ("mes-plan-achievement", 313),
        ("mes-work-center", 314),
        ("mes-process-cost-rate", 315),
    };

    [Fact]
    public void EnsureSeeded_AnchorsAll10KeysToExpectedMenuIds()
    {
        using var db = NewDb();
        MesMenuSeed.EnsureSeeded(db);

        foreach (var (key, menuId) in Anchors)
        {
            var menu = db.Sys_Menus.SingleOrDefault(m => m.MenuId == menuId);
            Assert.NotNull(menu);
            Assert.Equal(key, menu!.MenuKey);
        }
    }

    [Fact]
    public void EnsureSeeded_MachineAnchoredToMesMachine_NotBackfilledMesMachineList()
    {
        using var db = NewDb();
        MesMenuSeed.EnsureSeeded(db);

        // 命门2：菜单 310 RoutePath=/mes/machine-list（回填得 mes-machine-list），须显式赋 mes-machine。
        var m = db.Sys_Menus.Single(x => x.MenuId == 310);
        Assert.Equal("/mes/machine-list", m.RoutePath);
        Assert.Equal("mes-machine", m.MenuKey);
    }

    [Fact]
    public void EnsureSeeded_NoTwoRowsShareANonNullMenuKey()
    {
        using var db = NewDb();
        MesMenuSeed.EnsureSeeded(db);

        // Sys_Menus.MenuKey 有 IS NOT NULL 过滤唯一索引；一域两页禁共键。断言锚定键各占一行。
        var keyed = db.Sys_Menus.Where(m => m.MenuKey != null).Select(m => m.MenuKey!).ToList();
        Assert.Equal(keyed.Count, keyed.Distinct().Count());
        Assert.Equal(10, keyed.Count);
    }

    [Fact]
    public void EnsureSeeded_IsIdempotent_NoDuplicateRowsOrRoleMenus()
    {
        using var db = NewDb();
        MesMenuSeed.EnsureSeeded(db);
        var menuCount1 = db.Sys_Menus.Count();
        var roleMenuCount1 = db.Sys_RoleMenus.Count();

        MesMenuSeed.EnsureSeeded(db);   // 第二次
        Assert.Equal(menuCount1, db.Sys_Menus.Count());
        Assert.Equal(roleMenuCount1, db.Sys_RoleMenus.Count());

        // 全 16 行（300 父 + 301–315）+ 各授管理员一条 RoleMenu。
        Assert.Equal(16, menuCount1);
        Assert.Equal(16, roleMenuCount1);
    }

    [Fact]
    public void EnsureSeeded_CorrectsHistoricalBackfilledBareKey_ToMesPrefixed()
    {
        using var db = NewDb();
        // 模拟既有库：310 設備管理 曾被历史回填成 mes-machine-list（≠ 真相源 mes-machine）——命门2。
        db.Sys_Menus.Add(new Sys_Menu
        {
            MenuId = 310, MenuName = "設備管理", RoutePath = "/mes/machine-list",
            MenuKey = "mes-machine-list", ParentId = 300, OrderNo = 310, Enable = true,
        });
        db.SaveChanges();

        MesMenuSeed.EnsureSeeded(db);

        var m = db.Sys_Menus.Single(x => x.MenuId == 310);
        Assert.Equal("mes-machine", m.MenuKey);   // 就地矫正
    }

    [Fact]
    public void EnsureSeeded_ListPagesAndParentAndGetOnly_LeaveMenuKeyNull_ForBackfill()
    {
        using var db = NewDb();
        MesMenuSeed.EnsureSeeded(db);

        // 父行 300 + 一覧页（303/305/307）+ GET-only 看板（309 dashboard / 312 control-tower）
        // 不承载权限：MenuKey 留 null（交由回填派生后缀键，无 action 引用、无害）。
        foreach (var id in new[] { 300, 303, 305, 307, 309, 312 })
        {
            var m = db.Sys_Menus.Single(x => x.MenuId == id);
            Assert.Null(m.MenuKey);
        }
    }
}
