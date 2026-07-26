/* ============================================================
 * Space 空間管理 権限点（MenuAction / RoleAction）シードデータ（波4 横切）
 * ============================================================
 * 対象 DB     : CP6DB (SQL Server)
 * 対象テーブル : Sys_MenuAction（授権可能な操作点の登記）
 *              Sys_RoleAction（管理者ロール RoleId=1 への全動作授権）
 *
 * 前提:
 *   Space メニュー 902~906 は波2/波3 種子（space-menu-seed.sql /
 *   space-menu-seed-2.sql）で作成済みであること:
 *     902 space-site（サイト管理） / 903 space-floor（フロア管理） /
 *     904 space-code-rule（コード規則） / 905 space-publish（発行センター） /
 *     906 space-audit（イベント・監査）
 *
 * 動作定義（計画 2026-07-07-space-wave4-crosscutting.md Global Constraints 映射表 逐字）:
 *   902 space-site      : add / edit / delete
 *   903 space-floor     : add / edit / delete（zone/aisle/rack/scene/template/connector の
 *                         全変更は「フロア編集」単一動作 edit に集約——動作爆発回避）
 *   904 space-code-rule : add / edit / delete / generate（generate-codes + gen-code）
 *   905 space-publish   : publish / deactivate / adopt
 *   906 space-audit     : read（監査照会。RoleId=1 のみに初期授権）
 *
 * マルチテナント:
 *   Sys_MenuAction / Sys_RoleAction は BaseTenantEntity（TenantId 必須・行級隔離）。
 *   全テナントへ一括投入するため `CROSS JOIN (SELECT Id FROM Sys_Tenants) t` で逐租户展開。
 *   （Sys_Tenants の主键は Id[uniqueidentifier]。qa 種子 02_seed_roleaction_BC.sql の
 *     明示 TenantId 挿入と同系だが、本種子は全租户を自動列挙する。）
 *
 * 実行方法:
 *   sqlcmd -S localhost\KOUSQLSERVER -d CP6DB -E -f 65001 -i docs/seeds/space-roleaction-seed.sql -b
 *   又は SSMS / Azure Data Studio で本ファイルを開いて実行。
 *
 * 冪等性:
 *   各 INSERT 前に NOT EXISTS チェック（TenantId+MenuId+ActionCode 単位）→ 重複実行安全。
 * ============================================================ */

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
BEGIN TRY
BEGIN TRANSACTION;

PRINT '=== Space 権限点（MenuAction/RoleAction）シード 開始 ===';

/* ------------------------------------------------------------
 * 0. 動作定義テーブル（MenuId, ActionCode, ActionName, Sort）
 * ------------------------------------------------------------ */
DECLARE @Actions TABLE (MenuId INT, ActionCode NVARCHAR(50), ActionName NVARCHAR(100), Sort INT);
INSERT INTO @Actions (MenuId, ActionCode, ActionName, Sort) VALUES
 (902, N'add',        N'サイト作成',       1),
 (902, N'edit',       N'サイト編集',       2),
 (902, N'delete',     N'サイト削除',       3),
 (903, N'add',        N'フロア作成',       1),
 (903, N'edit',       N'フロア編集',       2),
 (903, N'delete',     N'フロア削除',       3),
 (904, N'add',        N'コード規則作成',   1),
 (904, N'edit',       N'コード規則編集',   2),
 (904, N'delete',     N'コード規則削除',   3),
 (904, N'generate',   N'コード生成',       4),
 (905, N'publish',    N'発行',             1),
 (905, N'deactivate', N'停止',             2),
 (905, N'adopt',      N'採納',             3),
 (906, N'read',       N'查看审计',         0);
-- 合計 14 動作定義／租户（E00-S04 は 906:read を追加）

/* ------------------------------------------------------------
 * 1. Sys_MenuAction 登記（逐租户・冪等 NOT EXISTS）
 * ------------------------------------------------------------ */
