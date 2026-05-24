/* ============================================================
 * MSBBWM WMS メニュー シードデータ
 * ============================================================
 * 対象 DB     : CP6 (SQL Server)
 * 対象テーブル : Sys_Menus / Sys_RoleMenus
 * 親メニューID : 400（WMS 倉庫管理）
 *
 * ID 採番ルール（既存と衝突しない）:
 *   100~199 : システム管理
 *   200~299 : 販売管理 (PA)
 *   300~399 : 製造執行 (MES)
 *   400~499 : 倉庫管理 (WMS) ← 本ファイル
 *
 * 階層構成:
 *   400 WMS 倉庫管理
 *   ├ 401~419 コア機能 (Phase WM-1~4)
 *   ├ 420 拡張機能 (parent)
 *   │  └ 421~428 (Phase WM-5~7)
 *   ├ 440 業界特化 (parent)
 *   │  └ 441~447 紙器包装業 (Phase WM-8~10)
 *   ├ 460 連携・モバイル (parent)
 *   │  └ 461~464 (Phase WM-11~13)
 *   └ 480 帳票分析 (parent)
 *      └ 481 (Phase WM-14)
 *
 * 実行方法:
 *   sqlcmd -S localhost -d CP6 -U sa -P <pw> -i wms-menu-seed.sql
 * 又は SSMS / Azure Data Studio で本ファイルを開いて実行。
 *
 * 冪等性:
 *   各 INSERT 前に NOT EXISTS チェック → 重複実行しても安全。
 *
 * 権限割当:
 *   既定で RoleId=1 (管理者) に全 WMS メニューを付与。
 *   他ロール（例: WMS 作業者）への一括割当が必要な場合は
 *   末尾のサンプル UPDATE を参考にカスタマイズ。
 * ============================================================ */

SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;

PRINT '=== WMS メニュー シード開始 ===';

/* ------------------------------------------------------------
 * 1. 親メニュー (Top)
 * ------------------------------------------------------------ */
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 400)
    INSERT INTO Sys_Menus (MenuId, MenuName, RoutePath, Icon, ParentId, OrderNo, Enable, CreateDate)
    VALUES (400, N'倉庫管理(WMS)', NULL, N'Box', NULL, 400, 1, SYSDATETIME());

/* ------------------------------------------------------------
 * 2. コア機能 (Phase WM-1 ~ WM-4)  MenuId 401~419
 * ------------------------------------------------------------ */
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 401)
    INSERT INTO Sys_Menus VALUES (401, N'倉庫マスタ',           N'/wms/warehouse',          N'OfficeBuilding', 400, 401, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 402)
    INSERT INTO Sys_Menus VALUES (402, N'ロケーション管理',     N'/wms/location',           N'Place',          400, 402, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 403)
    INSERT INTO Sys_Menus VALUES (403, N'在庫照会',             N'/wms/stock',              N'Search',         400, 403, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 404)
    INSERT INTO Sys_Menus VALUES (404, N'入庫予定 一覧',        N'/wms/inbound-order-list', N'List',           400, 404, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 405)
    INSERT INTO Sys_Menus VALUES (405, N'入庫予定 登録',        N'/wms/inbound-order',      N'DocumentAdd',    400, 405, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 406)
    INSERT INTO Sys_Menus VALUES (406, N'入庫実績 入力',        N'/wms/inbound-receipt',    N'TakeawayBox',    400, 406, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 407)
    INSERT INTO Sys_Menus VALUES (407, N'出庫指示 一覧',        N'/wms/outbound-order-list',N'Tickets',        400, 407, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 408)
    INSERT INTO Sys_Menus VALUES (408, N'出庫指示 登録',        N'/wms/outbound-order',     N'EditPen',        400, 408, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 409)
    INSERT INTO Sys_Menus VALUES (409, N'製品入庫',             N'/wms/product-inbound',    N'Goods',          400, 409, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 410)
    INSERT INTO Sys_Menus VALUES (410, N'出荷指示 一覧',        N'/wms/shipping-order-list',N'Files',          400, 410, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 411)
    INSERT INTO Sys_Menus VALUES (411, N'出荷指示 登録',        N'/wms/shipping-order',     N'Promotion',      400, 411, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 412)
    INSERT INTO Sys_Menus VALUES (412, N'ピッキング作業',       N'/wms/picking',            N'Pointer',        400, 412, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 413)
    INSERT INTO Sys_Menus VALUES (413, N'梱包・出荷確定',       N'/wms/packaging',          N'Suitcase',       400, 413, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 414)
    INSERT INTO Sys_Menus VALUES (414, N'棚卸 一覧',            N'/wms/stock-take-list',    N'Coordinate',     400, 414, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 415)
    INSERT INTO Sys_Menus VALUES (415, N'棚卸 作業',            N'/wms/stock-take',         N'Operation',      400, 415, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 416)
    INSERT INTO Sys_Menus VALUES (416, N'WMSダッシュボード',    N'/wms/dashboard',          N'DataAnalysis',   400, 416, 1, SYSDATETIME());

