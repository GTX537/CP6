/* ============================================================
 * WMS MSBBWM300 モバイル作業指示（RFハンディ）i18n シードデータ
 *   wms.mobile.tab.task / scan.* / act.* / fld.* / type.* / st.* / msg.*
 *   既存 wms-connectivity-i18n-seed.sql の wms.mobile.* を補完する。
 * ============================================================ */
SET NOCOUNT ON; SET XACT_ABORT ON;
PRINT '=== WMS Mobile (WM300) i18n seed start ===';

IF OBJECT_ID('tempdb..#i18n') IS NOT NULL DROP TABLE #i18n;
CREATE TABLE #i18n (
    LangKey nvarchar(200) NOT NULL PRIMARY KEY,
    ZhCN nvarchar(500), ZhTW nvarchar(500), En nvarchar(500), Ja nvarchar(500), Ko nvarchar(500)
);

INSERT INTO #i18n VALUES
  -- タブ
  (N'wms.mobile.tab.task',          N'作业指示',           N'作業指示',           N'Tasks',              N'作業指示',           N'작업 지시'),
  -- スキャン
  (N'wms.mobile.scan.title',        N'扫码',               N'掃碼',               N'Scan',               N'スキャン',           N'스캔'),
  (N'wms.mobile.scan.ph',           N'扫描/输入条码',      N'掃描/輸入條碼',      N'Scan/enter barcode', N'バーコードを入力',   N'바코드 입력'),
  (N'wms.mobile.scan.btn',          N'识别',               N'識別',               N'Resolve',            N'解決',               N'인식'),
  (N'wms.mobile.scan.location',     N'货位',               N'貨位',               N'Location',           N'ロケーション',       N'로케이션'),
  (N'wms.mobile.scan.product',      N'产品',               N'產品',               N'Product',            N'製品',               N'제품'),
  (N'wms.mobile.scan.unknown',      N'无法识别',           N'無法識別',           N'Not recognized',     N'該当なし',           N'인식 불가'),
  (N'wms.mobile.scan.blocked',      N'冻结中',             N'凍結中',             N'Blocked',            N'凍結中',             N'동결중'),
  (N'wms.mobile.scan.matched',      N'与指示一致',         N'與指示一致',         N'Matches task',       N'指示と一致',         N'지시 일치'),
  (N'wms.mobile.scan.unmatched',    N'与指示不一致',       N'與指示不一致',       N'Mismatch',           N'指示と不一致',       N'지시 불일치'),
  (N'wms.mobile.scan.onhand',       N'现有库存',           N'現有庫存',           N'On hand',            N'在庫',               N'재고'),
  (N'wms.mobile.scan.empty',        N'无库存',             N'無庫存',             N'No stock',           N'在庫なし',           N'재고 없음'),
  -- アクション
  (N'wms.mobile.act.start',         N'开始',               N'開始',               N'Start',              N'開始',               N'시작'),
  (N'wms.mobile.act.done',          N'完成',               N'完成',               N'Done',               N'完了',               N'완료'),
  (N'wms.mobile.act.cancel',        N'取消',               N'取消',               N'Cancel',             N'取消',               N'취소'),
  -- フィールド
  (N'wms.mobile.fld.qty',           N'数量',               N'數量',               N'Qty',                N'数量',               N'수량'),
  (N'wms.mobile.fld.from',          N'移出货位',           N'移出貨位',           N'From',               N'移動元',             N'출고 위치'),
  (N'wms.mobile.fld.to',            N'移入货位',           N'移入貨位',           N'To',                 N'移動先',             N'입고 위치'),
  (N'wms.mobile.fld.lot',           N'批次',               N'批次',               N'Lot',                N'ロット',             N'로트'),
  (N'wms.mobile.fld.product',       N'产品',               N'產品',               N'Product',            N'製品',               N'제품'),
  -- 作業種別
  (N'wms.mobile.type.RECEIVE',      N'入库检验',           N'入庫檢驗',           N'Receive',            N'入荷検品',           N'입고 검수'),
  (N'wms.mobile.type.PUTAWAY',      N'上架',               N'上架',               N'Putaway',            N'棚入れ',             N'적치'),
  (N'wms.mobile.type.PICK',         N'拣货',               N'揀貨',               N'Pick',               N'ピッキング',         N'피킹'),
  (N'wms.mobile.type.COUNT',        N'盘点',               N'盤點',               N'Count',              N'棚卸',               N'재고조사'),
  (N'wms.mobile.type.MOVE',         N'移库',               N'移庫',               N'Move',               N'棚移動',             N'이동'),
  (N'wms.mobile.type.LABEL',        N'打印标签',           N'列印標籤',           N'Label',              N'ラベル発行',         N'라벨 발행'),
  -- ステータス
  (N'wms.mobile.st.0',              N'未开始',             N'未開始',             N'Pending',            N'未着手',             N'대기'),
  (N'wms.mobile.st.1',              N'进行中',             N'進行中',             N'In Progress',        N'進行中',             N'진행중'),
  (N'wms.mobile.st.2',              N'已完成',             N'已完成',             N'Completed',          N'完了',               N'완료'),
  (N'wms.mobile.st.9',              N'已取消',             N'已取消',             N'Cancelled',          N'取消',               N'취소'),
  -- 完了ダイアログ・メッセージ
  (N'wms.mobile.done.title',        N'完成确认',           N'完成確認',           N'Confirm Done',       N'完了確認',           N'완료 확인'),
  (N'wms.mobile.msg.doneOk',        N'作业已完成',         N'作業已完成',         N'Task completed',     N'作業を完了しました', N'작업 완료'),
  (N'wms.mobile.msg.startOk',       N'作业已开始',         N'作業已開始',         N'Task started',       N'作業を開始しました', N'작업 시작'),
  (N'wms.mobile.msg.cancelOk',      N'作业已取消',         N'作業已取消',         N'Task cancelled',     N'作業を取消しました', N'작업 취소');

DECLARE @actionLog TABLE (act nvarchar(10));
MERGE Sys_Langs AS tgt USING #i18n AS src ON tgt.LangKey = src.LangKey
WHEN MATCHED THEN UPDATE SET tgt.ZhCN=src.ZhCN, tgt.ZhTW=src.ZhTW, tgt.En=src.En, tgt.Ja=src.Ja, tgt.Ko=src.Ko
WHEN NOT MATCHED BY TARGET THEN INSERT (LangKey,ZhCN,ZhTW,En,Ja,Ko) VALUES (src.LangKey,src.ZhCN,src.ZhTW,src.En,src.Ja,src.Ko)
OUTPUT $action INTO @actionLog;
DECLARE @ins INT=(SELECT COUNT(*) FROM @actionLog WHERE act='INSERT');
DECLARE @upd INT=(SELECT COUNT(*) FROM @actionLog WHERE act='UPDATE');
PRINT N'  add: ' + CAST(@ins AS nvarchar(10)) + N' / update: ' + CAST(@upd AS nvarchar(10));
DROP TABLE #i18n;
PRINT '=== Done ===';
