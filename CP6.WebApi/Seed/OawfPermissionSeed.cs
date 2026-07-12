using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Seed;

/// <summary>
/// OA/WF 電子表单・工作流 権限点（Sys_MenuAction + Sys_RoleAction）逐租户启动幂等种子（M-OA/WF 横切接线 Task 3b）。
///
/// 背景：Task 3a 为 OA/WF 16 控制器的 31 个真写端点贴了 <c>[RequirePermission("键","action")]</c>（Oa 21 + Wf 10），但
/// <c>PermissionService.HasActionAsync</c> 无 admin 旁路——不种 Sys_RoleAction 则 admin 也 403。
/// 运行时 <c>PermissionAggregator.FillActionKeysAsync</c> 以
/// <c>Sys_RoleActions join Sys_Menus on MenuId where MenuKey!=null → "{MenuKey}:{ActionCode}"</c>
/// 聚合当前用户 ActionKeys，故须为管理员角色 <c>RoleId=1</c> 在**每个租户**各授一份，
/// 并登记对应 <c>Sys_MenuAction</c>（操作点目录，供 UI 授权配置枚举）。
///
/// 三数闭环（本 C# 为正本，与真相源 docs/seeds/oawf-permission-keys.md §一/§七 1:1）：
///  - 控制器真写端点：<b>31</b>（grep <c>Controllers/Oa/*.cs</c> 21 + <c>Controllers/Wf/*.cs</c> 10 的 [RequirePermission]，逐字核对）。
///  - 去重 (menu-key, action) 元组：<b>20</b>（多处跨控制器归并消解重复，真相源 §五）：
///    <c>oa-inbox:read</c> 覆 Inbox task/cc-read + Notification read/read-all（4→1）；
///    <c>oa-inbox:approve</c> 覆 Inbox batch + Flow act（2→1）；<c>oa-inbox:sendback</c> 覆 Inbox + AdvancedFlow（2→1）；
///    <c>oa-form-catalog:submit</c> 覆 Draft submit + Approval submit + Form data + Flow submit（4→1）；
///    <c>oa-settings:delegate</c> 覆 Delegate add/remove + AdvancedFlow delegate（3→1，T2 委派合一拍板1）；
///    <c>oa-designer:edit</c> 覆 Designer save + Flow def（2→1）。
///  - 种子元组：<b>20</b>（下方 <see cref="Actions"/>；漏种 0 / 多种 0）。
///  - 覆盖 <b>6</b> 个 menu-key（有写端点者：733/734/735/737/738/739）。另 1 键 <c>oa-form-search</c>(736)
///    仅有 1 只读 POST 端点（Query search 豁免→view，未贴点），无键可种，故不在本种子——与 7 键总数不矛盾。
///  - 2 只读 POST 豁免（Forecast preview→<c>oa-form-catalog:view</c> / Query search→<c>oa-form-search:view</c>）
///    未贴点＝不入种子。资源键总 22 = 本 20 写键 + 2 view 豁免键。
///
/// 数据来源（执行真相）：
///  - MenuId 经锚定表 <c>docs/seeds/oawf-key-menu-anchor.md</c> 由 7 权限键映射而得（OA RoutePath 与键天然对齐，零错配）。
///  - ActionCode 与 <c>Controllers/Oa|Wf/*.cs</c> 的 [RequirePermission] 第二实参逐字一致（差一字审批全链 403）。
///  - 文档留档：<c>docs/seeds/oawf-permission-seed.sql</c>（本 C# 为正本，SQL 与此一致）。
///
/// 逐租户机制（照 MesPermissionSeed / ErpPermissionSeed / WmsPermissionSeed 先例）：
///  - 枚举 <c>Sys_Tenants</c>（共享表，非行级过滤）全部租户 Id，对每租户各插一份。
///  - 显式设 <c>TenantId=tid</c> → <c>CP6Context.StampTenant</c> 仅盖 <c>TenantId==Guid.Empty</c>，不覆盖显式值。
///  - 幂等判存用 <c>IgnoreQueryFilters()</c>，使跨租户既存行对当前上下文（默认租户作用域）可见，避免误判缺失重复插。
///
/// 接入：Program.cs 于 <see cref="OawfMenuSeed.EnsureSeeded"/> **之后**调用（锚定菜单行须先在，RoleAction 挂 MenuId）。
/// 幂等：重启不重复插（(TenantId,MenuId,ActionCode) / (TenantId,RoleId,MenuId,ActionCode) 判存守卫）。
/// </summary>
public static class OawfPermissionSeed
{
    /// <summary>
    /// (MenuId, ActionCode, ActionName) —— 与各 OA/WF 控制器 [RequirePermission(键, action)] 去重后 1:1。
    /// MenuId 依 <c>docs/seeds/oawf-key-menu-anchor.md</c> 锚定；ActionName 为中文显示名（照 MES/ERP/WMS 种子风格，
    /// 仅供 UI 显示，非权限判定依据——判定只看 ActionCode）。
    /// 计 20 条，覆盖 6 个有写端点的 menu-key。
    /// </summary>
    private static readonly (int MenuId, string Code, string Name)[] Actions =
    {
        // 733 oa-inbox — 电子表单信箱 + /wf 引擎审批动作（read 归并 4 已读端点；approve/sendback 跨控制器归并）
        (733, "read", "标记已读"), (733, "approve", "审批"), (733, "transfer", "转交"),
        (733, "sendback", "退回"), (733, "addsign", "加签"), (733, "withdraw", "撤回"),
        // 734 oa-flow-admin — 流程管理（enable 状态键，流程启停干预）
        (734, "enable", "启停"),
        // 735 oa-form-catalog — 填單（收藏 + 草稿 CRUD + 起流程/提交；submit 归并 4 起流程端点）
        (735, "add", "新建"), (735, "edit", "编辑"), (735, "submit", "提交"),
        (735, "del", "删除"), (735, "favorite", "收藏"),
        // 737 oa-settings — 设定（偏好 edit + 委派合一 delegate，含 OA add/remove + AdvancedFlow delegate）
        (737, "edit", "编辑"), (737, "delegate", "委派"),
        // 738 oa-designer — 流程设计器（新栈 save/clone + 旧栈 flow/form def；edit 归并 Save+FlowDef）
        (738, "edit", "编辑"), (738, "add", "克隆"), (738, "form-save", "表单保存"),
        // 739 oa-approver-map — 审批人映射维护
        (739, "add", "新建"), (739, "edit", "编辑"), (739, "del", "删除"),
    };

    /// <summary>
    /// 逐租户幂等播种 OA/WF 全部权限点 + 授管理员（RoleId=1）。
    /// 须在 <see cref="OawfMenuSeed.EnsureSeeded"/> 之后调用（锚定菜单行须先在）。
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
