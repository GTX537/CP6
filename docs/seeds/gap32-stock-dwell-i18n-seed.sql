/* ============================================================
 * Gap 3.2 Stock Dwell Report i18n seed
 * ============================================================ */
SET NOCOUNT ON; SET XACT_ABORT ON;
PRINT '=== Gap 3.2 Stock Dwell Report i18n seed start ===';

IF OBJECT_ID('tempdb..#i18n') IS NOT NULL DROP TABLE #i18n;
CREATE TABLE #i18n (
    LangKey nvarchar(200) NOT NULL PRIMARY KEY,
    ZhCN nvarchar(500), ZhTW nvarchar(500), En nvarchar(500), Ja nvarchar(500), Ko nvarchar(500)
);

INSERT INTO #i18n VALUES
  (N'wms.stockDwell.title', N'库存滞留报表', N'庫存滯留報表', N'Stock dwell report', N'在庫滞留レポート', N'재고 체류 리포트'),
  (N'wms.stockDwell.filter.groupBy', N'分组', N'分組', N'Group by', N'集計単位', N'그룹'),
  (N'wms.stockDwell.filter.owner', N'客户/货主', N'客戶/貨主', N'Owner', N'所有者', N'소유자'),
  (N'wms.stockDwell.filter.asOfDate', N'基准日', N'基準日', N'As of date', N'基準日', N'기준일'),
  (N'wms.stockDwell.groupBy.product', N'按产品', N'按產品', N'Product', N'製品別', N'제품별'),
  (N'wms.stockDwell.groupBy.customer', N'按客户', N'按客戶', N'Customer', N'得意先別', N'고객별'),
  (N'wms.stockDwell.btn.search', N'搜索', N'搜尋', N'Search', N'検索', N'검색'),
  (N'wms.stockDwell.btn.reset', N'重置', N'重設', N'Reset', N'クリア', N'초기화'),
  (N'wms.stockDwell.kpi.totalQty', N'总库存数量', N'總庫存數量', N'Total stock qty', N'総在庫数量', N'총 재고 수량'),
  (N'wms.stockDwell.kpi.over90Qty', N'90天以上数量', N'90天以上數量', N'90+ day qty', N'90日超数量', N'90일 초과 수량'),
  (N'wms.stockDwell.kpi.over90Rate', N'90天以上占比', N'90天以上占比', N'90+ day ratio', N'90日超比率', N'90일 초과 비율'),
  (N'wms.stockDwell.kpi.totalValue', N'库存金额', N'庫存金額', N'Stock value', N'在庫金額', N'재고 금액'),
  (N'wms.stockDwell.chart.title', N'库龄分布 Top 8', N'庫齡分布 Top 8', N'Dwell age buckets Top 8', N'滞留日数分布 Top 8', N'체류 기간 분포 Top 8'),
  (N'wms.stockDwell.bucket.b0', N'0-30天', N'0-30天', N'0-30 days', N'0-30日', N'0-30일'),
  (N'wms.stockDwell.bucket.b1', N'31-60天', N'31-60天', N'31-60 days', N'31-60日', N'31-60일'),
  (N'wms.stockDwell.bucket.b2', N'61-90天', N'61-90天', N'61-90 days', N'61-90日', N'61-90일'),
  (N'wms.stockDwell.bucket.b3', N'90天以上', N'90天以上', N'90+ days', N'90日超', N'90일 초과'),
  (N'wms.stockDwell.col.group', N'分组', N'分組', N'Group', N'集計単位', N'그룹'),
  (N'wms.stockDwell.col.totalQty', N'总数量', N'總數量', N'Total qty', N'総数量', N'총 수량'),
  (N'wms.stockDwell.col.totalValue', N'金额', N'金額', N'Value', N'金額', N'금액'),
  (N'wms.stockDwell.col.oldest', N'最早入库日', N'最早入庫日', N'Oldest receipt', N'最古入庫日', N'최초 입고일'),
  (N'wms.stockDwell.col.oldestAge', N'最大库龄', N'最大庫齡', N'Oldest age', N'最長滞留日数', N'최대 체류일'),
  (N'wms.stockDwell.empty', N'没有符合条件的库存', N'沒有符合條件的庫存', N'No matching stock', N'該当する在庫はありません', N'조건에 맞는 재고가 없습니다'),
  (N'dashboard.stockDwell.title', N'90天以上滞留库存', N'90天以上滯留庫存', N'90+ day stock dwell', N'90日超の滞留在庫', N'90일 초과 체류 재고'),
  (N'dashboard.stockDwell.open', N'打开报表', N'開啟報表', N'Open report', N'レポートを開く', N'리포트 열기');

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
PRINT CONCAT('=== Gap 3.2 Stock Dwell Report i18n complete: INSERT=', @ins, ' UPDATE=', @upd, ' ===');

DROP TABLE #i18n;
