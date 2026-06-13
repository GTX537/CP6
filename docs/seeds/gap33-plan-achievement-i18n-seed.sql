/* ============================================================
 * Gap 3.3 — 生産計画達成率レポート frontend i18n + nav.313
 * ============================================================ */
SET NOCOUNT ON; SET XACT_ABORT ON;
PRINT '=== Gap 3.3 Plan Achievement i18n seed start ===';

IF OBJECT_ID('tempdb..#i18n') IS NOT NULL DROP TABLE #i18n;
CREATE TABLE #i18n (
    LangKey nvarchar(200) NOT NULL PRIMARY KEY,
    ZhCN nvarchar(500), ZhTW nvarchar(500), En nvarchar(500), Ja nvarchar(500), Ko nvarchar(500)
);

INSERT INTO #i18n VALUES
  (N'nav.313', N'生产计划达成率', N'生產計劃達成率', N'Plan achievement', N'生産計画達成率', N'생산계획 달성률'),
  (N'mes.planAchievement.title', N'生产计划达成率报表', N'生產計劃達成率報表', N'Production plan achievement', N'生産計画達成率レポート', N'생산계획 달성률 보고서'),
  (N'mes.planAchievement.filter.dateFrom', N'起始日', N'起始日', N'From', N'開始日', N'시작일'),
  (N'mes.planAchievement.filter.dateTo', N'结束日', N'結束日', N'To', N'終了日', N'종료일'),
  (N'mes.planAchievement.filter.groupBy.label', N'分组', N'分組', N'Group by', N'集計軸', N'그룹화'),
  (N'mes.planAchievement.filter.groupBy.product', N'按产品', N'按產品', N'Product', N'製品', N'제품'),
  (N'mes.planAchievement.filter.groupBy.month', N'按月', N'按月', N'Month', N'月', N'월'),
  (N'mes.planAchievement.filter.groupBy.customer', N'按客户', N'按客戶', N'Customer', N'得意先', N'고객'),
  (N'mes.planAchievement.filter.product', N'产品CD', N'產品CD', N'Product', N'製品CD', N'제품 코드'),
  (N'mes.planAchievement.filter.onlyCompleted', N'仅完成', N'僅完成', N'Completed only', N'完了のみ', N'완료만'),
  (N'mes.planAchievement.btn.search', N'查询', N'查詢', N'Search', N'検索', N'조회'),
  (N'mes.planAchievement.btn.exportCsv', N'导出CSV', N'匯出CSV', N'Export CSV', N'CSV出力', N'CSV 내보내기'),
  (N'mes.planAchievement.btn.reset', N'重置', N'重設', N'Reset', N'クリア', N'초기화'),
  (N'mes.planAchievement.kpi.overallRate', N'整体达成率', N'整體達成率', N'Achievement rate', N'全体達成率', N'전체 달성률'),
  (N'mes.planAchievement.kpi.totalWo', N'指令数', N'指令數', N'Work orders', N'対象指図数', N'작업지시 수'),
  (N'mes.planAchievement.kpi.onTarget', N'达成数', N'達成數', N'On target', N'達成件数', N'달성 건수'),
  (N'mes.planAchievement.kpi.defectRate', N'不良率', N'不良率', N'Defect rate', N'不良率', N'불량률'),
  (N'mes.planAchievement.chart.title', N'达成率分布', N'達成率分佈', N'Achievement by group', N'グループ別達成率', N'그룹별 달성률'),
  (N'mes.planAchievement.col.group', N'分组', N'分組', N'Group', N'集計対象', N'그룹'),
  (N'mes.planAchievement.col.woCount', N'指令数', N'指令數', N'WO count', N'指図数', N'작업지시 수'),
  (N'mes.planAchievement.col.planned', N'计划数', N'計劃數', N'Planned', N'計画数', N'계획 수량'),
  (N'mes.planAchievement.col.good', N'良品数', N'良品數', N'Good', N'良品数', N'양품 수량'),
  (N'mes.planAchievement.col.defect', N'不良数', N'不良數', N'Defect', N'不良数', N'불량 수량'),
  (N'mes.planAchievement.col.achievementRate', N'达成率', N'達成率', N'Achievement', N'達成率', N'달성률'),
  (N'mes.planAchievement.col.defectRate', N'不良率', N'不良率', N'Defect rate', N'不良率', N'불량률'),
  (N'mes.planAchievement.col.onTarget', N'达成数', N'達成數', N'On target', N'達成件数', N'달성 건수'),
  (N'mes.planAchievement.msg.exported', N'CSV 已导出', N'CSV 已匯出', N'CSV exported', N'CSVを出力しました', N'CSV가 내보내졌습니다');

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
PRINT CONCAT('=== Gap 3.3 Plan Achievement i18n complete: INSERT=', @ins, ' UPDATE=', @upd, ' ===');

DROP TABLE #i18n;
