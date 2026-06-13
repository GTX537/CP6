/* ============================================================
 * WMS MSBBWM210/220/260 紙器業界特化 第2弾 i18n シードデータ
 *   wms.remnant.*    残材
 *   wms.plate.*      印版・木型
 *   wms.sample.*     サンプル品
 * ============================================================ */
SET NOCOUNT ON; SET XACT_ABORT ON;
PRINT '=== WMS PaperIndustry2 i18n シード開始 ===';

IF OBJECT_ID('tempdb..#i18n') IS NOT NULL DROP TABLE #i18n;
CREATE TABLE #i18n (
    LangKey nvarchar(200) NOT NULL PRIMARY KEY,
    ZhCN nvarchar(500), ZhTW nvarchar(500), En nvarchar(500), Ja nvarchar(500), Ko nvarchar(500)
);

-- ─── Remnant（wms.remnant.*）───
INSERT INTO #i18n VALUES
  (N'wms.remnant.title',             N'残材管理',           N'殘材管理',           N'Remnant Material',   N'残材管理',           N'잔재 관리'),
  (N'wms.remnant.fld.no',            N'残材NO',             N'殘材NO',             N'Remnant No',         N'残材NO',             N'잔재 NO'),
  (N'wms.remnant.fld.matType',       N'素材区分',           N'素材區分',           N'Material',           N'素材区分',           N'소재 구분'),
  (N'wms.remnant.fld.matGrade',      N'紙質/連量',          N'紙質/連量',          N'Grade',              N'紙質/連量',          N'지질/평량'),
  (N'wms.remnant.fld.widthMm',       N'幅(mm)',             N'幅(mm)',             N'Width(mm)',          N'幅(mm)',             N'폭(mm)'),
  (N'wms.remnant.fld.lengthMm',      N'長さ(mm)',           N'長度(mm)',           N'Length(mm)',         N'長さ(mm)',           N'길이(mm)'),
  (N'wms.remnant.fld.thickness',     N'厚み(μm)',           N'厚度(μm)',           N'Thickness(μm)',      N'厚み(μm)',           N'두께(μm)'),
  (N'wms.remnant.fld.qty',           N'数量',               N'數量',               N'Quantity',           N'数量',               N'수량'),
  (N'wms.remnant.fld.sourceWO',      N'由来製造指図',       N'由來製造指圖',       N'Source WO',          N'由来製造指図',       N'유래 작업지시'),
  (N'wms.remnant.fld.sourceRoll',    N'由来ロール',         N'由來捲',             N'Source Roll',        N'由来ロール',         N'유래 롤'),
  (N'wms.remnant.fld.reservedFor',   N'予約用途',           N'預約用途',           N'Reserved For',       N'予約用途',           N'예약 용도'),
  (N'wms.remnant.fld.minWidth',      N'最小幅',             N'最小幅',             N'Min Width',          N'最小幅',             N'최소 폭'),
  (N'wms.remnant.fld.minLength',     N'最小長',             N'最小長',             N'Min Length',         N'最小長',             N'최소 길이'),
  (N'wms.remnant.mat.paper',         N'纸',                 N'紙',                 N'Paper',              N'紙',                 N'종이'),
  (N'wms.remnant.mat.film',          N'膜',                 N'膜',                 N'Film',               N'フィルム',           N'필름'),
  (N'wms.remnant.mat.other',         N'其他',               N'其他',               N'Other',              N'その他',             N'기타'),
  (N'wms.remnant.status.available',  N'可用',               N'可用',               N'Available',          N'利用可',             N'사용 가능'),
  (N'wms.remnant.status.reserved',   N'已预约',             N'已預約',             N'Reserved',           N'予約済',             N'예약됨'),
  (N'wms.remnant.status.used',       N'已使用',             N'已使用',             N'Used',               N'使用済',             N'사용됨'),
  (N'wms.remnant.status.disposed',   N'已废弃',             N'已廢棄',             N'Disposed',           N'廃棄',               N'폐기'),
  (N'wms.remnant.btn.reserve',       N'预约',               N'預約',               N'Reserve',            N'予約',               N'예약'),
  (N'wms.remnant.btn.unreserve',     N'解除',               N'解除',               N'Unreserve',          N'予約解除',           N'예약 해제'),
  (N'wms.remnant.btn.use',           N'使用',               N'使用',               N'Mark Used',          N'使用済化',           N'사용 처리'),
  (N'wms.remnant.btn.dispose',       N'废弃',               N'廢棄',               N'Dispose',            N'廃棄',               N'폐기'),
  (N'wms.remnant.btn.match',         N'再利用检索',         N'再利用檢索',         N'Match',              N'再利用検索',         N'재이용 검색'),
  (N'wms.remnant.dlg.create',        N'新建残材',           N'新增殘材',           N'New Remnant',        N'残材新規',           N'잔재 신규'),
  (N'wms.remnant.dlg.reserve',       N'预约残材',           N'預約殘材',           N'Reserve Remnant',    N'残材予約',           N'잔재 예약'),
  (N'wms.remnant.dlg.match',         N'再利用候选检索',     N'再利用候選檢索',     N'Find Reusable',      N'再利用候補検索',     N'재이용 후보 검색'),
  (N'wms.remnant.msg.matchHint',     N'按素材类型+最小尺寸过滤，按尺寸升序', N'按素材類型+最小尺寸過濾', N'Filter by type+min size, ascending', N'素材区分 + 最小サイズで絞り込み、サイズ昇順', N'소재 + 최소 크기로 필터, 크기 오름차순');

