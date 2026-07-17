using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

/// <summary>
/// 普通角色授权放开波 T1：StandardRoleSeed 逐租户「一般用户」(RoleId=10) 幂等种子断言。
///
/// 面向内容（决策已定，硬编码独立 oracle，非引用种子内部常量，防自证假绿）：
///   角色：每租户 (RoleId=10, RoleName="一般用户")。
///   菜单：RoleId=10 → {740,733,735,737}（集合相等，不多不少）。
///   操作点 8 键：(733 read/approve/transfer/sendback/withdraw) + (735 submit/favorite) + (737 delegate)
///     —— 蓄意不含 (733,"addsign")。
///   幂等 insert-only：连跑两遍三表零增；绝不触碰 admin(RoleId=1)。
///   端到端：真 PermissionAggregator 对挂 RoleId=10 的用户聚出恰 8 个 "menu-key:action"
///     （含 "oa-inbox:approve"，不含 "oa-inbox:addsign"）。
/// </summary>
public class StandardRoleSeedTests
{
    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // 独立硬编码 oracle（不引用 StandardRoleSeed 内部字段）。
    private static readonly int[] ExpectedMenus = { 740, 733, 735, 737 };

    private static readonly (int MenuId, string Code)[] ExpectedActions =
    {
        (733, "read"), (733, "approve"), (733, "transfer"), (733, "sendback"), (733, "withdraw"),
        (735, "submit"), (735, "favorite"),
        (737, "delegate"),
    };

    /// <summary>锚定菜单（733/735/737 带 oa-* MenuKey，740 父行 null）+ 两租户注册；照 OawfPermissionSeedTests。</summary>
    private static void SeedTenantsAndMenus(CP6Context db)
    {
        OawfMenuSeed.EnsureSeeded(db);   // 建 733–740 菜单行并显式赋 oa-* MenuKey（733/735/737 承载键）
        db.Sys_Tenants.Add(new Sys_Tenant { Id = TenantA, TenantCode = "TA", TenantName = "TenantA", Enable = true });
        db.Sys_Tenants.Add(new Sys_Tenant { Id = TenantB, TenantCode = "TB", TenantName = "TenantB", Enable = true });
        db.SaveChanges();
    }

    [Fact]
    public void Seed_CreatesRole10_PerTenant()
    {
        using var db = NewDb();
        SeedTenantsAndMenus(db);

        StandardRoleSeed.EnsureSeeded(db);

        foreach (var tid in new[] { TenantA, TenantB })
        {
            var role = db.Sys_Roles.IgnoreQueryFilters()
                .SingleOrDefault(r => r.TenantId == tid && r.RoleId == 10);
            Assert.NotNull(role);
            Assert.Equal("一般用户", role!.RoleName);
        }
    }

    [Fact]
    public void Seed_GrantsExactly4Menus()
    {
        using var db = NewDb();
        SeedTenantsAndMenus(db);

        StandardRoleSeed.EnsureSeeded(db);

        foreach (var tid in new[] { TenantA, TenantB })
        {
            var menuIds = db.Sys_RoleMenus.IgnoreQueryFilters()
                .Where(rm => rm.TenantId == tid && rm.RoleId == 10)
                .Select(rm => rm.MenuId).ToHashSet();
            Assert.Equal(ExpectedMenus.ToHashSet(), menuIds);
        }
    }

    [Fact]
    public void Seed_GrantsExactly8Actions()
    {
        using var db = NewDb();
        SeedTenantsAndMenus(db);

        StandardRoleSeed.EnsureSeeded(db);

        foreach (var tid in new[] { TenantA, TenantB })
        {
            var acts = db.Sys_RoleActions.IgnoreQueryFilters()
                .Where(ra => ra.TenantId == tid && ra.RoleId == 10)
                .Select(ra => new { ra.MenuId, ra.ActionCode }).ToList()
                .Select(x => (x.MenuId, x.ActionCode)).ToHashSet();
            Assert.Equal(ExpectedActions.ToHashSet(), acts);
            Assert.DoesNotContain((733, "addsign"), acts);   // 蓄意不授加签
        }
    }

