/* ============================================================
 * WMS SignalR リアルタイム タイムライン i18n
 * ============================================================
 * 範囲：wms.dashboard.realtime.*
 * 冪等：MERGE
 * 実行：sqlcmd -S localhost\KOUSQLSERVER -d CP6DB -E -f 65001 -i docs/wms-realtime-i18n-seed.sql -b
 * ============================================================ */
SET NOCOUNT ON; SET XACT_ABORT ON;
PRINT '=== WMS Realtime i18n シード開始 ===';

IF OBJECT_ID('tempdb..#i18n') IS NOT NULL DROP TABLE #i18n;
CREATE TABLE #i18n (
    LangKey nvarchar(200) NOT NULL PRIMARY KEY,
    ZhCN nvarchar(500), ZhTW nvarchar(500), En nvarchar(500), Ja nvarchar(500), Ko nvarchar(500)
);

INSERT INTO #i18n VALUES
  (N'wms.dashboard.realtime.title',     N'实时事件',         N'即時事件',         N'Realtime Events',        N'リアルタイムイベント',  N'실시간 이벤트'),
  (N'wms.dashboard.realtime.connected', N'已连接',           N'已連線',           N'Connected',              N'接続中',              N'연결됨'),
  (N'wms.dashboard.realtime.disconnected', N'未连接',        N'未連線',           N'Disconnected',           N'未接続',              N'연결 안됨'),
  (N'wms.dashboard.realtime.connecting', N'连接中...',       N'連線中...',        N'Connecting...',          N'接続中...',           N'연결 중...'),
  (N'wms.dashboard.realtime.clear',     N'清空',             N'清空',             N'Clear',                  N'クリア',              N'지우기'),
  (N'wms.dashboard.realtime.noEvents',  N'暂无事件',         N'尚無事件',         N'No events yet',          N'イベントなし',        N'이벤트 없음'),
  (N'wms.dashboard.realtime.toastStock', N'库存变动: {type} {product} {qty}', N'庫存變動: {type} {product} {qty}', N'Stock changed: {type} {product} {qty}', N'在庫変動: {type} {product} {qty}', N'재고 변동: {type} {product} {qty}'),
  (N'wms.dashboard.realtime.toastInbound', N'入库完成: {no}', N'入庫完成: {no}', N'Inbound received: {no}', N'入庫実績確定: {no}', N'입고 완료: {no}'),
  (N'wms.dashboard.realtime.toastOutbound', N'出货完成: {no}', N'出貨完成: {no}', N'Outbound shipped: {no}', N'出庫確定: {no}', N'출고 완료: {no}');

DECLARE @actionLog TABLE (act nvarchar(10));
MERGE Sys_Langs AS tgt USING #i18n AS src ON tgt.LangKey = src.LangKey
WHEN MATCHED THEN UPDATE SET tgt.ZhCN = src.ZhCN, tgt.ZhTW = src.ZhTW, tgt.En = src.En, tgt.Ja = src.Ja, tgt.Ko = src.Ko
WHEN NOT MATCHED BY TARGET THEN INSERT (LangKey, ZhCN, ZhTW, En, Ja, Ko) VALUES (src.LangKey, src.ZhCN, src.ZhTW, src.En, src.Ja, src.Ko)
OUTPUT $action INTO @actionLog;

DECLARE @ins INT = (SELECT COUNT(*) FROM @actionLog WHERE act = 'INSERT');
DECLARE @upd INT = (SELECT COUNT(*) FROM @actionLog WHERE act = 'UPDATE');
PRINT N'  追加: ' + CAST(@ins AS nvarchar(10)) + N' 件 / 更新: ' + CAST(@upd AS nvarchar(10)) + N' 件';
DROP TABLE #i18n;
PRINT '=== Done ===';