-- ─── PlateMold（wms.plate.*）───
INSERT INTO #i18n VALUES
  (N'wms.plate.title',               N'印版/木型',          N'印版/木型',          N'Plate / Mold',       N'印版・木型',         N'인쇄판/금형'),
  (N'wms.plate.fld.no',              N'版NO',               N'版NO',               N'Plate No',           N'版NO',               N'판 NO'),
  (N'wms.plate.fld.type',            N'区分',               N'區分',               N'Type',               N'区分',               N'구분'),
  (N'wms.plate.fld.customer',        N'客户',               N'客戶',               N'Customer',           N'顧客',               N'고객'),
  (N'wms.plate.fld.product',         N'产品',               N'產品',               N'Product',            N'製品',               N'제품'),
  (N'wms.plate.fld.colorCount',      N'色数',               N'色數',               N'Colors',             N'色数',               N'색수'),
  (N'wms.plate.fld.sizeNote',        N'尺寸标记',           N'尺寸標記',           N'Size Note',          N'サイズ表記',         N'크기 표기'),
  (N'wms.plate.fld.madeDate',        N'制作日',             N'製作日',             N'Made Date',          N'製作日',             N'제작일'),
  (N'wms.plate.fld.madeCost',        N'制作费',             N'製作費',             N'Cost',               N'製作費',             N'제작비'),
  (N'wms.plate.fld.maxShots',        N'最大寿命',           N'最大壽命',           N'Max Shots',          N'最大寿命',           N'최대 수명'),
  (N'wms.plate.fld.usedShots',       N'累计使用',           N'累計使用',           N'Used',               N'累計使用',           N'누적 사용'),
  (N'wms.plate.fld.lifeRatio',       N'寿命率',             N'壽命率',             N'Life %',             N'寿命率',             N'수명률'),
  (N'wms.plate.fld.lastUsed',        N'最终使用',           N'最終使用',           N'Last Used',          N'最終使用',           N'최종 사용'),
  (N'wms.plate.fld.nextMaint',       N'下次维护',           N'下次維護',           N'Next Maint',         N'次回メンテ',         N'다음 정비'),
  (N'wms.plate.fld.shots',           N'本次使用ショット',   N'本次使用 Shot',      N'Shots',              N'今回ショット数',     N'이번 샷수'),
  (N'wms.plate.type.plate',          N'印版',               N'印版',               N'Plate',              N'印版',               N'인쇄판'),
  (N'wms.plate.type.mold',           N'木型',               N'木型',               N'Mold',               N'木型',               N'금형'),
  (N'wms.plate.type.cyl',            N'圆筒',               N'圓筒',               N'Cylinder',           N'シリンダ',           N'실린더'),
  (N'wms.plate.type.other',          N'其他',               N'其他',               N'Other',              N'その他',             N'기타'),
  (N'wms.plate.status.usable',       N'可用',               N'可用',               N'Usable',             N'使用可',             N'사용 가능'),
  (N'wms.plate.status.maintenance',  N'维护中',             N'維護中',             N'Maintenance',        N'メンテ中',           N'정비 중'),
  (N'wms.plate.status.lifeReached',  N'寿命到达',           N'壽命到達',           N'Life Reached',       N'寿命到達',           N'수명 도달'),
  (N'wms.plate.status.discarded',    N'已废版',             N'已廢版',             N'Discarded',          N'廃版',               N'폐기'),
  (N'wms.plate.btn.use',             N'记录使用',           N'記錄使用',           N'Record Usage',       N'使用記録',           N'사용 기록'),
  (N'wms.plate.btn.maintStart',      N'开始维护',           N'開始維護',           N'Start Maint',        N'メンテ開始',         N'정비 시작'),
  (N'wms.plate.btn.maintEnd',        N'完成维护',           N'完成維護',           N'End Maint',          N'メンテ完了',         N'정비 완료'),
  (N'wms.plate.btn.discard',         N'废版',               N'廢版',               N'Discard',            N'廃版化',             N'폐기 처리'),
  (N'wms.plate.btn.warnings',        N'寿命警报',           N'壽命警報',           N'Warnings',           N'寿命警報',           N'수명 경보'),
  (N'wms.plate.dlg.create',          N'新建版/型',          N'新增版/型',          N'New Plate/Mold',     N'印版・木型新規',     N'인쇄판/금형 신규'),
  (N'wms.plate.dlg.use',             N'记录使用ショット',   N'記錄使用 Shot',      N'Record Shots',       N'使用ショット記録',   N'사용 샷 기록'),
  (N'wms.plate.dlg.maintStart',      N'开始维护',           N'開始維護',           N'Start Maintenance',  N'メンテ開始',         N'정비 시작'),
  (N'wms.plate.msg.lifeAuto',        N'累计达到MaxShots 自动进入寿命到达状态', N'累計達到MaxShots 自動進入壽命到達狀態', N'Auto enters LifeReached when used ≥ max', N'累計が MaxShots に達すると自動で寿命到達状态に', N'누적이 MaxShots에 도달하면 자동 수명 도달'),
  (N'wms.plate.msg.maintReset',      N'维护完成会重置使用计数', N'維護完成會重置使用計數', N'Maintenance complete resets used count', N'メンテ完了で使用ショットがリセットされる', N'정비 완료 시 사용 카운트 리셋'),
  (N'wms.plate.msg.warnHint',        N'寿命率 ≥ 90% 或已寿命到达 的版/型', N'壽命率 ≥ 90% 或已壽命到達', N'Items with life% ≥ 90% or reached', N'寿命率 ≥ 90% または寿命到達済', N'수명률 ≥ 90% 또는 도달');

