using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Seed;

/// <summary>
/// 流程触发器权限点（Sys_MenuAction + Sys_RoleAction）逐租户启动幂等种子（WFS 波③ 事件触发 F-T2）。
///
/// 背景：波③ FlowTriggerAdminController（流程管理页「触发器」tab 后端，菜单 734 oa-flow-admin）为其
/// 变更端点贴了 <c>[RequirePermission("oa-flow-admin", "FlowTrigger.View" | "FlowTrigger.Edit")]</c>：
///  - <b>FlowTrigger.Edit</b>：Create / Update / Enable / ResetKey / ManualFire（增改·启停·试发·重置 key）。
///  - <b>FlowTrigger.View</b>：CronPreview（cron 预览 POST，只算不写；GET list/get/fires 循 oa-flow-admin
///    菜单既有约定[FlowAdminController]仅 [Authorize] 不贴细粒度键）。
/// <c>PermissionService.HasActionAsync</c> 无 admin 旁路——不种 Sys_RoleAction 则 admin 亦 403。
///
/// 「贴点⊆种子」互锁：控制器实际用到的 action 集 {FlowTrigger.View, FlowTrigger.Edit} 恰为本种子登记集，
/// 无孤儿键、无漏种。菜单 734 <c>MenuKey="oa-flow-admin"</c> 回填已由 <see cref="OawfMenuSeed"/> 落地
/// （M-OA/WF 波），本种子仅挂 Sys_MenuAction/Sys_RoleAction，不重做 MenuKey 回填。
///
/// 逐租户机制（照 <see cref="OawfPermissionSeed"/> / MesPermissionSeed 先例）：
///  - 枚举 <c>Sys_Tenants</c>（共享表）全部租户 Id，对每租户各插一份，显式 <c>TenantId=tid</c>
///    → <c>CP6Context.StampTenant</c> 仅盖 Guid.Empty 不覆盖显式值。
///  - 幂等判存用 <c>IgnoreQueryFilters()</c>（跨租户可见），避免默认租户作用域误判缺失重复插。
///
/// 接入：Program.cs 于 <see cref="OawfPermissionSeed.EnsureSeeded"/> **之后**调用（锚定菜单 734 须先在）。
/// </summary>
public static class FlowTriggerPermissionSeed
{
    /// <summary>(MenuId, ActionCode, ActionName) —— 与 FlowTriggerAdminController [RequirePermission] 第二实参逐字一致。</summary>
    private static readonly (int MenuId, string Code, string Name)[] Actions =
    {
        (734, "FlowTrigger.View", "触发器查看"),
        (734, "FlowTrigger.Edit", "触发器编辑"),
    };

    /// <summary>逐租户幂等播种 FlowTrigger.View/Edit 权限点 + 授管理员（RoleId=1）。须在 OawfPermissionSeed 之后调用。</summary>
    public static void EnsureSeeded(CP6Context db)
    {
        var tenantIds = db.Sys_Tenants.Select(t => t.Id).ToList();
        if (tenantIds.Count == 0) return;

        var changed = false;
        foreach (var tid in tenantIds)
        {
            foreach (var (menuId, code, name) in Actions)
            {
                if (!db.Sys_MenuActions.IgnoreQueryFilters()
                        .Any(x => x.TenantId == tid && x.MenuId == menuId && x.ActionCode == code))
                {
                    db.Sys_MenuActions.Add(new Sys_MenuAction
                    {
                        TenantId = tid,
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
                        TenantId = tid,
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
