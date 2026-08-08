using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Seed;

/// <summary>
/// 采购(Pur)権限点（Sys_MenuAction + Sys_RoleAction）逐租户启动幂等种子（M-PUR 横切接线 Task 2）。
///
/// 背景：Pur 8 控制器（7 含真写）逐写端点贴 <c>[RequirePermission("键","action")]</c>，但
/// <c>PermissionService.HasActionAsync</c> 无 admin 旁路——不种 Sys_RoleAction 则 admin 也 403。
/// 运行时 <c>PermissionAggregator</c> 以
/// <c>Sys_RoleActions join Sys_Menus on MenuId where MenuKey!=null → "{MenuKey}:{ActionCode}"</c>
/// 聚合当前用户 ActionKeys，故须为管理员角色 <c>RoleId=1</c> 在**每个租户**各授一份，
/// 并登记对应 <c>Sys_MenuAction</c>（操作点目录，供 UI 授权配置枚举）。
///
/// 取代 Program.cs 原内联块（仅默认租户、仅既有 10 键）：本 Seed 一次覆盖**全部 24 资源键**
/// （既有 10：supplier-price/po/gr/match；新增 14：pr/rfq/subcontract），逐租户播齐——
/// 修复真相源 docs/seeds/pur-permission-keys.md §六 头号硬前置②「既有种子仅默认租户」。
///
/// 数据来源（执行真相，与真相源 §一 24 行 1:1）：下方 <see cref="Actions"/> 清单逐字照
/// docs/seeds/pur-permission-keys.md §一/§二；MenuId 锚定 Program.cs Pur 菜单 701–707。
///
/// 逐租户机制（照 WmsPermissionSeed 先例）：
///  - 枚举 <c>Sys_Tenants</c>（共享表，非行级过滤）全部租户 Id，对每租户各插一份。
///  - 显式设 <c>TenantId=tid</c> → <c>CP6Context.StampTenant</c> 仅盖 <c>TenantId==Guid.Empty</c>，不覆盖显式值。
///  - 幂等判存用 <c>IgnoreQueryFilters()</c>，使跨租户既存行对当前上下文（默认租户作用域）可见，避免误判缺失重复插。
///
/// 接入：Program.cs 于 Pur 菜单插入 + 705/706/707 MenuKey 显式赋值 **之后**调用（锚定菜单行须先在，RoleAction 挂 MenuId）。
/// 幂等：重启不重复插（(TenantId,MenuId,ActionCode) 判存守卫）。
/// </summary>
public static class PurPermissionSeed
{
    /// <summary>
    /// (MenuId, ActionCode, ActionName) —— 与 docs/seeds/pur-permission-keys.md §一 24 行逐字 1:1。
    /// MenuId 锚定 Program.cs Pur 菜单 701–707；ActionName 为中文显示名。
    /// 计 24 条，覆盖 7 个 menu-key（既有 10 键 + 新增 14 键，含 subcontract:view 只读豁免键）。
    /// </summary>
    private static readonly (int MenuId, string Code, string Name)[] Actions =
    {
        // 701 pur-supplier-price — 供应商价表（既有）
        (701, "add", "新增/更新"), (701, "delete", "删除"),
        // 702 pur-po — 采购订单（既有）
        (702, "add", "建单"), (702, "submit", "送审"), (702, "cancel", "取消"),
        // 703 pur-gr — 采购收货（既有）
        (703, "add", "确认收货"), (703, "qc", "检收应用"),
        // 704 pur-match — 三单匹配（既有）
        (704, "add", "匹配建票"), (704, "release", "放行"), (704, "reject", "拒绝"),
        // 706 pur-pr — 采购申请（新增）
        (706, "query", "查看"), (706, "add", "建单"), (706, "submit", "送审"), (706, "convert", "转PO"),
        // 705 pur-rfq — 询价比价（新增）
        (705, "add", "发起询价"), (705, "invite", "邀请供应商"), (705, "quote", "收报价"),
        (705, "rank", "比价排名"), (705, "select", "选定"), (705, "writeback", "回写价表"),
        (705, "convert", "转PO"),
        // 707 pur-subcontract — 外注加工（新增，含只读豁免 view）
        (707, "consign", "登记支給材"), (707, "issue", "发支給材"),
        (707, "cost", "成品成本核算"), (707, "view", "对账查看"),
    };

    /// <summary>
    /// 逐租户幂等播种 Pur 全部权限点 + 授管理员（RoleId=1）。
    /// 须在 Pur 菜单插入 + 705/706/707 MenuKey 显式赋值之后调用（锚定菜单行须先在）。
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
