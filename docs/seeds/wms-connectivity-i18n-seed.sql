/* ============================================================
 * WMS MSBBWM310/320/330 連携・モバイル・IoT i18n シードデータ
 *   wms.mobile.*  D-1 統合タスク（MobileTaskView）
 *   wms.wcs.*     D-2 WCS
 *   wms.carrier.* D-3 配送業者
 *   wms.iot.*     D-4 IoT
 * ============================================================ */
SET NOCOUNT ON; SET XACT_ABORT ON;
PRINT '=== WMS Connectivity i18n seed start ===';

IF OBJECT_ID('tempdb..#i18n') IS NOT NULL DROP TABLE #i18n;
CREATE TABLE #i18n (
    LangKey nvarchar(200) NOT NULL PRIMARY KEY,
    ZhCN nvarchar(500), ZhTW nvarchar(500), En nvarchar(500), Ja nvarchar(500), Ko nvarchar(500)
);

-- ─── D-1 MobileTaskView (wms.mobile.*) ───
INSERT INTO #i18n VALUES
  (N'wms.mobile.title',             N'我的任务',           N'我的任務',           N'My Tasks',           N'私のタスク',         N'내 작업'),
  (N'wms.mobile.tab.picking',       N'拣货',               N'揀貨',               N'Picking',            N'ピッキング',         N'피킹'),
  (N'wms.mobile.tab.packing',       N'包装',               N'包裝',               N'Packing',            N'梱包',               N'포장'),
  (N'wms.mobile.tab.inbound',       N'入库',               N'入庫',               N'Inbound',            N'入庫',               N'입고'),
  (N'wms.mobile.tab.stocktake',     N'盘点',               N'盤點',               N'StockTake',          N'棚卸',               N'재고조사'),
  (N'wms.mobile.summary.pending',   N'待处理',             N'待處理',             N'Pending',            N'未処理',             N'대기'),
  (N'wms.mobile.summary.today',     N'今日',               N'今日',               N'Today',              N'本日',               N'오늘'),
  (N'wms.mobile.btn.scan',          N'扫码开始',           N'掃碼開始',           N'Scan to Start',      N'スキャン開始',       N'스캔 시작'),
  (N'wms.mobile.btn.openTask',      N'打开',               N'打開',               N'Open',               N'開く',               N'열기'),
  (N'wms.mobile.msg.nothing',       N'暂无任务',           N'暫無任務',           N'No tasks',           N'タスクなし',         N'작업 없음'),
  (N'wms.mobile.tag.allocated',     N'已分配',             N'已分配',             N'Allocated',          N'引当済',             N'할당'),
  (N'wms.mobile.tag.picking',       N'拣货中',             N'揀貨中',             N'Picking',            N'ピッキング中',       N'피킹 중'),
  (N'wms.mobile.tag.ready',         N'待包装',             N'待包裝',             N'Ready',              N'梱包待ち',           N'포장 대기');

