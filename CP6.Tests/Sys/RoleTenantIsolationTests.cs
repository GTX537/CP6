using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Sys;

/// <summary>
/// P0-T3 Sys_Role 租户化隔离测试。Sys_Role 由全局实体改为复合主键 (TenantId, RoleId) 的租户级实体：
/// 全局查询过滤（只见本租户角色）+ StampTenant（写入自动盖当前租户）+ 各租户可同号 RoleId 独立并存。
/// 另含存量归户回填的一致性不变式单测（迁移 SQL 在 SQL Server 执行，InMemory 不能跑 raw SQL，
/// 故以模型级预言复现 SQL 强制的不变式：任一租户所引用的 RoleId 必在该租户拥有对应角色行）。
/// </summary>
public class RoleTenantIsolationTests
{
    private static readonly Guid TenantA = TenantContext.DefaultTenant;                       // A1 默认租户
    private static readonly Guid TenantB = Guid.Parse("00000000-0000-0000-0000-0000000000B2");

    /// <summary>同一 InMemory 库、不同租户视图的上下文（隔离测试须共享库）。</summary>
    private static CP6Context CtxFor(string dbName, Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new CP6Context(options, new TenantContext { CurrentTenantId = tenantId });
    }

    // ① A 租户上下文查角色只见 A 的（全局过滤生效）——各租户皆有 RoleId=1，验证复合主键 + 过滤。
    [Fact]
    public void TenantContext_sees_only_own_roles()
    {
        var db = Guid.NewGuid().ToString();
        using (var a = CtxFor(db, TenantA))
        {
            a.Sys_Roles.Add(new Sys_Role { TenantId = TenantA, RoleId = 1, RoleName = "A-管理员" });
            a.SaveChanges();
        }
        using (var b = CtxFor(db, TenantB))
        {
            b.Sys_Roles.Add(new Sys_Role { TenantId = TenantB, RoleId = 1, RoleName = "B-管理员" });
            b.SaveChanges();
        }

        using (var a = CtxFor(db, TenantA))
        {
            var visible = a.Sys_Roles.ToList();
            Assert.Single(visible);
            Assert.Equal("A-管理员", visible[0].RoleName);
            Assert.All(visible, r => Assert.Equal(TenantA, r.TenantId));
        }
        using (var b = CtxFor(db, TenantB))
        {
            var visible = b.Sys_Roles.ToList();
            Assert.Single(visible);
            Assert.Equal("B-管理员", visible[0].RoleName);
        }
    }

    // ② A 改角色名不影响 B（回填后两行独立）。
    [Fact]
    public void Renaming_role_in_A_does_not_affect_B()
    {
        var db = Guid.NewGuid().ToString();
        using (var a = CtxFor(db, TenantA))
        { a.Sys_Roles.Add(new Sys_Role { TenantId = TenantA, RoleId = 1, RoleName = "原名" }); a.SaveChanges(); }
        using (var b = CtxFor(db, TenantB))
        { b.Sys_Roles.Add(new Sys_Role { TenantId = TenantB, RoleId = 1, RoleName = "原名" }); b.SaveChanges(); }

        using (var a = CtxFor(db, TenantA))
        {
            var r = a.Sys_Roles.Single(x => x.RoleId == 1);
            r.RoleName = "A-改名";
            a.SaveChanges();
        }

        using (var b = CtxFor(db, TenantB))
            Assert.Equal("原名", b.Sys_Roles.Single(x => x.RoleId == 1).RoleName);
        using (var a = CtxFor(db, TenantA))
            Assert.Equal("A-改名", a.Sys_Roles.Single(x => x.RoleId == 1).RoleName);
    }

    // ③ 新建角色未显式设 TenantId → StampTenant 自动盖当前租户。
    [Fact]
    public void New_role_auto_stamps_current_tenant()
    {
        var db = Guid.NewGuid().ToString();
        using (var b = CtxFor(db, TenantB))
        {
            b.Sys_Roles.Add(new Sys_Role { RoleId = 9, RoleName = "无租户新建" });   // 不设 TenantId
            b.SaveChanges();
        }
        // 用无过滤视图确认落库 TenantId = B。
        using (var raw = CtxFor(db, TenantB))
        {
            var stored = raw.Sys_Roles.IgnoreQueryFilters().Single();
            Assert.Equal(TenantB, stored.TenantId);
        }
        // A 看不见 B 盖章的角色。
        using (var a = CtxFor(db, TenantA))
            Assert.Empty(a.Sys_Roles.ToList());
    }

