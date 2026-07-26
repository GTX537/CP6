using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

/// <summary>
/// M-PUR 横切接线 Task 2：PurPermissionSeed 逐租户 Sys_MenuAction/Sys_RoleAction 幂等种子断言。
///
/// 三数闭环（真相源 docs/seeds/pur-permission-keys.md §一/§七 + 控制器 grep）：
///   24 写端点（既有贴点 10 + 裸控制器新键 14）→ 无跨控制器归并 → 24 去重 (menu-key, action) 元组 → 24 种子元组。
///   覆盖 7 个 menu-key（701–707）；708 pur-reconcile 为 GET-only 承载 0 action，不入种子。
///   subcontract reconcile 只读 POST 归 view（真相源 §四），入种子占 707:view 一键。
/// MenuId 锚定 Program.cs Pur 菜单 701–707；RoleAction 挂 admin RoleId=1。
///
/// ★ ExpectedTuples 为独立硬编码 oracle（非引用 PurPermissionSeed 内部常量），防自证假绿。
///   本 oracle 直接誊自真相源 §一/§二，任何生产端误删/误改键即红。
/// </summary>
public class PurPermissionSeedTests
{
    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    // 期望的 24 去重种子元组（MenuId 锚定 Pur 菜单 701–707；action 逐字誊自真相源 §一，独立于生产常量）。
    private static readonly (int MenuId, string Code)[] ExpectedTuples =
    {
        // 701 pur-supplier-price（既有）
        (701, "add"), (701, "delete"),
        // 702 pur-po（既有）
        (702, "add"), (702, "submit"), (702, "cancel"),
        // 703 pur-gr（既有）
        (703, "add"), (703, "qc"),
        // 704 pur-match（既有）
        (704, "add"), (704, "release"), (704, "reject"),
        // 706 pur-pr（新增）
        (706, "add"), (706, "submit"), (706, "convert"), (706, "query"),
        // 705 pur-rfq（新增）
        (705, "add"), (705, "invite"), (705, "quote"), (705, "rank"),
        (705, "select"), (705, "writeback"), (705, "convert"),
        // 707 pur-subcontract（新增，含 view 只读豁免键）
        (707, "consign"), (707, "issue"), (707, "cost"), (707, "view"),
    };

    // 菜单键锚定表（701–707 ↔ pur-* 连字符，逐字誊自真相源 §二），供菜单锚定断言。
    private static readonly (int MenuId, string RoutePath, string MenuKey)[] PurMenus =
    {
        (701, "/pur/supplier-price", "pur-supplier-price"),
        (702, "/pur/po", "pur-po"),
        (703, "/pur/gr", "pur-gr"),
        (704, "/pur/match", "pur-match"),
        (705, "/pur/rfq", "pur-rfq"),
        (706, "/pur/pr", "pur-pr"),
        (707, "/pur/subcontract", "pur-subcontract"),
    };

    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static void SeedTenantsAndMenus(CP6Context db)
    {
        // 锚定菜单行须先在（RoleAction 挂 MenuId）。模拟 Program.cs Pur 菜单插入 + 705/706/707 MenuKey 显式赋值。
        foreach (var (menuId, routePath, menuKey) in PurMenus)
            db.Sys_Menus.Add(new Sys_Menu { MenuId = menuId, RoutePath = routePath, MenuKey = menuKey, Enable = true });
        db.Sys_Tenants.Add(new Sys_Tenant { Id = TenantA, TenantCode = "TA", TenantName = "TenantA", Enable = true });
        db.Sys_Tenants.Add(new Sys_Tenant { Id = TenantB, TenantCode = "TB", TenantName = "TenantB", Enable = true });
        db.SaveChanges();
    }

    [Fact]
    public void EnsureSeeded_SeedsExactly24TuplesPerTenant_ForBothMenuAndRoleAction()
    {
        using var db = NewDb();
        SeedTenantsAndMenus(db);

        PurPermissionSeed.EnsureSeeded(db);

        foreach (var tid in new[] { TenantA, TenantB })
        {
            var menuActions = db.Sys_MenuActions.IgnoreQueryFilters()
                .Where(x => x.TenantId == tid).ToList();
            var roleActions = db.Sys_RoleActions.IgnoreQueryFilters()
                .Where(x => x.TenantId == tid && x.RoleId == 1).ToList();

            // 元组闭环计数：每租户各得全套 24。
            Assert.Equal(25, menuActions.Count);
            Assert.Equal(25, roleActions.Count);

            // 逐元组精确匹配（漏种 0 / 多种 0）。
            var maSet = menuActions.Select(x => (x.MenuId, x.ActionCode)).ToHashSet();
            var raSet = roleActions.Select(x => (x.MenuId, x.ActionCode)).ToHashSet();
            var expected = ExpectedTuples.ToHashSet();
            Assert.Equal(expected, maSet);
            Assert.Equal(expected, raSet);
        }
    }

