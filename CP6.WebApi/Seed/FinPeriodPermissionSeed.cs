using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Seed;

/// <summary>
/// F1 波G Task G.1：年结高危端点权限点（Sys_MenuAction + Sys_RoleAction）逐租户启动幂等种子。
///
/// 背景：波D D.1 给 <c>PeriodController</c> 新增了两个高危端点
/// <c>POST year-close/{fy}</c> / <c>POST reopen-year/{fy}</c>，各贴
/// <c>[RequirePermission("fin-period","year-close"/"reopen-year")]</c>。
/// <c>PermissionService.HasActionAsync</c> 无 admin 旁路——不种 <c>Sys_RoleAction</c> 则 admin 也 403。
/// 运行时 <c>PermissionAggregator</c> 以 <c>Sys_RoleActions join Sys_Menus on MenuId where MenuKey!=null →
/// "{MenuKey}:{ActionCode}"</c> 聚合，故须为管理员角色 <c>RoleId=1</c> 在**每个租户**各授一份。
///
/// 锚定：菜单 <c>604</c>（RoutePath <c>/fin/period</c> → MenuKey <c>fin-period</c>，由 Program.cs Fin 权限块回填），
/// 与既有 <c>close/reopen</c>（Program.cs D-2 单租户块，仅默认租户）同菜单不同 action，无冲突。
///
/// 逐租户机制（照 <see cref="WmsPermissionSeed"/> M-WMS T3b 先例，比 D-2 单租户 finActions 更正确）：
///  - 枚举 <c>Sys_Tenants</c>（共享表）全部租户，对每租户各插一份，显式 <c>TenantId=tid</c>。
///  - 幂等判存用 <c>IgnoreQueryFilters()</c>，跨租户可见避免误判缺失重复插。
///
/// 接入：Program.cs 于 Fin 权限块（MenuKey 604 回填就位）**之后**调用。幂等可重入。
/// </summary>
public static class FinPeriodPermissionSeed
{
    private const int PeriodMenuId = 604;

    /// <summary>(ActionCode, ActionName) —— 与 PeriodController 两个新高危端点 [RequirePermission] 一一对应。</summary>
    private static readonly (string Code, string Name)[] Actions =
    {
        ("year-close", "年结"),
        ("reopen-year", "反年结"),
    };

    public static void EnsureSeeded(CP6Context db)
    {
        var tenantIds = db.Sys_Tenants.Select(t => t.Id).ToList();
        if (tenantIds.Count == 0) return;

        var changed = false;
        foreach (var tid in tenantIds)
        {
            foreach (var (code, name) in Actions)
            {
                if (!db.Sys_MenuActions.IgnoreQueryFilters()
                        .Any(x => x.TenantId == tid && x.MenuId == PeriodMenuId && x.ActionCode == code))
                {
                    db.Sys_MenuActions.Add(new Sys_MenuAction
                    {
                        TenantId = tid,
                        MenuId = PeriodMenuId,
                        ActionCode = code,
                        ActionName = name,
                        Sort = 0,
                        Creator = "system",
                        CreateDate = DateTime.Now,
                    });
                    changed = true;
                }

                if (!db.Sys_RoleActions.IgnoreQueryFilters()
                        .Any(x => x.TenantId == tid && x.RoleId == 1 && x.MenuId == PeriodMenuId && x.ActionCode == code))
                {
                    db.Sys_RoleActions.Add(new Sys_RoleAction
                    {
                        TenantId = tid,
                        RoleId = 1,
                        MenuId = PeriodMenuId,
                        ActionCode = code,
                        Creator = "system",
                        CreateDate = DateTime.Now,
                    });
                    changed = true;
                }
            }
        }

        if (changed)
            db.SaveChanges();
    }
}
