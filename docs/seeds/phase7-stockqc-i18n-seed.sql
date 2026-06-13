/* ============================================================
 * Phase 7 Gap 1.3 QC ステータス UI i18n
 * ============================================================
 * 範囲：wms.stock.qc.*
 * 冪等：MERGE
 * 実行：sqlcmd -S "localhost\KOUSQLSERVER" -E -d CP6DB -f 65001 -i docs/phase7-stockqc-i18n-seed.sql -b
 * ============================================================ */
SET NOCOUNT ON; SET XACT_ABORT ON;
PRINT '=== Phase 7 Stock QC i18n シード開始 ===';

IF OBJECT_ID('tempdb..#i18n') IS NOT NULL DROP TABLE #i18n;
CREATE TABLE #i18n (
    LangKey nvarchar(200) NOT NULL PRIMARY KEY,
    ZhCN nvarchar(500), ZhTW nvarchar(500), En nvarchar(500), Ja nvarchar(500), Ko nvarchar(500)
);

INSERT INTO #i18n VALUES
  -- 列ヘッダ + ボタン
  (N'wms.stock.col.qc',         N'QC 状态',     N'QC 狀態',     N'QC',            N'QC',           N'QC'),
  (N'wms.stock.qc.btn',         N'QC 设置',     N'QC 設定',     N'Set QC',        N'QC 設定',      N'QC 설정'),
  (N'wms.stock.qc.dlgTitle',    N'设置 QC 状态', N'設定 QC 狀態', N'Set QC status', N'QC ステータス設定', N'QC 상태 설정'),
  (N'wms.stock.qc.current',     N'当前状态',     N'目前狀態',     N'Current',       N'現在ステータス', N'현재 상태'),
  (N'wms.stock.qc.newStatus',   N'新状态',       N'新狀態',       N'New status',    N'新ステータス',   N'새 상태'),
  (N'wms.stock.qc.reason',      N'变更理由',     N'變更理由',     N'Reason',        N'変更理由',      N'변경 사유'),
  (N'wms.stock.qc.reasonPlaceholder', N'例：批次抽检不合格', N'例：批次抽檢不合格', N'e.g. failed sample inspection', N'例：抽検不合格', N'예: 표본 검사 불합격'),
  (N'wms.stock.qc.savedMsg',    N'QC 状态已更新', N'QC 狀態已更新', N'QC status updated', N'QC ステータス更新済', N'QC 상태 갱신 완료'),
  -- ステータス取値ラベル
  (N'wms.stock.qc.PENDING',     N'待检验',       N'待檢驗',       N'Pending',       N'未検査',        N'대기'),
  (N'wms.stock.qc.PASSED',      N'合格',         N'合格',         N'Passed',        N'合格',          N'합격'),
  (N'wms.stock.qc.FAILED',      N'不合格',       N'不合格',       N'Failed',        N'不合格',        N'불합격'),
  (N'wms.stock.qc.HOLD',        N'保留',         N'保留',         N'On hold',       N'保留',          N'보류'),
  -- 共通ボタン（既存にあればただ上書き）
  (N'wms.common.confirm',       N'确认',         N'確認',         N'Confirm',       N'確認',          N'확인'),
  (N'wms.common.cancel',        N'取消',         N'取消',         N'Cancel',        N'キャンセル',    N'취소');

DECLARE @actionLog TABLE (act nvarchar(10));
MERGE Sys_Langs AS tgt
USING #i18n AS src ON tgt.LangKey = src.LangKey
WHEN MATCHED THEN UPDATE SET
    tgt.ZhCN = src.ZhCN, tgt.ZhTW = src.ZhTW, tgt.En = src.En,
    tgt.Ja = src.Ja, tgt.Ko = src.Ko
WHEN NOT MATCHED THEN INSERT
    (LangKey, ZhCN, ZhTW, En, Ja, Ko)
    VALUES
    (src.LangKey, src.ZhCN, src.ZhTW, src.En, src.Ja, src.Ko)
OUTPUT $action INTO @actionLog;

DECLARE @ins int = (SELECT COUNT(*) FROM @actionLog WHERE act = 'INSERT');
DECLARE @upd int = (SELECT COUNT(*) FROM @actionLog WHERE act = 'UPDATE');
PRINT CONCAT('=== Phase 7 Stock QC i18n 完了: INSERT=', @ins, ' UPDATE=', @upd, ' ===');

DROP TABLE #i18n;
