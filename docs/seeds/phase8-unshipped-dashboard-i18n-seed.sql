/* ============================================================
 * Phase 8 受注済未出荷 Dashboard widget i18n
 * ============================================================
 * 範囲：dashboard.unshipped.*
 * 冪等：MERGE
 * 実行：sqlcmd -S "localhost\KOUSQLSERVER" -E -d CP6DB -f 65001 -i docs/phase8-unshipped-dashboard-i18n-seed.sql -b
 * ============================================================ */
SET NOCOUNT ON; SET XACT_ABORT ON;
PRINT '=== Phase 8 Unshipped Dashboard i18n シード開始 ===';

IF OBJECT_ID('tempdb..#i18n') IS NOT NULL DROP TABLE #i18n;
CREATE TABLE #i18n (
    LangKey nvarchar(200) NOT NULL PRIMARY KEY,
    ZhCN nvarchar(500), ZhTW nvarchar(500), En nvarchar(500), Ja nvarchar(500), Ko nvarchar(500)
);

INSERT INTO #i18n VALUES
  -- Widget ヘッダ
  (N'dashboard.unshipped.title',       N'未出货受注', N'未出貨受注', N'Unshipped orders', N'受注済未出荷', N'미출하 주문'),
  (N'dashboard.unshipped.onlyOverdue', N'仅显示超期', N'僅顯示超期', N'Overdue only',      N'納期超のみ',   N'기한 초과만'),
  -- 列ヘッダ
  (N'dashboard.unshipped.no',           N'受注NO',    N'受注NO',    N'Order No',         N'受注NO',       N'주문번호'),
  (N'dashboard.unshipped.customer',     N'客户',      N'客戶',       N'Customer',         N'得意先',       N'고객'),
  (N'dashboard.unshipped.deliveryDate', N'交期',      N'交期',       N'Delivery date',    N'納期',         N'납기'),
  (N'dashboard.unshipped.status',       N'状态',      N'狀態',       N'Status',           N'ステータス',    N'상태'),
  (N'dashboard.unshipped.qty',          N'数量(出货/订)', N'數量(出貨/訂)', N'Qty (shipped/ordered)', N'数量(出荷/受注)', N'수량(출하/주문)'),
  (N'dashboard.unshipped.mes',          N'MES 状态',  N'MES 狀態',  N'MES status',       N'MES ステータス', N'MES 상태'),
  (N'dashboard.unshipped.wms',          N'WMS 状态',  N'WMS 狀態',  N'WMS status',       N'WMS ステータス', N'WMS 상태'),
  -- 標識
  (N'dashboard.unshipped.overdue',      N'超期',      N'超期',       N'Overdue',          N'納期超',        N'기한 초과'),
  -- OrderLifecycleStatus 取値ラベル
  (N'dashboard.unshipped.lifecycle.CONFIRMED',           N'已确认',     N'已確認',     N'Confirmed',     N'確定済',     N'확정됨'),
  (N'dashboard.unshipped.lifecycle.IN_PRODUCTION',       N'生产中',     N'生產中',     N'In production', N'製造中',     N'생산 중'),
  (N'dashboard.unshipped.lifecycle.PARTIALLY_CANCELLED', N'部分取消',   N'部分取消',   N'Partly cancelled', N'一部取消', N'부분 취소'),
  (N'dashboard.unshipped.lifecycle.SHIPPED',             N'已出货',     N'已出貨',     N'Shipped',       N'出荷済',     N'출하 완료'),
  (N'dashboard.unshipped.lifecycle.CANCELLED',           N'已取消',     N'已取消',     N'Cancelled',     N'取消済',     N'취소됨');

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
PRINT CONCAT('=== Phase 8 Unshipped i18n 完了: INSERT=', @ins, ' UPDATE=', @upd, ' ===');

DROP TABLE #i18n;
