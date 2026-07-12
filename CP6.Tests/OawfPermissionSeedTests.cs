using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

/// <summary>
/// M-OA/WF 横切接线 Task 3b：OawfPermissionSeed 逐租户 Sys_MenuAction/Sys_RoleAction 幂等种子断言。
///
/// 三数闭环（真相源 docs/seeds/oawf-permission-keys.md §一/§七 + 控制器 grep）：
///   31 写端点（[RequirePermission] 贴点：Oa 21 + Wf 10）→ 去重 (menu-key, action) 20 元组 → 20 种子元组（漏种 0 / 多种 0）。
///   多处跨控制器归并消解重复（真相源 §五）：
///     oa-inbox:read 覆 Inbox task/cc-read + Notification read/read-all（4→1）；
///     oa-inbox:approve 覆 Inbox batch + Flow act（2→1）；oa-inbox:sendback 覆 Inbox + AdvancedFlow（2→1）；
///     oa-form-catalog:submit 覆 Draft submit + Approval submit + Form data + Flow submit（4→1）；
///     oa-settings:delegate 覆 Delegate add/remove + AdvancedFlow delegate（3→1，T2 委派合一拍板1）；
///     oa-designer:edit 覆 Designer save + Flow def（2→1）。
///   2 只读 POST 豁免（Forecast preview→oa-form-catalog:view / Query search→oa-form-search:view）未贴点＝不入种子。
///   → 覆盖 6 有写端点 menu-key（733/734/735/737/738/739）；oa-form-search(736) 仅 view 豁免故不种，非 7。
/// MenuId 经锚定表 docs/seeds/oawf-key-menu-anchor.md 映射；RoleAction 挂 admin RoleId=1。
///
/// ★ ExpectedTuples 为独立硬编码 oracle（非引用 OawfPermissionSeed 内部常量），防自证假绿。
/// </summary>
public class OawfPermissionSeedTests
{
    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    // 期望的 20 去重种子元组（MenuId 来自锚定表 oawf-key-menu-anchor.md；action 来自 T3a 控制器贴点，逐字核对）。
    private static readonly (int MenuId, string Code)[] ExpectedTuples =
    {
        // 733 oa-inbox（信箱 + /wf 引擎审批动作）
        (733, "read"), (733, "approve"), (733, "transfer"), (733, "sendback"), (733, "addsign"), (733, "withdraw"),
        // 734 oa-flow-admin
        (734, "enable"),
        // 735 oa-form-catalog（填單：收藏 + 草稿 CRUD + 起流程/提交）
        (735, "add"), (735, "edit"), (735, "submit"), (735, "del"), (735, "favorite"),
        // 737 oa-settings（设定：偏好 + 委派合一）
        (737, "edit"), (737, "delegate"),
        // 738 oa-designer（设计器：新栈 save/clone + 旧栈 flow/form def）
        (738, "edit"), (738, "add"), (738, "form-save"),
        // 739 oa-approver-map
        (739, "add"), (739, "edit"), (739, "del"),
    };

    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static void SeedTenantsAndMenus(CP6Context db)
    {
        // 锚定菜单行须先在（RoleAction 挂 MenuId；OawfMenuSeed 缺行补建并显式赋 oa-* MenuKey）。
        OawfMenuSeed.EnsureSeeded(db);
        db.Sys_Tenants.Add(new Sys_Tenant { Id = TenantA, TenantCode = "TA", TenantName = "TenantA", Enable = true });
        db.Sys_Tenants.Add(new Sys_Tenant { Id = TenantB, TenantCode = "TB", TenantName = "TenantB", Enable = true });
        db.SaveChanges();
    }

    [Fact]
    public void EnsureSeeded_SeedsExactly20TuplesPerTenant_ForBothMenuAndRoleAction()
    {
        using var db = NewDb();
        SeedTenantsAndMenus(db);

        OawfPermissionSeed.EnsureSeeded(db);

        foreach (var tid in new[] { TenantA, TenantB })
        {
            var menuActions = db.Sys_MenuActions.IgnoreQueryFilters()
                .Where(x => x.TenantId == tid).ToList();
            var roleActions = db.Sys_RoleActions.IgnoreQueryFilters()
                .Where(x => x.TenantId == tid && x.RoleId == 1).ToList();

            // 元组闭环计数：每租户各得全套 20。
            Assert.Equal(20, menuActions.Count);
            Assert.Equal(20, roleActions.Count);

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

        OawfPermissionSeed.EnsureSeeded(db);
        var ma1 = db.Sys_MenuActions.IgnoreQueryFilters().Count();
        var ra1 = db.Sys_RoleActions.IgnoreQueryFilters().Count();

        OawfPermissionSeed.EnsureSeeded(db);   // 二次调用
        Assert.Equal(ma1, db.Sys_MenuActions.IgnoreQueryFilters().Count());
        Assert.Equal(ra1, db.Sys_RoleActions.IgnoreQueryFilters().Count());

        // 2 租户 × 20 元组 = 40。
        Assert.Equal(40, ma1);
        Assert.Equal(40, ra1);
    }

    [Fact]
    public void EnsureSeeded_RoleActionsAllAttachRoleId1_AndAnchoredMenuIds()
    {
        using var db = NewDb();
        SeedTenantsAndMenus(db);

        OawfPermissionSeed.EnsureSeeded(db);

        var anchoredMenuIds = ExpectedTuples.Select(t => t.MenuId).ToHashSet();
        var roleActions = db.Sys_RoleActions.IgnoreQueryFilters().ToList();

        Assert.NotEmpty(roleActions);
        Assert.All(roleActions, ra => Assert.Equal(1, ra.RoleId));
        // 每个 RoleAction.MenuId 必来自锚定表，且该菜单行确实存在且 MenuKey 非 null 且 oa- 前缀。
        Assert.All(roleActions, ra => Assert.Contains(ra.MenuId, anchoredMenuIds));
        foreach (var menuId in anchoredMenuIds)
        {
            var menu = db.Sys_Menus.Single(m => m.MenuId == menuId);
            Assert.NotNull(menu.MenuKey);
            Assert.StartsWith("oa-", menu.MenuKey!);
        }
    }

    [Fact]
    public void EnsureSeeded_ExplicitTenantId_NotOverwrittenByStampTenant()
    {
        using var db = NewDb();
        SeedTenantsAndMenus(db);

        OawfPermissionSeed.EnsureSeeded(db);

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
        OawfMenuSeed.EnsureSeeded(db);   // 菜单在，但无 Sys_Tenants 行

        OawfPermissionSeed.EnsureSeeded(db);

        Assert.Equal(0, db.Sys_MenuActions.IgnoreQueryFilters().Count());
        Assert.Equal(0, db.Sys_RoleActions.IgnoreQueryFilters().Count());
    }
}