-- ─── Sample（wms.sample.*）───
INSERT INTO #i18n VALUES
  (N'wms.sample.title',              N'样品库存',           N'樣品庫存',           N'Sample Stock',       N'サンプル品',         N'샘플 재고'),
  (N'wms.sample.fld.no',             N'样品NO',             N'樣品NO',             N'Sample No',          N'サンプルNO',         N'샘플 NO'),
  (N'wms.sample.fld.type',           N'种类',               N'種類',               N'Type',               N'種別',               N'종류'),
  (N'wms.sample.fld.customer',       N'客户',               N'客戶',               N'Customer',           N'顧客',               N'고객'),
  (N'wms.sample.fld.product',        N'产品',               N'產品',               N'Product',            N'製品',               N'제품'),
  (N'wms.sample.fld.qty',            N'数量',               N'數量',               N'Qty',                N'数量',               N'수량'),
  (N'wms.sample.fld.lentTo',         N'借出对象',           N'借出對象',           N'Lent To',            N'貸出先',             N'대여 대상'),
  (N'wms.sample.fld.lentAt',         N'借出日',             N'借出日',             N'Lent At',            N'貸出日',             N'대여일'),
  (N'wms.sample.fld.expReturn',      N'预定归还日',         N'預定歸還日',         N'Expected Return',    N'返却予定日',         N'반환 예정일'),
  (N'wms.sample.fld.returnedAt',     N'实际归还日',         N'實際歸還日',         N'Returned At',        N'実返却日',           N'실 반환일'),
  (N'wms.sample.fld.registeredAt',   N'登记日',             N'登記日',             N'Registered At',      N'登録日',             N'등록일'),
  (N'wms.sample.type.proto',         N'试作',               N'試作',               N'Prototype',          N'試作',               N'시작'),
  (N'wms.sample.type.color',         N'色样',               N'色樣',               N'Color Swatch',       N'色見本',             N'색 견본'),
  (N'wms.sample.type.dummy',         N'白模',               N'白模',               N'Dummy',              N'ダミー',             N'더미'),
  (N'wms.sample.type.other',         N'其他',               N'其他',               N'Other',              N'その他',             N'기타'),
  (N'wms.sample.status.inStock',     N'保管中',             N'保管中',             N'In Stock',           N'保管中',             N'보관 중'),
  (N'wms.sample.status.lentOut',     N'借出中',             N'借出中',             N'Lent Out',           N'貸出中',             N'대여 중'),
  (N'wms.sample.status.returned',    N'已归还',             N'已歸還',             N'Returned',           N'返却済',             N'반환됨'),
  (N'wms.sample.status.expired',     N'失效',               N'失效',               N'Expired',            N'失効',               N'실효'),
  (N'wms.sample.btn.lend',           N'借出',               N'借出',               N'Lend',               N'貸出',               N'대여'),
  (N'wms.sample.btn.return',         N'归还',               N'歸還',               N'Return',             N'返却',               N'반환'),
  (N'wms.sample.btn.expire',         N'失效',               N'失效',               N'Expire',             N'失効化',             N'실효 처리'),
  (N'wms.sample.btn.overdue',        N'未归还(超期)',       N'未歸還(超期)',       N'Overdue',            N'未返却(超過)',       N'미반환(초과)'),
  (N'wms.sample.dlg.create',         N'新建样品',           N'新增樣品',           N'New Sample',         N'サンプル新規',       N'샘플 신규'),
  (N'wms.sample.dlg.lend',           N'借出样品',           N'借出樣品',           N'Lend Sample',        N'サンプル貸出',       N'샘플 대여'),
  (N'wms.sample.msg.overdueHint',    N'借出中且超过预定归还日', N'借出中且超過預定歸還日', N'Lent and past expected return date', N'貸出中かつ返却予定日超過', N'대여 중이며 반환 예정일 초과');

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