INSERT INTO Sys_MenuAction (Id, MenuId, ActionCode, ActionName, Sort, CreateDate, TenantId)
SELECT NEWID(), a.MenuId, a.ActionCode, a.ActionName, a.Sort, SYSDATETIME(), t.Id
FROM @Actions a
CROSS JOIN (SELECT Id FROM Sys_Tenants) t
WHERE NOT EXISTS (
    SELECT 1 FROM Sys_MenuAction ma
    WHERE ma.TenantId = t.Id AND ma.MenuId = a.MenuId AND ma.ActionCode = a.ActionCode
);

/* ------------------------------------------------------------
 * 2. Sys_RoleAction 管理者(RoleId=1) へ全動作授権（逐租户・冪等）
 *    列序: (Id, RoleId, MenuId, ActionCode, CreateDate, TenantId)
 * ------------------------------------------------------------ */
INSERT INTO Sys_RoleAction (Id, RoleId, MenuId, ActionCode, CreateDate, TenantId)
SELECT NEWID(), 1, a.MenuId, a.ActionCode, SYSDATETIME(), t.Id
FROM @Actions a
CROSS JOIN (SELECT Id FROM Sys_Tenants) t
WHERE NOT EXISTS (
    SELECT 1 FROM Sys_RoleAction ra
    WHERE ra.TenantId = t.Id AND ra.RoleId = 1 AND ra.MenuId = a.MenuId AND ra.ActionCode = a.ActionCode
);

/* ------------------------------------------------------------
 * 3. 結果確認（PRINT）
 * ------------------------------------------------------------ */
DECLARE @Tn INT, @Ma INT, @Ra INT;
SELECT @Tn = COUNT(*) FROM Sys_Tenants;
SELECT @Ma = COUNT(*) FROM Sys_MenuAction WHERE MenuId BETWEEN 902 AND 906;
SELECT @Ra = COUNT(*) FROM Sys_RoleAction WHERE MenuId BETWEEN 902 AND 906 AND RoleId = 1;

PRINT N'  租户数                    : ' + CAST(@Tn AS NVARCHAR(10));
PRINT N'  MenuAction 件数(902-906)  : ' + CAST(@Ma AS NVARCHAR(10)) + N'（租户数 × 14 想定）';
PRINT N'  RoleAction 件数(管理者)   : ' + CAST(@Ra AS NVARCHAR(10)) + N'（租户数 × 14 想定）';

COMMIT TRANSACTION;
PRINT '=== Space 権限点シード 完了 ===';
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
-- 各テナントの Space 権限点（MenuAction）一覧
SELECT CONVERT(varchar(36), ma.TenantId) AS TenantId, ma.MenuId, ma.ActionCode, ma.ActionName, ma.Sort
FROM Sys_MenuAction ma
WHERE ma.MenuId BETWEEN 902 AND 906
ORDER BY ma.TenantId, ma.MenuId, ma.Sort;

-- 管理者ロールの授権（RoleAction）一覧
SELECT CONVERT(varchar(36), ra.TenantId) AS TenantId, ra.RoleId, ra.MenuId, ra.ActionCode
FROM Sys_RoleAction ra
WHERE ra.MenuId BETWEEN 902 AND 906 AND ra.RoleId = 1
ORDER BY ra.TenantId, ra.MenuId, ra.ActionCode;

/* ============================================================
 * 5. ロールバック用（緊急時のみ使用）
 * ============================================================
 * 注意: 本ファイルが追加した 902~906 の Space 権限点のみを対象とする。
 * ------------------------------------------------------------ */
/*
BEGIN TRANSACTION;
-- RoleId=1 限定：本种子只授过管理员；不限定会连带删掉日后经 UI 授出的其他角色 Space 授权（波4 终审 T4 修正）
DELETE FROM Sys_RoleAction WHERE RoleId = 1 AND MenuId BETWEEN 902 AND 906 AND ActionCode IN
 (N'add', N'edit', N'delete', N'generate', N'publish', N'deactivate', N'adopt', N'read');
DELETE FROM Sys_MenuAction WHERE MenuId BETWEEN 902 AND 906 AND ActionCode IN
 (N'add', N'edit', N'delete', N'generate', N'publish', N'deactivate', N'adopt', N'read');
COMMIT TRANSACTION;
*/
