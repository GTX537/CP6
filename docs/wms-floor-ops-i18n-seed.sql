/* ============================================================
 * WMS 現場運用 UI i18n シードデータ
 *   wms.prodIn.*    A. 製品入庫
 *   wms.pick.*      B. ピッキング作業
 *   wms.pack.*      C. 梱包・出荷確定
 * ============================================================ */
SET NOCOUNT ON; SET XACT_ABORT ON;
PRINT '=== WMS 現場運用 UI i18n シード開始 ===';

IF OBJECT_ID('tempdb..#i18n') IS NOT NULL DROP TABLE #i18n;
CREATE TABLE #i18n (
    LangKey nvarchar(200) NOT NULL PRIMARY KEY,
    ZhCN nvarchar(500), ZhTW nvarchar(500), En nvarchar(500), Ja nvarchar(500), Ko nvarchar(500)
);

-- ─── A. ProductionInbound（wms.prodIn.*）───
INSERT INTO #i18n VALUES
  (N'wms.prodIn.title',             N'制品入库',           N'製品入庫',           N'Product Inbound',    N'製品入庫',           N'제품 입고'),
  (N'wms.prodIn.title.recent',      N'最近入库',           N'最近入庫',           N'Recent Inbounds',    N'最近入庫',           N'최근 입고'),
  (N'wms.prodIn.sourceProduction',  N'生产完成品',         N'生產完成品',         N'Production',         N'生産完成品',         N'생산 완성품'),
  (N'wms.prodIn.fld.workOrder',     N'作业指示NO',         N'作業指示NO',         N'Work Order',         N'製造指図NO',         N'작업 지시 NO'),
  (N'wms.prodIn.fld.qty',           N'数量',               N'數量',               N'Qty',                N'数量',               N'수량'),
  (N'wms.prodIn.fld.quality',       N'品質',               N'品質',               N'Quality',            N'品質',               N'품질'),
  (N'wms.prodIn.fld.receiptNo',     N'入库NO',             N'入庫NO',             N'Receipt No',         N'入庫NO',             N'입고NO'),
  (N'wms.prodIn.fld.receivedAt',    N'入库时刻',           N'入庫時刻',           N'Received At',        N'入庫時刻',           N'입고 시각'),
  (N'wms.prodIn.quality.good',      N'良品',               N'良品',               N'Good',               N'良品',               N'양품'),
  (N'wms.prodIn.quality.defective', N'不良品',             N'不良品',             N'Defective',          N'不良品',             N'불량'),
  (N'wms.prodIn.btn.autoLot',       N'自动LOT',            N'自動LOT',            N'Auto Lot',           N'LOT自動',            N'LOT 자동'),
  (N'wms.prodIn.btn.confirm',       N'确认入库',           N'確認入庫',           N'Confirm Inbound',    N'入庫確定',           N'입고 확정'),
  (N'wms.prodIn.msg.scanWO',        N'扫码读取作业指示',   N'掃碼讀取作業指示',   N'Scan Work Order',    N'製造指図NOをスキャン', N'작업지시 스캔'),
  (N'wms.prodIn.msg.finishedWh',    N'完成品仓库',         N'完成品倉庫',         N'Finished Goods Wh',  N'完成品倉庫',         N'완성품 창고'),
  (N'wms.prodIn.msg.defectiveWh',   N'不良品仓库',         N'不良品倉庫',         N'Defective Wh',       N'不良品倉庫',         N'불량 창고');

