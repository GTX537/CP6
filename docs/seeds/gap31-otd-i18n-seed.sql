/* ============================================================
 * Gap 3.1 OTD Report i18n seed
 * ============================================================ */
SET NOCOUNT ON; SET XACT_ABORT ON;
PRINT '=== Gap 3.1 OTD Report i18n seed start ===';

IF OBJECT_ID('tempdb..#i18n') IS NOT NULL DROP TABLE #i18n;
CREATE TABLE #i18n (
    LangKey nvarchar(200) NOT NULL PRIMARY KEY,
    ZhCN nvarchar(500), ZhTW nvarchar(500), En nvarchar(500), Ja nvarchar(500), Ko nvarchar(500)
);

INSERT INTO #i18n VALUES
  (N'erp.otdReport.title', N'On-time delivery report', N'On-time delivery report', N'On-time delivery report', N'On-time delivery report', N'On-time delivery report'),
  (N'erp.otdReport.filter.dateFrom', N'Order date from', N'Order date from', N'Order date from', N'Order date from', N'Order date from'),
  (N'erp.otdReport.filter.dateTo', N'Order date to', N'Order date to', N'Order date to', N'Order date to', N'Order date to'),
  (N'erp.otdReport.filter.groupBy.label', N'Group by', N'Group by', N'Group by', N'Group by', N'Group by'),
  (N'erp.otdReport.filter.groupBy.customer', N'Customer', N'Customer', N'Customer', N'Customer', N'Customer'),
  (N'erp.otdReport.filter.groupBy.month', N'Month', N'Month', N'Month', N'Month', N'Month'),
  (N'erp.otdReport.filter.customer', N'Customer', N'Customer', N'Customer', N'Customer', N'Customer'),
  (N'erp.otdReport.btn.search', N'Search', N'Search', N'Search', N'Search', N'Search'),
  (N'erp.otdReport.btn.exportCsv', N'Export CSV', N'Export CSV', N'Export CSV', N'Export CSV', N'Export CSV'),
  (N'erp.otdReport.btn.reset', N'Reset', N'Reset', N'Reset', N'Reset', N'Reset'),
  (N'erp.otdReport.kpi.overallRate', N'Overall on-time rate', N'Overall on-time rate', N'Overall on-time rate', N'Overall on-time rate', N'Overall on-time rate'),
  (N'erp.otdReport.kpi.totalShipped', N'Total shipped orders', N'Total shipped orders', N'Total shipped orders', N'Total shipped orders', N'Total shipped orders'),
  (N'erp.otdReport.kpi.lateCount', N'Late orders', N'Late orders', N'Late orders', N'Late orders', N'Late orders'),
  (N'erp.otdReport.col.group', N'Group', N'Group', N'Group', N'Group', N'Group'),
  (N'erp.otdReport.col.total', N'Total', N'Total', N'Total', N'Total', N'Total'),
  (N'erp.otdReport.col.onTime', N'On-time', N'On-time', N'On-time', N'On-time', N'On-time'),
  (N'erp.otdReport.col.late', N'Late', N'Late', N'Late', N'Late', N'Late'),
  (N'erp.otdReport.col.onTimeRate', N'On-time %', N'On-time %', N'On-time %', N'On-time %', N'On-time %'),
  (N'erp.otdReport.col.avgLateDays', N'Avg late days', N'Avg late days', N'Avg late days', N'Avg late days', N'Avg late days'),
  (N'erp.otdReport.chart.title', N'On-time rate by group', N'On-time rate by group', N'On-time rate by group', N'On-time rate by group', N'On-time rate by group'),
  (N'erp.otdReport.msg.exported', N'CSV exported', N'CSV exported', N'CSV exported', N'CSV exported', N'CSV exported');

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
PRINT CONCAT('=== Gap 3.1 OTD Report i18n complete: INSERT=', @ins, ' UPDATE=', @upd, ' ===');

DROP TABLE #i18n;
