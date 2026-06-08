/* ============================================================
 * Gap 4.1 Backorder i18n seed
 * ============================================================ */
SET NOCOUNT ON; SET XACT_ABORT ON;
PRINT '=== Gap 4.1 Backorder i18n seed start ===';

IF OBJECT_ID('tempdb..#i18n') IS NOT NULL DROP TABLE #i18n;
CREATE TABLE #i18n (
    LangKey nvarchar(200) NOT NULL PRIMARY KEY,
    ZhCN nvarchar(500), ZhTW nvarchar(500), En nvarchar(500), Ja nvarchar(500), Ko nvarchar(500)
);

INSERT INTO #i18n VALUES
  (N'erp.backorder.title', N'Backorder queue', N'Backorder queue', N'Backorder queue', N'Backorder queue', N'Backorder queue'),
  (N'erp.backorder.search.customer', N'Customer', N'Customer', N'Customer', N'Customer', N'Customer'),
  (N'erp.backorder.search.dateFrom', N'Last ship from', N'Last ship from', N'Last ship from', N'Last ship from', N'Last ship from'),
  (N'erp.backorder.search.dateTo', N'Last ship to', N'Last ship to', N'Last ship to', N'Last ship to', N'Last ship to'),
  (N'erp.backorder.btn.search', N'Search', N'Search', N'Search', N'Search', N'Search'),
  (N'erp.backorder.btn.reset', N'Reset', N'Reset', N'Reset', N'Reset', N'Reset'),
  (N'erp.backorder.btn.close', N'Close remaining', N'Close remaining', N'Close remaining', N'Close remaining', N'Close remaining'),
  (N'erp.backorder.btn.split', N'Split order', N'Split order', N'Split order', N'Split order', N'Split order'),
  (N'erp.backorder.btn.cancel', N'Cancel', N'Cancel', N'Cancel', N'Cancel', N'Cancel'),
  (N'erp.backorder.btn.confirm', N'Confirm', N'Confirm', N'Confirm', N'Confirm', N'Confirm'),
  (N'erp.backorder.total', N'Total: {n}', N'Total: {n}', N'Total: {n}', N'Total: {n}', N'Total: {n}'),
  (N'erp.backorder.empty', N'No open backorders', N'No open backorders', N'No open backorders', N'No open backorders', N'No open backorders'),
  (N'erp.backorder.col.webOrderNo', N'Web order no', N'Web order no', N'Web order no', N'Web order no', N'Web order no'),
  (N'erp.backorder.col.customer', N'Customer', N'Customer', N'Customer', N'Customer', N'Customer'),
  (N'erp.backorder.col.detailNo', N'Detail', N'Detail', N'Detail', N'Detail', N'Detail'),
  (N'erp.backorder.col.product', N'Product', N'Product', N'Product', N'Product', N'Product'),
  (N'erp.backorder.col.orderedQty', N'Ordered', N'Ordered', N'Ordered', N'Ordered', N'Ordered'),
  (N'erp.backorder.col.shippedQty', N'Shipped', N'Shipped', N'Shipped', N'Shipped', N'Shipped'),
  (N'erp.backorder.col.backorderQty', N'Closed BO', N'Closed BO', N'Closed BO', N'Closed BO', N'Closed BO'),
  (N'erp.backorder.col.remainingQty', N'Remaining', N'Remaining', N'Remaining', N'Remaining', N'Remaining'),
  (N'erp.backorder.col.lastShipDate', N'Last ship', N'Last ship', N'Last ship', N'Last ship', N'Last ship'),
  (N'erp.backorder.col.actions', N'Actions', N'Actions', N'Actions', N'Actions', N'Actions'),
  (N'erp.backorder.dialog.closeTitle', N'Close remaining quantity', N'Close remaining quantity', N'Close remaining quantity', N'Close remaining quantity', N'Close remaining quantity'),
  (N'erp.backorder.dialog.splitTitle', N'Split remaining to new order', N'Split remaining to new order', N'Split remaining to new order', N'Split remaining to new order', N'Split remaining to new order'),
  (N'erp.backorder.dialog.reason', N'Reason', N'Reason', N'Reason', N'Reason', N'Reason'),
  (N'erp.backorder.msg.reasonRequired', N'Reason is required', N'Reason is required', N'Reason is required', N'Reason is required', N'Reason is required'),
  (N'erp.backorder.msg.closed', N'Remaining quantity closed', N'Remaining quantity closed', N'Remaining quantity closed', N'Remaining quantity closed', N'Remaining quantity closed'),
  (N'erp.backorder.msg.split', N'New order created: {no}', N'New order created: {no}', N'New order created: {no}', N'New order created: {no}', N'New order created: {no}');

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
PRINT CONCAT('=== Gap 4.1 Backorder i18n complete: INSERT=', @ins, ' UPDATE=', @upd, ' ===');

DROP TABLE #i18n;
