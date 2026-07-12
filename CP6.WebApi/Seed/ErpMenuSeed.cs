using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>
/// ERP 販売管理 菜单启动幂等种子（M-ERP 横切接线 Task 2）。
///
/// 背景（头号命门）：ERP 菜单 200 段（201–215）早已由 Program.cs 播种，但**均缺 MenuKey 且 RoutePath 为裸路径**
/// （<c>/order</c>、<c>/product</c>、<c>/estimate-calc</c>…无 <c>erp/</c> 段）。Program.cs 的「无 MenuKey 菜单
/// RoutePath 自动回填」块（<c>MenuKey = RoutePath.Trim('/').Replace('/','-')</c>）会把它们回填成
/// <c>order</c>/<c>product</c>/<c>estimate-calc</c>… **无 <c>erp-</c> 前缀**，与真相源
/// <c>docs/seeds/erp-permission-keys.md</c> 的 <c>erp-*</c> 键全体失配 → 整个 ERP fail-closed 403。
/// 本种子把 9 个有菜单的权限键各锚定到**一个**菜单行显式设 <see cref="Sys_Menu.MenuKey"/>=<c>erp-*</c>，
/// 并收编 5 条孤儿路由（order-trace/credit-note/backorder/otd-report/fx-rate）补 Sys_Menu 行。
/// 合计 14 个 <c>erp-*</c> menu-key 全部落定锚定 MenuId（供 T3b 种 Sys_RoleAction）。
///
/// MenuKey 策略（关键，与 WmsMenuSeed 一致）：
///  - **锚定行**显式设 MenuKey = T1 权威键，**绝不靠 RoutePath 自动回填**。
///  - **一域两页（一覧 + 登録）只锚定「登録/主操作页」一行**，一覧页 MenuKey 留 null
///    （由 Program.cs 回填得 RoutePath 派生键，不承载权限、无 RoleAction 引用）。
///    ★这是**硬约束**：<c>Sys_Menus.MenuKey</c> 有 <c>IS NOT NULL</c> 过滤唯一索引
///    （见 CP6ContextModelSnapshot: HasIndex("MenuKey").IsUnique().HasFilter），
///    两行共赋同一非空 MenuKey 会撞唯一键。故必须一键一锚定行（对齐 WMS inbound-order：
///    405 登録锚定 wms-inbound-order、404 一覧留 null）。
///  - 本方法在 Program.cs 的回填块**之前**调用：同一启动内非锚定 ERP 一覧页（null key）获派生键，
///    而锚定行 MenuKey 非 null 不受回填影响。
///
/// 幂等：MenuId 判存守卫不重复插入；防御矫正块把既有库中被历史回填写坏的锚定行 MenuKey 就地纠回
/// <c>erp-*</c>（仅矫正 9 个锚定行 + 5 孤儿行，一覧/父行不动）。RoleMenu 默认授管理员（RoleId=1，
/// 默认租户由 SaveChanges 拦截器盖章 TenantId；逐租户传播由 Program.cs「首次补建管理员角色」块负责）。
/// </summary>
public static class ErpMenuSeed
{
    /// <summary>
    /// 菜单定义：(MenuId, MenuName, RoutePath, Icon, ParentId, OrderNo, MenuKey)。
    /// MenuKey 非 null 者即 14 个权限锚定行（9 既有 + 5 孤儿）；null 者为一覧页/父行（不承载权限）。
    /// 200 段既有 201–215 已由 Program.cs 播种，本表含之仅为 (a) 显式锚定 MenuKey (b) 部分部署缺行时补建。
    /// 孤儿行取号 216–220：ERP 现用 200–215，216–229 段经全仓扫描无占用（backlog/其他模块均不占），
    /// 故取 215 之后最近连续段 216–220，OrderNo 同值使其紧随既有 ERP 菜单显示。
    /// </summary>
    private static readonly (int Id, string Name, string? Route, string Icon, int? Parent, int Order, string? Key)[] Rows =
    {
        // ── 親（Top） ───────────────────────────────────────────
        (200, "販売管理(ERP)",          null,                        "ShoppingBag",    null, 200, null),

        // ── 既有 201–215：一域两页只锚定「登録/主操作页」，一覧页 MenuKey 留 null ─────
        (201, "見積計算書 照会",         "/estimate-calc-list",       "List",           200, 201, null),
        (202, "見積計算書 登録",         "/estimate-calc",            "Money",          200, 202, "erp-estimate-calc"),
        (203, "御見積書 一覧",           "/quotation-list",           "Tickets",        200, 203, null),
        (204, "御見積書 登録",           "/quotation",                "EditPen",        200, 204, "erp-quotation"),
        (205, "製品マスタ 一覧",         "/product-list",             "Goods",          200, 205, null),
        (206, "製品マスタ 登録",         "/product",                  "Box",            200, 206, "erp-product"),
        (207, "受注一覧照会",            "/order-list",               "Files",          200, 207, null),
        (208, "受注入力",                "/order",                    "DocumentAdd",    200, 208, "erp-order"),
        (209, "単価訂正",                "/order-price-correction",   "PriceTag",       200, 209, "erp-order-price-correction"),
        (210, "FSC チェックシート",      "/fsc-checklist",            "Document",       200, 210, "erp-fsc-checklist"),
        (211, "取引先マスタ 一覧",       "/business-partner-list",    "OfficeBuilding", 200, 211, null),
        (212, "取引先マスタ 登録",       "/business-partner",         "User",           200, 212, "erp-business-partner"),
        (213, "シート単価マスタ",        "/sheet-unit-price",         "Coin",           200, 213, "erp-sheet-unit-price"),
        (214, "版型/木型 一覧",          "/plate-mold-list",          "Tools",          200, 214, null),
        (215, "版型/木型 登録",          "/plate-mold",               "Stamp",          200, 215, "erp-plate-mold"),

        // ── 孤儿路由收编 216–220（原计划 T4 并入）：前端路由已注册但无 Sys_Menu 行 ─────
        // RoutePath 与 cp6.web/src/router/index.ts 的 /erp/* 精确一致（回填派生亦得 erp-*，此处仍显式锚定）。
        (216, "受注トレース",            "/erp/order-trace",          "Search",         200, 216, "erp-order-trace"),
        (217, "クレジットノート照会",    "/erp/credit-note",          "Document",       200, 217, "erp-credit-note"),
        (218, "欠品・残数管理",          "/erp/backorder",            "Warning",        200, 218, "erp-backorder"),
        (219, "OTD納期遵守レポート",     "/erp/otd-report",           "TrendCharts",    200, 219, "erp-otd-report"),
        (220, "為替レートマスタ",        "/erp/fx-rate",              "Money",          200, 220, "erp-fx-rate"),
    };

    /// <summary>
    /// 幂等播种全部 ERP 菜单 + 授管理员（RoleId=1）+ 锚定行 MenuKey 显式矫正。
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

        // 锚定键防御：既有 201–215 缺 MenuKey 时会被历史 RoutePath 回填写成裸派生键（order/product/
        // estimate-calc…无 erp- 前缀），就地矫正为 T1 权威键。仅矫正 14 个锚定行（r.Key != null），
        // 一覧页/父行不动。幂等（已正确即跳过）。
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
