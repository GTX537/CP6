/* ============================================================
 * WMS MSBBWM200/230/240/250 紙器業界特化 i18n シードデータ
 *   wms.paperRoll.*  原紙ロール
 *   wms.ink.*        インキ管理
 *   wms.pallet.*     パレット管理
 *   wms.vmi.*        VMI 客先預り在庫
 * ============================================================ */
SET NOCOUNT ON; SET XACT_ABORT ON;
PRINT '=== WMS PaperIndustry i18n シード開始 ===';

IF OBJECT_ID('tempdb..#i18n') IS NOT NULL DROP TABLE #i18n;
CREATE TABLE #i18n (
    LangKey nvarchar(200) NOT NULL PRIMARY KEY,
    ZhCN nvarchar(500), ZhTW nvarchar(500), En nvarchar(500), Ja nvarchar(500), Ko nvarchar(500)
);

-- ─── PaperRoll（wms.paperRoll.*）───
INSERT INTO #i18n VALUES
  (N'wms.paperRoll.title',             N'原纸卷',           N'原紙卷',           N'Paper Roll',         N'原紙ロール',         N'원지 롤'),
  (N'wms.paperRoll.fld.rollNo',        N'卷NO',             N'卷NO',             N'Roll No',            N'ロールNO',           N'롤NO'),
  (N'wms.paperRoll.fld.grade',         N'纸种',             N'紙種',             N'Grade',              N'銘柄',               N'지종'),
  (N'wms.paperRoll.fld.widthMm',       N'幅(mm)',           N'幅(mm)',           N'Width(mm)',          N'幅(mm)',             N'폭(mm)'),
  (N'wms.paperRoll.fld.basis',         N'米坪(g/m²)',       N'米坪(g/m²)',       N'Basis Wt',           N'坪量(g/m²)',         N'평량(g/m²)'),
  (N'wms.paperRoll.fld.grain',         N'流目',             N'流目',             N'Grain',              N'流れ目',             N'결방향'),
  (N'wms.paperRoll.fld.lengthM',       N'原长(m)',          N'原長(m)',          N'Length(m)',          N'原長(m)',            N'원장(m)'),
  (N'wms.paperRoll.fld.remaining',     N'剩余/原长',        N'剩餘/原長',        N'Remaining/Original', N'残/原長',            N'잔/원장'),
  (N'wms.paperRoll.fld.core',          N'芯径(inch)',       N'芯徑(inch)',       N'Core(inch)',         N'芯径(inch)',         N'코어(inch)'),
  (N'wms.paperRoll.fld.mfgDate',       N'生产日期',         N'生產日期',         N'Mfg Date',           N'製造日',             N'제조일'),
  (N'wms.paperRoll.fld.mfgLot',        N'生产批',           N'生產批',           N'Mfg Lot',            N'製造ロット',         N'제조 LOT'),
  (N'wms.paperRoll.fld.parentRoll',    N'母卷NO',           N'母卷NO',           N'Parent Roll',        N'親ロールNO',         N'모롤NO'),
  (N'wms.paperRoll.fld.disposeTh',     N'残米廃棄阈值(m)',  N'殘米廢棄閾值(m)',  N'Dispose Th(m)',      N'残米廃棄閾値(m)',    N'잔미 폐기 임계값(m)'),
  (N'wms.paperRoll.fld.consumeLen',    N'消耗长度(m)',      N'消耗長度(m)',      N'Consume Len(m)',     N'消費長(m)',          N'소비 길이(m)'),
  (N'wms.paperRoll.fld.childWidths',   N'子卷宽度(mm)',     N'子卷寬度(mm)',     N'Child Widths(mm)',   N'子ロール幅(mm)',     N'자식 롤 폭(mm)'),
  (N'wms.paperRoll.fld.keepRemnant',   N'保留残米',         N'保留殘米',         N'Keep Remnant',       N'残米保留',           N'잔미 보존'),
  (N'wms.paperRoll.status.inStock',    N'在库',             N'在庫',             N'In Stock',           N'在庫',               N'재고'),
  (N'wms.paperRoll.status.inUse',      N'使用中',           N'使用中',           N'In Use',             N'使用中',             N'사용 중'),
  (N'wms.paperRoll.status.remnant',    N'残米',             N'殘米',             N'Remnant',            N'残米',               N'잔미'),
  (N'wms.paperRoll.status.disposed',   N'已废弃',           N'已廢棄',           N'Disposed',           N'廃棄',               N'폐기'),
  (N'wms.paperRoll.btn.consume',       N'消耗',             N'消耗',             N'Consume',            N'消費',               N'소비'),
  (N'wms.paperRoll.btn.dispose',       N'废弃',             N'廢棄',             N'Dispose',            N'廃棄',               N'폐기'),
  (N'wms.paperRoll.btn.slit',          N'分切',             N'分切',             N'Slit',               N'スリット',           N'슬릿'),
  (N'wms.paperRoll.dlg.create',        N'新建原纸卷',       N'新增原紙卷',       N'New Paper Roll',     N'原紙ロール新規',     N'원지 롤 신규'),
  (N'wms.paperRoll.msg.parentHint',    N'输入要分切的母卷NO',  N'輸入要分切的母卷NO',  N'Enter parent roll No', N'分切する親ロールNO を入力', N'분할할 모롤NO 입력'),
  (N'wms.paperRoll.msg.widthsHint',    N'用逗号分隔，例: 905,390', N'用逗號分隔，例: 905,390', N'Comma separated, e.g. 905,390', N'カンマ区切り, 例: 905,390', N'쉼표 구분, 예: 905,390'),
  (N'wms.paperRoll.msg.rollsCreated',  N'个子卷已生成',     N'個子卷已生成',     N'rolls created',      N'本の子ロール生成',   N'개의 자식 롤 생성');