    // ⑤（评审 Important 补口）A 编辑角色菜单映射（整体替换语义），B 同号角色的菜单不受影响。
    [Fact]
    public void Editing_role_menus_in_A_does_not_affect_B()
    {
        var db = Guid.NewGuid().ToString();
        using (var a = CtxFor(db, TenantA))
        {
            a.Sys_RoleMenus.Add(new Sys_RoleMenu { TenantId = TenantA, RoleId = 1, MenuId = 100 });
            a.Sys_RoleMenus.Add(new Sys_RoleMenu { TenantId = TenantA, RoleId = 1, MenuId = 101 });
            a.SaveChanges();
        }
        using (var b = CtxFor(db, TenantB))
        {
            b.Sys_RoleMenus.Add(new Sys_RoleMenu { TenantId = TenantB, RoleId = 1, MenuId = 100 });
            b.Sys_RoleMenus.Add(new Sys_RoleMenu { TenantId = TenantB, RoleId = 1, MenuId = 101 });
            b.SaveChanges();
        }

        // A 整体替换 RoleId=1 的菜单集（复刻 RoleController.SaveRoleMenus：删旧+插新，靠全局过滤圈定范围）。
        using (var a = CtxFor(db, TenantA))
        {
            var old = a.Sys_RoleMenus.Where(rm => rm.RoleId == 1).ToList();
            a.Sys_RoleMenus.RemoveRange(old);
            a.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 200 });   // 不设 TenantId → 盖章
            a.SaveChanges();
        }

        using (var b = CtxFor(db, TenantB))
        {
            var bMenus = b.Sys_RoleMenus.Where(rm => rm.RoleId == 1).Select(rm => rm.MenuId).OrderBy(x => x).ToList();
            Assert.Equal(new[] { 100, 101 }, bMenus);   // B 原集完好
        }
        using (var a = CtxFor(db, TenantA))
        {
            var aMenus = a.Sys_RoleMenus.Where(rm => rm.RoleId == 1).Select(rm => rm.MenuId).ToList();
            Assert.Equal(new[] { 200 }, aMenus);        // A 替换生效
        }
    }

    // ⑥（评审 Important 补口）新建 Sys_RoleMenu 未显式设 TenantId → StampTenant 自动盖当前租户。
    [Fact]
    public void New_role_menu_auto_stamps_current_tenant()
    {
        var db = Guid.NewGuid().ToString();
        using (var b = CtxFor(db, TenantB))
        {
            b.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 100 });   // 不设 TenantId
            b.SaveChanges();
        }
        using (var raw = CtxFor(db, TenantB))
            Assert.Equal(TenantB, raw.Sys_RoleMenus.IgnoreQueryFilters().Single().TenantId);
        using (var a = CtxFor(db, TenantA))
            Assert.Empty(a.Sys_RoleMenus.ToList());
    }

    // ④ 回填校验逻辑单测（映射完整性）：复现迁移 SQL 强制的不变式——
    //    任一租户被子表引用的 RoleId，必在该租户拥有对应 Sys_Role 行；否则回填不完整（迁移应 THROW）。
    [Fact]
    public void Backfill_invariant_holds_when_roles_copied_to_every_tenant()
    {
        // 存量：A 拥有角色 1、2；两租户各有引用 RoleId=1/2 的子表行（子表本已带 TenantId）。
        var roles = new List<(Guid TenantId, int RoleId)>
        {
            (TenantA, 1), (TenantA, 2),
            (TenantB, 1), (TenantB, 2),   // 回填复制：B 得到同号副本
        };
        var childRefs = new List<(Guid TenantId, int RoleId)>
        {
            (TenantA, 1), (TenantA, 2),
            (TenantB, 1), (TenantB, 2),
        };

        var missing = BackfillInvariant.FindUnmatchedRefs(roles, childRefs);
        Assert.Empty(missing);   // 完整复制 → 无缺失
    }

    // ④(反例) 若某租户漏复制角色副本，则其子表引用悬空 → 校验必须报出（迁移会 THROW 回滚）。
    [Fact]
    public void Backfill_invariant_detects_missing_tenant_role_copy()
    {
        var roles = new List<(Guid TenantId, int RoleId)>
        {
            (TenantA, 1), (TenantA, 2),
            (TenantB, 1),                 // 缺 (TenantB, 2) —— 回填不完整
        };
        var childRefs = new List<(Guid TenantId, int RoleId)>
        {
            (TenantB, 2),                 // B 的子表引用 RoleId=2，但 B 无该角色副本
        };

        var missing = BackfillInvariant.FindUnmatchedRefs(roles, childRefs);
        Assert.Contains((TenantB, 2), missing);
    }
}

/// <summary>
/// 回填不变式的纯函数实现（迁移 SQL THROW 段的可测镜像）：
/// 返回"子表引用了某 (TenantId, RoleId)，但 Sys_Role 中该租户没有此角色"的悬空引用集合。
/// SQL 侧对应：任一 UserRole/RoleAction/RoleDataScope/RoleFieldPerm/User 行的 (TenantId, RoleId)
/// 若 RoleId 是已知角色却在本租户缺副本 → THROW。空集合 = 回填完整。
/// </summary>
internal static class BackfillInvariant
{
    public static IReadOnlyList<(Guid TenantId, int RoleId)> FindUnmatchedRefs(
        IEnumerable<(Guid TenantId, int RoleId)> roles,
        IEnumerable<(Guid TenantId, int RoleId)> childRefs)
    {
        var owned = new HashSet<(Guid, int)>(roles);
        var knownRoleIds = new HashSet<int>(roles.Select(r => r.RoleId));   // 全局已知角色号
        return childRefs
            .Where(c => knownRoleIds.Contains(c.RoleId))                    // 仅校验指向已知角色的引用（忽略预存孤儿）
            .Where(c => !owned.Contains((c.TenantId, c.RoleId)))           // 本租户缺该角色副本
            .Distinct()
            .ToList();
    }
}
