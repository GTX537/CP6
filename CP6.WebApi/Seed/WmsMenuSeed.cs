using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>
/// WMS 倉庫管理 菜单启动幂等种子（M-WMS 横切接线 Task 2）。
///
/// 背景：WMS 39 页菜单原先只存在于手工 <c>docs/seeds/wms-menu-seed.sql</c>（未接入启动链），
/// 洁净部署时 <c>Sys_Menus</c> 无 WMS 行 → 菜单不可达（横切接线规范 §三「没有菜单种子的页面=暗物质」）。
/// 本种子把 400 段 WMS 菜单接入 Program.cs 启动链，逐行 <c>if (!Any(MenuId==X))</c> 幂等，
/// 并给 30 个权限资源键各指定**一个锚定菜单行**显式设 <see cref="Sys_Menu.MenuKey"/>
/// （对齐 <c>docs/seeds/wms-permission-keys.md</c> 的 30 个连字符键；供 T3 种 Sys_RoleAction）。
///
/// MenuKey 策略（关键）：
///  - **锚定行**显式设 MenuKey = T1 权限键。**绝不能靠 Program.cs 的 RoutePath 自动回填**
///    （<c>RoutePath.Replace('/','-')</c>）——它会把 8 个键拆碎（如 /wms/stock-take→wms-stock-take
///    ≠ 键 wms-stocktake；/wms/inspection→wms-inspection ≠ wms-qc-inspection；kit/plate-mold-stock/
///    ink-lot/mobile-task/iot-monitor 同理）。故锚定行一律显式。
///  - **非锚定内容页**（一覧/列表页、product-inbound/shipping/picking/packaging/dashboard/report-center
///    /bridge-health）MenuKey 留 null：本方法在 Program.cs 的 RoutePath 自动回填块**之前**调用，
///    同一次启动内这些 null 行会被自动回填出各自 RoutePath 派生键（如 wms-inbound-order-list），
///    它们不承载权限（无 Sys_RoleAction 引用），与锚定键无冲突。
///  - **父/分组行**（400/420/440/460/480）RoutePath 为 null，MenuKey 恒 null。
///
/// 幂等：重复启动不重复插入（MenuId 判存守卫）。RoleMenu 默认授管理员（RoleId=1，默认租户由拦截器盖章）。
/// </summary>
public static class WmsMenuSeed
{
    /// <summary>
    /// 菜单定义：(MenuId, MenuName, RoutePath, Icon, ParentId, OrderNo, MenuKey)。
    /// MenuKey 非 null 者即 30 个权限锚定行；null 者为非锚定内容页/分组行。
    /// MenuId 400 段与 <c>docs/seeds/wms-menu-seed.sql</c> 一致；新增 429=在庫QC（wms-stock-qc）。
    /// </summary>
    private static readonly (int Id, string Name, string? Route, string Icon, int? Parent, int Order, string? Key)[] Rows =
    {
        // ── 親（Top） ───────────────────────────────────────────
        (400, "倉庫管理(WMS)",        null,                        "Box",            null, 400, null),

        // ── コア機能 (WM-1~4)  401~419 ─────────────────────────
        (401, "倉庫マスタ",           "/wms/warehouse",            "OfficeBuilding", 400, 401, "wms-warehouse"),
        (402, "ロケーション管理",     "/wms/location",             "Place",          400, 402, "wms-location"),
        (403, "在庫照会",             "/wms/stock",                "Search",         400, 403, "wms-stock"),
        (404, "入庫予定 一覧",        "/wms/inbound-order-list",   "List",           400, 404, null),
        (405, "入庫予定 登録",        "/wms/inbound-order",        "DocumentAdd",    400, 405, "wms-inbound-order"),
        (406, "入庫実績 入力",        "/wms/inbound-receipt",      "TakeawayBox",    400, 406, "wms-inbound-receipt"),
        (407, "出庫指示 一覧",        "/wms/outbound-order-list",  "Tickets",        400, 407, null),
        (408, "出庫指示 登録",        "/wms/outbound-order",       "EditPen",        400, 408, "wms-outbound-order"),
        (409, "製品入庫",             "/wms/product-inbound",      "Goods",          400, 409, null),
        (410, "出荷指示 一覧",        "/wms/shipping-order-list",  "Files",          400, 410, null),
        (411, "出荷指示 登録",        "/wms/shipping-order",       "Promotion",      400, 411, null),
        (412, "ピッキング作業",       "/wms/picking",              "Pointer",        400, 412, null),
        (413, "梱包・出荷確定",       "/wms/packaging",            "Suitcase",       400, 413, null),
        (414, "棚卸 一覧",            "/wms/stock-take-list",      "Coordinate",     400, 414, null),
        (415, "棚卸 作業",            "/wms/stock-take",           "Operation",      400, 415, "wms-stocktake"),
        (416, "WMSダッシュボード",    "/wms/dashboard",            "DataAnalysis",   400, 416, null),
        (417, "材料欠品管理",         "/wms/material-shortage",    "Warning",        400, 417, "wms-material-shortage"),
        // 429=在庫QC（品質保留/放行）：StockQcController（/api/wms/stock-qc）原无菜单行，T1 标注【菜单缺】。
        // MenuId 418 已被 backlog 的波次拣货计划保留，故取 429；OrderNo=418 使其在 在庫 コア簇内显示。
        (429, "在庫QC(保留/放行)",    "/wms/stock-qc",             "CircleCheck",    400, 418, "wms-stock-qc"),
        (419, "出庫ルーティング",     "/wms/outbound-routing",     "Switch",         400, 419, "wms-outbound-routing"),

        // ── 拡張機能 (WM-5~7)  420(parent) 421~428 ─────────────
        (420, "WMS 拡張機能",         null,                        "Setting",        400, 420, null),
        (421, "入荷検品(QC)",         "/wms/inspection",           "CircleCheck",    420, 421, "wms-qc-inspection"),
        (422, "スロッティング最適化", "/wms/slotting",             "MagicStick",     420, 422, "wms-slotting"),
        (423, "補充指示",             "/wms/replenish",            "Refresh",        420, 423, "wms-replenish"),
        (424, "クロスドッキング",     "/wms/cross-dock",           "Connection",     420, 424, "wms-cross-dock"),
        (425, "キッティング・組立",   "/wms/kit",                  "Box",            420, 425, "wms-kitting"),
        (426, "返品管理(RMA)",        "/wms/rma",                  "RefreshLeft",    420, 426, "wms-rma"),
        (427, "ロット追溯・回収",     "/wms/lot-trace",            "Share",          420, 427, "wms-lot-trace"),
        (428, "賞味期限管理(FEFO)",   "/wms/expiry",               "Timer",          420, 428, "wms-expiry"),

        // ── 業界特化(紙器) (WM-8~10)  440(parent) 441~447 ──────
        (440, "業界特化(紙器)",       null,                        "Postcard",       400, 440, null),
        (441, "原紙ロール管理",       "/wms/paper-roll",           "Notebook",       440, 441, "wms-paper-roll"),
        (442, "残材・端材管理",       "/wms/remnant",              "Scissor",        440, 442, "wms-remnant"),
        (443, "印版・木型倉庫",       "/wms/plate-mold-stock",     "Stamp",          440, 443, "wms-plate-mold"),
        (444, "インキ・接着剤管理",   "/wms/ink-lot",              "Brush",          440, 444, "wms-ink"),
        (445, "パレット管理",         "/wms/pallet",               "Grid",           440, 445, "wms-pallet"),
        (446, "客先預り在庫(VMI)",    "/wms/vmi",                  "Handshake",      440, 446, "wms-vmi"),
        (447, "試作・サンプル在庫",   "/wms/sample-stock",         "Present",        440, 447, "wms-sample-stock"),

        // ── 連携・モバイル (WM-11~13)  460(parent) 461~464 ─────
        (460, "連携・モバイル",       null,                        "Link",           400, 460, null),
        (461, "モバイル作業指示",     "/wms/mobile-task",          "Iphone",         460, 461, "wms-mobile"),
        (462, "WCS/自動倉庫連携",     "/wms/wcs-task",             "Cpu",            460, 462, "wms-wcs-task"),
        (463, "配送業者連携",         "/wms/carrier",              "Van",            460, 463, "wms-carrier"),
        (464, "IoT温湿度モニタ",      "/wms/iot-monitor",          "Sunrise",        460, 464, "wms-iot"),

        // ── 帳票分析 (WM-14)  480(parent) 481~483 ──────────────
        (480, "帳票分析",             null,                        "PieChart",       400, 480, null),
        (481, "帳票センター",         "/wms/report-center",        "Printer",        480, 481, null),
        (482, "連携ヘルス監視",       "/wms/bridge-health",        "Monitor",        480, 482, null),
        (483, "在庫滞留レポート",     "/wms/stock-dwell",          "TrendCharts",    480, 483, "wms-stock-dwell"),
    };

    /// <summary>
    /// 幂等播种全部 WMS 菜单 + 授管理员（RoleId=1）。
    /// 须在 Program.cs 的「无 MenuKey 菜单 RoutePath 自动回填」块**之前**调用，
    /// 以便同一启动内非锚定行获派生键（锚定行 MenuKey 非 null，不受回填影响）。
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

        // 锚定键防御：若既有库中锚定行的 MenuKey 因历史 RoutePath 自动回填被写成派生键（如 wms-stock-take、
        // wms-inspection、wms-kit、wms-ink-lot、wms-mobile-task、wms-iot-monitor、wms-plate-mold-stock），
        // 就地矫正为 T1 权威键。仅矫正 30 个锚定行，非锚定行不动。幂等（已正确即跳过）。
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
