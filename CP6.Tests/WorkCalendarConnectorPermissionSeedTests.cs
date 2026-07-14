using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

/// <summary>
/// WFS 波⑤ F-T1：WorkCalendarConnectorPermissionSeed 幂等断言。
///   ① 年历新菜单 743（oa-work-calendar，共享表，MenuKey 插入时显式赋值）+ 授管理员 RoleMenu。
///   ② 逐租户 Sys_MenuAction/Sys_RoleAction：Calendar.View/Edit（743）+ Connector.View/Edit（734 oa-flow-admin）。
/// ★ ExpectedTuples 为独立硬编码 oracle（非引用 seed 内部常量），防自证假绿。
/// </summary>
public class WorkCalendarConnectorPermissionSeedTests
{
    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    private static readonly (int MenuId, string Code)[] ExpectedTuples =
    {
        (743, "Calendar.View"), (743, "Calendar.Edit"),
        (734, "Connector.View"), (734, "Connector.Edit"),
    };

    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static void SeedTenantsAndMenus(CP6Context db)
    {
        OawfMenuSeed.EnsureSeeded(db);   // 734 oa-flow-admin 锚定行须先在
        db.Sys_Tenants.Add(new Sys_Tenant { Id = TenantA, TenantCode = "TA", TenantName = "TenantA", Enable = true });
        db.Sys_Tenants.Add(new Sys_Tenant { Id = TenantB, TenantCode = "TB", TenantName = "TenantB", Enable = true });
        db.SaveChanges();
    }

    [Fact]
    public void EnsureSeeded_CreatesCalendarMenu743_WithExplicitMenuKey_AndAdminRoleMenu()
    {
        using var db = NewDb();
        SeedTenantsAndMenus(db);

        WorkCalendarConnectorPermissionSeed.EnsureSeeded(db);

        var menu = db.Sys_Menus.SingleOrDefault(m => m.MenuId == 743);
        Assert.NotNull(menu);
        Assert.Equal("oa-work-calendar", menu!.MenuKey);   // 插入时显式赋值——非靠回填
        Assert.Equal("/oa/work-calendar", menu.RoutePath);
        Assert.Equal(740, menu.ParentId);
        Assert.True(menu.Enable);
        Assert.True(db.Sys_RoleMenus.Any(rm => rm.RoleId == 1 && rm.MenuId == 743));
    }

    [Fact]
    public void EnsureSeeded_SeedsExactly4TuplesPerTenant_ForBothMenuAndRoleAction()
    {
        using var db = NewDb();
        SeedTenantsAndMenus(db);

        WorkCalendarConnectorPermissionSeed.EnsureSeeded(db);

        var expected = ExpectedTuples.ToHashSet();
        foreach (var tid in new[] { TenantA, TenantB })
        {
            var maSet = db.Sys_MenuActions.IgnoreQueryFilters()
                .Where(x => x.TenantId == tid).Select(x => new ValueTuple<int, string>(x.MenuId, x.ActionCode)).ToHashSet();
            var raSet = db.Sys_RoleActions.IgnoreQueryFilters()
                .Where(x => x.TenantId == tid && x.RoleId == 1).Select(x => new ValueTuple<int, string>(x.MenuId, x.ActionCode)).ToHashSet();
            Assert.Equal(expected, maSet);
            Assert.Equal(expected, raSet);
        }
    }

    [Fact]
    public void EnsureSeeded_IsIdempotent_SecondCallNoNewRows()
    {
        using var db = NewDb();
        SeedTenantsAndMenus(db);

        WorkCalendarConnectorPermissionSeed.EnsureSeeded(db);
        var menuCount1 = db.Sys_Menus.Count();
        var roleMenuCount1 = db.Sys_RoleMenus.Count();
        var ma1 = db.Sys_MenuActions.IgnoreQueryFilters().Count();
        var ra1 = db.Sys_RoleActions.IgnoreQueryFilters().Count();

        WorkCalendarConnectorPermissionSeed.EnsureSeeded(db);   // 二次
        Assert.Equal(menuCount1, db.Sys_Menus.Count());
        Assert.Equal(roleMenuCount1, db.Sys_RoleMenus.Count());
        Assert.Equal(ma1, db.Sys_MenuActions.IgnoreQueryFilters().Count());
        Assert.Equal(ra1, db.Sys_RoleActions.IgnoreQueryFilters().Count());

        // 2 租户 × 4 元组 = 8。
        Assert.Equal(8, ma1);
        Assert.Equal(8, ra1);
    }

    [Fact]
    public void EnsureSeeded_RoleActionsAllAttachRoleId1_ExplicitTenantId()
    {
        using var db = NewDb();
        SeedTenantsAndMenus(db);

        WorkCalendarConnectorPermissionSeed.EnsureSeeded(db);

        var roleActions = db.Sys_RoleActions.IgnoreQueryFilters().ToList();
        Assert.NotEmpty(roleActions);
        Assert.All(roleActions, ra => Assert.Equal(1, ra.RoleId));
        var tenants = roleActions.Select(x => x.TenantId).Distinct().ToList();
        Assert.Contains(TenantA, tenants);
        Assert.Contains(TenantB, tenants);
    }

    [Fact]
    public void EnsureSeeded_NoTenants_StillCreatesMenu_ButNoActions()
    {
        using var db = NewDb();
        OawfMenuSeed.EnsureSeeded(db);   // 无 Sys_Tenants 行

        WorkCalendarConnectorPermissionSeed.EnsureSeeded(db);

        Assert.NotNull(db.Sys_Menus.SingleOrDefault(m => m.MenuId == 743));   // 菜单仍建（共享表）
        Assert.Equal(0, db.Sys_MenuActions.IgnoreQueryFilters().Count());
        Assert.Equal(0, db.Sys_RoleActions.IgnoreQueryFilters().Count());
    }
}
