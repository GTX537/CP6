using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

/// <summary>
/// M-OA/WF 横切接线 Task 2：OawfMenuSeed 幂等 + MenuKey 锚定断言。
/// 头号命门回归：OA 菜单 733–740 在 Program.cs 回填块（:908）之后 Add 且未设 MenuKey → 洁净首启 null 全 403。
///   OawfMenuSeed 须在回填前把 7 锚定行显式赋 oa-*（含缺行补建），并把已被历史回填/异常写坏的行就地纠回。
///   与 MES 不同：OA 派生键与真相源逐字一致（零错配），命门纯为时序；矫正块仍严限 7 锚定行按 MenuId 定位。
/// </summary>
public class OawfMenuSeedTests
{
    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    // 真相源 docs/seeds/oawf-permission-keys.md §二 的 7 个 menu-key → 锚定 MenuId。
    private static readonly (string Key, int MenuId)[] Anchors =
    {
        ("oa-inbox", 733),
        ("oa-flow-admin", 734),
        ("oa-form-catalog", 735),
        ("oa-form-search", 736),
        ("oa-settings", 737),
        ("oa-designer", 738),
        ("oa-approver-map", 739),
    };

    [Fact]
    public void EnsureSeeded_AnchorsAll7KeysToExpectedMenuIds()
    {
        using var db = NewDb();
        OawfMenuSeed.EnsureSeeded(db);

        foreach (var (key, menuId) in Anchors)
        {
            var menu = db.Sys_Menus.SingleOrDefault(m => m.MenuId == menuId);
            Assert.NotNull(menu);
            Assert.Equal(key, menu!.MenuKey);
        }
    }

    [Fact]
    public void EnsureSeeded_AnchoredRowsMatchTruthSourceRoutePaths()
    {
        using var db = NewDb();
        OawfMenuSeed.EnsureSeeded(db);

        // OA RoutePath 与 menu-key 天然对齐（回填即一致），仍显式锚定规避时序命门；断言 RoutePath 不被改动。
        var expected = new (int Id, string Route, string Key)[]
        {
            (733, "/oa/inbox", "oa-inbox"),
            (734, "/oa/flow-admin", "oa-flow-admin"),
            (735, "/oa/form-catalog", "oa-form-catalog"),
            (736, "/oa/form-search", "oa-form-search"),
            (737, "/oa/settings", "oa-settings"),
            (738, "/oa/designer", "oa-designer"),
            (739, "/oa/approver-map", "oa-approver-map"),
        };
        foreach (var (id, route, key) in expected)
        {
            var m = db.Sys_Menus.Single(x => x.MenuId == id);
            Assert.Equal(route, m.RoutePath);
            Assert.Equal(key, m.MenuKey);
        }
    }

    [Fact]
    public void EnsureSeeded_NoTwoRowsShareANonNullMenuKey()
    {
        using var db = NewDb();
        OawfMenuSeed.EnsureSeeded(db);

        // Sys_Menus.MenuKey 有 IS NOT NULL 过滤唯一索引；两行禁共键。断言 7 锚定键各占一行。
        var keyed = db.Sys_Menus.Where(m => m.MenuKey != null).Select(m => m.MenuKey!).ToList();
        Assert.Equal(keyed.Count, keyed.Distinct().Count());
        Assert.Equal(7, keyed.Count);
    }

    [Fact]
    public void EnsureSeeded_IsIdempotent_NoDuplicateRowsOrRoleMenus()
    {
        using var db = NewDb();
        OawfMenuSeed.EnsureSeeded(db);
        var menuCount1 = db.Sys_Menus.Count();
        var roleMenuCount1 = db.Sys_RoleMenus.Count();

        OawfMenuSeed.EnsureSeeded(db);   // 第二次（含新收编行，幂等不重复插入）
        Assert.Equal(menuCount1, db.Sys_Menus.Count());
        Assert.Equal(roleMenuCount1, db.Sys_RoleMenus.Count());

        // 全 10 行（740 父 + 733–739 七锚定 + 741/742 双栈收编）+ 各授管理员一条 RoleMenu。
        Assert.Equal(10, menuCount1);
        Assert.Equal(10, roleMenuCount1);
    }

    // ── 双栈收编（用户裁决 2026-07-12）：追补 2 用例 ──────────────────────────

