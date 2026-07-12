using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Seed;

/// <summary>
/// MES 製造執行 権限点（Sys_MenuAction + Sys_RoleAction）逐租户启动幂等种子（M-MES 横切接线 Task 3b）。
///
/// 背景：Task 3a 为 9 个 MES 控制器的 28 个写端点贴了 <c>[RequirePermission("键","action")]</c>，但
/// <c>PermissionService.HasActionAsync</c> 无 admin 旁路——不种 Sys_RoleAction 则 admin 也 403。
/// 运行时 <c>PermissionAggregator.FillActionKeysAsync</c> 以
/// <c>Sys_RoleActions join Sys_Menus on MenuId where MenuKey!=null → "{MenuKey}:{ActionCode}"</c>
/// 聚合当前用户 ActionKeys，故须为管理员角色 <c>RoleId=1</c> 在**每个租户**各授一份，
/// 并登记对应 <c>Sys_MenuAction</c>（操作点目录，供 UI 授权配置枚举）。
///
/// 三数闭环（本 C# 为正本，与真相源 1:1）：
///  - 控制器写端点：<b>28</b>（grep <c>CP6.WebApi/Controllers/Mes/*.cs</c> 的 [RequirePermission]，逐字核对）。
///  - 去重 (menu-key, action) 元组：<b>25</b>（3 处归并消解重复：
///    mes-work-order:add 覆 Create+ExpandFromOrder / mes-production-result:suspend 覆 Suspend+Resume /
///    mes-machine:downtime 覆 RegisterDowntime+CloseDowntime。真相源 §五 归并 1/2/3）。
///  - 种子元组：<b>25</b>（下方 <see cref="Actions"/>；漏种 0 / 多种 0）。
///  - 覆盖 <b>9</b> 个 menu-key（有写端点者）。另 1 键 mes-plan-achievement 仅有 2 只读 POST 端点
///    （豁免→view，未贴点），无键可种，故不在本种子——与 10 键总数不矛盾。
///
/// 数据来源（执行真相）：
///  - MenuId 经锚定表 <c>docs/seeds/mes-key-menu-anchor.md</c> 由权限键映射而得（10 键 → 锚定 MenuId）。
///  - ActionCode 与 <c>CP6.WebApi/Controllers/Mes/*.cs</c> 的 [RequirePermission] 第二实参逐字一致（差一字全链 403）。
///  - 文档留档：<c>docs/seeds/mes-permission-seed.sql</c>（本 C# 为正本，SQL 与此一致）。
///
/// 逐租户机制（关键，照 ErpPermissionSeed / WmsPermissionSeed 先例）：
///  - 枚举 <c>Sys_Tenants</c>（共享表，非行级过滤）全部租户 Id，对每租户各插一份。
///  - 显式设 <c>TenantId=tid</c> → <c>CP6Context.StampTenant</c> 仅盖 <c>TenantId==Guid.Empty</c>，不覆盖显式值。
///  - 幂等判存用 <c>IgnoreQueryFilters()</c>，使跨租户既存行对当前上下文（默认租户作用域）可见，避免误判缺失重复插。
///
/// 接入：Program.cs 于 <see cref="MesMenuSeed.EnsureSeeded"/> **之后**调用（锚定菜单行须先在，RoleAction 挂 MenuId）。
/// 幂等：重启不重复插（(TenantId,MenuId,ActionCode) / (TenantId,RoleId,MenuId,ActionCode) 判存守卫）。
/// </summary>
public static class MesPermissionSeed
{
    /// <summary>
    /// (MenuId, ActionCode, ActionName) —— 与各 MES 控制器 [RequirePermission(键, action)] 去重后 1:1。
    /// MenuId 依 <c>docs/seeds/mes-key-menu-anchor.md</c> 锚定；ActionName 为中文显示名（照 ERP/WMS 种子风格，
    /// 仅供 UI 显示，非权限判定依据——判定只看 ActionCode）。
    /// 计 25 条，覆盖 9 个有写端点的 menu-key。
    /// </summary>
    private static readonly (int MenuId, string Code, string Name)[] Actions =
    {
        // 301 mes-planning-board — 生産計画ボード（reschedule/arrange 两状态键）
        (301, "reschedule", "改期"), (301, "arrange", "自动排产"),
        // 302 mes-work-order — 製造指図 入力（add 复用 Create+ExpandFromOrder；issue 状态键）
        (302, "add", "新建"), (302, "edit", "编辑"), (302, "del", "删除"), (302, "issue", "发行"),
        // 304 mes-production-result — 製造実績 入力（suspend 归并 Suspend+Resume；complete 高危反冲）
        (304, "start", "开始"), (304, "suspend", "中断"), (304, "complete", "完了"), (304, "report", "报工"),
        // 306 mes-quality-inspection — 品質検査 入力
        (306, "add", "新建"), (306, "edit", "编辑"),
        // 308 mes-defect — 不良品管理
        (308, "add", "新建"), (308, "edit", "编辑"), (308, "del", "删除"),
        // 310 mes-machine — 設備管理（downtime 归并 Register+Close；status 状态键）
        (310, "add", "新建"), (310, "edit", "编辑"), (310, "del", "删除"),
        (310, "status", "状态变更"), (310, "downtime", "停机记录"),
        // 311 mes-oee — OEE 分析（recalculate 状态键）
        (311, "recalculate", "重算"),
        // 314 mes-work-center — 工作中心 master（upsert=edit）
        (314, "edit", "编辑"), (314, "del", "删除"),
        // 315 mes-process-cost-rate — 工序费率 master（upsert=edit，高危）
        (315, "edit", "编辑"), (315, "del", "删除"),
    };

    /// <summary>
    /// 逐租户幂等播种 MES 全部权限点 + 授管理员（RoleId=1）。
    /// 须在 <see cref="MesMenuSeed.EnsureSeeded"/> 之后调用（锚定菜单行须先在）。
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
