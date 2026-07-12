/* ============================================================
 * MSBBPA 販売管理(ERP) メニュー MenuKey 锚定 + 孤儿路由收编 シード
 * ============================================================
 * 対象 DB      : CP6 (SQL Server)
 * 対象テーブル  : Sys_Menus / Sys_RoleMenus
 * 親メニューID  : 200（販売管理 ERP）
 *
 * 接入正本 = C# 起動シード `CP6.WebApi/Seed/ErpMenuSeed.cs`（起動毎冪等、
 * Program.cs の RoutePath 自動回填ブロックより前に実行）。本 SQL は文書対照用
 * （C# 正本と一致）。手動投入時は SSMS / sqlcmd で実行。
 *
 * 内容:
 *   (A) 既有 201–215 の 9 錨定行に erp-* MenuKey を明示 UPDATE（一覧页は null 据え置き）。
 *   (B) 孤儿路由 216–220 の 5 行を Sys_Menus へ INSERT（erp-* MenuKey 付き）+ 管理者(RoleId=1)へ RoleMenu 付与。
 *
 * 冪等性: UPDATE は値一致で無害再実行可；INSERT は NOT EXISTS ガード。
 *
 * ★MenuKey 唯一制約: Sys_Menus.MenuKey に IS NOT NULL フィルタ付きユニークインデックス有り。
 *   一域两页は「登録页」1 行のみ錨定し、一覧页 MenuKey は null（唯一键衝突回避）。
 * ============================================================ */

/* ── (A) 既有 201–215：9 錨定行に erp-* MenuKey 明示（一覧页 201/203/205/207/211/214 は据え置き） ── */
UPDATE Sys_Menus SET MenuKey = N'erp-estimate-calc'          WHERE MenuId = 202;
UPDATE Sys_Menus SET MenuKey = N'erp-quotation'              WHERE MenuId = 204;
UPDATE Sys_Menus SET MenuKey = N'erp-product'                WHERE MenuId = 206;
UPDATE Sys_Menus SET MenuKey = N'erp-order'                  WHERE MenuId = 208;
UPDATE Sys_Menus SET MenuKey = N'erp-order-price-correction' WHERE MenuId = 209;
UPDATE Sys_Menus SET MenuKey = N'erp-fsc-checklist'          WHERE MenuId = 210;
UPDATE Sys_Menus SET MenuKey = N'erp-business-partner'       WHERE MenuId = 212;
UPDATE Sys_Menus SET MenuKey = N'erp-sheet-unit-price'       WHERE MenuId = 213;
UPDATE Sys_Menus SET MenuKey = N'erp-plate-mold'             WHERE MenuId = 215;

/* ── (B) 孤儿路由 216–220 収編（RoutePath は cp6.web/src/router/index.ts の /erp/* と一致） ── */
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 216)
    INSERT INTO Sys_Menus (MenuId, MenuName, RoutePath, MenuKey, Icon, ParentId, OrderNo, Enable)
    VALUES (216, N'受注トレース',         N'/erp/order-trace', N'erp-order-trace', N'Search',      200, 216, 1);
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 217)
    INSERT INTO Sys_Menus (MenuId, MenuName, RoutePath, MenuKey, Icon, ParentId, OrderNo, Enable)
    VALUES (217, N'クレジットノート照会', N'/erp/credit-note', N'erp-credit-note', N'Document',    200, 217, 1);
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 218)
    INSERT INTO Sys_Menus (MenuId, MenuName, RoutePath, MenuKey, Icon, ParentId, OrderNo, Enable)
    VALUES (218, N'欠品・残数管理',       N'/erp/backorder',   N'erp-backorder',   N'Warning',     200, 218, 1);
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 219)
    INSERT INTO Sys_Menus (MenuId, MenuName, RoutePath, MenuKey, Icon, ParentId, OrderNo, Enable)
    VALUES (219, N'OTD納期遵守レポート',  N'/erp/otd-report',  N'erp-otd-report',  N'TrendCharts', 200, 219, 1);
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 220)
    INSERT INTO Sys_Menus (MenuId, MenuName, RoutePath, MenuKey, Icon, ParentId, OrderNo, Enable)
    VALUES (220, N'為替レートマスタ',     N'/erp/fx-rate',     N'erp-fx-rate',     N'Money',       200, 220, 1);

/* ── 管理者(RoleId=1) へ孤儿菜单を付与（既定テナント; マルチテナントは各テナント TenantId 明示要） ── */
IF NOT EXISTS (SELECT 1 FROM Sys_RoleMenus WHERE RoleId = 1 AND MenuId = 216)
    INSERT INTO Sys_RoleMenus (RoleId, MenuId) VALUES (1, 216);
IF NOT EXISTS (SELECT 1 FROM Sys_RoleMenus WHERE RoleId = 1 AND MenuId = 217)
    INSERT INTO Sys_RoleMenus (RoleId, MenuId) VALUES (1, 217);
IF NOT EXISTS (SELECT 1 FROM Sys_RoleMenus WHERE RoleId = 1 AND MenuId = 218)
    INSERT INTO Sys_RoleMenus (RoleId, MenuId) VALUES (1, 218);
IF NOT EXISTS (SELECT 1 FROM Sys_RoleMenus WHERE RoleId = 1 AND MenuId = 219)
    INSERT INTO Sys_RoleMenus (RoleId, MenuId) VALUES (1, 219);
IF NOT EXISTS (SELECT 1 FROM Sys_RoleMenus WHERE RoleId = 1 AND MenuId = 220)
    INSERT INTO Sys_RoleMenus (RoleId, MenuId) VALUES (1, 220);
