using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Seed;

/// <summary>
/// WFS 三期波⑤ 引擎基建六件套：年历管理页 + 连接器管理 tab 权限点逐租户启动幂等种子（F-T1 汇总落库）。
///
/// 波内既定分工：A-T4/D-T2 只在控制器贴 <c>[RequirePermission]</c>，全部菜单/权限/i18n seed 延交 F-T1。本种子落：
///  ①「工作日历」新菜单 <b>Sys_Menu 743</b>（<c>MenuKey="oa-work-calendar"</c>，路由 <c>/oa/work-calendar</c>，父组 740 OA工作流）
///    + 授管理员 <c>Sys_RoleMenu</c>。<b>MenuKey 插入时显式赋值</b>（全仓命门：Program.cs :1005「无 MenuKey RoutePath 自动
///    回填」块只填 null 行——显式赋值即免疫时序，且 743 未被任何既有段占用[全库 grep 核实，OawfMenuSeed 止于 742]）。
///  ② 逐租户 <c>Sys_MenuAction</c>/<c>Sys_RoleAction</c>（RoleId=1 管理员）：
///     - 年历 743：<c>Calendar.View</c>（GET 列一年，只读，供 UI 可见性）/ <c>Calendar.Edit</c>（反转·清除·导入，
///       WorkCalendarController 3 写端点已贴此键）。
///     - 连接器（挂既有菜单 <b>734 oa-flow-admin</b>，连接器 tab 在流程管理页；<b>非 733</b>——733=oa-inbox）：
///       <c>Connector.View</c>（只读 GET 循守卫约定不贴键，仅登记词表/菜单 View 位）/ <c>Connector.Edit</c>
///       （WfConnectorController create/update/enabled 3 端点已贴此键）。
///
/// <c>PermissionService.HasActionAsync</c> 无 admin 旁路——不种 Sys_RoleAction 则 admin 亦 403。
/// 「贴点⊆种子」互锁：控制器实际用到的写 action {Calendar.Edit, Connector.Edit} ⊆ 本种子登记集，View 补齐词表位。
///
/// 逐租户机制（照 <see cref="FlowTriggerPermissionSeed"/>/<see cref="OawfPermissionSeed"/> 先例）：
///  - MenuAction/RoleAction 枚举 <c>Sys_Tenants</c> 全部租户 Id 各插一份，显式 <c>TenantId=tid</c>
///    → <c>CP6Context.StampTenant</c> 仅盖 Guid.Empty 不覆盖显式值。幂等判存用 <c>IgnoreQueryFilters()</c>。
///  - Sys_Menu / Sys_RoleMenu 非 BaseTenantEntity（IAuditable/共享），照 <see cref="OawfMenuSeed"/> 只播一次。
///
/// 接入：Program.cs 于 <see cref="OawfPermissionSeed.EnsureSeeded"/> **之后**、且早于 :1005 回填块调用
/// （锚定菜单 734 须先在——OawfMenuSeed :934 已播；743 由本种子就地插入并显式赋键）。
/// </summary>
public static class WorkCalendarConnectorPermissionSeed
{
    /// <summary>新增菜单：工作日历（743）。(MenuId, MenuName, RoutePath, Icon, ParentId, OrderNo, MenuKey)。</summary>
    private const int CalendarMenuId = 743;
    private const int FlowAdminMenuId = 734; // 连接器 tab 挂 oa-flow-admin

    /// <summary>(MenuId, ActionCode, ActionName) —— 与控制器 [RequirePermission] 第二实参逐字一致（View 补词表位）。</summary>
    private static readonly (int MenuId, string Code, string Name)[] Actions =
    {
        (CalendarMenuId, "Calendar.View", "工作日历查看"),
        (CalendarMenuId, "Calendar.Edit", "工作日历编辑"),
        (FlowAdminMenuId, "Connector.View", "连接器查看"),
        (FlowAdminMenuId, "Connector.Edit", "连接器编辑"),
    };

    /// <summary>逐租户幂等播种年历菜单 + Calendar/Connector 权限点 + 授管理员（RoleId=1）。须在 OawfPermissionSeed 之后调用。</summary>
    public static void EnsureSeeded(CP6Context db)
    {
        var changed = false;

        // ① 年历菜单 743（共享表，只播一次；MenuKey 插入时显式赋值——全仓时序命门）。
        if (!db.Sys_Menus.Any(m => m.MenuId == CalendarMenuId))
        {
            db.Sys_Menus.Add(new Sys_Menu
            {
                MenuId = CalendarMenuId,
                MenuName = "工作日历",
                RoutePath = "/oa/work-calendar",
                MenuKey = "oa-work-calendar",
                Icon = "Calendar",
                ParentId = 740,   // OA工作流 父组
                OrderNo = CalendarMenuId,
                Enable = true,
            });
            changed = true;
        }
        if (!db.Sys_RoleMenus.Any(rm => rm.RoleId == 1 && rm.MenuId == CalendarMenuId))
        {
            db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = CalendarMenuId });
            changed = true;
        }

        // ② 逐租户 MenuAction/RoleAction。
        var tenantIds = db.Sys_Tenants.Select(t => t.Id).ToList();
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
