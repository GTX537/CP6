using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Seed;

/// <summary>
/// 计划中台(Plan)+公共(Pub)権限点（Sys_MenuAction + Sys_RoleAction）逐租户启动幂等种子（M-PLAN/PUB 横切接线 Task 2）。
///
/// 背景：Plan/Pub 5 控制器逐写端点贴 <c>[RequirePermission("键","action")]</c>，但
/// <c>PermissionService.HasActionAsync</c> 无 admin 旁路——不种 Sys_RoleAction 则 admin 也 403。
/// 运行时 <c>PermissionAggregator</c> 以
/// <c>Sys_RoleActions join Sys_Menus on MenuId where MenuKey!=null → "{MenuKey}:{ActionCode}"</c>
/// 聚合当前用户 ActionKeys，故须为管理员角色 <c>RoleId=1</c> 在**每个租户**各授一份，
/// 并登记对应 <c>Sys_MenuAction</c>（操作点目录，供 UI 授权配置枚举）。
///
/// 修复真相源 docs/seeds/planpub-permission-keys.md §六 次硬前置②「四菜单 731/732/112/113 零 RoleAction」：
/// Sys 族权限种子（Program.cs :1446-1457）只覆盖 MenuId 101–111，112/113 完全不在其中，Plan 731/732 亦无任何种子。
///
/// 数据来源（执行真相，与真相源 §一/§七 11 资源键 1:1）：下方 <see cref="Actions"/> 清单逐字照
/// docs/seeds/planpub-permission-keys.md §一/§二；MenuId 锚定 Program.cs 菜单 731/732/113/112。
/// 含只读豁免键 <c>pub-codegen:view</c>（§四.4a PreviewInline 归 view 贴点非旁路）。
/// **Attachment 三端点（upload/delete/rebind）不入种子**（组件豁免，§五.4，T3 反射测试显式豁免表）。
///
/// 逐租户机制（照 PurPermissionSeed 先例）：
///  - 枚举 <c>Sys_Tenants</c>（共享表，非行级过滤）全部租户 Id，对每租户各插一份。
///  - 显式设 <c>TenantId=tid</c> → <c>CP6Context.StampTenant</c> 仅盖 <c>TenantId==Guid.Empty</c>，不覆盖显式值。
///  - 幂等判存用 <c>IgnoreQueryFilters()</c>，使跨租户既存行对当前上下文（默认租户作用域）可见，避免误判缺失重复插。
///
/// 接入：Program.cs 于 Plan 菜单插入 + 731/732 MenuKey 显式赋值 **之后**调用（锚定菜单行须先在，RoleAction 挂 MenuId）。
/// 幂等：重启不重复插（(TenantId,MenuId,ActionCode) 判存守卫）。
/// </summary>
public static class PlanPubPermissionSeed
{
    /// <summary>
    /// (MenuId, ActionCode, ActionName) —— 与 docs/seeds/planpub-permission-keys.md §一/§七 11 资源键逐字 1:1。
    /// MenuId 锚定 Program.cs 菜单 731/732/113/112；ActionName 为中文显示名。
    /// 计 11 条，覆盖 4 个 menu-key（plan-mrp/plan-item-policy/pub-codegen/pub-seq，含 pub-codegen:view 只读豁免键）。
    /// Attachment 组件豁免 3 端点不入本表（§五.4）。
    /// </summary>
    private static readonly (int MenuId, string Code, string Name)[] Actions =
    {
        // 731 plan-mrp — MRP运算看板
        (731, "run", "运算"), (731, "confirm", "确认计划订单"),
        (731, "convert", "转单"), (731, "ignore", "忽略计划订单"),
        // 732 plan-item-policy — 计划主数据
        (732, "add", "新增/更新"), (732, "delete", "删除"),
        // 113 pub-codegen — 代码生成（含只读豁免 view）
        (113, "save", "保存"), (113, "view", "预览"),
        // 112 pub-seq — 采番规则
        (112, "add", "新增"), (112, "edit", "修改"), (112, "delete", "删除"),
    };

    /// <summary>
    /// 逐租户幂等播种 Plan/Pub 全部权限点 + 授管理员（RoleId=1）。
    /// 须在 Plan 菜单插入 + 731/732 MenuKey 显式赋值之后调用（锚定菜单行须先在）。
    /// </summary>
    public static void EnsureSeeded(CP6Context db)
    {
        // Sys_Tenant 为共享表（BaseEntity，非行级过滤）：Id 即 TenantId。
        var tenantIds = db.Sys_Tenants.Select(t => t.Id).ToList();
        if (tenantIds.Count == 0) return;

        var changed = false;
        foreach (var tid in tenantIds)
        {
            foreach (var (menuId, code, name) in Actions)
            {
                // IgnoreQueryFilters：跨租户可见，避免默认租户作用域误判其他租户既存行缺失而重复插。
                if (!db.Sys_MenuActions.IgnoreQueryFilters()
                        .Any(x => x.TenantId == tid && x.MenuId == menuId && x.ActionCode == code))
                {
                    db.Sys_MenuActions.Add(new Sys_MenuAction
                    {
                        TenantId = tid,           // 显式设 → StampTenant 不覆盖（仅盖 Guid.Empty）
                        MenuId = menuId,
                        ActionCode = code,
                        ActionName = name,
                        Sort = 0,
                    });
                    changed = true;
                }

                if (!db.Sys_RoleActions.IgnoreQueryFilters()
                        .Any(x => x.TenantId == tid && x.RoleId == 1 && x.MenuId == menuId && x.ActionCode == code))
                {
                    db.Sys_RoleActions.Add(new Sys_RoleAction
                    {
                        TenantId = tid,           // 显式设 → StampTenant 不覆盖
                        RoleId = 1,
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