-- ─── Ink（wms.ink.*）───
INSERT INTO #i18n VALUES
  (N'wms.ink.title',                N'油墨管理',           N'油墨管理',           N'Ink Management',     N'インキ管理',         N'잉크 관리'),
  (N'wms.ink.tab.lots',             N'墨水批次',           N'墨水批次',           N'Ink Lots',           N'インキロット',       N'잉크 LOT'),
  (N'wms.ink.tab.matches',          N'调色履历',           N'調色履歷',           N'Color Matches',      N'色合わせ履歴',       N'색맞춤 이력'),
  (N'wms.ink.fld.lotNo',            N'墨水批次NO',         N'墨水批次NO',         N'Ink Lot No',         N'インキロットNO',     N'잉크 LOT NO'),
  (N'wms.ink.fld.colorCode',        N'色号',               N'色號',               N'Color',              N'色番',               N'색번호'),
  (N'wms.ink.fld.inkType',          N'类型',               N'類型',               N'Type',               N'インキ種別',         N'잉크 종류'),
  (N'wms.ink.fld.openStatus',       N'开封状态',           N'開封狀態',           N'Open Status',        N'開封状態',           N'개봉 상태'),
  (N'wms.ink.fld.expiry',           N'有效期',             N'有效期',             N'Expiry',             N'有効期限',           N'유통기한'),
  (N'wms.ink.fld.viscosity',        N'粘度(cP)',           N'粘度(cP)',           N'Viscosity(cP)',      N'粘度(cP)',           N'점도(cP)'),
  (N'wms.ink.fld.solidContent',     N'固含(%)',            N'固含(%)',            N'Solids(%)',          N'固形分(%)',          N'고형분(%)'),
  (N'wms.ink.fld.supplier',         N'供应商',             N'供應商',             N'Supplier',           N'仕入先',             N'공급사'),
  (N'wms.ink.fld.parentLotA',       N'母批A',              N'母批A',              N'Parent A',           N'親ロットA',          N'모LOT A'),
  (N'wms.ink.fld.parentLotB',       N'母批B',              N'母批B',              N'Parent B',           N'親ロットB',          N'모LOT B'),
  (N'wms.ink.fld.parentQtyA',       N'母批A数量',          N'母批A數量',          N'Qty A',              N'親A 数量',           N'A 수량'),
  (N'wms.ink.fld.parentQtyB',       N'母批B数量',          N'母批B數量',          N'Qty B',              N'親B 数量',           N'B 수량'),
  (N'wms.ink.fld.newColorCode',     N'新色号',             N'新色號',             N'New Color',          N'新色番',             N'새 색번호'),
  (N'wms.ink.fld.newLocationCd',    N'新货位',             N'新庫位',             N'New Loc',            N'新ロケ',             N'새 로케'),
  (N'wms.ink.fld.matchNo',          N'调色NO',             N'調色NO',             N'Match No',           N'色合わせNO',         N'색맞춤 NO'),
  (N'wms.ink.fld.formula',          N'配方JSON',           N'配方JSON',           N'Formula JSON',       N'処方JSON',           N'배합 JSON'),
  (N'wms.ink.fld.consumedQty',      N'消耗量',             N'消耗量',             N'Consumed',           N'消費量',             N'소비량'),
  (N'wms.ink.fld.matchedAt',        N'调色时间',           N'調色時間',           N'Matched At',         N'調合時刻',           N'조합 시각'),
  (N'wms.ink.fld.operator',         N'操作员',             N'操作員',             N'Operator',           N'担当者',             N'담당자'),
  (N'wms.ink.type.offset',          N'胶印',               N'膠印',               N'Offset',             N'オフセット',         N'옵셋'),
  (N'wms.ink.type.flexo',           N'柔印',               N'柔印',               N'Flexo',              N'フレキソ',           N'플렉소'),
  (N'wms.ink.type.uv',              N'UV',                 N'UV',                 N'UV',                 N'UV',                 N'UV'),
  (N'wms.ink.type.other',           N'其他',               N'其他',               N'Other',              N'その他',             N'기타'),
  (N'wms.ink.open.unopened',        N'未开封',             N'未開封',             N'Unopened',           N'未開封',             N'미개봉'),
  (N'wms.ink.open.opened',          N'已开封',             N'已開封',             N'Opened',             N'開封済',             N'개봉됨'),
  (N'wms.ink.btn.open',             N'开封',               N'開封',               N'Open Lot',           N'開封処理',           N'개봉 처리'),
  (N'wms.ink.btn.mix',              N'调合',               N'調合',               N'Mix',                N'混合',               N'혼합'),
  (N'wms.ink.btn.record',           N'登记调色',           N'登記調色',           N'Record Match',       N'色合せ記録',         N'색맞춤 기록'),
  (N'wms.ink.btn.expiring',         N'30日内到期',         N'30日內到期',         N'Expiring 30d',       N'30日以内期限切れ',   N'30일내 만료'),
  (N'wms.ink.dlg.create',           N'新建墨水批次',       N'新增墨水批次',       N'New Ink Lot',        N'インキロット新規',   N'잉크 LOT 신규'),
  (N'wms.ink.dlg.open',             N'墨水开封',           N'墨水開封',           N'Open Ink Lot',       N'インキ開封',         N'잉크 개봉'),
  (N'wms.ink.dlg.mix',              N'调合(2種混合)',      N'調合(2種混合)',      N'Mix (2 lots)',       N'混合(2ロット)',      N'혼합(2 LOT)'),
  (N'wms.ink.dlg.record',           N'登记调色履历',       N'登記調色履歷',       N'Record Color Match', N'色合せ履歴登録',     N'색맞춤 이력 등록'),
  (N'wms.ink.fld.newExpiry',        N'开封后新期限',       N'開封後新期限',       N'New Expiry After Open', N'開封後新期限',   N'개봉 후 새 기한'),
  (N'wms.ink.msg.openHint',         N'开封后通常有效期会缩短，请重新指定', N'開封後通常有效期會縮短', N'After opening, expiry usually shortens', N'開封後は通常有効期限が短縮されます', N'개봉 후 유효기간이 단축됩니다');

