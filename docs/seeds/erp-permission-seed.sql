/* ============================================================
 * ERP 販売管理 権限点（MenuAction / RoleAction）シードデータ（M-ERP 横切接線 Task 3b）
 * ============================================================
 * 対象 DB     : CP6DB (SQL Server)
 * 対象テーブル : Sys_MenuAction（授権可能な操作点の登記）
 *              Sys_RoleAction（管理者ロール RoleId=1 への全動作授権）
 *
 * ★正本は C#：CP6.WebApi/Seed/ErpPermissionSeed.cs（起動時逐租户冪等種子）。
 *   本 SQL は同一集合の文書留档・手動投入用であり、C# と 1:1 一致。乖離時は C# を正とする。
 *
 * 前提:
 *   ERP メニュー 202/204/206/208/209/210/212/213/215/218/220 は Task 2 種子
 *   （ErpMenuSeed / docs/seeds/erp-menu-seed.sql）で MenuKey=erp-* 锚定済みであること。
 *   RoleAction は当該 MenuId 上に挂かり、実行時 PermissionAggregator が
 *   Sys_RoleActions join Sys_Menus on MenuId where MenuKey!=null → "{MenuKey}:{ActionCode}" を組む。
 *
 * 三数閉環（真相源 docs/seeds/erp-permission-keys.md + 控制器 grep）:
 *   写端点 35 → 去重 (menu-key, action) 30 元组 → 種子 30 元组（漏種 0 / 多種 0）。
 *   11 只读 POST 豁免は未貼点＝不入種子。erp-order-trace / erp-credit-note / erp-otd-report
 *   の 3 键は view 端点のみ（GET-only 或豁免）→ 写元组なし→本種子対象外。覆盖 11 有写端点 menu-key。
 *
 * 動作定義（docs/seeds/erp-key-menu-anchor.md 锚定 MenuId × 控制器 [RequirePermission] action 逐字）:
 *   202 erp-estimate-calc           : add / edit / del
 *   204 erp-quotation               : add / edit / del / confirm / issue
 *   206 erp-product                 : add / edit / del
 *   208 erp-order                   : add / edit / del / cancel（cancel 高危）
 *   209 erp-order-price-correction  : correct（高危・跨菜单）
 *   210 erp-fsc-checklist           : issue
 *   212 erp-business-partner        : add / edit / del
 *   213 erp-sheet-unit-price        : import / edit
 *   215 erp-plate-mold              : add / edit / del
 *   218 erp-backorder               : close / split
 *   220 erp-fx-rate                 : add / edit / del
 *
 * マルチテナント:
 *   Sys_MenuAction / Sys_RoleAction は BaseTenantEntity（TenantId 必須・行級隔離）。
 *   全テナントへ一括投入するため `CROSS JOIN (SELECT Id FROM Sys_Tenants) t` で逐租户展開。
 *
 * 実行方法:
 *   sqlcmd -S localhost\KOUSQLSERVER -d CP6DB -E -f 65001 -i docs/seeds/erp-permission-seed.sql -b
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

PRINT '=== ERP 権限点（MenuAction/RoleAction）シード 開始 ===';

/* ------------------------------------------------------------
 * 0. 動作定義テーブル（MenuId, ActionCode, ActionName, Sort）— 計 30 動作／租户
 * ------------------------------------------------------------ */
DECLARE @Actions TABLE (MenuId INT, ActionCode NVARCHAR(50), ActionName NVARCHAR(100), Sort INT);
INSERT INTO @Actions (MenuId, ActionCode, ActionName, Sort) VALUES
 (202, N'add',     N'新建',       0),
 (202, N'edit',    N'编辑',       0),
 (202, N'del',     N'删除',       0),
 (204, N'add',     N'新建',       0),
 (204, N'edit',    N'编辑',       0),
 (204, N'del',     N'删除',       0),
 (204, N'confirm', N'确定',       0),
 (204, N'issue',   N'发行',       0),
 (206, N'add',     N'新建',       0),
 (206, N'edit',    N'编辑',       0),
 (206, N'del',     N'删除',       0),
 (208, N'add',     N'新建',       0),
 (208, N'edit',    N'编辑',       0),
 (208, N'del',     N'删除',       0),
 (208, N'cancel',  N'受注取消',   0),
 (209, N'correct', N'单价订正',   0),
 (210, N'issue',   N'发行',       0),
 (212, N'add',     N'新建',       0),
 (212, N'edit',    N'编辑',       0),
 (212, N'del',     N'删除',       0),
 (213, N'import',  N'取込',       0),
 (213, N'edit',    N'编辑',       0),
 (215, N'add',     N'新建',       0),
 (215, N'edit',    N'编辑',       0),
 (215, N'del',     N'删除',       0),
 (218, N'close',   N'关闭残数',   0),
 (218, N'split',   N'拆分新单',   0),
 (220, N'add',     N'新建',       0),
 (220, N'edit',    N'编辑',       0),
 (220, N'del',     N'删除',       0);
-- 合計 30 動作定義／租户

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
DECLARE @Menus TABLE (MenuId INT);
INSERT INTO @Menus VALUES (202),(204),(206),(208),(209),(210),(212),(213),(215),(218),(220);
SELECT @Tn = COUNT(*) FROM Sys_Tenants;
SELECT @Ma = COUNT(*) FROM Sys_MenuAction WHERE MenuId IN (SELECT MenuId FROM @Menus);
SELECT @Ra = COUNT(*) FROM Sys_RoleAction WHERE MenuId IN (SELECT MenuId FROM @Menus) AND RoleId = 1;

PRINT N'  租户数                  : ' + CAST(@Tn AS NVARCHAR(10));
PRINT N'  MenuAction 件数(ERP)    : ' + CAST(@Ma AS NVARCHAR(10)) + N'（租户数 × 30 想定）';
PRINT N'  RoleAction 件数(管理者) : ' + CAST(@Ra AS NVARCHAR(10)) + N'（租户数 × 30 想定）';

COMMIT TRANSACTION;
PRINT '=== ERP 権限点シード 完了 ===';
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
-- 管理者ロールの ERP 授権（RoleAction）一覧
SELECT CONVERT(varchar(36), ra.TenantId) AS TenantId, ra.RoleId, ra.MenuId, ra.ActionCode
FROM Sys_RoleAction ra
WHERE ra.MenuId IN (202,204,206,208,209,210,212,213,215,218,220) AND ra.RoleId = 1
ORDER BY ra.TenantId, ra.MenuId, ra.ActionCode;

/* ============================================================
 * 5. ロールバック用（緊急時のみ使用）
 * ============================================================
 * 注意: RoleId=1 限定（本种子只授过管理员；不限定会连带删掉日后经 UI 授出的其他角色 ERP 授权）。
 * ------------------------------------------------------------ */
/*
BEGIN TRANSACTION;
DELETE FROM Sys_RoleAction WHERE RoleId = 1 AND MenuId IN (202,204,206,208,209,210,212,213,215,218,220);
DELETE FROM Sys_MenuAction WHERE MenuId IN (202,204,206,208,209,210,212,213,215,218,220);
COMMIT TRANSACTION;
*/
