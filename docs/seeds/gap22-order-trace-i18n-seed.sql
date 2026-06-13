/* ============================================================
 * Gap 2.2 Order Trace i18n seed
 * ============================================================ */
SET NOCOUNT ON; SET XACT_ABORT ON;
PRINT '=== Gap 2.2 Order Trace i18n seed start ===';

IF OBJECT_ID('tempdb..#i18n') IS NOT NULL DROP TABLE #i18n;
CREATE TABLE #i18n (
    LangKey nvarchar(200) NOT NULL PRIMARY KEY,
    ZhCN nvarchar(500), ZhTW nvarchar(500), En nvarchar(500), Ja nvarchar(500), Ko nvarchar(500)
);

INSERT INTO #i18n VALUES
  (N'erp.orderTrace.title', N'Order trace', N'Order trace', N'Order trace', N'Order trace', N'Order trace'),
  (N'erp.orderTrace.label.webOrderNo', N'Web order no', N'Web order no', N'Web order no', N'Web order no', N'Web order no'),
  (N'erp.orderTrace.label.customer', N'Customer', N'Customer', N'Customer', N'Customer', N'Customer'),
  (N'erp.orderTrace.label.orderDate', N'Order date', N'Order date', N'Order date', N'Order date', N'Order date'),
  (N'erp.orderTrace.summary.totalEvents', N'Total events', N'Total events', N'Total events', N'Total events', N'Total events'),
  (N'erp.orderTrace.summary.success', N'Success', N'Success', N'Success', N'Success', N'Success'),
  (N'erp.orderTrace.summary.failed', N'Failed', N'Failed', N'Failed', N'Failed', N'Failed'),
  (N'erp.orderTrace.summary.skipped', N'Skipped', N'Skipped', N'Skipped', N'Skipped', N'Skipped'),
  (N'erp.orderTrace.summary.dead', N'Dead', N'Dead', N'Dead', N'Dead', N'Dead'),
  (N'erp.orderTrace.summary.distinctChains', N'Trace chains', N'Trace chains', N'Trace chains', N'Trace chains', N'Trace chains'),
  (N'erp.orderTrace.timeline.empty', N'No Bridge Hook activity recorded for this order', N'No Bridge Hook activity recorded for this order', N'No Bridge Hook activity recorded for this order', N'No Bridge Hook activity recorded for this order', N'No Bridge Hook activity recorded for this order'),
  (N'erp.orderTrace.group.byCorrelation', N'Group by correlation', N'Group by correlation', N'Group by correlation', N'Group by correlation', N'Group by correlation'),
  (N'erp.orderTrace.status.SUCCESS', N'Success', N'Success', N'Success', N'Success', N'Success'),
  (N'erp.orderTrace.status.SKIPPED', N'Skipped', N'Skipped', N'Skipped', N'Skipped', N'Skipped'),
  (N'erp.orderTrace.status.FAILED', N'Failed', N'Failed', N'Failed', N'Failed', N'Failed'),
  (N'erp.orderTrace.status.DEAD', N'Dead', N'Dead', N'Dead', N'Dead', N'Dead'),
  (N'erp.orderTrace.status.PENDING', N'Pending', N'Pending', N'Pending', N'Pending', N'Pending'),
  (N'erp.orderTrace.status.COMPENSATED', N'Compensated', N'Compensated', N'Compensated', N'Compensated', N'Compensated'),
  (N'erp.orderTrace.kindLabel.BRIDGE_HOOK', N'Bridge hook timeline', N'Bridge hook timeline', N'Bridge hook timeline', N'Bridge hook timeline', N'Bridge hook timeline'),
  (N'erp.orderTrace.btn.copyCorrelationId', N'Copy chain id', N'Copy chain id', N'Copy chain id', N'Copy chain id', N'Copy chain id'),
  (N'erp.orderTrace.btn.search', N'Search', N'Search', N'Search', N'Search', N'Search'),
  (N'erp.orderTrace.btn.trace', N'Trace', N'Trace', N'Trace', N'Trace', N'Trace'),
  (N'erp.orderTrace.msg.copied', N'Copied', N'Copied', N'Copied', N'Copied', N'Copied');

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
PRINT CONCAT('=== Gap 2.2 Order Trace i18n complete: INSERT=', @ins, ' UPDATE=', @upd, ' ===');

DROP TABLE #i18n;
