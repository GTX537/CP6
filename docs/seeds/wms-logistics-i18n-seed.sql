/* ============================================================
 * WMS MSBBWM110/120/130 Logistics i18n シードデータ
 * ============================================================ */
SET NOCOUNT ON; SET XACT_ABORT ON;
PRINT '=== WMS Logistics i18n シード開始 ===';

IF OBJECT_ID('tempdb..#i18n') IS NOT NULL DROP TABLE #i18n;
CREATE TABLE #i18n (
    LangKey nvarchar(200) NOT NULL PRIMARY KEY,
    ZhCN nvarchar(500), ZhTW nvarchar(500), En nvarchar(500), Ja nvarchar(500), Ko nvarchar(500)
);

-- ─── CrossDock（wms.xdock.*）───
INSERT INTO #i18n VALUES
  (N'wms.xdock.title',           N'越库直通',         N'越庫直通',         N'Cross-Docking',     N'クロスドッキング',   N'크로스 도킹'),
  (N'wms.xdock.fld.no',          N'越库指示NO',       N'越庫指示NO',       N'XDock No',          N'XDock NO',           N'XDock NO'),
  (N'wms.xdock.fld.tempLoc',     N'临时货位',         N'臨時庫位',         N'Temp Location',     N'仮置きロケ',         N'임시 로케'),
  (N'wms.xdock.fld.fromDock',    N'到达月台',         N'到達月台',         N'From Dock',         N'到着ドック',         N'도착 도크'),
  (N'wms.xdock.fld.toDock',      N'出货月台',         N'出貨月台',         N'To Dock',           N'出荷ドック',         N'출하 도크'),
  (N'wms.xdock.status.planned',  N'计划',             N'計劃',             N'Planned',           N'計画',               N'계획'),
  (N'wms.xdock.status.executed', N'已执行',           N'已執行',           N'Executed',          N'実行済',             N'실행됨'),
  (N'wms.xdock.status.cancelled', N'已取消',          N'已取消',           N'Cancelled',         N'取消',               N'취소됨'),
  (N'wms.xdock.dlg.create',      N'新建越库指示',     N'新增越庫指示',     N'New Cross-Dock',    N'クロスドック新規',   N'크로스 도크 신규'),
  (N'wms.xdock.msg.executeAsk',  N'要执行越库吗？IN + OUT 会一对发出', N'要執行越庫嗎？IN + OUT 會一對發出', N'Execute cross-dock? IN + OUT will be issued.', N'クロスドック実行しますか？IN + OUT を一対で発行します', N'크로스 도크 실행하시겠습니까? IN + OUT 한 쌍 발행됩니다');

-- ─── Replenish（wms.replenish.*）───
INSERT INTO #i18n VALUES
  (N'wms.replenish.title',          N'补货指令',         N'補貨指令',         N'Replenishment',     N'補充指示',           N'보충 지시'),
  (N'wms.replenish.fld.no',         N'补货NO',           N'補貨NO',           N'Replenish No',      N'補充NO',             N'보충NO'),
  (N'wms.replenish.fld.priority',   N'优先级',           N'優先級',           N'Priority',          N'優先度',             N'우선순위'),
  (N'wms.replenish.fld.trigger',    N'触发',             N'觸發',             N'Trigger',           N'トリガ',             N'트리거'),
  (N'wms.replenish.fld.fromLoc',    N'补货来源',         N'補貨來源',         N'From',              N'補充元(保管棚)',     N'보충 출처(보관)'),
  (N'wms.replenish.fld.toLoc',      N'补货目的',         N'補貨目的',         N'To',                N'補充先(ピッキング棚)', N'보충 대상(피킹)'),
  (N'wms.replenish.fld.minQty',     N'下限阈值',         N'下限閾值',         N'Min Threshold',     N'下限閾値',           N'하한 임계값'),
  (N'wms.replenish.priority.urgent', N'紧急',            N'緊急',             N'Urgent',            N'至急',               N'긴급'),
  (N'wms.replenish.priority.normal', N'普通',            N'普通',             N'Normal',            N'通常',               N'보통'),
  (N'wms.replenish.trigger.batch',  N'批量',             N'批量',             N'Batch',             N'バッチ',             N'배치'),
  (N'wms.replenish.trigger.manual', N'手动',             N'手動',             N'Manual',            N'手動',               N'수동'),
  (N'wms.replenish.trigger.alert',  N'警报',             N'警報',             N'Alert',             N'警報',               N'경보'),
  (N'wms.replenish.status.pending', N'未执行',           N'未執行',           N'Pending',           N'未実行',             N'미실행'),
  (N'wms.replenish.status.executed', N'已执行',          N'已執行',           N'Executed',          N'実行済',             N'실행됨'),
  (N'wms.replenish.status.cancelled', N'已取消',         N'已取消',           N'Cancelled',         N'取消',               N'취소됨'),
  (N'wms.replenish.dlg.create',     N'新建补货',         N'新增補貨',         N'New Replenish',     N'補充新規',           N'보충 신규'),
  (N'wms.replenish.dlg.batch',      N'批量生成补货',     N'批量產生補貨',     N'Generate Batch',    N'バッチ生成',         N'배치 생성'),
  (N'wms.replenish.btn.genBatch',   N'批量生成',         N'批量產生',         N'Gen Batch',         N'バッチ生成',         N'배치 생성'),
  (N'wms.replenish.msg.batchHint',  N'PIK-* ピッキング棚で MinQty 未満 + RES-* 保管棚に在庫あり の組合せで一括生成', N'PIK-* 揀貨棚で MinQty 未滿 + RES-* 保管棚に在庫あり', N'For PIK-* shelves below min qty with RES-* stock available', N'PIK-* ピッキング棚で MinQty 未満 + RES-* 保管棚に在庫あり の組合せで一括生成', N'PIK-* 피킹 선반에서 MinQty 미만 + RES-* 보관 선반에 재고 있음 조합'),
  (N'wms.replenish.msg.batchGen',   N'已生成 {n} 笔补货指示', N'已產生 {n} 筆補貨指示', N'{n} replenish orders generated', N'{n} 件の補充指示を生成', N'{n}건의 보충 지시 생성'),
  (N'wms.replenish.msg.executeAsk', N'要执行补货吗？MOVE 一对发出', N'要執行補貨嗎？MOVE 一對發出', N'Execute replenish? MOVE pair will be issued.', N'補充を実行しますか？MOVE ペアを発行します', N'보충 실행하시겠습니까? MOVE 쌍 발행됩니다');