/* ------------------------------------------------------------
 * 3. 拡張機能 (Phase WM-5 ~ WM-7)  MenuId 420~439
 * ------------------------------------------------------------ */
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 420)
    INSERT INTO Sys_Menus VALUES (420, N'WMS 拡張機能',         NULL,                       N'Setting',        400, 420, 1, SYSDATETIME());

IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 421)
    INSERT INTO Sys_Menus VALUES (421, N'入荷検品(QC)',         N'/wms/inspection',         N'CircleCheck',    420, 421, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 422)
    INSERT INTO Sys_Menus VALUES (422, N'スロッティング最適化', N'/wms/slotting',           N'MagicStick',     420, 422, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 423)
    INSERT INTO Sys_Menus VALUES (423, N'補充指示',             N'/wms/replenish',          N'Refresh',        420, 423, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 424)
    INSERT INTO Sys_Menus VALUES (424, N'クロスドッキング',     N'/wms/cross-dock',         N'Connection',     420, 424, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 425)
    INSERT INTO Sys_Menus VALUES (425, N'キッティング・組立',   N'/wms/kit',                N'Box',            420, 425, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 426)
    INSERT INTO Sys_Menus VALUES (426, N'返品管理(RMA)',        N'/wms/rma',                N'RefreshLeft',    420, 426, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 427)
    INSERT INTO Sys_Menus VALUES (427, N'ロット追溯・回収',     N'/wms/lot-trace',          N'Share',          420, 427, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 428)
    INSERT INTO Sys_Menus VALUES (428, N'賞味期限管理(FEFO)',   N'/wms/expiry',             N'Timer',          420, 428, 1, SYSDATETIME());

/* ------------------------------------------------------------
 * 4. 業界特化 (Phase WM-8 ~ WM-10)  MenuId 440~459
 *    紙器包装業向け
 * ------------------------------------------------------------ */
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 440)
    INSERT INTO Sys_Menus VALUES (440, N'業界特化(紙器)',       NULL,                       N'Postcard',       400, 440, 1, SYSDATETIME());

IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 441)
    INSERT INTO Sys_Menus VALUES (441, N'原紙ロール管理',       N'/wms/paper-roll',         N'Notebook',       440, 441, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 442)
    INSERT INTO Sys_Menus VALUES (442, N'残材・端材管理',       N'/wms/remnant',            N'Scissor',        440, 442, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 443)
    INSERT INTO Sys_Menus VALUES (443, N'印版・木型倉庫',       N'/wms/plate-mold-stock',   N'Stamp',          440, 443, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 444)
    INSERT INTO Sys_Menus VALUES (444, N'インキ・接着剤管理',   N'/wms/ink-lot',            N'Brush',          440, 444, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 445)
    INSERT INTO Sys_Menus VALUES (445, N'パレット管理',         N'/wms/pallet',             N'Grid',           440, 445, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 446)
    INSERT INTO Sys_Menus VALUES (446, N'客先預り在庫(VMI)',    N'/wms/vmi',                N'Handshake',      440, 446, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 447)
    INSERT INTO Sys_Menus VALUES (447, N'試作・サンプル在庫',   N'/wms/sample-stock',       N'Present',        440, 447, 1, SYSDATETIME());

/* ------------------------------------------------------------
 * 5. 連携・モバイル (Phase WM-11 ~ WM-13)  MenuId 460~479
 * ------------------------------------------------------------ */
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 460)
    INSERT INTO Sys_Menus VALUES (460, N'連携・モバイル',       NULL,                       N'Link',           400, 460, 1, SYSDATETIME());

IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 461)
    INSERT INTO Sys_Menus VALUES (461, N'モバイル作業指示',     N'/wms/mobile-task',        N'Iphone',         460, 461, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 462)
    INSERT INTO Sys_Menus VALUES (462, N'WCS/自動倉庫連携',     N'/wms/wcs-task',           N'Cpu',            460, 462, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 463)
    INSERT INTO Sys_Menus VALUES (463, N'配送業者連携',         N'/wms/carrier',            N'Van',            460, 463, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 464)
    INSERT INTO Sys_Menus VALUES (464, N'IoT温湿度モニタ',      N'/wms/iot-monitor',        N'Sunrise',        460, 464, 1, SYSDATETIME());

