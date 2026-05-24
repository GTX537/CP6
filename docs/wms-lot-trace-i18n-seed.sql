/* ============================================================
 * WMS MSBBWM160 ロット追溯 i18n シードデータ
 * ============================================================ */
SET NOCOUNT ON; SET XACT_ABORT ON;
PRINT '=== WMS LotTrace i18n シード開始 ===';

IF OBJECT_ID('tempdb..#i18n') IS NOT NULL DROP TABLE #i18n;
CREATE TABLE #i18n (
    LangKey nvarchar(200) NOT NULL PRIMARY KEY,
    ZhCN nvarchar(500), ZhTW nvarchar(500), En nvarchar(500), Ja nvarchar(500), Ko nvarchar(500)
);

INSERT INTO #i18n VALUES
  (N'wms.lotTrace.title',              N'批次追溯',            N'批次追溯',            N'Lot Tracing',          N'ロット追溯',          N'로트 추적'),
  (N'wms.lotTrace.fld.direction',      N'追溯方向',            N'追溯方向',            N'Direction',            N'追溯方向',            N'추적 방향'),
  (N'wms.lotTrace.dir.forward',        N'顺向(→ 出货客户)',    N'順向(→ 出貨客戶)',    N'Forward (→ Customers)', N'順追溯(→ 出荷顧客)', N'순방향(→ 출하 고객)'),
  (N'wms.lotTrace.dir.backward',       N'逆向(← 仕入元)',      N'逆向(← 進貨來源)',    N'Backward (← Suppliers)', N'逆追溯(← 仕入先)', N'역방향(← 공급사)'),
  (N'wms.lotTrace.btn.trace',          N'执行追溯',            N'執行追溯',            N'Trace',                N'追溯実行',            N'추적 실행'),
  (N'wms.lotTrace.btn.summary',        N'查看在库摘要',        N'查看庫存摘要',        N'Stock Summary',        N'在庫サマリ',          N'재고 요약'),
  (N'wms.lotTrace.btn.setRecall',      N'设为召回',            N'設為召回',            N'Set Recall',           N'リコール対象に設定',  N'리콜 대상으로 설정'),
  (N'wms.lotTrace.btn.clearRecall',    N'解除召回',            N'解除召回',            N'Clear Recall',         N'リコール解除',        N'리콜 해제'),
  (N'wms.lotTrace.summary.title',      N'在库摘要',            N'庫存摘要',            N'Stock Summary',        N'在庫サマリ',          N'재고 요약'),
  (N'wms.lotTrace.summary.locationCount', N'分布位置数',       N'分佈位置數',          N'Locations',            N'分散ロケ数',          N'분포 위치 수'),
  (N'wms.lotTrace.summary.recalled',   N'已召回',              N'已召回',              N'Recalled',             N'回収対象',            N'리콜됨'),
  (N'wms.lotTrace.affected.customers', N'影响客户',            N'影響客戶',            N'Affected Customers',   N'影響顧客',            N'영향 받은 고객'),
  (N'wms.lotTrace.affected.suppliers', N'影响仕入元',          N'影響進貨來源',        N'Affected Suppliers',   N'影響仕入先',          N'영향 받은 공급사'),
  (N'wms.lotTrace.affected.none',      N'无影响范围',          N'無影響範圍',          N'No affected entities', N'影響範囲なし',        N'영향 범위 없음'),
  (N'wms.lotTrace.nodes.title',        N'变动履历(时序)',      N'變動履歷(時序)',      N'Transaction Timeline', N'変動履歴(時系列)',    N'변동 이력(시계열)'),
  (N'wms.lotTrace.col.at',             N'时点',                N'時點',                N'At',                   N'発生時刻',            N'발생 시각'),
  (N'wms.lotTrace.msg.recallApplied',  N'已对 {n} 笔库存设置召回标记', N'已對 {n} 筆庫存設置召回標記', N'Recall flag set on {n} stock rows', N'{n} 件の在庫にリコール反映', N'{n}건의 재고에 리콜 적용');

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
