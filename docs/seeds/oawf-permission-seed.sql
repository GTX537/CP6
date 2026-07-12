/* ============================================================
 * OA/WF 電子表单・工作流 権限点（MenuAction / RoleAction）シードデータ（M-OA/WF 横切接線 Task 3b）
 * ============================================================
 * 対象 DB     : CP6DB (SQL Server)
 * 対象テーブル : Sys_MenuAction（授権可能な操作点の登記）
 *              Sys_RoleAction（管理者ロール RoleId=1 への全動作授権）
 *
 * ★正本は C#：CP6.WebApi/Seed/OawfPermissionSeed.cs（起動時逐租户冪等種子）。
 *   本 SQL は同一集合の文書留档・手動投入用であり、C# と 1:1 一致。乖離時は C# を正とする。
 *
 * 前提:
 *   OA/WF メニュー 733/734/735/737/738/739 は Task 2 種子
 *   （OawfMenuSeed / docs/seeds/oawf-menu-seed.sql）で MenuKey=oa-* 锚定済みであること。
 *   （OA は RoutePath 派生キー＝真相源キー逐字一致、零錯配。命門は回填時序＝OawfMenuSeed が回填前に明示赋値。）
 *   RoleAction は当該 MenuId 上に挂かり、実行時 PermissionAggregator が
 *   Sys_RoleActions join Sys_Menus on MenuId where MenuKey!=null → "{MenuKey}:{ActionCode}" を組む。
 *
 * 三数閉環（真相源 docs/seeds/oawf-permission-keys.md §一/§七 + 控制器 grep）:
 *   真写端点 31（Oa 21 + Wf 10）→ 去重 (menu-key, action) 20 元组 → 種子 20 元组（漏種 0 / 多種 0）。
 *   多处跨控制器归并（§五）消解重复：
 *     oa-inbox:read 覆 Inbox task/cc-read + Notification read/read-all（4→1）；
 *     oa-inbox:approve 覆 Inbox batch + Flow act（2→1）；oa-inbox:sendback 覆 Inbox + AdvancedFlow（2→1）；
 *     oa-form-catalog:submit 覆 Draft submit + Approval submit + Form data + Flow submit（4→1）；
 *     oa-settings:delegate 覆 Delegate add/remove + AdvancedFlow delegate（3→1，T2 委派合一拍板1）；
 *     oa-designer:edit 覆 Designer save + Flow def（2→1）。
 *   2 只読 POST 豁免（Forecast preview→oa-form-catalog:view / Query search→oa-form-search:view）は未貼点＝不入種子
 *   → 覆盖 6 有写端点 menu-key（oa-form-search(736) 仅 view 豁免故不種，非 7）。
 *
 * 動作定義（docs/seeds/oawf-key-menu-anchor.md 锚定 MenuId × 控制器 [RequirePermission] action 逐字）:
 *   733 oa-inbox        : read / approve（高危）/ transfer（高危）/ sendback（高危）/ addsign（高危）/ withdraw（状态）
 *   734 oa-flow-admin   : enable（状态）
 *   735 oa-form-catalog : add / edit / submit（状态）/ del / favorite
 *   737 oa-settings     : edit / delegate（高危・委派合一）
 *   738 oa-designer     : edit（高危）/ add（高危・克隆）/ form-save（高危）
 *   739 oa-approver-map : add / edit / del
 *
 * マルチテナント:
 *   Sys_MenuAction / Sys_RoleAction は BaseTenantEntity（TenantId 必須・行級隔離）。
 *   全テナントへ一括投入するため `CROSS JOIN (SELECT Id FROM Sys_Tenants) t` で逐租户展開。
 *
 * 実行方法:
 *   sqlcmd -S localhost\KOUSQLSERVER -d CP6DB -E -f 65001 -i docs/seeds/oawf-permission-seed.sql -b
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

PRINT '=== OA/WF 権限点（MenuAction/RoleAction）シード 開始 ===';

/* ------------------------------------------------------------
 * 0. 動作定義テーブル（MenuId, ActionCode, ActionName, Sort）— 計 20 動作／租户
 * ------------------------------------------------------------ */
DECLARE @Actions TABLE (MenuId INT, ActionCode NVARCHAR(50), ActionName NVARCHAR(100), Sort INT);
INSERT INTO @Actions (MenuId, ActionCode, ActionName, Sort) VALUES
 (733, N'read',      N'标记已读',   0),
 (733, N'approve',   N'审批',       0),
 (733, N'transfer',  N'转交',       0),
 (733, N'sendback',  N'退回',       0),
 (733, N'addsign',   N'加签',       0),
 (733, N'withdraw',  N'撤回',       0),
 (734, N'enable',    N'启停',       0),
 (735, N'add',       N'新建',       0),
 (735, N'edit',      N'编辑',       0),
 (735, N'submit',    N'提交',       0),
 (735, N'del',       N'删除',       0),
 (735, N'favorite',  N'收藏',       0),
 (737, N'edit',      N'编辑',       0),
 (737, N'delegate',  N'委派',       0),
 (738, N'edit',      N'编辑',       0),
 (738, N'add',       N'克隆',       0),
 (738, N'form-save', N'表单保存',   0),
 (739, N'add',       N'新建',       0),
 (739, N'edit',      N'编辑',       0),
 (739, N'del',       N'删除',       0);
-- 合計 20 動作定義／租户

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
INSERT INTO @Menus VALUES (733),(734),(735),(737),(738),(739);
SELECT @Tn = COUNT(*) FROM Sys_Tenants;
SELECT @Ma = COUNT(*) FROM Sys_MenuAction WHERE MenuId IN (SELECT MenuId FROM @Menus);
SELECT @Ra = COUNT(*) FROM Sys_RoleAction WHERE MenuId IN (SELECT MenuId FROM @Menus) AND RoleId = 1;

PRINT N'  租户数                  : ' + CAST(@Tn AS NVARCHAR(10));
PRINT N'  MenuAction 件数(OA/WF)  : ' + CAST(@Ma AS NVARCHAR(10)) + N'（租户数 × 20 想定）';
PRINT N'  RoleAction 件数(管理者) : ' + CAST(@Ra AS NVARCHAR(10)) + N'（租户数 × 20 想定）';

COMMIT TRANSACTION;
PRINT '=== OA/WF 権限点シード 完了 ===';
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
-- 管理者ロールの OA/WF 授権（RoleAction）一覧
SELECT CONVERT(varchar(36), ra.TenantId) AS TenantId, ra.RoleId, ra.MenuId, ra.ActionCode
FROM Sys_RoleAction ra
WHERE ra.MenuId IN (733,734,735,737,738,739) AND ra.RoleId = 1
ORDER BY ra.TenantId, ra.MenuId, ra.ActionCode;

/* ============================================================
 * 5. ロールバック用（緊急時のみ使用）
 * ============================================================
 * 注意: RoleId=1 限定（本种子只授过管理员；不限定会连带删掉日后经 UI 授出的其他角色 OA/WF 授权）。
 * ------------------------------------------------------------ */
/*
BEGIN TRANSACTION;
DELETE FROM Sys_RoleAction WHERE RoleId = 1 AND MenuId IN (733,734,735,737,738,739);
DELETE FROM Sys_MenuAction WHERE MenuId IN (733,734,735,737,738,739);
COMMIT TRANSACTION;
*/
