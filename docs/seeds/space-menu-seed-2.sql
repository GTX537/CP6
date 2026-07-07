/* ============================================================
 * Space 空間管理 メニュー シードデータ 第2弾（波3 生命周期）
 * ============================================================
 * 対象 DB     : CP6DB (SQL Server)
 * 対象テーブル : Sys_Menus / Sys_RoleMenus
 * 親メニューID : 900（空間管理 Space）← 波2 space-menu-seed.sql で作成済み
 *
 * ID 採番ルール（波2 の続き・900~919 区間内）:
 *   900 空間管理(Space)                     ← 波2
 *   ├ 901 スペースホーム   /space/home       ← 波2
 *   ├ 902 サイト管理       /space/site        ← 波2
 *   ├ 903 フロア管理       /space/floor       ← 波2
 *   ├ 904 コード規則       /space/code-rule   ← 本ファイル（波3）
 *   ├ 905 発行センター     /space/publish     ← 本ファイル（波3）
 *   └ 906 連携イベント     /space/events      ← 本ファイル（波3）
 *
 * ★ 実行前に必ず下記で 904~906 段が空いていることを複核すること:
 *     SELECT MenuId FROM Sys_Menus WHERE MenuId BETWEEN 900 AND 919;
 *   （静的検索では 904 段に占用なし・2026-07-06 裁決記録。
 *     親 900 が未作成なら先に space-menu-seed.sql を実行すること。）
 *
 * 実行方法:
 *   sqlcmd -S localhost\KOUSQLSERVER -d CP6DB -E -f 65001 -i docs/seeds/space-menu-seed-2.sql -b
 *   又は SSMS / Azure Data Studio で本ファイルを開いて実行。
 *
 * 冪等性:
 *   各 INSERT 前に NOT EXISTS チェック → 重複実行しても安全。
 *
 * 権限割当:
 *   既定で RoleId=1 (管理者) に全 Space メニューを付与（900~919 区間幂等）。
 *   ★ 波2 種子は自身の実行時点で存在した行のみ授権したため、
 *     本種子でも同款の 900~919 授権ブロックを再実行し 904~906 を確実に付与する。
 *
 * Icon:
 *   Element Plus 図標集に実在し、かつ wms/space 種子で使用実績のある名を採用:
 *     Operation = wms415 棚卸作業（規則/設定系） / Promotion = wms411 出荷指示登録（発行系） /
 *     List      = wms404 入庫予定一覧（一覧系）。
 * ============================================================ */

SET NOCOUNT ON;
SET XACT_ABORT ON;
-- sqlcmd/ODBC defaults QUOTED_IDENTIFIER OFF; Sys_Menus 上のフィルター付き索引/計算列 INSERT には ON 必須（Msg 1934 回避）
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
BEGIN TRY
BEGIN TRANSACTION;

PRINT '=== Space メニュー シード第2弾 開始 ===';

/* ------------------------------------------------------------
 * 1. 子メニュー  MenuId 904~906（親 900 配下）
 * ------------------------------------------------------------ */
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 904)
    INSERT INTO Sys_Menus (MenuId, MenuName, RoutePath, MenuKey, Icon, ParentId, OrderNo, Enable, CreateDate)
    VALUES (904, N'コード規則', N'/space/code-rule', N'space-code-rule', N'Operation', 900, 904, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 905)
    INSERT INTO Sys_Menus (MenuId, MenuName, RoutePath, MenuKey, Icon, ParentId, OrderNo, Enable, CreateDate)
    VALUES (905, N'発行センター', N'/space/publish', N'space-publish', N'Promotion', 900, 905, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 906)
    INSERT INTO Sys_Menus (MenuId, MenuName, RoutePath, MenuKey, Icon, ParentId, OrderNo, Enable, CreateDate)
    VALUES (906, N'連携イベント', N'/space/events', N'space-events', N'List', 900, 906, 1, SYSDATETIME());

/* ------------------------------------------------------------
 * 2. 管理者ロール (RoleId=1) に全 Space メニューを付与（900~919 幂等）
 *    波2 種子と同款：実行時点で存在する未授権行のみを補填する。
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
 * 3. 結果確認
 * ------------------------------------------------------------ */
DECLARE @MenuCount INT, @RoleMenuCount INT;
SELECT @MenuCount     = COUNT(*) FROM Sys_Menus     WHERE MenuId BETWEEN 900 AND 919;
SELECT @RoleMenuCount = COUNT(*) FROM Sys_RoleMenus WHERE MenuId BETWEEN 900 AND 919 AND RoleId = 1;

PRINT N'  Space メニュー件数       : ' + CAST(@MenuCount AS NVARCHAR(10)) + N'（904~906 追加後は 6 想定）';
PRINT N'  管理者ロール付与件数     : ' + CAST(@RoleMenuCount AS NVARCHAR(10));

COMMIT TRANSACTION;
PRINT '=== Space メニュー シード第2弾 完了 ===';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT 'ERROR: ' + ERROR_MESSAGE();
    THROW;
END CATCH;
GO

/* ============================================================
 * 4. 動作確認クエリ（実行後の検証用、任意）
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

/* ============================================================
 * 5. ロールバック用（緊急時のみ使用）
 * ============================================================
 * 注意: 本ファイルが追加した 904~906 のみを対象とする。
 * ------------------------------------------------------------ */
/*
BEGIN TRANSACTION;
DELETE FROM Sys_RoleMenus WHERE MenuId BETWEEN 904 AND 906;
DELETE FROM Sys_Menus     WHERE MenuId BETWEEN 904 AND 906;
COMMIT TRANSACTION;
*/
