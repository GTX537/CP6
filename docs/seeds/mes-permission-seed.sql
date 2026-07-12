/* ============================================================
 * MES 製造執行 権限点（MenuAction / RoleAction）シードデータ（M-MES 横切接線 Task 3b）
 * ============================================================
 * 対象 DB     : CP6DB (SQL Server)
 * 対象テーブル : Sys_MenuAction（授権可能な操作点の登記）
 *              Sys_RoleAction（管理者ロール RoleId=1 への全動作授権）
 *
 * ★正本は C#：CP6.WebApi/Seed/MesPermissionSeed.cs（起動時逐租户冪等種子）。
 *   本 SQL は同一集合の文書留档・手動投入用であり、C# と 1:1 一致。乖離時は C# を正とする。
 *
 * 前提:
 *   MES メニュー 301/302/304/306/308/310/311/314/315 は Task 2 種子
 *   （MesMenuSeed / docs/seeds/mes-menu-seed.sql）で MenuKey=mes-* 锚定済みであること。
 *   （特に 310 は RoutePath=/mes/machine-list だが MenuKey=mes-machine を明示赋値済み。）
 *   RoleAction は当該 MenuId 上に挂かり、実行時 PermissionAggregator が
 *   Sys_RoleActions join Sys_Menus on MenuId where MenuKey!=null → "{MenuKey}:{ActionCode}" を組む。
 *
 * 三数閉環（真相源 docs/seeds/mes-permission-keys.md §一/§七 + 控制器 grep）:
 *   写端点 28 → 去重 (menu-key, action) 25 元组 → 種子 25 元组（漏種 0 / 多種 0）。
 *   3 处归并（§五 1/2/3）消解重复：mes-work-order:add 覆 Create+ExpandFromOrder /
 *   mes-production-result:suspend 覆 Suspend+Resume / mes-machine:downtime 覆 Register+Close。
 *   2 只读 POST 豁免（mes-plan-achievement:view）は未貼点＝不入種子→覆盖 9 有写端点 menu-key（非 10）。
 *
 * 動作定義（docs/seeds/mes-key-menu-anchor.md 锚定 MenuId × 控制器 [RequirePermission] action 逐字）:
 *   301 mes-planning-board     : reschedule / arrange
 *   302 mes-work-order         : add / edit / del / issue
 *   304 mes-production-result  : start / suspend / complete（高危・反冲）/ report
 *   306 mes-quality-inspection : add / edit
 *   308 mes-defect             : add / edit / del
 *   310 mes-machine            : add / edit / del / status / downtime
 *   311 mes-oee                : recalculate
 *   314 mes-work-center        : edit / del
 *   315 mes-process-cost-rate  : edit（高危）/ del
 *
 * マルチテナント:
 *   Sys_MenuAction / Sys_RoleAction は BaseTenantEntity（TenantId 必須・行級隔離）。
 *   全テナントへ一括投入するため `CROSS JOIN (SELECT Id FROM Sys_Tenants) t` で逐租户展開。
 *
 * 実行方法:
 *   sqlcmd -S localhost\KOUSQLSERVER -d CP6DB -E -f 65001 -i docs/seeds/mes-permission-seed.sql -b
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

PRINT '=== MES 権限点（MenuAction/RoleAction）シード 開始 ===';

/* ------------------------------------------------------------
 * 0. 動作定義テーブル（MenuId, ActionCode, ActionName, Sort）— 計 25 動作／租户
 * ------------------------------------------------------------ */
DECLARE @Actions TABLE (MenuId INT, ActionCode NVARCHAR(50), ActionName NVARCHAR(100), Sort INT);
INSERT INTO @Actions (MenuId, ActionCode, ActionName, Sort) VALUES
 (301, N'reschedule',  N'改期',       0),
 (301, N'arrange',     N'自动排产',   0),
 (302, N'add',         N'新建',       0),
 (302, N'edit',        N'编辑',       0),
 (302, N'del',         N'删除',       0),
 (302, N'issue',       N'发行',       0),
 (304, N'start',       N'开始',       0),
 (304, N'suspend',     N'中断',       0),
 (304, N'complete',    N'完了',       0),
 (304, N'report',      N'报工',       0),
 (306, N'add',         N'新建',       0),
 (306, N'edit',        N'编辑',       0),
 (308, N'add',         N'新建',       0),
 (308, N'edit',        N'编辑',       0),
 (308, N'del',         N'删除',       0),
 (310, N'add',         N'新建',       0),
 (310, N'edit',        N'编辑',       0),
 (310, N'del',         N'删除',       0),
 (310, N'status',      N'状态变更',   0),
 (310, N'downtime',    N'停机记录',   0),
 (311, N'recalculate', N'重算',       0),
 (314, N'edit',        N'编辑',       0),
 (314, N'del',         N'删除',       0),
 (315, N'edit',        N'编辑',       0),
 (315, N'del',         N'删除',       0);
-- 合計 25 動作定義／租户

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
INSERT INTO @Menus VALUES (301),(302),(304),(306),(308),(310),(311),(314),(315);
SELECT @Tn = COUNT(*) FROM Sys_Tenants;
SELECT @Ma = COUNT(*) FROM Sys_MenuAction WHERE MenuId IN (SELECT MenuId FROM @Menus);
SELECT @Ra = COUNT(*) FROM Sys_RoleAction WHERE MenuId IN (SELECT MenuId FROM @Menus) AND RoleId = 1;

PRINT N'  租户数                  : ' + CAST(@Tn AS NVARCHAR(10));
PRINT N'  MenuAction 件数(MES)    : ' + CAST(@Ma AS NVARCHAR(10)) + N'（租户数 × 25 想定）';
PRINT N'  RoleAction 件数(管理者) : ' + CAST(@Ra AS NVARCHAR(10)) + N'（租户数 × 25 想定）';

COMMIT TRANSACTION;
PRINT '=== MES 権限点シード 完了 ===';
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
-- 管理者ロールの MES 授権（RoleAction）一覧
SELECT CONVERT(varchar(36), ra.TenantId) AS TenantId, ra.RoleId, ra.MenuId, ra.ActionCode
FROM Sys_RoleAction ra
WHERE ra.MenuId IN (301,302,304,306,308,310,311,314,315) AND ra.RoleId = 1
ORDER BY ra.TenantId, ra.MenuId, ra.ActionCode;

/* ============================================================
 * 5. ロールバック用（緊急時のみ使用）
 * ============================================================
 * 注意: RoleId=1 限定（本种子只授过管理员；不限定会连带删掉日后经 UI 授出的其他角色 MES 授权）。
 * ------------------------------------------------------------ */
/*
BEGIN TRANSACTION;
DELETE FROM Sys_RoleAction WHERE RoleId = 1 AND MenuId IN (301,302,304,306,308,310,311,314,315);
DELETE FROM Sys_MenuAction WHERE MenuId IN (301,302,304,306,308,310,311,314,315);
COMMIT TRANSACTION;
*/
