/* ============================================================
 * Phase 9 Material Shortage frontend i18n
 * ============================================================ */
SET NOCOUNT ON; SET XACT_ABORT ON;
PRINT '=== Phase 9 Material Shortage i18n seed start ===';

IF OBJECT_ID('tempdb..#i18n') IS NOT NULL DROP TABLE #i18n;
CREATE TABLE #i18n (
    LangKey nvarchar(200) NOT NULL PRIMARY KEY,
    ZhCN nvarchar(500), ZhTW nvarchar(500), En nvarchar(500), Ja nvarchar(500), Ko nvarchar(500)
);

INSERT INTO #i18n VALUES
  (N'wms.materialShortage.title', N'材料欠品管理', N'材料欠品管理', N'Material shortages', N'材料欠品管理', N'자재 부족 관리'),
  (N'wms.materialShortage.kpi.openCount', N'未处理欠品', N'未處理欠品', N'Open shortages', N'未対応欠品', N'미처리 부족'),
  (N'wms.materialShortage.search.workOrderNo', N'工单号', N'工單號', N'Work order', N'製造指図No', N'작업지시 번호'),
  (N'wms.materialShortage.search.status', N'状态', N'狀態', N'Status', N'ステータス', N'상태'),
  (N'wms.materialShortage.search.btnSearch', N'搜索', N'搜尋', N'Search', N'検索', N'검색'),
  (N'wms.materialShortage.search.btnReset', N'重置', N'重設', N'Reset', N'クリア', N'초기화'),
  (N'wms.materialShortage.status.ALL', N'全部', N'全部', N'All', N'すべて', N'전체'),
  (N'wms.materialShortage.status.OPEN', N'未处理', N'未處理', N'Open', N'未対応', N'미처리'),
  (N'wms.materialShortage.status.RESOLVED', N'已解决', N'已解決', N'Resolved', N'対応済', N'해결됨'),
  (N'wms.materialShortage.status.DISMISSED', N'已忽略', N'已忽略', N'Dismissed', N'不要', N'무시됨'),
  (N'wms.materialShortage.col.detectedAt', N'检出时间', N'檢出時間', N'Detected at', N'検出時刻', N'감지 시간'),
  (N'wms.materialShortage.col.wo', N'工单号', N'工單號', N'WO No', N'製造指図No', N'작업지시'),
  (N'wms.materialShortage.col.outbound', N'关联出库', N'關聯出庫', N'Related outbound', N'関連出庫', N'관련 출고'),
  (N'wms.materialShortage.col.product', N'产品', N'產品', N'Product', N'製品', N'제품'),
  (N'wms.materialShortage.col.lot', N'批次', N'批次', N'Lot', N'Lot', N'로트'),
  (N'wms.materialShortage.col.requiredQty', N'必要数量', N'必要數量', N'Required qty', N'必要数量', N'필요 수량'),
  (N'wms.materialShortage.col.availableQty', N'现有数量', N'現有數量', N'Available qty', N'有効数量', N'가용 수량'),
  (N'wms.materialShortage.col.shortQty', N'不足数量', N'不足數量', N'Short qty', N'欠品数量', N'부족 수량'),
  (N'wms.materialShortage.col.status', N'状态', N'狀態', N'Status', N'ステータス', N'상태'),
  (N'wms.materialShortage.col.remark', N'备注', N'備註', N'Remark', N'備考', N'비고'),
  (N'wms.materialShortage.col.action', N'操作', N'操作', N'Action', N'操作', N'작업'),
  (N'wms.materialShortage.btn.resolve', N'对应済', N'對應済', N'Resolve', N'対応済', N'해결'),
  (N'wms.materialShortage.btn.dismiss', N'对应不要', N'對應不要', N'Dismiss', N'対応不要', N'무시'),
  (N'wms.materialShortage.btn.confirm', N'确认', N'確認', N'Confirm', N'確定', N'확인'),
  (N'wms.materialShortage.btn.cancel', N'取消', N'取消', N'Cancel', N'キャンセル', N'취소'),
  (N'wms.materialShortage.dlg.resolveTitle', N'标记为已解决', N'標記為已解決', N'Mark resolved', N'対応済にする', N'해결로 표시'),
  (N'wms.materialShortage.dlg.dismissTitle', N'标记为无需对应', N'標記為無需對應', N'Mark dismissed', N'対応不要にする', N'무시로 표시'),
  (N'wms.materialShortage.dlg.remarkLabel', N'处理备注', N'處理備註', N'Action remark', N'処理備考', N'처리 비고'),
  (N'wms.materialShortage.dlg.remarkPlaceholder', N'请输入补充、采购或忽略原因', N'請輸入補充、採購或忽略原因', N'Enter replenishment, purchase, or dismissal reason', N'補充・購買・対応不要の理由を入力', N'보충, 구매 또는 무시 사유 입력'),
  (N'wms.materialShortage.msg.resolved', N'欠品已标记为解决', N'欠品已標記為解決', N'Shortage marked resolved', N'欠品を対応済にしました', N'부족이 해결 처리되었습니다'),
  (N'wms.materialShortage.msg.dismissed', N'欠品已标记为无需对应', N'欠品已標記為無需對應', N'Shortage dismissed', N'欠品を対応不要にしました', N'부족이 무시 처리되었습니다'),
  (N'wms.materialShortage.realtime.newShortageDetected', N'检测到新的材料欠品', N'偵測到新的材料欠品', N'New material shortage detected', N'新しい材料欠品を検出しました', N'새 자재 부족이 감지되었습니다');

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
PRINT CONCAT('=== Phase 9 Material Shortage i18n complete: INSERT=', @ins, ' UPDATE=', @upd, ' ===');

DROP TABLE #i18n;