/* ------------------------------------------------------------
 * 6. 帳票分析 (Phase WM-14)  MenuId 480~499
 * ------------------------------------------------------------ */
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 480)
    INSERT INTO Sys_Menus VALUES (480, N'帳票分析',             NULL,                       N'PieChart',       400, 480, 1, SYSDATETIME());

IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 481)
    INSERT INTO Sys_Menus VALUES (481, N'帳票センター',         N'/wms/report-center',      N'Printer',        480, 481, 1, SYSDATETIME());

/* ------------------------------------------------------------
 * 7. 管理者ロール (RoleId=1) に全 WMS メニューを付与
 * ------------------------------------------------------------ */
INSERT INTO Sys_RoleMenus (RoleId, MenuId)
SELECT 1, m.MenuId
FROM Sys_Menus m
WHERE m.MenuId BETWEEN 400 AND 499
  AND NOT EXISTS (
      SELECT 1 FROM Sys_RoleMenus rm
      WHERE rm.RoleId = 1 AND rm.MenuId = m.MenuId
  );

/* ------------------------------------------------------------
 * 8. 結果確認
 * ------------------------------------------------------------ */
DECLARE @MenuCount INT, @RoleMenuCount INT;
SELECT @MenuCount    = COUNT(*) FROM Sys_Menus     WHERE MenuId BETWEEN 400 AND 499;
SELECT @RoleMenuCount = COUNT(*) FROM Sys_RoleMenus WHERE MenuId BETWEEN 400 AND 499 AND RoleId = 1;

PRINT N'  WMS メニュー件数         : ' + CAST(@MenuCount AS NVARCHAR(10));
PRINT N'  管理者ロール付与件数     : ' + CAST(@RoleMenuCount AS NVARCHAR(10));

COMMIT TRANSACTION;
PRINT '=== WMS メニュー シード完了 ===';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT 'ERROR: ' + ERROR_MESSAGE();
    THROW;
END CATCH;
GO

/* ============================================================
 * 9. 動作確認クエリ（実行後の検証用、任意）
 * ============================================================ */
-- WMS メニュー階層を表示
SELECT
    REPLICATE(N'  ', CASE WHEN m.ParentId IS NULL THEN 0
                          WHEN m.ParentId = 400 THEN 1 ELSE 2 END)
        + CAST(m.MenuId AS NVARCHAR(10)) + N' ' + m.MenuName AS MenuTree,
    m.RoutePath,
    m.Icon,
    m.OrderNo,
    m.Enable
FROM Sys_Menus m
WHERE m.MenuId BETWEEN 400 AND 499
ORDER BY m.OrderNo;

-- 管理者ロールの WMS メニュー付与状況
SELECT
    rm.RoleId,
    r.RoleName,
    COUNT(rm.MenuId) AS WmsMenuCount
FROM Sys_RoleMenus rm
INNER JOIN Sys_Roles r ON rm.RoleId = r.RoleId
WHERE rm.MenuId BETWEEN 400 AND 499
GROUP BY rm.RoleId, r.RoleName;

/* ============================================================
 * 10. 補足: 他ロールへの付与サンプル
 * ============================================================
 * 例: RoleId=2「WMS作業者」にコア機能のみ付与
 * ------------------------------------------------------------ */
/*
INSERT INTO Sys_RoleMenus (RoleId, MenuId)
SELECT 2, m.MenuId
FROM Sys_Menus m
WHERE m.MenuId BETWEEN 400 AND 419   -- コアのみ
  AND NOT EXISTS (
      SELECT 1 FROM Sys_RoleMenus rm
      WHERE rm.RoleId = 2 AND rm.MenuId = m.MenuId
  );
*/

/* ============================================================
 * 11. ロールバック用（緊急時のみ使用）
 * ============================================================
 * 注意: 関連 Sys_RoleMenus も先に削除されるため、慎重に。
 * ------------------------------------------------------------ */
/*
BEGIN TRANSACTION;
DELETE FROM Sys_RoleMenus WHERE MenuId BETWEEN 400 AND 499;
DELETE FROM Sys_Menus     WHERE MenuId BETWEEN 400 AND 499;
COMMIT TRANSACTION;
*/