-- ─── D-2 WCS (wms.wcs.*) ───
INSERT INTO #i18n VALUES
  (N'wms.wcs.title',                N'WCS 任务',           N'WCS 任務',           N'WCS Tasks',          N'WCS タスク',         N'WCS 작업'),
  (N'wms.wcs.fld.no',               N'任务NO',             N'任務NO',             N'Task No',            N'タスクNO',           N'작업 NO'),
  (N'wms.wcs.fld.type',             N'类型',               N'類型',               N'Type',               N'種別',               N'종류'),
  (N'wms.wcs.fld.priority',         N'优先度',             N'優先度',             N'Priority',           N'優先度',             N'우선도'),
  (N'wms.wcs.fld.device',           N'装置',               N'裝置',               N'Device',             N'装置',               N'장치'),
  (N'wms.wcs.fld.from',             N'起点',               N'起點',               N'From',               N'移動元',             N'시작점'),
  (N'wms.wcs.fld.to',               N'终点',               N'終點',               N'To',                 N'移動先',             N'종점'),
  (N'wms.wcs.fld.related',          N'关联单据',           N'關聯單據',           N'Related',            N'関連伝票',           N'관련 전표'),
  (N'wms.wcs.fld.created',          N'创建',               N'建立',               N'Created',            N'作成',               N'생성'),
  (N'wms.wcs.fld.completed',        N'完成',               N'完成',               N'Completed',          N'完了',               N'완료'),
  (N'wms.wcs.fld.error',            N'错误',               N'錯誤',               N'Error',              N'エラー',             N'에러'),
  (N'wms.wcs.type.move',            N'棚移动',             N'棚移動',             N'Move',               N'棚移動',             N'선반 이동'),
  (N'wms.wcs.type.pick',            N'拣货',               N'揀貨',               N'Pick',               N'ピッキング',         N'피킹'),
  (N'wms.wcs.type.put',             N'棚入',               N'棚入',               N'Put',                N'棚入れ',             N'선반 입고'),
  (N'wms.wcs.type.count',           N'盘点',               N'盤點',               N'Count',              N'棚卸',               N'재고조사'),
  (N'wms.wcs.status.created',       N'已创建',             N'已建立',             N'Created',            N'作成済',             N'생성됨'),
  (N'wms.wcs.status.dispatched',    N'已派发',             N'已派發',             N'Dispatched',         N'払出済',             N'배포됨'),
  (N'wms.wcs.status.executing',     N'执行中',             N'執行中',             N'Executing',          N'実行中',             N'실행 중'),
  (N'wms.wcs.status.completed',     N'已完成',             N'已完成',             N'Completed',          N'完了',               N'완료'),
  (N'wms.wcs.status.failed',        N'失败',               N'失敗',               N'Failed',             N'失敗',               N'실패'),
  (N'wms.wcs.btn.dispatch',         N'派发',               N'派發',               N'Dispatch',           N'払出',               N'배포'),
  (N'wms.wcs.btn.start',            N'开始',               N'開始',               N'Start',              N'開始',               N'시작'),
  (N'wms.wcs.btn.complete',         N'完成',               N'完成',               N'Complete',           N'完了',               N'완료'),
  (N'wms.wcs.btn.fail',             N'报告失败',           N'報告失敗',           N'Report Fail',        N'失敗報告',           N'실패 보고'),
  (N'wms.wcs.dlg.create',           N'新建 WCS 任务',      N'新增 WCS 任務',      N'New WCS Task',       N'WCS タスク新規',     N'WCS 작업 신규'),
  (N'wms.wcs.dlg.dispatch',         N'派发到装置',         N'派發到裝置',         N'Dispatch to Device', N'装置へ払出',         N'장치 배포'),
  (N'wms.wcs.dlg.fail',             N'报告失败原因',       N'報告失敗原因',       N'Report Failure',     N'失敗理由を報告',     N'실패 사유 보고');

-- ─── D-3 Carrier (wms.carrier.*) ───
INSERT INTO #i18n VALUES
  (N'wms.carrier.title',            N'配送公司',           N'配送公司',           N'Carrier',            N'配送業者',           N'배송 회사'),
  (N'wms.carrier.fld.no',           N'运单NO',             N'運單NO',             N'Shipment',           N'シップメントNO',     N'운송장 NO'),
  (N'wms.carrier.fld.pkg',          N'箱NO',               N'箱NO',               N'Package',            N'梱包NO',             N'박스 NO'),
  (N'wms.carrier.fld.carrier',      N'公司',               N'公司',               N'Carrier',            N'業者',               N'회사'),
  (N'wms.carrier.fld.tracking',     N'追跡番号',           N'追跡番號',           N'Tracking',           N'追跡番号',           N'추적 번호'),
  (N'wms.carrier.fld.service',      N'服务',               N'服務',               N'Service',            N'サービス',           N'서비스'),
  (N'wms.carrier.fld.customer',     N'客户',               N'客戶',               N'Customer',           N'顧客',               N'고객'),
  (N'wms.carrier.fld.address',      N'地址',               N'地址',               N'Address',            N'住所',               N'주소'),
  (N'wms.carrier.fld.weight',       N'重量',               N'重量',               N'Weight',             N'重量',               N'중량'),
  (N'wms.carrier.fld.fee',          N'运费',               N'運費',               N'Fee',                N'料金',               N'요금'),
  (N'wms.carrier.fld.pickedAt',     N'集荷',               N'集荷',               N'Picked Up',          N'集荷時刻',           N'집하시각'),
  (N'wms.carrier.fld.deliveredAt',  N'送达',               N'送達',               N'Delivered',          N'配達時刻',           N'배달시각'),
  (N'wms.carrier.fld.events',       N'事件历史',           N'事件歷史',           N'Events',             N'イベント履歴',       N'이벤트 이력'),
  (N'wms.carrier.fld.reason',       N'原因',               N'原因',               N'Reason',             N'理由',               N'사유'),
  (N'wms.carrier.status.created',   N'已创建',             N'已建立',             N'Created',            N'作成済',             N'생성됨'),
  (N'wms.carrier.status.pickedup',  N'已集荷',             N'已集荷',             N'Picked Up',          N'集荷済',             N'집하됨'),
  (N'wms.carrier.status.transit',   N'配送中',             N'配送中',             N'In Transit',         N'配送中',             N'배송 중'),
  (N'wms.carrier.status.delivered', N'已送达',             N'已送達',             N'Delivered',          N'配達完了',           N'배달 완료'),
  (N'wms.carrier.status.failed',    N'失败',               N'失敗',               N'Failed',             N'失敗',               N'실패'),
  (N'wms.carrier.btn.pickup',       N'集荷',               N'集荷',               N'Pick Up',            N'集荷',               N'집하'),
  (N'wms.carrier.btn.transit',      N'配送中',             N'配送中',             N'In Transit',         N'配送中',             N'배송 중'),
  (N'wms.carrier.btn.delivered',    N'已送达',             N'已送達',             N'Delivered',          N'配達完了',           N'배달 완료'),
  (N'wms.carrier.btn.fail',         N'报告失败',           N'報告失敗',           N'Report Fail',        N'失敗報告',           N'실패 보고'),
  (N'wms.carrier.btn.addEvent',     N'追加事件',           N'追加事件',           N'Add Event',          N'イベント追加',       N'이벤트 추가'),
  (N'wms.carrier.dlg.create',       N'新建运单',           N'新增運單',           N'New Shipment',       N'シップメント新規',   N'운송장 신규'),
  (N'wms.carrier.dlg.addEvent',     N'追加事件',           N'追加事件',           N'Add Event',          N'イベント追加',       N'이벤트 추가');