-- ─── Pallet（wms.pallet.*）───
INSERT INTO #i18n VALUES
  (N'wms.pallet.title',             N'托盘管理',           N'棧板管理',           N'Pallet Mgmt',        N'パレット管理',       N'팔레트 관리'),
  (N'wms.pallet.fld.no',            N'托盘NO',             N'棧板NO',             N'Pallet No',          N'パレットNO',         N'팔레트 NO'),
  (N'wms.pallet.fld.cartonQty',     N'箱数',               N'箱數',               N'Cartons',            N'カートン数',         N'카톤 수'),
  (N'wms.pallet.fld.weightKg',      N'重量(kg)',           N'重量(kg)',           N'Weight(kg)',         N'重量(kg)',           N'중량(kg)'),
  (N'wms.pallet.fld.heightMm',      N'高度(mm)',           N'高度(mm)',           N'Height(mm)',         N'高さ(mm)',           N'높이(mm)'),
  (N'wms.pallet.fld.maxStack',      N'最大堆叠',           N'最大堆疊',           N'Max Stack',          N'最大段積',           N'최대 적재단'),
  (N'wms.pallet.fld.outboundNo',    N'出库NO',             N'出庫NO',             N'Outbound No',        N'出庫NO',             N'출고 NO'),
  (N'wms.pallet.fld.toLoc',         N'目的货位',           N'目的庫位',           N'To Location',        N'移動先ロケ',         N'이동 대상 로케'),
  (N'wms.pallet.status.building',   N'组装中',             N'組裝中',             N'Building',           N'組成中',             N'조립 중'),
  (N'wms.pallet.status.inStock',    N'保管中',             N'保管中',             N'In Stock',           N'保管中',             N'보관 중'),
  (N'wms.pallet.status.waitingShip', N'待出货',            N'待出貨',             N'Waiting Ship',       N'出荷待機',           N'출하 대기'),
  (N'wms.pallet.status.shipped',    N'已出货',             N'已出貨',             N'Shipped',            N'出荷済',             N'출하됨'),
  (N'wms.pallet.btn.complete',      N'完成组装',           N'完成組裝',           N'Complete Build',     N'組成完了',           N'조립 완료'),
  (N'wms.pallet.btn.moveShip',      N'移至待出货',         N'移至待出貨',         N'Move to Shipping',   N'出荷待機へ移動',     N'출하 대기로 이동'),
  (N'wms.pallet.btn.markShipped',   N'标记已出货',         N'標記已出貨',         N'Mark Shipped',       N'出荷済にマーク',     N'출하 처리'),
  (N'wms.pallet.dlg.create',        N'新建托盘',           N'新增棧板',           N'New Pallet',         N'パレット新規',       N'팔레트 신규'),
  (N'wms.pallet.dlg.moveShip',      N'移至待出货货位',     N'移至待出貨庫位',     N'Move to Shipping Loc', N'出荷待機ロケへ移動', N'출하 대기 로케로 이동'),
  (N'wms.pallet.dlg.markShipped',   N'确认出货',           N'確認出貨',           N'Confirm Shipped',    N'出荷確定',           N'출하 확정');

