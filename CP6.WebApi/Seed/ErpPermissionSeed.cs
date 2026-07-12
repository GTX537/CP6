using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Seed;

/// <summary>
/// ERP 販売管理 権限点（Sys_MenuAction + Sys_RoleAction）逐租户启动幂等种子（M-ERP 横切接线 Task 3b）。
///
/// 背景：Task 3a 为 10 个 ERP 控制器的 35 个写端点贴了 <c>[RequirePermission("键","action")]</c>，但
/// <c>PermissionService.HasActionAsync</c> 无 admin 旁路——不种 Sys_RoleAction 则 admin 也 403。
/// 运行时 <c>PermissionAggregator.FillActionKeysAsync</c> 以
/// <c>Sys_RoleActions join Sys_Menus on MenuId where MenuKey!=null → "{MenuKey}:{ActionCode}"</c>
/// 聚合当前用户 ActionKeys，故须为管理员角色 <c>RoleId=1</c> 在**每个租户**各授一份，
/// 并登记对应 <c>Sys_MenuAction</c>（操作点目录，供 UI 授权配置枚举）。
///
/// 三数闭环（本 C# 为正本，与真相源 1:1）：
///  - 控制器写端点：<b>35</b>（grep <c>CP6.WebApi/Controllers/Erp/*.cs</c> 的 [RequirePermission]，逐字核对）。
///  - 去重 (menu-key, action) 元组：<b>30</b>（Copy 复用 add、CancelConfirm 归并 confirm、Revise 归并 edit 等
///    5 处重复消解：estimate-calc:add / quotation:add / quotation:confirm / product:add / plate-mold:edit 各出现两次）。
///  - 种子元组：<b>30</b>（下方 <see cref="Actions"/>；漏种 0 / 多种 0）。
///  - 覆盖 <b>11</b> 个 menu-key（有写端点者）。另 3 键 erp-order-trace / erp-credit-note / erp-otd-report
///    仅有 view 端点（GET-only 或只读 POST 豁免），未贴点即无键可种，故不在本种子——与 14 键总数不矛盾。
///
/// 数据来源（执行真相）：
///  - MenuId 经锚定表 <c>docs/seeds/erp-key-menu-anchor.md</c> 由权限键映射而得（14 键 → 锚定 MenuId）。
///  - ActionCode 与 <c>CP6.WebApi/Controllers/Erp/*.cs</c> 的 [RequirePermission] 第二实参逐字一致（差一字全链 403）。
///  - 文档留档：<c>docs/seeds/erp-permission-seed.sql</c>（本 C# 为正本，SQL 与此一致）。
///
/// 逐租户机制（关键，照 WmsPermissionSeed / space-roleaction-seed.sql 先例）：
///  - 枚举 <c>Sys_Tenants</c>（共享表，非行级过滤）全部租户 Id，对每租户各插一份。
///  - 显式设 <c>TenantId=tid</c> → <c>CP6Context.StampTenant</c> 仅盖 <c>TenantId==Guid.Empty</c>，不覆盖显式值。
///  - 幂等判存用 <c>IgnoreQueryFilters()</c>，使跨租户既存行对当前上下文（默认租户作用域）可见，避免误判缺失重复插。
///
/// 接入：Program.cs 于 <see cref="ErpMenuSeed.EnsureSeeded"/> **之后**调用（锚定菜单行须先在，RoleAction 挂 MenuId）。
/// 幂等：重启不重复插（(TenantId,MenuId,ActionCode) / (TenantId,RoleId,MenuId,ActionCode) 判存守卫）。
/// </summary>
public static class ErpPermissionSeed
{
    /// <summary>
    /// (MenuId, ActionCode, ActionName) —— 与各 ERP 控制器 [RequirePermission(键, action)] 去重后 1:1。
    /// MenuId 依 <c>docs/seeds/erp-key-menu-anchor.md</c> 锚定；ActionName 为中文显示名（照 WMS/Fin 种子风格）。
    /// 计 30 条，覆盖 11 个有写端点的 menu-key。
    /// </summary>
    private static readonly (int MenuId, string Code, string Name)[] Actions =
    {
        // 202 erp-estimate-calc — 見積計算書 登録（Create/Update/Delete/Copy；Copy 复用 add，Calculate 只读豁免）
        (202, "add", "新建"), (202, "edit", "编辑"), (202, "del", "删除"),
        // 204 erp-quotation — 御見積書 登録（confirm 含 CancelConfirm 归并）
        (204, "add", "新建"), (204, "edit", "编辑"), (204, "del", "删除"),
        (204, "confirm", "确定"), (204, "issue", "发行"),
        // 206 erp-product — 製品マスタ 登録（Copy 复用 add）
        (206, "add", "新建"), (206, "edit", "编辑"), (206, "del", "删除"),
        // 208 erp-order — 受注入力（4 只读计算 POST 与未出荷子视图豁免；cancel 高危独立键）
        (208, "add", "新建"), (208, "edit", "编辑"), (208, "del", "删除"), (208, "cancel", "受注取消"),
        // 209 erp-order-price-correction — 単価訂正（跨菜单：OrderController.BatchUpdatePrice=correct 高危）
        (209, "correct", "单价订正"),
        // 210 erp-fsc-checklist — FSC チェックシート（Issue 写发行履历）
        (210, "issue", "发行"),
        // 212 erp-business-partner — 取引先マスタ 登録（del 另挂 Roles 闸，双闸并存）
        (212, "add", "新建"), (212, "edit", "编辑"), (212, "del", "删除"),
        // 213 erp-sheet-unit-price — シート単価マスタ（Import 状态键 / BatchUpdate=edit）
        (213, "import", "取込"), (213, "edit", "编辑"),
        // 215 erp-plate-mold — 版型/木型 登録（Revise 归并 edit；Label 只读豁免）
        (215, "add", "新建"), (215, "edit", "编辑"), (215, "del", "删除"),
        // 218 erp-backorder — 欠品・残数管理（close/split 两状态键，孤儿收编菜单）
        (218, "close", "关闭残数"), (218, "split", "拆分新单"),
        // 220 erp-fx-rate — 為替レートマスタ（add/edit/del，孤儿收编菜单）
        (220, "add", "新建"), (220, "edit", "编辑"), (220, "del", "删除"),
    };

    /// <summary>
    /// 逐租户幂等播种 ERP 全部权限点 + 授管理员（RoleId=1）。
    /// 须在 <see cref="ErpMenuSeed.EnsureSeeded"/> 之后调用（锚定菜单行须先在）。
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
