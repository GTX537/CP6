using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Seed;

/// <summary>
/// 标准角色种子（普通角色授权放开波 T1）：逐租户预置「一般用户」(RoleId=10) + OA 办理最小键集。
///
/// 背景：CP6 授权 P0 收官后各模块 fail-closed（PermissionService 无 admin 旁路，键全靠 Sys_RoleAction 数据驱动）。
/// 洁净部署下只有 admin(RoleId=1) 被各 *PermissionSeed 逐租户授权，普通员工无任何角色可用 → OA 办理全 403。
/// 本种子给每租户预置一个可开箱办公的「一般用户」角色，仅授 OA 电子表单办理最小闭环（收件箱办理 + 填单 + 委派）。
///
/// 幂等 insert-only：行存在即跳过，**绝不更新/删除**——admin 后续经 RolePermView 对本角色的手工增删授权
/// （加菜单/加操作点/去操作点）不被重启重置（用户手工裁剪须存活）。故本种子只补「首见缺失」的行，从不回收多余行。
///
/// 依赖（Program.cs 注册序保证）：菜单 740/733/735/737 与 OawfPermissionSeed 的 MenuActions 目录已播种
/// （置于 OawfPermissionSeed 及其三附加种子调用之后）。RoleAction 挂锚定 MenuId，菜单行须先在。
///
/// 蓄意不授（admin 手工按需放）：addsign（加签）/ oa-flow-admin / oa-designer / oa-approver-map /
/// oa-work-calendar —— 最小键集只覆盖普通员工日常办理，管理/设计/加签类留 admin 裁量。
///
/// 逐租户机制（照 PurPermissionSeed / OawfPermissionSeed 先例）：
///  - 枚举 <c>Sys_Tenants</c>（共享表，非行级过滤）全部租户 Id，对每租户各插一份。
///  - 显式设 <c>TenantId=tid</c> → <c>CP6Context.StampTenant</c> 仅盖 <c>TenantId==Guid.Empty</c>，不覆盖显式值。
///  - 幂等判存用 <c>IgnoreQueryFilters()</c>，使跨租户既存行对当前上下文（默认租户作用域）可见，避免误判缺失重复插。
/// </summary>
public static class StandardRoleSeed
{
    /// <summary>「一般用户」角色 Id（各租户内固定 10；租户内不可与既有角色号冲突，10 为本波约定号）。</summary>
    public const int GeneralRoleId = 10;

    private const string GeneralRoleName = "一般用户";

    /// <summary>授予「一般用户」的菜单（MenuId）：740 OA工作流父组 + 733 信箱 + 735 填單 + 737 设定。</summary>
    private static readonly int[] Menus = { 740, 733, 735, 737 };

    /// <summary>
    /// 授予「一般用户」的操作点最小键集（MenuId, ActionCode）——恰 8 条：
    ///  733 oa-inbox：read/approve/transfer/sendback/withdraw（办理闭环，**不含 addsign**）；
    ///  735 oa-form-catalog：submit/favorite（起单提交 + 收藏）；
    ///  737 oa-settings：delegate（委派）。
    /// </summary>
    private static readonly (int MenuId, string Code)[] Actions =
    {
        (733, "read"), (733, "approve"), (733, "transfer"), (733, "sendback"), (733, "withdraw"),
        (735, "submit"), (735, "favorite"),
        (737, "delegate"),
    };

    /// <summary>逐租户幂等 insert-only 播种「一般用户」(RoleId=10) + 4 菜单授权 + 8 操作点授权。</summary>
    public static void EnsureSeeded(CP6Context db)
    {
        // Sys_Tenant 为共享表（BaseEntity，非行级过滤）：Id 即 TenantId。
        var tenantIds = db.Sys_Tenants.Select(t => t.Id).ToList();
        if (tenantIds.Count == 0) return;

        var changed = false;
        foreach (var tid in tenantIds)
        {
            // ① 角色行（复合主键 (TenantId, RoleId)）。IgnoreQueryFilters：跨租户可见，避免默认租户作用域误判缺失重复插。
            if (!db.Sys_Roles.IgnoreQueryFilters().Any(r => r.TenantId == tid && r.RoleId == GeneralRoleId))
            {
                db.Sys_Roles.Add(new Sys_Role
                {
                    TenantId = tid,               // 显式设 → StampTenant 不覆盖（仅盖 Guid.Empty）
                    RoleId = GeneralRoleId,
                    RoleName = GeneralRoleName,
                });
                changed = true;
            }

            // ② 菜单授权（RoleId=10 × Menus）。
            foreach (var menuId in Menus)
            {
                if (!db.Sys_RoleMenus.IgnoreQueryFilters()
                        .Any(rm => rm.TenantId == tid && rm.RoleId == GeneralRoleId && rm.MenuId == menuId))
                {
                    db.Sys_RoleMenus.Add(new Sys_RoleMenu
                    {
                        TenantId = tid,           // 显式设 → StampTenant 不覆盖
                        RoleId = GeneralRoleId,
                        MenuId = menuId,
                    });
                    changed = true;
                }
            }

            // ③ 操作点授权（RoleId=10 × Actions 最小键集）。
            foreach (var (menuId, code) in Actions)
            {
                if (!db.Sys_RoleActions.IgnoreQueryFilters()
                        .Any(ra => ra.TenantId == tid && ra.RoleId == GeneralRoleId
                                   && ra.MenuId == menuId && ra.ActionCode == code))
                {
                    db.Sys_RoleActions.Add(new Sys_RoleAction
                    {
                        TenantId = tid,           // 显式设 → StampTenant 不覆盖
                        RoleId = GeneralRoleId,
                        MenuId = menuId,
                        ActionCode = code,
                    });
                    changed = true;
                }
            }
        }

        if (changed)
            db.SaveChanges();
    }
}