-- ─── B. Picking（wms.pick.*）───
INSERT INTO #i18n VALUES
  (N'wms.pick.title',               N'拣货作业',           N'揀貨作業',           N'Picking Work',       N'ピッキング作業',     N'피킹 작업'),
  (N'wms.pick.title.tasks',         N'我的任务',           N'我的任務',           N'My Tasks',           N'自分のタスク',       N'내 작업'),
  (N'wms.pick.title.task',          N'当前任务',           N'目前任務',           N'Current Task',       N'現在のタスク',       N'현재 작업'),
  (N'wms.pick.fld.outboundNo',      N'出库指示',           N'出庫指示',           N'Outbound',           N'出庫指示',           N'출고 지시'),
  (N'wms.pick.fld.lineNo',          N'明细行',             N'明細行',             N'Line',               N'明細行',             N'명세 행'),
  (N'wms.pick.fld.fromLoc',         N'拣货位置',           N'揀貨位置',           N'From Loc',           N'ピッキング元',       N'피킹 위치'),
  (N'wms.pick.fld.product',         N'产品',               N'產品',               N'Product',            N'製品',               N'제품'),
  (N'wms.pick.fld.lot',             N'LOT',                N'LOT',                N'Lot',                N'ロット',             N'LOT'),
  (N'wms.pick.fld.reqQty',          N'要求数量',           N'要求數量',           N'Req Qty',            N'要求数量',           N'요청 수량'),
  (N'wms.pick.fld.pickedQty',       N'已拣',               N'已揀',               N'Picked',             N'拣取済',             N'피킹됨'),
  (N'wms.pick.fld.scan',            N'扫码/输入',          N'掃碼/輸入',          N'Scan / Input',       N'スキャン/入力',      N'스캔/입력'),
  (N'wms.pick.fld.actualQty',       N'实际数量',           N'實際數量',           N'Actual',             N'実数',               N'실수'),
  (N'wms.pick.btn.start',           N'开始拣货',           N'開始揀貨',           N'Start Picking',      N'ピッキング開始',     N'피킹 시작'),
  (N'wms.pick.btn.confirmLine',     N'确认本行',           N'確認本行',           N'Confirm Line',       N'本行確定',           N'본 행 확정'),
  (N'wms.pick.btn.short',           N'报告短缺',           N'報告短缺',           N'Report Short',       N'欠品報告',           N'결품 보고'),
  (N'wms.pick.btn.skip',            N'跳过',               N'跳過',               N'Skip',               N'スキップ',           N'건너뛰기'),
  (N'wms.pick.btn.complete',        N'完成全部',           N'完成全部',           N'Complete All',       N'全完了',             N'전체 완료'),
  (N'wms.pick.msg.scanLoc',         N'扫描或输入拣货位置CD',   N'掃描或輸入揀貨位置CD',   N'Scan / type location code', N'ロケCDをスキャン/入力', N'로케 CD 스캔/입력'),
  (N'wms.pick.msg.scanProduct',     N'扫描产品CD',         N'掃描產品CD',         N'Scan product code',  N'製品CDをスキャン',   N'제품 CD 스캔'),
  (N'wms.pick.msg.locMismatch',     N'位置不一致！',       N'位置不一致！',       N'Location mismatch!', N'ロケ不一致！',       N'위치 불일치!'),
  (N'wms.pick.msg.productMismatch', N'产品不一致！',       N'產品不一致！',       N'Product mismatch!',  N'製品不一致！',       N'제품 불일치!'),
  (N'wms.pick.msg.noTask',          N'暂无任务',           N'暫無任務',           N'No tasks',           N'タスクなし',         N'작업 없음'),
  (N'wms.pick.msg.allDone',         N'全部完成！',         N'全部完成！',         N'All done!',          N'全完了！',           N'전체 완료!'),
  (N'wms.pick.msg.shortReason',     N'短缺原因',           N'短缺原因',           N'Short reason',       N'欠品理由',           N'결품 사유'),
  (N'wms.pick.status.allocated',    N'已分配',             N'已分配',             N'Allocated',          N'引当済',             N'할당'),
  (N'wms.pick.status.picking',      N'拣货中',             N'揀貨中',             N'Picking',            N'ピッキング中',       N'피킹 중'),
  (N'wms.pick.status.shortage',     N'短缺',               N'短缺',               N'Short',              N'欠品',               N'결품');

