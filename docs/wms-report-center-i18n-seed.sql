/* ============================================================
 * WMS MSBBWM900 帳票センター i18n シードデータ
 *   wms.report.*  共通 + 5 报表
 * ============================================================ */
SET NOCOUNT ON; SET XACT_ABORT ON;
PRINT '=== WMS ReportCenter i18n シード開始 ===';

IF OBJECT_ID('tempdb..#i18n') IS NOT NULL DROP TABLE #i18n;
CREATE TABLE #i18n (
    LangKey nvarchar(200) NOT NULL PRIMARY KEY,
    ZhCN nvarchar(500), ZhTW nvarchar(500), En nvarchar(500), Ja nvarchar(500), Ko nvarchar(500)
);

INSERT INTO #i18n VALUES
  -- 共通
  (N'wms.report.title',             N'报表中心',           N'報表中心',           N'Report Center',      N'帳票センター',       N'리포트 센터'),
  (N'wms.report.fld.type',          N'报表种类',           N'報表種類',           N'Report Type',        N'帳票種類',           N'리포트 종류'),
  (N'wms.report.fld.yearMonth',     N'年月',               N'年月',               N'YearMonth',          N'年月',               N'연월'),
  (N'wms.report.fld.fromDate',      N'起始日',             N'起始日',             N'From Date',          N'開始日',             N'시작일'),
  (N'wms.report.fld.toDate',        N'结束日',             N'結束日',             N'To Date',            N'終了日',             N'종료일'),
  (N'wms.report.fld.analysisDays',  N'分析期间(日)',       N'分析期間(日)',       N'Analysis Days',      N'分析対象日数',       N'분석 일수'),
  (N'wms.report.fld.idleDays',      N'滞留阈值(日)',       N'滯留閾值(日)',       N'Idle Days',          N'滞留閾値(日)',       N'정체 임계값(일)'),
  (N'wms.report.fld.warehouse',     N'仓库',               N'倉庫',               N'Warehouse',          N'倉庫',               N'창고'),
  (N'wms.report.fld.product',       N'产品',               N'產品',               N'Product',            N'製品',               N'제품'),
  (N'wms.report.fld.rows',          N'记录数',             N'紀錄數',             N'Rows',               N'件数',               N'건수'),
  (N'wms.report.btn.run',           N'执行查询',           N'執行查詢',           N'Run',                N'実行',               N'실행'),
  (N'wms.report.btn.csv',           N'下载 CSV',           N'下載 CSV',           N'Download CSV',       N'CSV ダウンロード',   N'CSV 다운로드'),
  (N'wms.report.msg.noData',        N'无数据',             N'無資料',             N'No data',            N'データなし',         N'데이터 없음'),
  (N'wms.report.msg.maxLimit',      N'最多返回 5000 笔，超过请缩小期间', N'最多返回 5000 筆', N'Max 5000 rows; narrow the period if more', N'最大 5000 件まで、超える場合は期間を絞ってください', N'최대 5000건, 초과 시 기간 좁히기'),

  -- 报表种类名
  (N'wms.report.type.monthly',      N'库存月报',           N'庫存月報',           N'Monthly Stock',      N'在庫月報',           N'재고 월보'),
  (N'wms.report.type.abc',          N'ABC 分析',           N'ABC 分析',           N'ABC Analysis',       N'ABC 分析',           N'ABC 분석'),
  (N'wms.report.type.dead',         N'滞留品',             N'滯留品',             N'Dead Stock',         N'滞留品',             N'정체 재고'),
  (N'wms.report.type.inbound',      N'入库实绩',           N'入庫實績',           N'Inbound History',    N'入庫実績',           N'입고 실적'),
  (N'wms.report.type.outbound',     N'出库实绩',           N'出庫實績',           N'Outbound History',   N'出庫実績',           N'출고 실적'),

  -- Monthly fields
  (N'wms.report.monthly.physical',  N'物理库存',           N'物理庫存',           N'Physical',           N'物理在庫',           N'물리 재고'),
  (N'wms.report.monthly.allocated', N'已分配',             N'已分配',             N'Allocated',          N'引当済',             N'할당'),
  (N'wms.report.monthly.available', N'可用',               N'可用',               N'Available',          N'利用可能',           N'사용 가능'),
  (N'wms.report.monthly.lotCount',  N'批次数',             N'批次數',             N'Lots',               N'ロット数',           N'LOT 수'),
  (N'wms.report.monthly.value',     N'估算金额',           N'估算金額',           N'Est. Value',         N'推定金額',           N'추정 금액'),

  -- ABC fields
  (N'wms.report.abc.outCount',      N'出货次数',           N'出貨次數',           N'Out Count',          N'出庫回数',           N'출고 횟수'),
  (N'wms.report.abc.outQty',        N'出货数量',           N'出貨數量',           N'Out Qty',            N'出庫数量',           N'출고 수량'),
  (N'wms.report.abc.cumRatio',      N'累计%',              N'累計%',              N'Cum %',              N'累計%',              N'누적 %'),
  (N'wms.report.abc.rank',          N'ABC',                N'ABC',                N'ABC',                N'ABC',                N'ABC'),

  -- DeadStock fields
  (N'wms.report.dead.lastMoved',    N'最后动作',           N'最後動作',           N'Last Moved',         N'最終動作',           N'최종 동작'),
  (N'wms.report.dead.idleDays',     N'滞留天数',           N'滯留天數',           N'Idle Days',          N'滞留日数',           N'정체 일수'),

  -- History fields
  (N'wms.report.hist.txnNo',        N'交易NO',             N'交易NO',             N'Txn No',             N'TxnNO',              N'트랜잭션 NO'),
  (N'wms.report.hist.dateTime',     N'日时',               N'日時',               N'DateTime',           N'日時',               N'일시'),
  (N'wms.report.hist.relatedNo',    N'关联单据',           N'關聯單據',           N'Related No',         N'関連伝票NO',         N'관련 전표 NO'),
  (N'wms.report.hist.relatedType',  N'关联种类',           N'關聯種類',           N'Related Type',       N'関連区分',           N'관련 구분'),
  (N'wms.report.hist.operator',     N'操作员',             N'操作員',             N'Operator',           N'担当者',             N'담당자');

DECLARE @actionLog TABLE (act nvarchar(10));
MERGE Sys_Langs AS tgt USING #i18n AS src ON tgt.LangKey = src.LangKey
WHEN MATCHED THEN UPDATE SET tgt.ZhCN=src.ZhCN, tgt.ZhTW=src.ZhTW, tgt.En=src.En, tgt.Ja=src.Ja, tgt.Ko=src.Ko
WHEN NOT MATCHED BY TARGET THEN INSERT (LangKey,ZhCN,ZhTW,En,Ja,Ko) VALUES (src.LangKey,src.ZhCN,src.ZhTW,src.En,src.Ja,src.Ko)
OUTPUT $action INTO @actionLog;
DECLARE @ins INT=(SELECT COUNT(*) FROM @actionLog WHERE act='INSERT');
DECLARE @upd INT=(SELECT COUNT(*) FROM @actionLog WHERE act='UPDATE');
PRINT N'  追加: ' + CAST(@ins AS nvarchar(10)) + N' / 更新: ' + CAST(@upd AS nvarchar(10));
DROP TABLE #i18n;
PRINT '=== Done ===';