    [Fact]
    public void Seed_IsIdempotent()
    {
        using var db = NewDb();
        SeedTenantsAndMenus(db);

        StandardRoleSeed.EnsureSeeded(db);
        var roles1 = db.Sys_Roles.IgnoreQueryFilters().Count(r => r.RoleId == 10);
        var menus1 = db.Sys_RoleMenus.IgnoreQueryFilters().Count(rm => rm.RoleId == 10);
        var acts1 = db.Sys_RoleActions.IgnoreQueryFilters().Count(ra => ra.RoleId == 10);

        StandardRoleSeed.EnsureSeeded(db);   // 二次调用
        Assert.Equal(roles1, db.Sys_Roles.IgnoreQueryFilters().Count(r => r.RoleId == 10));
        Assert.Equal(menus1, db.Sys_RoleMenus.IgnoreQueryFilters().Count(rm => rm.RoleId == 10));
        Assert.Equal(acts1, db.Sys_RoleActions.IgnoreQueryFilters().Count(ra => ra.RoleId == 10));

        // 2 租户 × (1 角色 / 4 菜单 / 8 操作点)。
        Assert.Equal(2, roles1);
        Assert.Equal(8, menus1);
        Assert.Equal(16, acts1);
    }

    [Fact]
    public void Seed_DoesNotTouchAdminRole()
    {
        using var db = NewDb();
        SeedTenantsAndMenus(db);
        // 预置 admin(RoleId=1) 三表存量：角色行 + OA 菜单/操作点授权（OawfMenuSeed 已授 RoleId=1 菜单；补角色行 + OA 操作点）。
        foreach (var tid in new[] { TenantA, TenantB })
            db.Sys_Roles.Add(new Sys_Role { TenantId = tid, RoleId = 1, RoleName = "管理员" });
        db.SaveChanges();
        OawfPermissionSeed.EnsureSeeded(db);   // 逐租户授 admin(RoleId=1) OA 操作点

        int AdminRoles() => db.Sys_Roles.IgnoreQueryFilters().Count(r => r.RoleId == 1);
        int AdminMenus() => db.Sys_RoleMenus.IgnoreQueryFilters().Count(rm => rm.RoleId == 1);
        int AdminActs() => db.Sys_RoleActions.IgnoreQueryFilters().Count(ra => ra.RoleId == 1);
        var before = (AdminRoles(), AdminMenus(), AdminActs());

        StandardRoleSeed.EnsureSeeded(db);

        Assert.Equal(before, (AdminRoles(), AdminMenus(), AdminActs()));   // admin 零 diff
    }

    [Fact]
    public void Seed_ActionsSubsetOfCatalog()
    {
        using var db = NewDb();
        SeedTenantsAndMenus(db);
        OawfPermissionSeed.EnsureSeeded(db);   // 播 Sys_MenuActions 操作点目录（含本 8 键的超集）
        StandardRoleSeed.EnsureSeeded(db);

        foreach (var tid in new[] { TenantA, TenantB })
        {
            var catalog = db.Sys_MenuActions.IgnoreQueryFilters()
                .Where(ma => ma.TenantId == tid)
                .Select(ma => new { ma.MenuId, ma.ActionCode }).ToList()
                .Select(x => (x.MenuId, x.ActionCode)).ToHashSet();
            foreach (var key in ExpectedActions)
                Assert.Contains(key, catalog);
        }
    }

    [Fact]
    public async Task Aggregator_UserWithRole10_GetsExactly8Keys()
    {
        using var db = NewDb();
        // 端到端须走查询过滤器（默认租户作用域）：菜单/租户/种子皆落 DefaultTenant，聚合器方可 join 见。
        OawfMenuSeed.EnsureSeeded(db);
        db.Sys_Tenants.Add(new Sys_Tenant
        {
            Id = TenantContext.DefaultTenant, TenantCode = "DEF", TenantName = "Default", Enable = true
        });
        db.SaveChanges();
        StandardRoleSeed.EnsureSeeded(db);

        var uid = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User { Id = uid, UserName = "emp", Password = "x", RoleId = StandardRoleSeed.GeneralRoleId });
        await db.SaveChangesAsync();

        var ctx = await new PermissionAggregator(db).BuildAsync(uid);

        var expectedKeys = new HashSet<string>
        {
            "oa-inbox:read", "oa-inbox:approve", "oa-inbox:transfer", "oa-inbox:sendback", "oa-inbox:withdraw",
            "oa-form-catalog:submit", "oa-form-catalog:favorite",
            "oa-settings:delegate",
        };
        Assert.Equal(expectedKeys, ctx.ActionKeys);
        Assert.Contains("oa-inbox:approve", ctx.ActionKeys);
        Assert.DoesNotContain("oa-inbox:addsign", ctx.ActionKeys);
        Assert.Equal(8, ctx.ActionKeys.Count);
    }
}