-- ─── VMI（wms.vmi.*）───
INSERT INTO #i18n VALUES
  (N'wms.vmi.title',                N'VMI 寄存库存',       N'VMI 寄存庫存',       N'VMI Stock',          N'VMI 預り在庫',       N'VMI 위탁 재고'),
  (N'wms.vmi.tab.customers',        N'客户汇总',           N'客戶彙總',           N'Customers',          N'客先別サマリ',       N'고객별 요약'),
  (N'wms.vmi.tab.details',          N'明细',               N'明細',               N'Details',            N'明細',               N'명세'),
  (N'wms.vmi.tab.billings',         N'保管费',             N'保管費',             N'Billings',           N'保管料請求',         N'보관료 청구'),
  (N'wms.vmi.fld.customerCd',       N'客户CD',             N'客戶CD',             N'Customer',           N'客先CD',             N'고객 CD'),
  (N'wms.vmi.fld.customerName',     N'客户名',             N'客戶名',             N'Customer Name',      N'客先名',             N'고객명'),
  (N'wms.vmi.fld.skuCount',         N'SKU数',              N'SKU數',              N'SKU Cnt',            N'SKU 数',             N'SKU 수'),
  (N'wms.vmi.fld.physical',         N'物理库存',           N'物理庫存',           N'Physical',           N'物理在庫',           N'물리 재고'),
  (N'wms.vmi.fld.allocated',        N'已分配',             N'已分配',             N'Allocated',          N'引当済',             N'할당됨'),
  (N'wms.vmi.fld.available',        N'可用',               N'可用',               N'Available',          N'利用可能',           N'사용 가능'),
  (N'wms.vmi.fld.estValue',         N'估算金额',           N'估算金額',           N'Est. Value',         N'推定金額',           N'추정 금액'),
  (N'wms.vmi.fld.yearMonth',        N'年月',               N'年月',               N'YearMonth',          N'年月',               N'연월'),
  (N'wms.vmi.fld.dailyRate',        N'日单价',             N'日單價',             N'Daily Rate',         N'日割単価',           N'일 단가'),
  (N'wms.vmi.fld.beginQty',         N'期初数',             N'期初數',             N'Begin Qty',          N'期首数量',           N'기초 수량'),
  (N'wms.vmi.fld.endQty',           N'期末数',             N'期末數',             N'End Qty',            N'期末数量',           N'기말 수량'),
  (N'wms.vmi.fld.avgQty',           N'平均数',             N'平均數',             N'Avg Qty',            N'平均数量',           N'평균 수량'),
  (N'wms.vmi.fld.billingAmt',       N'保管费',             N'保管費',             N'Billing Amt',        N'保管料',             N'보관료'),
  (N'wms.vmi.fld.billingNo',        N'账单NO',             N'帳單NO',             N'Billing No',         N'請求NO',             N'청구 NO'),
  (N'wms.vmi.fld.calculatedAt',     N'计算时间',           N'計算時間',           N'Calculated At',      N'計算時刻',           N'계산 시각'),
  (N'wms.vmi.fld.confirmed',        N'已确认',             N'已確認',             N'Confirmed',          N'確定済',             N'확정됨'),
  (N'wms.vmi.fld.receiveDate',      N'入库日',             N'入庫日',             N'Receive Date',       N'入庫日',             N'입고일'),
  (N'wms.vmi.btn.calculate',        N'计算月度保管费',     N'計算月度保管費',     N'Calculate Monthly',  N'月次保管料計算',     N'월별 보관료 계산'),
  (N'wms.vmi.btn.confirm',          N'确认账单',           N'確認帳單',           N'Confirm Billing',    N'請求確定',           N'청구 확정'),
  (N'wms.vmi.btn.viewDetail',       N'查看明细',           N'查看明細',           N'View Details',       N'明細を見る',         N'명세 보기'),
  (N'wms.vmi.dlg.calculate',        N'计算月度保管费',     N'計算月度保管費',     N'Calculate Monthly Storage Fee', N'月次保管料計算', N'월별 보관료 계산'),
  (N'wms.vmi.msg.calcHint',         N'按月平均库存 × 日单价 × 当月天数 计算保管费', N'按月平均庫存 × 日單價 × 當月天數 計算保管費', N'Monthly fee = avg stock × daily rate × days in month', N'月平均在庫 × 日割単価 × 月日数 で算出', N'월평균 재고 × 일단가 × 월일수로 산출'),
  (N'wms.vmi.msg.confirmedTitle',   N'账单已确认，不可再算', N'帳單已確認，不可再算', N'Billing confirmed; cannot recalculate', N'請求確定済、再計算不可', N'청구 확정됨, 재계산 불가'),
  (N'wms.vmi.msg.upserted',         N'已生成/更新 {n} 笔保管费记录', N'已產生/更新 {n} 筆保管費紀錄', N'{n} billings upserted', N'{n} 件の保管料を生成/更新', N'{n}건 보관료 처리됨');

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
