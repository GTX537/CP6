using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>
/// OA/WF 電子表单・工作流 菜单启动幂等种子（M-OA/WF 横切接线 Task 2）。
///
/// 背景（头号命门·回填时序）：OA 菜单 733–740 由 Program.cs 播种（:1446–1496），但插入块位于
/// 「无 MenuKey 菜单 RoutePath 自动回填」块（:908，<c>MenuKey = RoutePath.Trim('/').Replace('/','-')</c>）
/// **之后**，且 Add 时**均未设 MenuKey**。洁净部署首启：回填块先跑（OA 菜单尚不存在）→ 跳过；OA 菜单随后
/// 插入 MenuKey 留 null → <see cref="Sys_Menu.MenuKey"/> 为 null 被 PermissionAggregator 过滤掉 → OA/WF action
/// 键无法 join 出 → 首启即 fail-closed 403，需二次重启回填才生效（真相源 §六头号命门）。本种子在回填块**之前**
/// 调用（Program.cs 紧随 MesPermissionSeed 之后、先于 :908 回填），把 7 个有菜单的 menu-key 各锚定到**一个**
/// 菜单行显式设 MenuKey=<c>oa-*</c>（含缺行补建），首启即生效。
///
/// 与 MES 不同：OA 733–739 的 RoutePath 派生键与真相源 menu-key **逐字一致**（<c>/oa/inbox</c>→<c>oa-inbox</c> …），
/// **零错配**（不同于 MES machine-list 命门）。故本种子的防御矫正块在正常运行下恒为 no-op，仅为结构对齐与
/// 防御历史/异常写坏而保留（严限 7 锚定行）。命门纯粹是**时序**：显式赋值须先于回填以消除 null-全-403 窗口。
///
/// MenuKey 策略（与 ErpMenuSeed/WmsMenuSeed/MesMenuSeed 一致）：
///  - **锚定行**显式设 MenuKey = T1 权威键（真相源 §二 7 键），**绝不靠 RoutePath 自动回填**。
///  - **740 父组行**（OA工作流，无 RoutePath）MenuKey 留 null，不承载权限（回填因 RoutePath==null 亦跳过）。
///    ★<c>Sys_Menus.MenuKey</c> 有 <c>IS NOT NULL</c> 过滤唯一索引（HasIndex("MenuKey").IsUnique().HasFilter，
///    CP6Context.cs:602）——两行共赋同一非空 MenuKey 会撞唯一键。7 锚定键互不相同（真相源 §二，OA RoutePath
///    与键天然对齐），安全。
///  - **委派双键合一裁决**（主控 T2 拍板1）：<c>oa-inbox:delegate</c> 与 <c>oa-settings:delegate</c> 在 action
///    层合一为 <c>oa-settings:delegate</c>（权限面统一），**不影响 menu-key 集**——<c>oa-inbox</c>/<c>oa-settings</c>
///    两菜单键均照旧锚定。<c>oa-flow-admin:enable</c> 维持状态级（拍板2）。
///  - **双栈收编裁决**（用户 2026-07-12，拍板3 落地）：旧设计器孤儿路由 <c>/wf/form-designer</c>、
///    <c>/wf/flow-designer</c>（前端 router/index.ts:46-47 viewModules 已注册组件映射，但无 Sys_Menu 行 →
///    <c>addDynamicRoutes</c> 不注册 → 洁净部署下不可达，真相源 §六头号裁决点）**收编而非退役**：补两行
///    Sys_Menu（741/742）令其可达，双栈并存，旧栈写端点不删。<b>MenuKey 留 null</b>——权限已锚在 738
///    （<c>oa-designer:edit</c>/<c>oa-designer:form-save</c>，旧栈 FlowController.SaveDef/FormController.SaveDef
///    归并入该键，真相源 §一 #28/#31），741/742 若也赋 <c>oa-designer</c> 会与 738 撞
///    <c>Sys_Menus.MenuKey IS NOT NULL</c> 过滤唯一索引（同键不可两行）。留 null 后由 Program.cs :908 回填块
///    派生 <c>wf-form-designer</c>/<c>wf-flow-designer</c>——无 RoleAction 引用，纯挂菜单树无害（与 MES
///    非锚定行同型）。
///
/// 幂等：MenuId 判存守卫不重复插入；防御矫正块把既有库中被历史回填/异常写坏的 7 锚定行 MenuKey 就地纠回
/// <c>oa-*</c>（作用域严限 7 锚定行 r.Key != null；740 父行、741/742 收编行不动）。RoleMenu 默认授管理员
/// （RoleId=1，默认租户由 SaveChanges 拦截器盖章 TenantId；逐租户传播由 Program.cs「首次补建管理员角色」块负责）。
/// </summary>
public static class OawfMenuSeed
{
    /// <summary>
    /// 菜单定义：(MenuId, MenuName, RoutePath, Icon, ParentId, OrderNo, MenuKey)。
    /// MenuKey 非 null 者即 7 个权限锚定行（733–739）；null 者为 740 父行（不承载权限）+ 741/742 双栈
    /// 收编行（承载权限，但锚在 738，本行故意不赋键）。
    /// 733–740 既有由 Program.cs（:1446–1496）播种，本表含之仅为 (a) 显式锚定 MenuKey (b) 部分部署缺行时补建。
    /// RoutePath/Icon/ParentId/OrderNo 与 Program.cs 现有 OA 菜单块逐字一致。
    /// 741/742 取号：全仓扫描（Seed 目录 + Program.cs + 迁移）740 段仅占用至 740，741/742 无占用
    /// （730–732 计划中台段与本段不重叠），照 ErpMenuSeed 五孤儿收编先例（216–220）就近连续取号。
    /// </summary>
    private static readonly (int Id, string Name, string? Route, string? Icon, int? Parent, int Order, string? Key)[] Rows =
    {
        // ── 親（Top，无 RoutePath，不锚定） ─────────────────────────────
        (740, "OA工作流",     null,               "MessageBox", null, 249, null),

        // ── 7 锚定行（真相源 §二，回填派生键与 menu-key 逐字一致，仍显式赋值规避时序命门） ──
        (733, "电子表单信箱",  "/oa/inbox",         "Inbox",     740, 733, "oa-inbox"),
        (734, "流程管理",      "/oa/flow-admin",    "Operation", 740, 734, "oa-flow-admin"),
        (735, "填單",          "/oa/form-catalog",  "EditPen",   740, 735, "oa-form-catalog"),
        (736, "表单查询",      "/oa/form-search",   "Search",    740, 736, "oa-form-search"),
        (737, "设定",          "/oa/settings",      "Setting",   740, 737, "oa-settings"),
        (738, "流程设计器",    "/oa/designer",      "Edit",      740, 738, "oa-designer"),
        (739, "approverMap",   "/oa/approver-map",  "Edit",      740, 739, "oa-approver-map"),

        // ── 双栈孤儿路由收编 741–742（用户裁决 2026-07-12，照 ErpMenuSeed 五孤儿先例）：RoutePath 与
        // cp6.web/src/router/index.ts:46-47 viewModules 键逐字一致，令其经 addDynamicRoutes 可达。
        // MenuKey 留 null（权限已锚 738，见上方类注释「双栈收编裁决」），Icon 照 738 同款 Edit。
        (741, "フォームデザイナー(旧)", "/wf/form-designer", "Edit", 740, 741, null),
        (742, "フローデザイナー(旧)",   "/wf/flow-designer", "Edit", 740, 742, null),
    };

