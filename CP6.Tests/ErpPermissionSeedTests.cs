using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

/// <summary>
/// M-ERP 横切接线 Task 3b：ErpPermissionSeed 逐租户 Sys_MenuAction/Sys_RoleAction 幂等种子断言。
///
/// 三数闭环（真相源 docs/seeds/erp-permission-keys.md + 控制器 grep）：
///   35 写端点（[RequirePermission] 贴点）→ 去重 (menu-key, action) 30 元组 → 30 种子元组。
///   （11 只读 POST 豁免不贴点、不入种子；erp-order-trace/erp-credit-note/erp-otd-report 三键只有
///    view 端点被豁免或 GET-only，故无写元组，不在本种子——覆盖 11 键。）
/// MenuId 经锚定表 docs/seeds/erp-key-menu-anchor.md 映射；RoleAction 挂 admin RoleId=1。
/// </summary>
public class ErpPermissionSeedTests
{
    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    // 期望的 30 去重种子元组（MenuId 来自锚定表；action 来自 T3a 控制器贴点，逐字核对）。
    private static readonly (int MenuId, string Code)[] ExpectedTuples =
    {
        (202, "add"), (202, "edit"), (202, "del"),                                   // erp-estimate-calc
        (204, "add"), (204, "edit"), (204, "del"), (204, "confirm"), (204, "issue"), // erp-quotation
        (206, "add"), (206, "edit"), (206, "del"),                                   // erp-product
        (208, "add"), (208, "edit"), (208, "del"), (208, "cancel"),                  // erp-order
        (209, "correct"),                                                            // erp-order-price-correction
        (210, "issue"),                                                              // erp-fsc-checklist
        (212, "add"), (212, "edit"), (212, "del"),                                   // erp-business-partner
        (213, "import"), (213, "edit"),                                              // erp-sheet-unit-price
        (215, "add"), (215, "edit"), (215, "del"),                                   // erp-plate-mold
        (218, "close"), (218, "split"),                                              // erp-backorder
        (220, "add"), (220, "edit"), (220, "del"),                                   // erp-fx-rate
    };

    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static void SeedTenantsAndMenus(CP6Context db)
    {
        // 锚定菜单行须先在（RoleAction 挂 MenuId）。
        ErpMenuSeed.EnsureSeeded(db);
        db.Sys_Tenants.Add(new Sys_Tenant { Id = TenantA, TenantCode = "TA", TenantName = "TenantA", Enable = true });
        db.Sys_Tenants.Add(new Sys_Tenant { Id = TenantB, TenantCode = "TB", TenantName = "TenantB", Enable = true });
        db.SaveChanges();
    }

    [Fact]
    public void EnsureSeeded_SeedsExactly30TuplesPerTenant_ForBothMenuAndRoleAction()
    {
        using var db = NewDb();
        SeedTenantsAndMenus(db);

        ErpPermissionSeed.EnsureSeeded(db);

        foreach (var tid in new[] { TenantA, TenantB })
        {
            var menuActions = db.Sys_MenuActions.IgnoreQueryFilters()
                .Where(x => x.TenantId == tid).ToList();
            var roleActions = db.Sys_RoleActions.IgnoreQueryFilters()
                .Where(x => x.TenantId == tid && x.RoleId == 1).ToList();

            // 元组闭环计数：每租户各得全套 30。
            Assert.Equal(30, menuActions.Count);
            Assert.Equal(30, roleActions.Count);

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

        ErpPermissionSeed.EnsureSeeded(db);
        var ma1 = db.Sys_MenuActions.IgnoreQueryFilters().Count();
        var ra1 = db.Sys_RoleActions.IgnoreQueryFilters().Count();

        ErpPermissionSeed.EnsureSeeded(db);   // 二次调用
        Assert.Equal(ma1, db.Sys_MenuActions.IgnoreQueryFilters().Count());
        Assert.Equal(ra1, db.Sys_RoleActions.IgnoreQueryFilters().Count());

        // 2 租户 × 30 元组 = 60。
        Assert.Equal(60, ma1);
        Assert.Equal(60, ra1);
    }

    [Fact]
    public void EnsureSeeded_RoleActionsAllAttachRoleId1_AndAnchoredMenuIds()
    {
        using var db = NewDb();
        SeedTenantsAndMenus(db);

        ErpPermissionSeed.EnsureSeeded(db);

        var anchoredMenuIds = ExpectedTuples.Select(t => t.MenuId).ToHashSet();
        var roleActions = db.Sys_RoleActions.IgnoreQueryFilters().ToList();

        Assert.NotEmpty(roleActions);
        Assert.All(roleActions, ra => Assert.Equal(1, ra.RoleId));
        // 每个 RoleAction.MenuId 必来自锚定表，且该菜单行确实存在且 MenuKey 非 null。
        Assert.All(roleActions, ra => Assert.Contains(ra.MenuId, anchoredMenuIds));
        foreach (var menuId in anchoredMenuIds)
        {
            var menu = db.Sys_Menus.Single(m => m.MenuId == menuId);
            Assert.NotNull(menu.MenuKey);
            Assert.StartsWith("erp-", menu.MenuKey!);
        }
    }

    [Fact]
    public void EnsureSeeded_ExplicitTenantId_NotOverwrittenByStampTenant()
    {
        using var db = NewDb();
        SeedTenantsAndMenus(db);

        ErpPermissionSeed.EnsureSeeded(db);

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
        ErpMenuSeed.EnsureSeeded(db);   // 菜单在，但无 Sys_Tenants 行

        ErpPermissionSeed.EnsureSeeded(db);

        Assert.Equal(0, db.Sys_MenuActions.IgnoreQueryFilters().Count());
        Assert.Equal(0, db.Sys_RoleActions.IgnoreQueryFilters().Count());
    }
}