-- ─── Slotting（wms.slotting.*）───
INSERT INTO #i18n VALUES
  (N'wms.slotting.title',            N'货位优化',        N'貨位優化',        N'Slotting',           N'スロッティング',      N'슬로팅'),
  (N'wms.slotting.fld.no',           N'方案NO',          N'方案NO',          N'Plan No',            N'方案NO',              N'방안NO'),
  (N'wms.slotting.fld.analysisDays', N'分析期间(日)',    N'分析期間(日)',    N'Analysis Days',      N'分析対象日数',        N'분석 일수'),
  (N'wms.slotting.fld.sampleCount',  N'样本量',          N'樣本量',          N'Sample Count',       N'サンプル件数',        N'샘플 수'),
  (N'wms.slotting.fld.recCount',     N'推荐数',          N'推薦數',          N'Recommendations',    N'推奨件数',            N'추천 수'),
  (N'wms.slotting.fld.analyzedAt',   N'分析时间',        N'分析時間',        N'Analyzed At',        N'分析時刻',            N'분석 시각'),
  (N'wms.slotting.status.analyzing', N'分析中',          N'分析中',          N'Analyzing',          N'分析中',              N'분석 중'),
  (N'wms.slotting.status.recommended', N'已推荐',        N'已推薦',          N'Recommended',        N'推奨完了',            N'추천 완료'),
  (N'wms.slotting.status.approved',  N'已批准',          N'已批准',          N'Approved',           N'承認済',              N'승인됨'),
  (N'wms.slotting.status.cancelled', N'已取消',          N'已取消',          N'Cancelled',          N'取消',                N'취소됨'),
  (N'wms.slotting.rec.title',        N'推荐明细',        N'推薦明細',        N'Recommendations',    N'推奨明細',            N'추천 명세'),
  (N'wms.slotting.rec.rank',         N'ABC',             N'ABC',             N'ABC',                N'ABC',                 N'ABC'),
  (N'wms.slotting.rec.outCount',     N'出货次数',        N'出貨次數',        N'Out Count',          N'出庫回数',            N'출고 횟수'),
  (N'wms.slotting.rec.outQty',       N'出货数量',        N'出貨數量',        N'Out Qty',            N'出庫数量',            N'출고 수량'),
  (N'wms.slotting.rec.currentLoc',   N'当前货位',        N'目前庫位',        N'Current Loc',        N'現在ロケ',            N'현재 로케'),
  (N'wms.slotting.rec.recPattern',   N'推荐货位',        N'推薦庫位',        N'Recommended',        N'推奨ロケ',            N'추천 로케'),
  (N'wms.slotting.rec.needsMove',    N'需要移动',        N'需要移動',        N'Needs Move',         N'移動候補',            N'이동 후보'),
  (N'wms.slotting.btn.analyze',      N'执行分析',        N'執行分析',        N'Analyze',            N'分析実行',            N'분석 실행'),
  (N'wms.slotting.unit.day',         N'日',              N'日',              N'days',               N'日',                  N'일'),
  (N'wms.slotting.msg.analyzeHint',  N'分析过去 N 天 OUT 事务、按出货频率 ABC 分级（80/15/5），推荐货位前缀', N'分析過去 N 天 OUT 事務、按出貨頻率 ABC 分級', N'Analyze past N days OUT txns, ABC rank by frequency (80/15/5)', N'過去 N 日の OUT トランザクションを集計、出庫頻度で ABC ランク（80/15/5）', N'과거 N일 OUT 트랜잭션을 집계, 출고 빈도로 ABC 랭크'),
  (N'wms.slotting.msg.approveAsk',   N'要批准这个方案吗？后续棚移動需另外执行', N'要批准這個方案嗎？後續棚移動需另外執行', N'Approve this plan? Stock relocations need separate execution.', N'この方案を承認しますか？棚移動は別途実行が必要', N'이 방안을 승인합니까? 선반 이동은 별도 실행 필요');

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