    /// <summary>
    /// 幂等播种全部 OA/WF 菜单 + 授管理员（RoleId=1）+ 锚定行 MenuKey 显式矫正。
    /// 须在 Program.cs 的「无 MenuKey 菜单 RoutePath 自动回填」块（:908）**之前**调用。
    /// </summary>
    public static void EnsureSeeded(CP6Context db)
    {
        var changed = false;
        foreach (var r in Rows)
        {
            if (!db.Sys_Menus.Any(m => m.MenuId == r.Id))
            {
                db.Sys_Menus.Add(new Sys_Menu
                {
                    MenuId = r.Id,
                    MenuName = r.Name,
                    RoutePath = r.Route,
                    MenuKey = r.Key,
                    Icon = r.Icon,
                    ParentId = r.Parent,
                    OrderNo = r.Order,
                    Enable = true,
                });
                changed = true;
            }

            // 授管理员菜单（默认租户由 SaveChanges 拦截器盖章 TenantId）。菜单已存在但未授权时补授（幂等）。
            if (!db.Sys_RoleMenus.Any(rm => rm.RoleId == 1 && rm.MenuId == r.Id))
            {
                db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = r.Id });
                changed = true;
            }
        }

        if (changed)
            db.SaveChanges();

        // 锚定键防御：既有 733–739 缺 MenuKey 时会被历史 RoutePath 回填写成派生键（OA 派生即等于目标，正常
        // 恒一致）；若因异常/半迁移写坏则就地矫正为 T1 权威键。作用域严限 7 锚定行（r.Key != null，按 MenuId
        // 定位——按 Key 找会漏被写坏行），740 父行不动。幂等（已正确即跳过）。
        var fixedAny = false;
        foreach (var r in Rows)
        {
            if (r.Key == null) continue;
            var menu = db.Sys_Menus.FirstOrDefault(m => m.MenuId == r.Id);
            if (menu != null && menu.MenuKey != r.Key)
            {
                menu.MenuKey = r.Key;
                fixedAny = true;
            }
        }
        if (fixedAny)
            db.SaveChanges();
    }
}
