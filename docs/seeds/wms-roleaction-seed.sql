/* ============================================================
 * WMS 倉庫管理 権限点（MenuAction / RoleAction）シードデータ（M-WMS 横切接線 Task 3b）
 * ============================================================
 * 正本   : CP6.WebApi/Seed/WmsPermissionSeed.cs（启动幂等・逐租户・执行真相）。
 *          本 SQL は文档留档／手动灾备用。清单必须与 C# Actions[] 一致（112 条 (键,action)／30 键）。
 * 対象 DB     : CP6DB (SQL Server)
 * 対象テーブル : Sys_MenuAction（授権可能な操作点の登記）
 *              Sys_RoleAction（管理者ロール RoleId=1 への全動作授権）
 *
 * 由来:
 *   下記 (MenuId, ActionCode) は CP6.WebApi/Controllers/Wms/*.cs の
 *   [RequirePermission("键","action")] 属性から grep 去重派生（強校验と 1:1）。
 *   MenuId は docs/seeds/wms-key-menu-anchor.md の 30 键→锚定 MenuId で映射。
 *   前提: WMS 400 段メニュー（401~483）は WmsMenuSeed（起動幂等）で作成済みであること。
 *
 * マルチテナント:
 *   Sys_MenuAction / Sys_RoleAction は BaseTenantEntity（TenantId 必須・行級隔離）。
 *   全テナントへ一括投入するため `CROSS JOIN (SELECT Id FROM Sys_Tenants) t` で逐租户展開。
 *
 * 実行方法:
 *   sqlcmd -S localhost\KOUSQLSERVER -d CP6DB -E -f 65001 -i docs/seeds/wms-roleaction-seed.sql -b
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

PRINT '=== WMS 権限点（MenuAction/RoleAction）シード 開始 ===';

/* ------------------------------------------------------------
 * 0. 動作定義テーブル（MenuId, ActionCode, ActionName）——112 条／租户
 *    C# WmsPermissionSeed.Actions[] と逐字一致。
 * ------------------------------------------------------------ */
DECLARE @Actions TABLE (MenuId INT, ActionCode NVARCHAR(50), ActionName NVARCHAR(100));
INSERT INTO @Actions (MenuId, ActionCode, ActionName) VALUES
 -- 401 wms-warehouse
 (401, N'add', N'新建'), (401, N'edit', N'编辑'), (401, N'del', N'删除'),
 -- 402 wms-location
 (402, N'add', N'新建'), (402, N'edit', N'编辑'), (402, N'del', N'删除'),
 -- 403 wms-stock
 (403, N'adjust', N'库存调整'), (403, N'move', N'移库'),
 -- 429 wms-stock-qc
 (429, N'set', N'保留放行'),
 -- 405 wms-inbound-order
 (405, N'add', N'新建'), (405, N'edit', N'编辑'), (405, N'del', N'删除'),
 (405, N'confirm', N'确认'), (405, N'cancel', N'取消'),
 -- 406 wms-inbound-receipt
 (406, N'post', N'入库过账'),
 -- 408 wms-outbound-order
 (408, N'add', N'新建'), (408, N'edit', N'编辑'), (408, N'del', N'删除'),
 (408, N'confirm', N'确认'), (408, N'cancel', N'取消'),
 (408, N'allocate', N'引当分配'), (408, N'pick', N'拣货'), (408, N'ship', N'出库'),
 -- 415 wms-stocktake
 (415, N'add', N'新建'), (415, N'count', N'盘点计数'), (415, N'submit', N'提交'),
 (415, N'approve', N'承认'), (415, N'cancel', N'取消'),
 -- 417 wms-material-shortage
 (417, N'resolve', N'解决'), (417, N'dismiss', N'消除'),
 -- 419 wms-outbound-routing
 (419, N'add', N'新建'), (419, N'edit', N'编辑'), (419, N'del', N'删除'),
 -- 421 wms-qc-inspection
 (421, N'add', N'新建'), (421, N'edit', N'编辑'), (421, N'judge', N'判定处置'), (421, N'cancel', N'取消'),
 -- 422 wms-slotting
 (422, N'analyze', N'分析'), (422, N'approve', N'承认'), (422, N'cancel', N'取消'),
 -- 423 wms-replenish
 (423, N'add', N'新建'), (423, N'generate', N'生成'), (423, N'execute', N'执行'), (423, N'cancel', N'取消'),
 -- 424 wms-cross-dock
 (424, N'add', N'新建'), (424, N'execute', N'执行'), (424, N'cancel', N'取消'),
 -- 425 wms-kitting
 (425, N'add', N'新建'), (425, N'edit', N'编辑'), (425, N'del', N'删除'),
 (425, N'execute', N'执行'), (425, N'cancel', N'取消'),
 -- 426 wms-rma
 (426, N'add', N'新建'), (426, N'receive', N'入库'), (426, N'inspect', N'检查'),
 (426, N'judge', N'判定'), (426, N'close', N'关闭'), (426, N'cancel', N'取消'),
 -- 427 wms-lot-trace
 (427, N'recall', N'回收'),
 -- 428 wms-expiry
 (428, N'dispose', N'报废'),
 -- 441 wms-paper-roll
 (441, N'add', N'新建'), (441, N'consume', N'消费'), (441, N'slit', N'分切'), (441, N'dispose', N'报废'),
 -- 442 wms-remnant
 (442, N'add', N'新建'), (442, N'edit', N'编辑'), (442, N'reserve', N'预留'),
 (442, N'use', N'使用'), (442, N'dispose', N'报废'), (442, N'del', N'删除'),
 -- 443 wms-plate-mold
 (443, N'add', N'新建'), (443, N'edit', N'编辑'), (443, N'use', N'使用'),
 (443, N'maintenance', N'维护'), (443, N'dispose', N'报废'), (443, N'del', N'删除'),
 -- 444 wms-ink
 (444, N'add', N'新建'), (444, N'open', N'开封'), (444, N'mix', N'调墨'),
 -- 445 wms-pallet
 (445, N'add', N'新建'), (445, N'edit', N'编辑'), (445, N'complete', N'完了'),
 (445, N'move', N'移动'), (445, N'ship', N'出库'), (445, N'del', N'删除'),
 -- 446 wms-vmi
 (446, N'calculate', N'计算'), (446, N'confirm', N'确认'),
 -- 447 wms-sample-stock
 (447, N'add', N'新建'), (447, N'edit', N'编辑'), (447, N'lend', N'出借'),
 (447, N'return', N'返却'), (447, N'expire', N'失效'), (447, N'del', N'删除'),
 -- 461 wms-mobile
 (461, N'add', N'新建'), (461, N'start', N'开始'), (461, N'scan', N'扫描'),
 (461, N'complete', N'完了'), (461, N'cancel', N'取消'),
 -- 462 wms-wcs-task
 (462, N'add', N'新建'), (462, N'dispatch', N'派发'), (462, N'start', N'开始'),
 (462, N'complete', N'完了'), (462, N'fail', N'失败'), (462, N'del', N'删除'),
 -- 463 wms-carrier
 (463, N'add', N'新建'), (463, N'event', N'状态更新'),
 -- 464 wms-iot
 (464, N'add', N'新建'), (464, N'edit', N'编辑'), (464, N'del', N'删除'),
 (464, N'ingest', N'数据取込'), (464, N'simulate', N'模拟'),
 -- 483 wms-stock-dwell
 (483, N'view', N'查看');
