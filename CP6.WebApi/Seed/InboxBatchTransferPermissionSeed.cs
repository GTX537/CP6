using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Seed;

/// <summary>
/// OA 信箱批量改派权限点（Sys_MenuAction + Sys_RoleAction）逐租户启动幂等种子（WFS 波④ 信箱体验 B-T2）。
///
/// 背景：波④ InboxController 新增两个在途批量转单 POST 端点
///  - <c>POST /api/oa/inbox/batch-transfer</c>（执行批量改派）
///  - <c>POST /api/oa/inbox/batch-transfer/preview</c>（预览，只算不写，同请求体）
/// 二者同贴 <c>[RequirePermission("oa-inbox", "batch-transfer")]</c>（spec <c>OA.Inbox.BatchTransfer</c> → (oa-inbox,
/// batch-transfer) 的落地映射，C4/C8：preview 同权限点）。<c>PermissionService.HasActionAsync</c> 无 admin 旁路
/// ——不种 Sys_RoleAction 则 admin 亦 403。
///
/// 「贴点⊆种子」互锁：InboxController 实际用到的新 action 集 {batch-transfer} 恰为本种子登记集，无孤儿键、无漏种。
/// 菜单 733 <c>MenuKey="oa-inbox"</c> 回填已由 <see cref="OawfMenuSeed"/> 落地（M-OA/WF 波，锚定行显式赋值），
/// 本种子仅挂 Sys_MenuAction/Sys_RoleAction，不重做 MenuKey 回填。
///
/// 逐租户机制（照 <see cref="FlowTriggerPermissionSeed"/> / <see cref="OawfPermissionSeed"/> 先例）：
///  - 枚举 <c>Sys_Tenants</c>（共享表）全部租户 Id，对每租户各插一份，显式 <c>TenantId=tid</c>
///    → <c>CP6Context.StampTenant</c> 仅盖 Guid.Empty 不覆盖显式值。
///  - 幂等判存用 <c>IgnoreQueryFilters()</c>（跨租户可见），避免默认租户作用域误判缺失重复插。
///
/// 接入：Program.cs 于 <see cref="OawfPermissionSeed.EnsureSeeded"/> **之后**调用（锚定菜单 733 须先在）。
/// </summary>
public static class InboxBatchTransferPermissionSeed
{
    /// <summary>(MenuId, ActionCode, ActionName) —— 与 InboxController [RequirePermission] 第二实参逐字一致。</summary>
    private static readonly (int MenuId, string Code, string Name)[] Actions =
    {
        (733, "batch-transfer", "批量改派"),
    };

    /// <summary>逐租户幂等播种 batch-transfer 权限点 + 授管理员（RoleId=1）。须在 OawfPermissionSeed 之后调用。</summary>
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
