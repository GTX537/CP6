using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

/// <summary>
/// M-ERP 横切接线 Task 2：ErpMenuSeed 幂等 + MenuKey 锚定断言。
/// 头号命门回归：既有 201–215 裸路径若被回填成 order/product… 无 erp- 前缀会与真相源失配 → 全 ERP 403。
/// </summary>
public class ErpMenuSeedTests
{
    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    // 真相源 docs/seeds/erp-permission-keys.md §二 的 14 个 menu-key → 锚定 MenuId。
    private static readonly (string Key, int MenuId)[] Anchors =
    {
        ("erp-estimate-calc", 202),
        ("erp-quotation", 204),
        ("erp-product", 206),
        ("erp-order", 208),
        ("erp-order-price-correction", 209),
        ("erp-fsc-checklist", 210),
        ("erp-business-partner", 212),
        ("erp-sheet-unit-price", 213),
        ("erp-plate-mold", 215),
        ("erp-order-trace", 216),
        ("erp-credit-note", 217),
        ("erp-backorder", 218),
        ("erp-otd-report", 219),
        ("erp-fx-rate", 220),
    };

    [Fact]
    public void EnsureSeeded_AnchorsAll14KeysToExpectedMenuIds()
    {
        using var db = NewDb();
        ErpMenuSeed.EnsureSeeded(db);

        foreach (var (key, menuId) in Anchors)
        {
            var menu = db.Sys_Menus.SingleOrDefault(m => m.MenuId == menuId);
            Assert.NotNull(menu);
            Assert.Equal(key, menu!.MenuKey);
        }
    }

    [Fact]
    public void EnsureSeeded_OrphanRoutes_216To220_CreatedWithRoutePathAndParent()
    {
        using var db = NewDb();
        ErpMenuSeed.EnsureSeeded(db);

        var orphans = new (int Id, string Route)[]
        {
            (216, "/erp/order-trace"),
            (217, "/erp/credit-note"),
            (218, "/erp/backorder"),
            (219, "/erp/otd-report"),
            (220, "/erp/fx-rate"),
        };
        foreach (var (id, route) in orphans)
        {
            var m = db.Sys_Menus.SingleOrDefault(x => x.MenuId == id);
            Assert.NotNull(m);
            Assert.Equal(route, m!.RoutePath);
            Assert.Equal(200, m.ParentId);
            Assert.True(m.Enable);
        }
    }

    [Fact]
    public void EnsureSeeded_NoTwoRowsShareANonNullMenuKey()
    {
        using var db = NewDb();
        ErpMenuSeed.EnsureSeeded(db);

        // Sys_Menus.MenuKey 有 IS NOT NULL 过滤唯一索引；一域两页禁共键。断言锚定键各占一行。
        var keyed = db.Sys_Menus.Where(m => m.MenuKey != null).Select(m => m.MenuKey!).ToList();
        Assert.Equal(keyed.Count, keyed.Distinct().Count());
    }

    [Fact]
    public void EnsureSeeded_IsIdempotent_NoDuplicateRowsOrRoleMenus()
    {
        using var db = NewDb();
        ErpMenuSeed.EnsureSeeded(db);
        var menuCount1 = db.Sys_Menus.Count();
        var roleMenuCount1 = db.Sys_RoleMenus.Count();

        ErpMenuSeed.EnsureSeeded(db);   // 第二次
        Assert.Equal(menuCount1, db.Sys_Menus.Count());
        Assert.Equal(roleMenuCount1, db.Sys_RoleMenus.Count());

        // 全 21 行（200 父 + 201–215 + 216–220）+ 各授管理员一条 RoleMenu。
        Assert.Equal(21, menuCount1);
        Assert.Equal(21, roleMenuCount1);
    }

    [Fact]
    public void EnsureSeeded_CorrectsHistoricalBackfilledBareKey_ToErpPrefixed()
    {
        using var db = NewDb();
        // 模拟既有库：208 受注入力 曾被历史回填成裸键 "order"（无 erp- 前缀）——头号命门。
        db.Sys_Menus.Add(new Sys_Menu
        {
            MenuId = 208, MenuName = "受注入力", RoutePath = "/order",
            MenuKey = "order", ParentId = 200, OrderNo = 208, Enable = true,
        });
        db.SaveChanges();

        ErpMenuSeed.EnsureSeeded(db);

        var m = db.Sys_Menus.Single(x => x.MenuId == 208);
        Assert.Equal("erp-order", m.MenuKey);   // 就地矫正
    }

    [Fact]
    public void EnsureSeeded_ListPages_LeaveMenuKeyNull_ForBackfill()
    {
        using var db = NewDb();
        ErpMenuSeed.EnsureSeeded(db);

        // 一覧页（201/203/205/207/211/214）与父行 200 不承载权限：MenuKey 留 null（交由回填派生）。
        foreach (var id in new[] { 200, 201, 203, 205, 207, 211, 214 })
        {
            var m = db.Sys_Menus.Single(x => x.MenuId == id);
            Assert.Null(m.MenuKey);
        }
    }
}