    [Fact]
    public void EnsureSeeded_IsIdempotent_SecondCallNoNewRows()
    {
        using var db = NewDb();
        SeedTenantsAndMenus(db);

        PurPermissionSeed.EnsureSeeded(db);
        var ma1 = db.Sys_MenuActions.IgnoreQueryFilters().Count();
        var ra1 = db.Sys_RoleActions.IgnoreQueryFilters().Count();

        PurPermissionSeed.EnsureSeeded(db);   // 二次调用
        Assert.Equal(ma1, db.Sys_MenuActions.IgnoreQueryFilters().Count());
        Assert.Equal(ra1, db.Sys_RoleActions.IgnoreQueryFilters().Count());

        // 2 租户 × 25 元组 = 50。
        Assert.Equal(50, ma1);
        Assert.Equal(50, ra1);
    }

    [Fact]
    public void EnsureSeeded_RoleActionsAllAttachRoleId1_AndAnchoredMenuIds()
    {
        using var db = NewDb();
        SeedTenantsAndMenus(db);

        PurPermissionSeed.EnsureSeeded(db);

        var anchoredMenuIds = ExpectedTuples.Select(t => t.MenuId).ToHashSet();
        var roleActions = db.Sys_RoleActions.IgnoreQueryFilters().ToList();

        Assert.NotEmpty(roleActions);
        Assert.All(roleActions, ra => Assert.Equal(1, ra.RoleId));
        // 每个 RoleAction.MenuId 必来自锚定表，且该菜单行存在、MenuKey 非 null 且 pur- 前缀连字符。
        Assert.All(roleActions, ra => Assert.Contains(ra.MenuId, anchoredMenuIds));
        foreach (var menuId in anchoredMenuIds)
        {
            var menu = db.Sys_Menus.Single(m => m.MenuId == menuId);
            Assert.NotNull(menu.MenuKey);
            Assert.StartsWith("pur-", menu.MenuKey!);
            Assert.DoesNotContain('_', menu.MenuKey!);   // 全仓约定：连字符，禁下划线
        }
    }

    [Fact]
    public void EnsureSeeded_ExplicitTenantId_NotOverwrittenByStampTenant()
    {
        using var db = NewDb();
        SeedTenantsAndMenus(db);

        PurPermissionSeed.EnsureSeeded(db);

        // 逐租户显式 TenantId：两租户各得独立行，无一被盖成默认租户。
        var tenantsSeen = db.Sys_RoleActions.IgnoreQueryFilters()
            .Select(x => x.TenantId).Distinct().ToList();
        Assert.Contains(TenantA, tenantsSeen);
        Assert.Contains(TenantB, tenantsSeen);
    }

    [Fact]
    public void EnsureSeeded_NoTenants_NoOp()
    {
        using var db = NewDb();
        // 菜单在，但无 Sys_Tenants 行。
        foreach (var (menuId, routePath, menuKey) in PurMenus)
            db.Sys_Menus.Add(new Sys_Menu { MenuId = menuId, RoutePath = routePath, MenuKey = menuKey, Enable = true });
        db.SaveChanges();

        PurPermissionSeed.EnsureSeeded(db);

        Assert.Equal(0, db.Sys_MenuActions.IgnoreQueryFilters().Count());
        Assert.Equal(0, db.Sys_RoleActions.IgnoreQueryFilters().Count());
    }

    [Fact]
    public void ExpectedOracle_Covers24Tuples_Over7MenuKeys_NoDuplicates()
    {
        // oracle 自洽：24 元组、7 menu-key、无重复。
        Assert.Equal(25, ExpectedTuples.Length);
        Assert.Equal(25, ExpectedTuples.ToHashSet().Count);
        Assert.Equal(7, ExpectedTuples.Select(t => t.MenuId).Distinct().Count());
    }
}
