/* ============================================================
 * Phase 10a CreditNote frontend i18n
 * ============================================================ */
SET NOCOUNT ON; SET XACT_ABORT ON;
PRINT '=== Phase 10a CreditNote i18n seed start ===';

IF OBJECT_ID('tempdb..#i18n') IS NOT NULL DROP TABLE #i18n;
CREATE TABLE #i18n (
    LangKey nvarchar(200) NOT NULL PRIMARY KEY,
    ZhCN nvarchar(500), ZhTW nvarchar(500), En nvarchar(500), Ja nvarchar(500), Ko nvarchar(500)
);

INSERT INTO #i18n VALUES
  (N'erp.creditNote.title', N'贷项通知单', N'貸項通知單', N'Credit notes', N'クレジットノート', N'크레딧 노트'),
  (N'erp.creditNote.total', N'合计: {n}', N'合計: {n}', N'Total: {n}', N'合計: {n}', N'합계: {n}'),
  (N'erp.creditNote.search.customer', N'客户', N'客戶', N'Customer', N'得意先', N'고객'),
  (N'erp.creditNote.search.webOrderNo', N'受注号', N'受注號', N'Order no', N'受注No', N'주문번호'),
  (N'erp.creditNote.search.type', N'类型', N'類型', N'Type', N'種別', N'유형'),
  (N'erp.creditNote.search.dateFrom', N'起票日从', N'起票日從', N'Issue date from', N'起票日From', N'발행일 시작'),
  (N'erp.creditNote.search.dateTo', N'起票日到', N'起票日到', N'Issue date to', N'起票日To', N'발행일 종료'),
  (N'erp.creditNote.btn.search', N'搜索', N'搜尋', N'Search', N'検索', N'검색'),
  (N'erp.creditNote.btn.reset', N'重置', N'重設', N'Reset', N'クリア', N'초기화'),
  (N'erp.creditNote.type.ALL', N'全部', N'全部', N'All', N'すべて', N'전체'),
  (N'erp.creditNote.type.REFUND', N'退款', N'退款', N'Refund', N'返金', N'환불'),
  (N'erp.creditNote.type.EXCHANGE', N'换货', N'換貨', N'Exchange', N'交換', N'교환'),
  (N'erp.creditNote.type.SCRAP', N'报废', N'報廢', N'Scrap', N'廃棄', N'폐기'),
  (N'erp.creditNote.col.issueDate', N'起票日', N'起票日', N'Issue date', N'起票日', N'발행일'),
  (N'erp.creditNote.col.no', N'CN号', N'CN號', N'CN No', N'CN No', N'CN 번호'),
  (N'erp.creditNote.col.webOrderNo', N'受注号', N'受注號', N'Order no', N'受注No', N'주문번호'),
  (N'erp.creditNote.col.rmaNo', N'RMA号', N'RMA號', N'RMA No', N'RMA No', N'RMA 번호'),
  (N'erp.creditNote.col.customer', N'客户', N'客戶', N'Customer', N'得意先', N'고객'),
  (N'erp.creditNote.col.type', N'类型', N'類型', N'Type', N'種別', N'유형'),
  (N'erp.creditNote.col.product', N'产品', N'產品', N'Product', N'製品', N'제품'),
  (N'erp.creditNote.col.qty', N'数量', N'數量', N'Qty', N'数量', N'수량'),
  (N'erp.creditNote.col.amount', N'金额', N'金額', N'Amount', N'金額', N'금액'),
  (N'erp.creditNote.col.reason', N'理由', N'理由', N'Reason', N'理由', N'사유');

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
PRINT CONCAT('=== Phase 10a CreditNote i18n complete: INSERT=', @ins, ' UPDATE=', @upd, ' ===');

DROP TABLE #i18n;
