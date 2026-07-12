using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

/// <summary>
/// M-MES 横切接线 Task 3b：MesPermissionSeed 逐租户 Sys_MenuAction/Sys_RoleAction 幂等种子断言。
///
/// 三数闭环（真相源 docs/seeds/mes-permission-keys.md §一/§七 + 控制器 grep）：
///   28 写端点（[RequirePermission] 贴点）→ 去重 (menu-key, action) 25 元组 → 25 种子元组（漏种 0 / 多种 0）。
///   （3 处归并消解重复：mes-work-order:add 覆 Create+ExpandFromOrder / mes-production-result:suspend
///    覆 Suspend+Resume / mes-machine:downtime 覆 Register+Close。2 只读 POST 豁免
///    mes-plan-achievement:view 未贴点→不入种子，故覆盖 9 有写端点 menu-key，非 10。）
/// MenuId 经锚定表 docs/seeds/mes-key-menu-anchor.md 映射；RoleAction 挂 admin RoleId=1。
///
/// ★ ExpectedTuples 为独立硬编码 oracle（非引用 MesPermissionSeed 内部常量），防自证假绿。
/// </summary>
public class MesPermissionSeedTests
{
    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    // 期望的 25 去重种子元组（MenuId 来自锚定表 mes-key-menu-anchor.md；action 来自 T3a 控制器贴点，逐字核对）。
    private static readonly (int MenuId, string Code)[] ExpectedTuples =
    {
        (301, "reschedule"), (301, "arrange"),                                        // mes-planning-board
        (302, "add"), (302, "edit"), (302, "del"), (302, "issue"),                    // mes-work-order
        (304, "start"), (304, "suspend"), (304, "complete"), (304, "report"),         // mes-production-result
        (306, "add"), (306, "edit"),                                                  // mes-quality-inspection
        (308, "add"), (308, "edit"), (308, "del"),                                    // mes-defect
        (310, "add"), (310, "edit"), (310, "del"), (310, "status"), (310, "downtime"),// mes-machine
        (311, "recalculate"),                                                         // mes-oee
        (314, "edit"), (314, "del"),                                                  // mes-work-center
        (315, "edit"), (315, "del"),                                                  // mes-process-cost-rate
    };

    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static void SeedTenantsAndMenus(CP6Context db)
    {
        // 锚定菜单行须先在（RoleAction 挂 MenuId；MesMenuSeed 缺行补建并显式赋 mes-* MenuKey）。
        MesMenuSeed.EnsureSeeded(db);
        db.Sys_Tenants.Add(new Sys_Tenant { Id = TenantA, TenantCode = "TA", TenantName = "TenantA", Enable = true });
        db.Sys_Tenants.Add(new Sys_Tenant { Id = TenantB, TenantCode = "TB", TenantName = "TenantB", Enable = true });
        db.SaveChanges();
    }

    [Fact]
    public void EnsureSeeded_SeedsExactly25TuplesPerTenant_ForBothMenuAndRoleAction()
    {
        using var db = NewDb();
        SeedTenantsAndMenus(db);

        MesPermissionSeed.EnsureSeeded(db);

        foreach (var tid in new[] { TenantA, TenantB })
        {
            var menuActions = db.Sys_MenuActions.IgnoreQueryFilters()
                .Where(x => x.TenantId == tid).ToList();
            var roleActions = db.Sys_RoleActions.IgnoreQueryFilters()
                .Where(x => x.TenantId == tid && x.RoleId == 1).ToList();

            // 元组闭环计数：每租户各得全套 25。
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

        MesPermissionSeed.EnsureSeeded(db);
        var ma1 = db.Sys_MenuActions.IgnoreQueryFilters().Count();
        var ra1 = db.Sys_RoleActions.IgnoreQueryFilters().Count();

        MesPermissionSeed.EnsureSeeded(db);   // 二次调用
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

        MesPermissionSeed.EnsureSeeded(db);

        var anchoredMenuIds = ExpectedTuples.Select(t => t.MenuId).ToHashSet();
        var roleActions = db.Sys_RoleActions.IgnoreQueryFilters().ToList();

        Assert.NotEmpty(roleActions);
        Assert.All(roleActions, ra => Assert.Equal(1, ra.RoleId));
        // 每个 RoleAction.MenuId 必来自锚定表，且该菜单行确实存在且 MenuKey 非 null 且 mes- 前缀。
        Assert.All(roleActions, ra => Assert.Contains(ra.MenuId, anchoredMenuIds));
        foreach (var menuId in anchoredMenuIds)
        {
            var menu = db.Sys_Menus.Single(m => m.MenuId == menuId);
            Assert.NotNull(menu.MenuKey);
            Assert.StartsWith("mes-", menu.MenuKey!);
        }
    }

    [Fact]
    public void EnsureSeeded_ExplicitTenantId_NotOverwrittenByStampTenant()
    {
        using var db = NewDb();
        SeedTenantsAndMenus(db);

        MesPermissionSeed.EnsureSeeded(db);

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
        MesMenuSeed.EnsureSeeded(db);   // 菜单在，但无 Sys_Tenants 行

        MesPermissionSeed.EnsureSeeded(db);

        Assert.Equal(0, db.Sys_MenuActions.IgnoreQueryFilters().Count());
        Assert.Equal(0, db.Sys_RoleActions.IgnoreQueryFilters().Count());
    }
}
