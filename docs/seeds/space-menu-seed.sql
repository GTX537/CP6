/* ============================================================
 * Space 空間管理 メニュー シードデータ（波2 P0）
 * ============================================================
 * 対象 DB     : CP6 (SQL Server)
 * 対象テーブル : Sys_Menus / Sys_RoleMenus
 * 親メニューID : 900（空間管理 Space）
 *
 * ID 採番ルール（既存と衝突しない）:
 *   900~919 : 空間管理 (Space) ← 本ファイル
 *   （100~199 システム / 200~299 販売 / 300~399 MES /
 *     400~499 WMS / 500~599 OA … と重複しない空段を採用）
 *
 * 階層構成:
 *   900 空間管理(Space)
 *   ├ 901 スペースホーム   /space/home
 *   ├ 902 サイト管理       /space/site
 *   └ 903 フロア管理       /space/floor
 *
 * ★ 実行前に必ず下記で 900 段が空いていることを複核すること:
 *     SELECT MenuId FROM Sys_Menus WHERE MenuId BETWEEN 900 AND 919;
 *   （静的検索では 900 段に占用なし・2026-07-06 裁決記録。
 *     もし何らかの占用があれば採番を見直すこと。）
 *
 * 実行方法:
 *   sqlcmd -S localhost\KOUSQLSERVER -d CP6DB -E -f 65001 -i docs/seeds/space-menu-seed.sql -b
 *   又は SSMS / Azure Data Studio で本ファイルを開いて実行。
 *
 * 冪等性:
 *   各 INSERT 前に NOT EXISTS チェック → 重複実行しても安全。
 *
 * 権限割当:
 *   既定で RoleId=1 (管理者) に全 Space メニューを付与（900~919 区間幂等）。
 *
 * Icon:
 *   Element Plus 図標集に実在し、かつ wms/oa 種子で使用実績のある名を採用
 *   （Grid=445パレット / OfficeBuilding=401倉庫マスタ / Files=410出荷指示一覧 と同名。
 *     HomeFilled は Element Plus 標準アイコン）。
 * ============================================================ */

SET NOCOUNT ON;
SET XACT_ABORT ON;
-- sqlcmd/ODBC defaults QUOTED_IDENTIFIER OFF; Sys_Menus 上のフィルター付き索引/計算列 INSERT には ON 必須（Msg 1934 回避）
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
BEGIN TRY
BEGIN TRANSACTION;

PRINT '=== Space メニュー シード開始 ===';

/* ------------------------------------------------------------
 * 1. 親メニュー (Top)  MenuId 900
 * ------------------------------------------------------------ */
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 900)
    INSERT INTO Sys_Menus (MenuId, MenuName, RoutePath, MenuKey, Icon, ParentId, OrderNo, Enable, CreateDate)
    VALUES (900, N'空間管理(Space)', NULL, N'space', N'Grid', NULL, 900, 1, SYSDATETIME());

/* ------------------------------------------------------------
 * 2. 子メニュー  MenuId 901~903
 * ------------------------------------------------------------ */
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 901)
    INSERT INTO Sys_Menus (MenuId, MenuName, RoutePath, MenuKey, Icon, ParentId, OrderNo, Enable, CreateDate)
    VALUES (901, N'スペースホーム', N'/space/home', N'space-home', N'HomeFilled', 900, 901, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 902)
    INSERT INTO Sys_Menus (MenuId, MenuName, RoutePath, MenuKey, Icon, ParentId, OrderNo, Enable, CreateDate)
    VALUES (902, N'サイト管理', N'/space/site', N'space-site', N'OfficeBuilding', 900, 902, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 903)
    INSERT INTO Sys_Menus (MenuId, MenuName, RoutePath, MenuKey, Icon, ParentId, OrderNo, Enable, CreateDate)
    VALUES (903, N'フロア管理', N'/space/floor', N'space-floor', N'Files', 900, 903, 1, SYSDATETIME());

/* ------------------------------------------------------------
 * 3. 管理者ロール (RoleId=1) に全 Space メニューを付与（900~919 幂等）
 * ------------------------------------------------------------ */
INSERT INTO Sys_RoleMenus (RoleId, MenuId)
SELECT 1, m.MenuId
FROM Sys_Menus m
WHERE m.MenuId BETWEEN 900 AND 919
  AND NOT EXISTS (
      SELECT 1 FROM Sys_RoleMenus rm
      WHERE rm.RoleId = 1 AND rm.MenuId = m.MenuId
  );

/* ------------------------------------------------------------
 * 4. 結果確認
 * ------------------------------------------------------------ */
DECLARE @MenuCount INT, @RoleMenuCount INT;
SELECT @MenuCount     = COUNT(*) FROM Sys_Menus     WHERE MenuId BETWEEN 900 AND 919;
SELECT @RoleMenuCount = COUNT(*) FROM Sys_RoleMenus WHERE MenuId BETWEEN 900 AND 919 AND RoleId = 1;

PRINT N'  Space メニュー件数       : ' + CAST(@MenuCount AS NVARCHAR(10));
PRINT N'  管理者ロール付与件数     : ' + CAST(@RoleMenuCount AS NVARCHAR(10));

COMMIT TRANSACTION;
PRINT '=== Space メニュー シード完了 ===';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT 'ERROR: ' + ERROR_MESSAGE();
    THROW;
END CATCH;
GO

/* ============================================================
 * 5. 動作確認クエリ（実行後の検証用、任意）
 * ============================================================ */
-- Space メニュー階層を表示
SELECT
    REPLICATE(N'  ', CASE WHEN m.ParentId IS NULL THEN 0 ELSE 1 END)
        + CAST(m.MenuId AS NVARCHAR(10)) + N' ' + m.MenuName AS MenuTree,
    m.RoutePath,
    m.MenuKey,
    m.Icon,
    m.OrderNo,
    m.Enable
FROM Sys_Menus m
WHERE m.MenuId BETWEEN 900 AND 919
ORDER BY m.OrderNo;

-- 管理者ロールの Space メニュー付与状況
SELECT
    rm.RoleId,
    r.RoleName,
    COUNT(rm.MenuId) AS SpaceMenuCount
FROM Sys_RoleMenus rm
INNER JOIN Sys_Roles r ON rm.RoleId = r.RoleId
WHERE rm.MenuId BETWEEN 900 AND 919
GROUP BY rm.RoleId, r.RoleName;

/* ============================================================
 * 6. ロールバック用（緊急時のみ使用）
 * ============================================================
 * 注意: 関連 Sys_RoleMenus も先に削除されるため、慎重に。
 * ------------------------------------------------------------ */
/*
BEGIN TRANSACTION;
DELETE FROM Sys_RoleMenus WHERE MenuId BETWEEN 900 AND 919;
DELETE FROM Sys_Menus     WHERE MenuId BETWEEN 900 AND 919;
COMMIT TRANSACTION;
*/
