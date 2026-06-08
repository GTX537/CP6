/* ============================================================
 * Phase 10b Bridge Health Monitor i18n
 * ============================================================ */
SET NOCOUNT ON; SET XACT_ABORT ON;
PRINT '=== Phase 10b Bridge Health i18n seed start ===';

IF OBJECT_ID('tempdb..#i18n') IS NOT NULL DROP TABLE #i18n;
CREATE TABLE #i18n (
    LangKey nvarchar(200) NOT NULL PRIMARY KEY,
    ZhCN nvarchar(500), ZhTW nvarchar(500), En nvarchar(500), Ja nvarchar(500), Ko nvarchar(500)
);

INSERT INTO #i18n VALUES
  (N'wms.bridgeHealth.title',           N'桥接健康监控',       N'橋接健康監控',       N'Bridge health monitor', N'連携ヘルス監視',       N'브리지 상태 모니터'),
  (N'wms.bridgeHealth.window',          N'统计窗口',           N'統計視窗',           N'Window',                N'集計期間',             N'집계 구간'),
  (N'wms.bridgeHealth.refresh',         N'刷新',               N'重新整理',           N'Refresh',               N'更新',                 N'새로고침'),
  (N'wms.bridgeHealth.successRate',     N'24小时成功率',       N'24小時成功率',       N'24h success rate',      N'24h 成功率',           N'24시간 성공률'),
  (N'wms.bridgeHealth.queueDepth',      N'重试队列',           N'重試佇列',           N'Retry queue',           N'再試行キュー',         N'재시도 큐'),
  (N'wms.bridgeHealth.deadLetterCount', N'死信数量',           N'死信數量',           N'Dead letters',          N'デッドレター数',       N'데드레터 수'),
  (N'wms.bridgeHealth.hooks',           N'Hook汇总',           N'Hook彙總',           N'Hook summary',          N'Hook サマリ',          N'Hook 요약'),
  (N'wms.bridgeHealth.hookName',        N'Hook名称',           N'Hook名稱',           N'Hook',                  N'Hook 名',              N'Hook 이름'),
  (N'wms.bridgeHealth.sourceTarget',    N'来源→目标',          N'來源→目標',          N'Source→Target',         N'送信元→宛先',          N'소스→대상'),
  (N'wms.bridgeHealth.totalCount',      N'总数',               N'總數',               N'Total',                 N'合計',                 N'합계'),
  (N'wms.bridgeHealth.skippedCount',    N'跳过',               N'略過',               N'Skipped',               N'スキップ',             N'건너뜀'),
  (N'wms.bridgeHealth.failedCount',     N'失败',               N'失敗',               N'Failed',                N'失敗',                 N'실패'),
  (N'wms.bridgeHealth.deadCount',       N'死信',               N'死信',               N'Dead',                  N'Dead',                 N'데드'),
  (N'wms.bridgeHealth.latestDeadLetters', N'最新死信',         N'最新死信',           N'Latest dead letters',   N'最新デッドレター',     N'최신 데드레터'),
  (N'wms.bridgeHealth.status',          N'状态',               N'狀態',               N'Status',                N'ステータス',           N'상태'),
  (N'wms.bridgeHealth.status.SUCCESS',  N'成功',               N'成功',               N'Success',               N'成功',                 N'성공'),
  (N'wms.bridgeHealth.status.SKIPPED',  N'跳过',               N'略過',               N'Skipped',               N'スキップ',             N'건너뜀'),
  (N'wms.bridgeHealth.status.FAILED',   N'失败',               N'失敗',               N'Failed',                N'失敗',                 N'실패'),
  (N'wms.bridgeHealth.status.DEAD',     N'死信',               N'死信',               N'Dead',                  N'デッドレター',         N'데드레터'),
  (N'wms.bridgeHealth.status.COMPENSATED', N'已补偿',          N'已補償',             N'Compensated',           N'補償済',               N'보정 완료'),
  (N'wms.bridgeHealth.sourceNo',        N'来源单号',           N'來源單號',           N'Source No',             N'元伝票No',             N'소스 번호'),
  (N'wms.bridgeHealth.attempts',        N'尝试次数',           N'嘗試次數',           N'Attempts',              N'試行回数',             N'시도 횟수'),
  (N'wms.bridgeHealth.lastError',       N'最后错误',           N'最後錯誤',           N'Last error',            N'最終エラー',           N'마지막 오류'),
  (N'wms.bridgeHealth.createDate',      N'创建时间',           N'建立時間',           N'Created at',            N'作成日時',             N'생성 시간'),
  (N'wms.bridgeHealth.action',          N'操作',               N'操作',               N'Action',                N'操作',                 N'작업'),
  (N'wms.bridgeHealth.compensateBtn',   N'标记已补偿',         N'標記已補償',         N'Mark compensated',      N'補償済にする',         N'보정 완료 표시'),
  (N'wms.bridgeHealth.compensateConfirm', N'确认将该死信标记为已补偿？', N'確認將該死信標記為已補償？', N'Mark this dead letter as compensated?', N'このデッドレターを補償済にしますか？', N'이 데드레터를 보정 완료로 표시할까요?'),
  (N'wms.bridgeHealth.compensateSuccess', N'已标记为补偿完成', N'已標記為補償完成',   N'Marked compensated',    N'補償済にしました',     N'보정 완료로 표시했습니다');

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
PRINT CONCAT('=== Phase 10b Bridge Health i18n complete: INSERT=', @ins, ' UPDATE=', @upd, ' ===');

DROP TABLE #i18n;