    [Fact]
    public void EnsureSeeded_CollectsOrphanDesignerRoutes_741And742_WithNullMenuKeyAndRoutePathMatchingViewModules()
    {
        using var db = NewDb();
        OawfMenuSeed.EnsureSeeded(db);

        // 收编行存在，RoutePath 须与 cp6.web/src/router/index.ts:46-47 viewModules 键逐字一致，
        // 否则 addDynamicRoutes 仍不注册（前端仍不可达）。MenuKey 留 null：权限已锚 738 oa-designer。
        var formDesigner = db.Sys_Menus.SingleOrDefault(m => m.MenuId == 741);
        Assert.NotNull(formDesigner);
        Assert.Equal("/wf/form-designer", formDesigner!.RoutePath);
        Assert.Null(formDesigner.MenuKey);
        Assert.Equal(740, formDesigner.ParentId);
        Assert.True(formDesigner.Enable);

        var flowDesigner = db.Sys_Menus.SingleOrDefault(m => m.MenuId == 742);
        Assert.NotNull(flowDesigner);
        Assert.Equal("/wf/flow-designer", flowDesigner!.RoutePath);
        Assert.Null(flowDesigner.MenuKey);
        Assert.Equal(740, flowDesigner.ParentId);
        Assert.True(flowDesigner.Enable);

        // 均授管理员菜单（RoleId=1），照收编先例。
        Assert.True(db.Sys_RoleMenus.Any(rm => rm.RoleId == 1 && rm.MenuId == 741));
        Assert.True(db.Sys_RoleMenus.Any(rm => rm.RoleId == 1 && rm.MenuId == 742));
    }

    [Fact]
    public void EnsureSeeded_IsIdempotent_WithOrphanCollectionRows_NoDuplicatesOnSecondRun()
    {
        using var db = NewDb();
        OawfMenuSeed.EnsureSeeded(db);
        OawfMenuSeed.EnsureSeeded(db);   // 第二次

        // 741/742 各恰一行、MenuKey 仍为 null（未被防御矫正块误赋值——该块严限 r.Key != null）。
        Assert.Equal(1, db.Sys_Menus.Count(m => m.MenuId == 741));
        Assert.Equal(1, db.Sys_Menus.Count(m => m.MenuId == 742));
        Assert.Null(db.Sys_Menus.Single(m => m.MenuId == 741).MenuKey);
        Assert.Null(db.Sys_Menus.Single(m => m.MenuId == 742).MenuKey);

        // 唯一索引安全：非空 MenuKey 仍恰 7 个（733–739 锚定，"oa-designer" 仅 738 一行持有），
        // 741/742 未与 738 共键——若共键会撞 Sys_Menus.MenuKey IS NOT NULL 过滤唯一索引。
        var keyed = db.Sys_Menus.Where(m => m.MenuKey != null).Select(m => m.MenuKey!).ToList();
        Assert.Equal(7, keyed.Count);
        Assert.Equal(1, keyed.Count(k => k == "oa-designer"));
    }

    [Fact]
    public void EnsureSeeded_CorrectsHistoricalWrongKey_ToOaAuthoritative()
    {
        using var db = NewDb();
        // 模拟既有库：738 流程设计器 曾被异常写成 wf-flow-designer（≠ 真相源 oa-designer）。
        db.Sys_Menus.Add(new Sys_Menu
        {
            MenuId = 738, MenuName = "流程设计器", RoutePath = "/oa/designer",
            MenuKey = "wf-flow-designer", ParentId = 740, OrderNo = 738, Enable = true,
        });
        db.SaveChanges();

        OawfMenuSeed.EnsureSeeded(db);

        var m = db.Sys_Menus.Single(x => x.MenuId == 738);
        Assert.Equal("oa-designer", m.MenuKey);   // 就地矫正（按 MenuId 定位）
    }

    [Fact]
    public void EnsureSeeded_ParentRow740_LeavesMenuKeyNull()
    {
        using var db = NewDb();
        OawfMenuSeed.EnsureSeeded(db);

        // 740 OA工作流 父组行无 RoutePath、不承载权限：MenuKey 留 null。
        var m = db.Sys_Menus.Single(x => x.MenuId == 740);
        Assert.Null(m.MenuKey);
        Assert.Null(m.RoutePath);
    }
}
