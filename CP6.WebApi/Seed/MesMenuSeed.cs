using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>
/// MES 製造執行 菜单启动幂等种子（M-MES 横切接线 Task 2）。
///
/// 背景（头号命门·回填时序）：MES 300 段菜单（300–315）由 Program.cs 播种，但插入块位于
/// 「无 MenuKey 菜单 RoutePath 自动回填」块（<c>MenuKey = RoutePath.Trim('/').Replace('/','-')</c>）
/// **之后**，且 Add 时**均未设 MenuKey**。洁净部署首启：回填块先跑（MES 菜单尚不存在）→ 跳过；MES 菜单随后
/// 插入 MenuKey 留 null → <see cref="Sys_Menu.MenuKey"/> 为 null 被 PermissionAggregator 过滤掉 → MES action
/// 键无法 join 出 → 首启即 fail-closed 403，需二次重启回填才生效。本种子在回填块**之前**调用，把 10 个
/// 有菜单的权限键各锚定到**一个**菜单行显式设 MenuKey=<c>mes-*</c>（含缺行补建），首启即生效。
///
/// 命门2·machine 键回填错配：菜单 310 RoutePath=<c>/mes/machine-list</c>，回填得 <c>mes-machine-list</c>
/// ≠ 真相源 <c>docs/seeds/mes-permission-keys.md</c> §二 的 <c>mes-machine</c>。若依赖回填，Machine 全键
/// （add/edit/del/status/downtime）锚定到错误键 → 与 T3 贴的 <c>mes-machine:*</c> 对不上 → Machine 全 403。
/// 本种子对 310 显式赋 <c>MenuKey="mes-machine"</c>（RoutePath 不动），并由防御矫正块把已被历史回填写坏的
/// 行就地纠回。
///
/// MenuKey 策略（与 ErpMenuSeed/WmsMenuSeed 一致）：
///  - **锚定行**显式设 MenuKey = T1 权威键，**绝不靠 RoutePath 自动回填**。
///  - **一域两页（入力 + 一覧）只锚定「入力/主操作页」一行**，一覧页（303/305/307）MenuKey 留 null
///    （由 Program.cs 回填得 RoutePath 派生 <c>*-list</c> 键，不承载权限、无 RoleAction 引用）。
///    GET-only 看板（309 dashboard / 312 control-tower）同理留 null。
///    ★这是**硬约束**：<c>Sys_Menus.MenuKey</c> 有 <c>IS NOT NULL</c> 过滤唯一索引
///    （HasIndex("MenuKey").IsUnique().HasFilter），两行共赋同一非空 MenuKey 会撞唯一键。
///    故必须一键一锚定行（真相源 §二：MES 因回填按 RoutePath 天然差异化，10 锚定键互不相同）。
///  - MES 无孤儿路由（T1 已证 15 前端页全映射到 300 段菜单），本种子纯锚定、无收编。
///
/// 幂等：MenuId 判存守卫不重复插入；防御矫正块把既有库中被历史回填写坏的 10 锚定行 MenuKey 就地纠回
/// <c>mes-*</c>（作用域严限 10 锚定行 r.Key != null；一覧/看板/父行不动）。RoleMenu 默认授管理员
/// （RoleId=1，默认租户由 SaveChanges 拦截器盖章 TenantId；逐租户传播由 Program.cs「首次补建管理员角色」块负责）。
/// </summary>
public static class MesMenuSeed
{
    /// <summary>
    /// 菜单定义：(MenuId, MenuName, RoutePath, Icon, ParentId, OrderNo, MenuKey)。
    /// MenuKey 非 null 者即 10 个权限锚定行；null 者为父行/一覧页/GET-only 看板（不承载权限）。
    /// 300 段既有 300–315 已由 Program.cs 播种，本表含之仅为 (a) 显式锚定 MenuKey (b) 部分部署缺行时补建。
    /// RoutePath 与 Program.cs 现有 MES 菜单块逐字一致（310 保持 /mes/machine-list）。
    /// </summary>
    private static readonly (int Id, string Name, string? Route, string Icon, int? Parent, int Order, string? Key)[] Rows =
    {
        // ── 親（Top） ───────────────────────────────────────────
        (300, "製造執行(MES)",        null,                            "SetUp",       null, 300, null),

        // ── 生産計画ボード（单页，锚定） ─────────────────────────
        (301, "生産計画ボード",        "/mes/planning-board",           "Calendar",    300, 301, "mes-planning-board"),

        // ── 製造指図（入力锚定 / 一覧留 null） ───────────────────
        (302, "製造指図 入力",         "/mes/work-order",               "DocumentAdd", 300, 302, "mes-work-order"),
        (303, "製造指図 一覧",         "/mes/work-order-list",          "Files",       300, 303, null),

        // ── 製造実績（入力锚定 / 一覧留 null） ───────────────────
        (304, "製造実績 入力",         "/mes/production-result",        "EditPen",     300, 304, "mes-production-result"),
        (305, "製造実績 一覧",         "/mes/production-result-list",   "DataLine",    300, 305, null),

        // ── 品質検査（入力锚定 / 一覧留 null；键取菜单域名 quality-inspection，非控制器路由 inspections）─
        (306, "品質検査 入力",         "/mes/quality-inspection",       "Operation",   300, 306, "mes-quality-inspection"),
        (307, "品質検査 一覧",         "/mes/quality-inspection-list",  "Histogram",   300, 307, null),

        // ── 不良品管理（单页，锚定） ─────────────────────────────
        (308, "不良品管理",           "/mes/defect",                   "Warning",     300, 308, "mes-defect"),

        // ── GET-only 看板（留 null，不承载权限） ─────────────────
        (309, "MESダッシュボード",     "/mes/dashboard",                "PieChart",    300, 309, null),

        // ── 設備管理（★命门2：RoutePath /mes/machine-list，显式赋 mes-machine 而非回填 mes-machine-list）─
        (310, "設備管理",             "/mes/machine-list",             "Monitor",     300, 310, "mes-machine"),

        // ── OEE 分析（单页，锚定） ───────────────────────────────
        (311, "OEE 分析",            "/mes/oee",                      "TrendCharts", 300, 311, "mes-oee"),

        // ── Control Tower 大屏（GET-only 可视化，留 null） ───────
        (312, "Control Tower 大屏",   "/mes/control-tower",            "Aim",         300, 312, null),

        // ── 生産計画達成率（仅 view，锚定） ──────────────────────
        (313, "生産計画達成率",        "/mes/plan-achievement",         "DataLine",    300, 313, "mes-plan-achievement"),

        // ── 工作中心 / 工序费率 主数据（锚定） ───────────────────
        (314, "工作中心",             "/mes/work-center",              "Setting",     300, 314, "mes-work-center"),
        (315, "工序费率",             "/mes/process-cost-rate",        "Money",       300, 315, "mes-process-cost-rate"),
    };

    /// <summary>
    /// 幂等播种全部 MES 菜单 + 授管理员（RoleId=1）+ 锚定行 MenuKey 显式矫正。
    /// 须在 Program.cs 的「无 MenuKey 菜单 RoutePath 自动回填」块**之前**调用。
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

        // 锚定键防御：既有 300 段缺 MenuKey 时会被历史 RoutePath 回填写成派生键（310→mes-machine-list…），
        // 就地矫正为 T1 权威键。作用域严限 10 个锚定行（r.Key != null），一覧页/看板/父行不动。幂等（已正确即跳过）。
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