-- 合計 112 動作定義／租户（30 键）

/* ------------------------------------------------------------
 * 1. Sys_MenuAction 登記（逐租户・冪等 NOT EXISTS）
 * ------------------------------------------------------------ */
INSERT INTO Sys_MenuAction (Id, MenuId, ActionCode, ActionName, Sort, CreateDate, TenantId)
SELECT NEWID(), a.MenuId, a.ActionCode, a.ActionName, 0, SYSDATETIME(), t.Id
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
SELECT @Ma = COUNT(*) FROM Sys_MenuAction WHERE MenuId IN (SELECT DISTINCT MenuId FROM @Actions);
SELECT @Ra = COUNT(*) FROM Sys_RoleAction WHERE RoleId = 1 AND MenuId IN (SELECT DISTINCT MenuId FROM @Actions);

PRINT N'  租户数                       : ' + CAST(@Tn AS NVARCHAR(10));
PRINT N'  MenuAction 件数(WMS)         : ' + CAST(@Ma AS NVARCHAR(10)) + N'（租户数 × 112 想定）';
PRINT N'  RoleAction 件数(管理者・WMS) : ' + CAST(@Ra AS NVARCHAR(10)) + N'（租户数 × 112 想定）';

COMMIT TRANSACTION;
PRINT '=== WMS 権限点シード 完了 ===';
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
-- 管理者ロールの WMS 授権件数（租户別）
SELECT CONVERT(varchar(36), ra.TenantId) AS TenantId, COUNT(*) AS RoleActionCnt
FROM Sys_RoleAction ra
WHERE ra.RoleId = 1 AND ra.MenuId BETWEEN 400 AND 483
GROUP BY ra.TenantId
ORDER BY TenantId;

/* ============================================================
 * 5. ロールバック用（緊急時のみ使用）
 * ============================================================
 * 注意: RoleId=1 限定（本種子は管理者のみ授権）。MenuId は WMS 400 段のみ対象。
 * ------------------------------------------------------------ */
/*
BEGIN TRANSACTION;
DELETE ra FROM Sys_RoleAction ra
  INNER JOIN (SELECT DISTINCT MenuId, ActionCode FROM @Actions) a
    ON ra.MenuId = a.MenuId AND ra.ActionCode = a.ActionCode
  WHERE ra.RoleId = 1;
DELETE ma FROM Sys_MenuAction ma
  INNER JOIN (SELECT DISTINCT MenuId, ActionCode FROM @Actions) a
    ON ma.MenuId = a.MenuId AND ma.ActionCode = a.ActionCode;
COMMIT TRANSACTION;
*/