-- ─── C. Packing & Ship（wms.pack.*）───
INSERT INTO #i18n VALUES
  (N'wms.pack.title',               N'包装·出货确定',      N'包裝·出貨確定',      N'Packing & Ship',     N'梱包・出荷確定',     N'포장·출하 확정'),
  (N'wms.pack.title.queue',         N'待包装队列',         N'待包裝佇列',         N'Ready to Pack',      N'梱包待ち',           N'포장 대기'),
  (N'wms.pack.title.packages',      N'本次包装',           N'本次包裝',           N'Packages',           N'梱包',               N'포장'),
  (N'wms.pack.fld.packageNo',       N'箱NO',               N'箱NO',               N'Pkg No',             N'箱NO',               N'박스 NO'),
  (N'wms.pack.fld.weightKg',        N'重量(kg)',           N'重量(kg)',           N'Weight(kg)',         N'重量(kg)',           N'중량(kg)'),
  (N'wms.pack.fld.lengthMm',        N'长(mm)',             N'長(mm)',             N'L(mm)',              N'長さ(mm)',           N'길이(mm)'),
  (N'wms.pack.fld.widthMm',         N'宽(mm)',             N'寬(mm)',             N'W(mm)',              N'幅(mm)',             N'폭(mm)'),
  (N'wms.pack.fld.heightMm',        N'高(mm)',             N'高(mm)',             N'H(mm)',              N'高さ(mm)',           N'높이(mm)'),
  (N'wms.pack.fld.trackingNo',      N'运单NO',             N'運單NO',             N'Tracking No',        N'送り状NO',           N'송장 NO'),
  (N'wms.pack.fld.carrier',         N'配送公司',           N'配送公司',           N'Carrier',            N'配送業者',           N'배송 회사'),
  (N'wms.pack.fld.qty',             N'数量',               N'數量',               N'Qty',                N'数量',               N'수량'),
  (N'wms.pack.btn.newPackage',      N'新增箱',             N'新增箱',             N'New Package',        N'箱を追加',           N'박스 추가'),
  (N'wms.pack.btn.packItem',        N'装入',               N'裝入',               N'Pack',               N'箱詰',               N'담기'),
  (N'wms.pack.btn.removeItem',      N'取出',               N'取出',               N'Remove',             N'取出',               N'꺼내기'),
  (N'wms.pack.btn.weigh',           N'称重',               N'秤重',               N'Weigh',              N'計量',               N'중량 측정'),
  (N'wms.pack.btn.assignTracking',  N'分配运单',           N'分配運單',           N'Assign Tracking',    N'送り状割当',         N'송장 할당'),
  (N'wms.pack.btn.confirmShip',     N'确认出货',           N'確認出貨',           N'Confirm Ship',       N'出荷確定',           N'출하 확정'),
  (N'wms.pack.msg.noPackages',      N'尚无包装',           N'尚無包裝',           N'No packages',        N'梱包なし',           N'포장 없음'),
  (N'wms.pack.msg.confirmShipAsk',  N'确认要发货吗？',     N'確認要發貨嗎？',     N'Confirm shipment?',  N'出荷確定しますか？', N'출하 확정?'),
  (N'wms.pack.msg.allPacked',       N'全部已包装',         N'全部已包裝',         N'All packed',         N'梱包完了',           N'전체 포장됨'),
  (N'wms.pack.msg.qtyTooMuch',      N'超过未包装数量',     N'超過未包裝數量',     N'Exceeds unpacked qty', N'未梱包数量を超過',  N'미포장 수량 초과'),
  (N'wms.pack.carrier.yamato',      N'大和运输',           N'大和運輸',           N'Yamato',             N'ヤマト運輸',         N'야마토 운수'),
  (N'wms.pack.carrier.sagawa',      N'佐川急便',           N'佐川急便',           N'Sagawa',             N'佐川急便',           N'사가와 큐빈'),
  (N'wms.pack.carrier.jp',          N'日本邮政',           N'日本郵政',           N'JP Post',            N'日本郵便',           N'일본 우편'),
  (N'wms.pack.carrier.self',        N'自配送',             N'自配送',             N'Self',               N'自社便',             N'자체 배송'),
  (N'wms.pack.carrier.other',       N'其他',               N'其他',               N'Other',              N'その他',             N'기타');

DECLARE @actionLog TABLE (act nvarchar(10));
MERGE Sys_Langs AS tgt USING #i18n AS src ON tgt.LangKey = src.LangKey
WHEN MATCHED THEN UPDATE SET tgt.ZhCN=src.ZhCN, tgt.ZhTW=src.ZhTW, tgt.En=src.En, tgt.Ja=src.Ja, tgt.Ko=src.Ko
WHEN NOT MATCHED BY TARGET THEN INSERT (LangKey,ZhCN,ZhTW,En,Ja,Ko) VALUES (src.LangKey,src.ZhCN,src.ZhTW,src.En,src.Ja,src.Ko)
OUTPUT $action INTO @actionLog;
DECLARE @ins INT=(SELECT COUNT(*) FROM @actionLog WHERE act='INSERT');
DECLARE @upd INT=(SELECT COUNT(*) FROM @actionLog WHERE act='UPDATE');
PRINT N'  追加: ' + CAST(@ins AS nvarchar(10)) + N' / 更新: ' + CAST(@upd AS nvarchar(10));
DROP TABLE #i18n;
PRINT '=== Done ===';
