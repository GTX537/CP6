using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Seed;

/// <summary>
/// WMS 権限点（Sys_MenuAction + Sys_RoleAction）逐租户启动幂等种子（M-WMS 横切接线 Task 3b）。
///
/// 背景：Task 3a 为 29 个 WMS 控制器 125 写端点贴了 <c>[RequirePermission("键","action")]</c>，但
/// <c>PermissionService.HasActionAsync</c> 无 admin 旁路——不种 Sys_RoleAction 则 admin 也 403。
/// 运行时 <c>PermissionAggregator.FillActionKeysAsync</c> 以
/// <c>Sys_RoleActions join Sys_Menus on MenuId where MenuKey!=null → "{MenuKey}:{ActionCode}"</c>
/// 聚合当前用户的 ActionKeys，故须为管理员角色 <c>RoleId=1</c> 在**每个租户**各授一份，
/// 并登记对应 <c>Sys_MenuAction</c>（操作点目录，供 UI 授权配置枚举）。
///
/// 数据来源（执行真相，与强校验 1:1）：
///  - 下方 <see cref="Actions"/> 清单直接从 <c>CP6.WebApi/Controllers/Wms/*.cs</c> 的
///    <c>[RequirePermission(键,action)]</c> 属性 grep 去重派生（106 条去重 (键,action)，覆盖全 30 键）。
///  - MenuId 经锚定表 <c>docs/seeds/wms-key-menu-anchor.md</c> 由权限键映射而得（30 键 → 锚定 MenuId）。
///  - 文档留档：<c>docs/seeds/wms-roleaction-seed.sql</c>（本 C# 为正本，SQL 与此一致）。
///
/// 逐租户机制（关键，照 P0-T3 逐租户种子 / space-roleaction-seed.sql 先例）：
///  - 枚举 <c>Sys_Tenants</c>（共享表，非行级过滤）全部租户 Id，对每租户各插一份。
///  - 显式设 <c>TenantId=tid</c> → <c>CP6Context.StampTenant</c> 仅盖 <c>TenantId==Guid.Empty</c>，不覆盖显式值。
///  - 幂等判存用 <c>IgnoreQueryFilters()</c>，使跨租户既存行对当前上下文（默认租户作用域）可见，避免误判缺失重复插。
///
/// 接入：Program.cs 于 <see cref="WmsMenuSeed.EnsureSeeded"/> **之后**调用（锚定菜单行须先在，RoleAction 挂 MenuId）。
/// 幂等：重启不重复插（(TenantId,MenuId,ActionCode) 判存守卫）。
/// </summary>
public static class WmsPermissionSeed
{
    /// <summary>
    /// (MenuId, ActionCode, ActionName) —— 与各 WMS 控制器 [RequirePermission(键, action)] 去重后 1:1。
    /// MenuId 依 <c>docs/seeds/wms-key-menu-anchor.md</c> 锚定；ActionName 为中文显示名（照 Fin 种子风格）。
    /// 计 106 条，覆盖 30 个 menu-key。
    /// </summary>
    private static readonly (int MenuId, string Code, string Name)[] Actions =
    {
        // 401 wms-warehouse — 倉庫マスタ
        (401, "add", "新建"), (401, "edit", "编辑"), (401, "del", "删除"),
        // 402 wms-location — ロケーション管理（寄居 WarehouseController）
        (402, "add", "新建"), (402, "edit", "编辑"), (402, "del", "删除"),
        // 403 wms-stock — 在庫照会
        (403, "adjust", "库存调整"), (403, "move", "移库"),
        // 429 wms-stock-qc — 在庫QC(保留/放行)
        (429, "set", "保留放行"),
        // 405 wms-inbound-order — 入庫予定 登録
        (405, "add", "新建"), (405, "edit", "编辑"), (405, "del", "删除"),
        (405, "confirm", "确认"), (405, "cancel", "取消"),
        // 406 wms-inbound-receipt — 入庫実績 入力
        (406, "post", "入库过账"),
        // 408 wms-outbound-order — 出庫指示 登録
        (408, "add", "新建"), (408, "edit", "编辑"), (408, "del", "删除"),
        (408, "confirm", "确认"), (408, "cancel", "取消"),
        (408, "allocate", "引当分配"), (408, "pick", "拣货"), (408, "ship", "出库"),
        // 415 wms-stocktake — 棚卸 作業
        (415, "add", "新建"), (415, "count", "盘点计数"), (415, "submit", "提交"),
        (415, "approve", "承认"), (415, "cancel", "取消"),
        // 417 wms-material-shortage — 材料欠品管理
        (417, "resolve", "解决"), (417, "dismiss", "消除"),
        // 419 wms-outbound-routing — 出庫ルーティング
        (419, "add", "新建"), (419, "edit", "编辑"), (419, "del", "删除"),
        // 421 wms-qc-inspection — 入荷検品(QC)
        (421, "add", "新建"), (421, "edit", "编辑"), (421, "judge", "判定处置"), (421, "cancel", "取消"),
        // 422 wms-slotting — スロッティング最適化
        (422, "analyze", "分析"), (422, "approve", "承认"), (422, "cancel", "取消"),
        // 423 wms-replenish — 補充指示
        (423, "add", "新建"), (423, "generate", "生成"), (423, "execute", "执行"), (423, "cancel", "取消"),
        // 424 wms-cross-dock — クロスドッキング
        (424, "add", "新建"), (424, "execute", "执行"), (424, "cancel", "取消"),
        // 425 wms-kitting — キッティング・組立
        (425, "add", "新建"), (425, "edit", "编辑"), (425, "del", "删除"),
        (425, "execute", "执行"), (425, "cancel", "取消"),
        // 426 wms-rma — 返品管理(RMA)
        (426, "add", "新建"), (426, "receive", "入库"), (426, "inspect", "检查"),
        (426, "judge", "判定"), (426, "close", "关闭"), (426, "cancel", "取消"),
        // 427 wms-lot-trace — ロット追溯・回収
        (427, "recall", "回收"),
        // 428 wms-expiry — 賞味期限管理(FEFO)
        (428, "dispose", "报废"),
        // 441 wms-paper-roll — 原紙ロール管理
        (441, "add", "新建"), (441, "consume", "消费"), (441, "slit", "分切"), (441, "dispose", "报废"),
        // 442 wms-remnant — 残材・端材管理
        (442, "add", "新建"), (442, "edit", "编辑"), (442, "reserve", "预留"),
        (442, "use", "使用"), (442, "dispose", "报废"), (442, "del", "删除"),
        // 443 wms-plate-mold — 印版・木型倉庫
        (443, "add", "新建"), (443, "edit", "编辑"), (443, "use", "使用"),
        (443, "maintenance", "维护"), (443, "dispose", "报废"), (443, "del", "删除"),
        // 444 wms-ink — インキ・接着剤管理
        (444, "add", "新建"), (444, "open", "开封"), (444, "mix", "调墨"),
        // 445 wms-pallet — パレット管理
        (445, "add", "新建"), (445, "edit", "编辑"), (445, "complete", "完了"),
        (445, "move", "移动"), (445, "ship", "出库"), (445, "del", "删除"),
        // 446 wms-vmi — 客先預り在庫(VMI)
        (446, "calculate", "计算"), (446, "confirm", "确认"),
        // 447 wms-sample-stock — 試作・サンプル在庫
        (447, "add", "新建"), (447, "edit", "编辑"), (447, "lend", "出借"),
        (447, "return", "返却"), (447, "expire", "失效"), (447, "del", "删除"),
        // 461 wms-mobile — モバイル作業指示
        (461, "add", "新建"), (461, "start", "开始"), (461, "scan", "扫描"),
        (461, "complete", "完了"), (461, "cancel", "取消"),
        // 462 wms-wcs-task — WCS/自動倉庫連携
        (462, "add", "新建"), (462, "dispatch", "派发"), (462, "start", "开始"),
        (462, "complete", "完了"), (462, "fail", "失败"), (462, "del", "删除"),
        // 463 wms-carrier — 配送業者連携
        (463, "add", "新建"), (463, "event", "状态更新"),
        // 464 wms-iot — IoT温湿度モニタ
        (464, "add", "新建"), (464, "edit", "编辑"), (464, "del", "删除"),
        (464, "ingest", "数据取込"), (464, "simulate", "模拟"),
        // 483 wms-stock-dwell — 在庫滞留レポート（唯一只读 POST，强校验 view）
        (483, "view", "查看"),
    };

    /// <summary>
    /// 逐租户幂等播种 WMS 全部权限点 + 授管理员（RoleId=1）。
    /// 须在 <see cref="WmsMenuSeed.EnsureSeeded"/> 之后调用（锚定菜单行须先在）。
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