-- ─── D-4 IoT (wms.iot.*) ───
INSERT INTO #i18n VALUES
  (N'wms.iot.title',                N'IoT 监控',           N'IoT 監控',           N'IoT Monitor',        N'IoT 監視',           N'IoT 모니터'),
  (N'wms.iot.tab.sensors',          N'传感器',             N'感測器',             N'Sensors',            N'センサ',             N'센서'),
  (N'wms.iot.tab.alerts',           N'警报',               N'警報',               N'Alerts',             N'警報',               N'경보'),
  (N'wms.iot.tab.history',          N'历史',               N'歷史',               N'History',            N'履歴',               N'이력'),
  (N'wms.iot.fld.id',               N'传感器ID',           N'感測器ID',           N'Sensor ID',          N'センサID',           N'센서 ID'),
  (N'wms.iot.fld.name',             N'名称',               N'名稱',               N'Name',               N'名称',               N'명칭'),
  (N'wms.iot.fld.type',             N'类型',               N'類型',               N'Type',               N'種別',               N'종류'),
  (N'wms.iot.fld.unit',             N'单位',               N'單位',               N'Unit',               N'単位',               N'단위'),
  (N'wms.iot.fld.min',              N'下限',               N'下限',               N'Min',                N'下限',               N'하한'),
  (N'wms.iot.fld.max',              N'上限',               N'上限',               N'Max',                N'上限',               N'상한'),
  (N'wms.iot.fld.enabled',          N'启用',               N'啟用',               N'Enabled',            N'有効',               N'활성'),
  (N'wms.iot.fld.lastValue',        N'最新值',             N'最新值',             N'Last Value',         N'最新値',             N'최신값'),
  (N'wms.iot.fld.lastRead',         N'最新时刻',           N'最新時刻',           N'Last Read',          N'最新時刻',           N'최신시각'),
  (N'wms.iot.fld.value',            N'值',                 N'值',                 N'Value',              N'値',                 N'값'),
  (N'wms.iot.fld.alert',            N'警报',               N'警報',               N'Alert',              N'警報',               N'경보'),
  (N'wms.iot.type.temp',            N'温度',               N'溫度',               N'Temperature',        N'温度',               N'온도'),
  (N'wms.iot.type.humid',           N'湿度',               N'濕度',               N'Humidity',           N'湿度',               N'습도'),
  (N'wms.iot.type.shock',           N'冲击',               N'衝擊',               N'Shock',              N'衝撃',               N'충격'),
  (N'wms.iot.type.shelf',           N'棚位',               N'棚位',               N'Shelf',              N'棚センサ',           N'선반'),
  (N'wms.iot.btn.create',           N'新建传感器',         N'新增感測器',         N'New Sensor',         N'センサ新規',         N'센서 신규'),
  (N'wms.iot.btn.simulate',         N'模拟一批',           N'模擬一批',           N'Simulate',           N'疑似生成',           N'시뮬레이션'),
  (N'wms.iot.btn.viewHistory',      N'查看历史',           N'查看歷史',           N'View History',       N'履歴表示',           N'이력 보기'),
  (N'wms.iot.btn.postReading',      N'手动投入',           N'手動投入',           N'Post Reading',       N'手動投入',           N'수동 입력'),
  (N'wms.iot.dlg.create',           N'新建传感器',         N'新增感測器',         N'New Sensor',         N'センサ新規',         N'센서 신규'),
  (N'wms.iot.dlg.postReading',      N'投入读数',           N'投入讀數',           N'Post Reading',       N'読み投入',           N'읽기 입력'),
  (N'wms.iot.msg.noAlerts',         N'当前无警报',         N'目前無警報',         N'No active alerts',   N'現在警報なし',       N'현재 경보 없음'),
  (N'wms.iot.msg.simulated',        N'已生成',             N'已生成',             N'Generated',          N'生成完了',           N'생성 완료');

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
