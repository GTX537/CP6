/* ============================================================
 * WMS 全画面 UI 文字列 i18n シードデータ
 * ============================================================
 * 対象テーブル：Sys_Langs
 * 範囲：wms.* (about 200+ keys × 5 languages = 1000+ rows)
 *
 * Naming convention:
 *   wms.common.{label}    – 全画面共通（search/save/cancel/...）
 *   wms.{module}.title    – 画面タイトル
 *   wms.{module}.col.{x}  – テーブル列見出し
 *   wms.{module}.fld.{x}  – フォーム項目
 *   wms.{module}.btn.{x}  – ボタン
 *   wms.{module}.dlg.{x}  – ダイアログ
 *   wms.{module}.msg.{x}  – メッセージ
 *   wms.{module}.status.{x} / type.{x} – 区分名
 *
 * 冪等：MERGE。何度実行しても安全。
 * 反映：実行後にバックエンド再起動（Lang Cache 1h）
 *
 * 実行：
 *   sqlcmd -S localhost\KOUSQLSERVER -d CP6DB -E -f 65001 -i docs/wms-views-i18n-seed.sql -b
 * ============================================================ */

SET NOCOUNT ON;
SET XACT_ABORT ON;

PRINT '=== WMS Views i18n シード開始 ===';

IF OBJECT_ID('tempdb..#i18n') IS NOT NULL DROP TABLE #i18n;
CREATE TABLE #i18n (
    LangKey nvarchar(200) NOT NULL PRIMARY KEY,
    ZhCN    nvarchar(500),
    ZhTW    nvarchar(500),
    En      nvarchar(500),
    Ja      nvarchar(500),
    Ko      nvarchar(500)
);

/* ============================================================
 * 1. 全画面共通（wms.common.*）
 * ============================================================ */
INSERT INTO #i18n VALUES
  (N'wms.common.search',     N'查询',       N'查詢',       N'Search',    N'検索',     N'검색'),
  (N'wms.common.clear',      N'清空',       N'清空',       N'Clear',     N'クリア',   N'지우기'),
  (N'wms.common.save',       N'保存',       N'儲存',       N'Save',      N'保存',     N'저장'),
  (N'wms.common.cancel',     N'取消',       N'取消',       N'Cancel',    N'キャンセル', N'취소'),
  (N'wms.common.edit',       N'编辑',       N'編輯',       N'Edit',      N'編集',     N'편집'),
  (N'wms.common.delete',     N'删除',       N'刪除',       N'Delete',    N'削除',     N'삭제'),
  (N'wms.common.create',     N'新建',       N'新增',       N'Create',    N'新規',     N'신규'),
  (N'wms.common.confirm',    N'确认',       N'確認',       N'Confirm',   N'確定',     N'확정'),
  (N'wms.common.back',       N'返回',       N'返回',       N'Back',      N'戻る',     N'뒤로'),
  (N'wms.common.refresh',    N'刷新',       N'重新整理',   N'Refresh',   N'更新',     N'새로고침'),
  (N'wms.common.open',       N'打开',       N'開啟',       N'Open',      N'開く',     N'열기'),
  (N'wms.common.action',     N'操作',       N'操作',       N'Action',    N'操作',     N'작업'),
  (N'wms.common.status',     N'状态',       N'狀態',       N'Status',    N'ステータス', N'상태'),
  (N'wms.common.type',       N'类型',       N'類型',       N'Type',      N'区分',     N'구분'),
  (N'wms.common.code',       N'编码',       N'編號',       N'Code',      N'CD',       N'코드'),
  (N'wms.common.name',       N'名称',       N'名稱',       N'Name',      N'名称',     N'명칭'),
  (N'wms.common.remarks',    N'备注',       N'備註',       N'Remarks',   N'備考',     N'비고'),
  (N'wms.common.required',   N'必填',       N'必填',       N'Required',  N'必須',     N'필수'),
  (N'wms.common.optional',   N'可选',       N'可選',       N'Optional',  N'任意',     N'선택'),
  (N'wms.common.total',      N'共计',       N'共計',       N'Total',     N'件数',     N'합계'),
  (N'wms.common.detail',     N'明细',       N'明細',       N'Detail',    N'明細',     N'명세'),
  (N'wms.common.history',    N'历史',       N'歷史',       N'History',   N'履歴',     N'이력'),
  (N'wms.common.from',       N'起',         N'從',         N'From',      N'FROM',     N'부터'),
  (N'wms.common.to',         N'止',         N'至',         N'To',        N'TO',       N'까지'),
  (N'wms.common.addLine',    N'+ 添加行',   N'+ 新增行',   N'+ Add Row', N'+ 行追加', N'+ 행 추가'),
  (N'wms.common.success',    N'成功',       N'成功',       N'Success',   N'成功',     N'성공'),
  (N'wms.common.confirmDelete', N'确认删除？', N'確認刪除？', N'Confirm delete?', N'削除しますか？', N'삭제하시겠습니까?'),
  (N'wms.common.unitPrice',  N'单价',       N'單價',       N'Unit Price', N'単価',    N'단가'),
  (N'wms.common.qty',        N'数量',       N'數量',       N'Quantity',  N'数量',     N'수량'),
  (N'wms.common.unit',       N'单位',       N'單位',       N'Unit',      N'単位',     N'단위'),
  (N'wms.common.line',       N'行',         N'行',         N'Line',      N'行',       N'행'),
  (N'wms.common.warehouse',  N'仓库',       N'倉庫',       N'Warehouse', N'倉庫',     N'창고'),
  (N'wms.common.location',   N'货位',       N'庫位',       N'Location',  N'ロケーション', N'로케이션'),
  (N'wms.common.product',    N'产品',       N'產品',       N'Product',   N'製品',     N'제품'),
  (N'wms.common.productName', N'产品名称',  N'產品名稱',   N'Product Name', N'製品名', N'제품명'),
  (N'wms.common.lot',        N'批次NO',     N'批次NO',     N'Lot No',    N'ロットNO', N'로트NO'),
  (N'wms.common.expiryDate', N'有效期',     N'有效期',     N'Expiry',    N'賞味期限', N'유통기한'),
  (N'wms.common.operator',   N'操作员',     N'操作員',     N'Operator',  N'作業者',   N'작업자'),
  (N'wms.common.createDate', N'创建日',     N'建立日',     N'Created',   N'登録日',   N'등록일'),
  (N'wms.common.noSelection', N'未选择',    N'未選擇',     N'Not selected', N'未選択', N'미선택'),
  (N'wms.common.expand',     N'展开',       N'展開',       N'Expand',    N'展開',     N'전개');

/* ============================================================
 * 2. 倉庫マスタ（wms.warehouse.*）
 * ============================================================ */
INSERT INTO #i18n VALUES
  (N'wms.warehouse.title',         N'仓库主数据',     N'倉庫主資料',     N'Warehouse Master', N'倉庫マスタ',   N'창고 마스터'),
  (N'wms.warehouse.fld.cd',        N'仓库CD',         N'倉庫CD',         N'Warehouse CD',     N'倉庫CD',       N'창고CD'),
  (N'wms.warehouse.fld.name',      N'仓库名称',       N'倉庫名稱',       N'Warehouse Name',   N'倉庫名',       N'창고명'),
  (N'wms.warehouse.fld.type',      N'仓库区分',       N'倉庫區分',       N'Warehouse Type',   N'倉庫区分',     N'창고 구분'),
  (N'wms.warehouse.fld.baseCd',    N'基地CD',         N'據點CD',         N'Base CD',          N'拠点CD',       N'거점CD'),
  (N'wms.warehouse.fld.manager',   N'负责人',         N'負責人',         N'Manager',          N'責任者',       N'책임자'),
  (N'wms.warehouse.fld.address',   N'地址',           N'地址',           N'Address',          N'住所',         N'주소'),
  (N'wms.warehouse.fld.allowNegative', N'允许负库存', N'允許負庫存',     N'Allow Negative',   N'マイナス在庫許可', N'마이너스 재고 허용'),
  (N'wms.warehouse.fld.allowed',   N'已允许',         N'已允許',         N'Allowed',          N'許可',         N'허용'),
  (N'wms.warehouse.dlg.create',    N'新建仓库',       N'新增倉庫',       N'New Warehouse',    N'倉庫新規',     N'창고 신규'),
  (N'wms.warehouse.dlg.edit',      N'编辑仓库',       N'編輯倉庫',       N'Edit Warehouse',   N'倉庫編集',     N'창고 편집'),
  (N'wms.warehouse.type.raw',      N'原材料',         N'原材料',         N'Raw Material',     N'原材料',       N'원재료'),
  (N'wms.warehouse.type.wip',      N'半成品',         N'半成品',         N'Work In Process',  N'半製品',       N'반제품'),
  (N'wms.warehouse.type.finished', N'成品',           N'成品',           N'Finished Goods',   N'完成品',       N'완성품'),
  (N'wms.warehouse.type.defective', N'不良品',        N'不良品',         N'Defective',        N'不良品',       N'불량품'),
  (N'wms.warehouse.type.external', N'外协',           N'外協',           N'External',         N'外注',         N'외주');

/* ============================================================
 * 3. ロケーション（wms.location.*）
 * ============================================================ */
INSERT INTO #i18n VALUES
  (N'wms.location.title',          N'货位管理',       N'庫位管理',       N'Location Management', N'ロケーション管理', N'로케이션 관리'),
  (N'wms.location.warehouseList',  N'仓库列表',       N'倉庫列表',       N'Warehouse List',   N'倉庫一覧',     N'창고 목록'),
  (N'wms.location.locationList',   N'货位列表',       N'庫位列表',       N'Location List',    N'ロケーション一覧', N'로케이션 목록'),
  (N'wms.location.selectWh',       N'请从左侧选择仓库', N'請從左側選擇倉庫', N'Please select a warehouse from the left', N'左の倉庫一覧から倉庫を選択してください', N'왼쪽 창고 목록에서 창고를 선택해 주세요'),
  (N'wms.location.noLocations',    N'尚无货位，请点击"新建"添加', N'尚無庫位，請點擊「新增」添加', N'No locations. Click "Create" to add.', N'ロケーション 0 件。「+ 新規」で追加してください。', N'로케이션 없음. 「+ 신규」로 추가하세요.'),
  (N'wms.location.fld.cd',         N'货位CD',         N'庫位CD',         N'Location CD',      N'ロケーションCD', N'로케이션CD'),
  (N'wms.location.fld.parentCd',   N'上级货位',       N'上級庫位',       N'Parent Location',  N'親ロケ',       N'상위 로케이션'),
  (N'wms.location.fld.parentHint', N'（顶层时留空）', N'（最上層時留空）', N'(empty for top level)', N'（最上位の場合は空）', N'（최상위 시 비움）'),
  (N'wms.location.fld.level',      N'层级',           N'層級',           N'Level',            N'階層レベル',   N'계층 레벨'),
  (N'wms.location.fld.displayName', N'显示名',        N'顯示名',         N'Display Name',     N'表示名',       N'표시명'),
  (N'wms.location.fld.coord',      N'坐标',           N'座標',           N'Coord',            N'座標',         N'좌표'),
  (N'wms.location.fld.capacity',   N'最大容量',       N'最大容量',       N'Max Capacity',     N'最大収容数',   N'최대 수용량'),
  (N'wms.location.fld.capacityHint', N'0 = 无限制',   N'0 = 無限制',     N'0 = unlimited',    N'0 = 制限なし', N'0 = 제한 없음'),
  (N'wms.location.fld.allowedType', N'允许收容类型',  N'允許收容類型',   N'Allowed Type',     N'収容可能区分', N'수용 가능 구분'),
  (N'wms.location.fld.allowedHint', N'例: RAW,WIP', N'例: RAW,WIP',   N'e.g. RAW,WIP',     N'例: RAW,WIP', N'예: RAW,WIP'),
  (N'wms.location.fld.pickable',   N'可拣货',         N'可揀貨',         N'Pickable',         N'ピッキング可', N'피킹 가능'),
  (N'wms.location.fld.blocked',    N'冻结',           N'凍結',           N'Blocked',          N'凍結中',       N'동결 중'),
  (N'wms.location.fld.barcode',    N'条码',           N'條碼',           N'Barcode',          N'バーコード',   N'바코드'),
  (N'wms.location.level.zone',     N'区域',           N'區域',           N'Zone',             N'ゾーン',       N'존'),
  (N'wms.location.level.aisle',    N'通道',           N'通道',           N'Aisle',            N'通路',         N'통로'),
  (N'wms.location.level.shelf',    N'货架',           N'貨架',           N'Shelf',            N'棚',           N'선반'),
  (N'wms.location.level.tier',     N'层',             N'層',             N'Tier',             N'棚段',         N'단'),
  (N'wms.location.level.bin',      N'储位',           N'儲位',           N'Bin',              N'ビン',         N'빈'),
  (N'wms.location.flag.frozen',    N'已冻结',         N'已凍結',         N'Frozen',           N'凍結',         N'동결'),
  (N'wms.location.flag.notPickable', N'不可拣',       N'不可揀',         N'No Pick',          N'不可',         N'불가'),
  (N'wms.location.capacityUnlimited', N'无限',        N'無限',           N'∞',                N'∞',            N'무한'),
  (N'wms.location.dlg.create',     N'新建货位',       N'新增庫位',       N'New Location',     N'ロケーション新規', N'로케이션 신규'),
  (N'wms.location.dlg.edit',       N'编辑货位',       N'編輯庫位',       N'Edit Location',    N'ロケーション編集', N'로케이션 편집');

/* ============================================================
 * 4. 在庫照会（wms.stock.*）
 * ============================================================ */
INSERT INTO #i18n VALUES
  (N'wms.stock.title',             N'库存查询',       N'庫存查詢',       N'Stock Inquiry',    N'在庫照会',     N'재고 조회'),
  (N'wms.stock.fld.owner',         N'所有者',         N'所有者',         N'Owner',            N'所有者',       N'소유자'),
  (N'wms.stock.fld.ownerSelf',     N'自有',           N'自有',           N'Self',             N'自社',         N'자사'),
  (N'wms.stock.fld.ownerCustomer', N'客户寄存',       N'客戶寄存',       N'Customer Consigned', N'客先預り',   N'고객 위탁'),
  (N'wms.stock.fld.hasStockOnly',  N'仅有库存',       N'僅有庫存',       N'In Stock Only',    N'残あり',       N'재고 있음'),
  (N'wms.stock.col.physical',      N'物理库存',       N'物理庫存',       N'Physical',         N'物理在庫',     N'물리 재고'),
  (N'wms.stock.col.allocated',     N'已分配',         N'已分配',         N'Allocated',        N'引当中',       N'할당 중'),
  (N'wms.stock.col.available',     N'可用',           N'可用',           N'Available',        N'利用可能',     N'사용 가능'),
  (N'wms.stock.col.owner',         N'所有',           N'所有',           N'Owner',            N'所有',         N'소유'),
  (N'wms.stock.col.flag',          N'标志',           N'標誌',           N'Flag',             N'フラグ',       N'플래그'),
  (N'wms.stock.flag.recall',       N'召回',           N'召回',           N'Recall',           N'回収',         N'리콜'),
  (N'wms.stock.flag.vmi',          N'VMI',            N'VMI',            N'VMI',              N'VMI',          N'VMI'),
  (N'wms.stock.dlg.history',       N'库存变动历史',   N'庫存變動歷史',   N'Stock History',    N'在庫変動履歴', N'재고 변동 이력'),
  (N'wms.stock.col.txnDateTime',   N'日时',           N'日時',           N'DateTime',         N'日時',         N'일시'),
  (N'wms.stock.col.txnType',       N'区分',           N'區分',           N'Type',             N'区分',         N'구분'),
  (N'wms.stock.col.relatedNo',     N'相关单号',       N'相關單號',       N'Related No',       N'関連伝票',     N'관련 전표');

/* ============================================================
 * 5. 入庫予定（wms.inbound.*）+ 実績（wms.receipt.*）
 * ============================================================ */
INSERT INTO #i18n VALUES
  (N'wms.inbound.title',           N'入库预定',       N'入庫預定',       N'Inbound Order',    N'入庫予定',     N'입고 예정'),
  (N'wms.inbound.titleList',       N'入库预定列表',   N'入庫預定列表',   N'Inbound Order List', N'入庫予定 一覧', N'입고 예정 목록'),
  (N'wms.inbound.titleNew',        N'入库预定 新建',  N'入庫預定 新增',  N'New Inbound Order', N'入庫予定 登録', N'입고 예정 신규'),
  (N'wms.inbound.titleEdit',       N'入库预定 编辑',  N'入庫預定 編輯',  N'Edit Inbound Order',N'入庫予定 編集', N'입고 예정 편집'),
  (N'wms.inbound.fld.no',          N'入库预定NO',     N'入庫預定NO',     N'Inbound No',       N'入庫予定NO',   N'입고 예정NO'),
  (N'wms.inbound.fld.supplierCd',  N'供应商CD',       N'供應商CD',       N'Supplier CD',      N'仕入先CD',     N'공급사CD'),
  (N'wms.inbound.fld.supplierName', N'供应商名',      N'供應商名',       N'Supplier Name',    N'仕入先名',     N'공급사명'),
  (N'wms.inbound.fld.poNo',        N'采购单NO',       N'採購單NO',       N'PO No',            N'発注書NO',     N'발주서NO'),
  (N'wms.inbound.fld.expectedArrival', N'预计到货日', N'預計到貨日',     N'Expected Arrival', N'入荷予定日',   N'입하 예정일'),
  (N'wms.inbound.fld.inboundWh',   N'入库仓库',       N'入庫倉庫',       N'Inbound Warehouse', N'入庫倉庫',   N'입고 창고'),
  (N'wms.inbound.col.expectedQty', N'预计数量',       N'預計數量',       N'Expected Qty',     N'予定数量',     N'예정 수량'),
  (N'wms.inbound.col.receivedQtyAccum', N'累计入库', N'累計入庫',       N'Cumulative Recv',  N'累計受入',     N'누계 입고'),
  (N'wms.inbound.col.expectedLoc', N'预定货位',       N'預定庫位',       N'Expected Loc',     N'予定ロケ',     N'예정 로케'),
  (N'wms.inbound.type.purchase',   N'采购入库',       N'採購入庫',       N'Purchase',         N'購買入庫',     N'구매 입고'),
  (N'wms.inbound.type.rework',     N'外协返还',       N'外協返還',       N'Subcontract Return', N'外注戻入',   N'외주 반환'),
  (N'wms.inbound.type.return',     N'退货入库',       N'退貨入庫',       N'Return',           N'返品入庫',     N'반품 입고'),
  (N'wms.inbound.type.other',      N'其他',           N'其他',           N'Other',            N'その他',       N'기타'),
  (N'wms.inbound.status.draft',    N'草稿',           N'草稿',           N'Draft',            N'下書き',       N'초안'),
  (N'wms.inbound.status.confirmed', N'已确认',        N'已確認',         N'Confirmed',        N'確定済',       N'확정됨'),
  (N'wms.inbound.status.partial',  N'入库中',         N'入庫中',         N'Partial',          N'入庫中',       N'입고 중'),
  (N'wms.inbound.status.completed', N'已完成',        N'已完成',         N'Completed',        N'完了',         N'완료'),
  (N'wms.inbound.status.cancelled', N'已取消',        N'已取消',         N'Cancelled',        N'取消',         N'취소됨'),
  (N'wms.inbound.btn.confirm',     N'确认',           N'確認',           N'Confirm',          N'確定',         N'확정'),
  (N'wms.inbound.btn.cancel',      N'取消',           N'取消',           N'Cancel',           N'取消',         N'취소'),
  (N'wms.inbound.btn.receipt',     N'入库',           N'入庫',           N'Receive',          N'入庫',         N'입고'),
  (N'wms.inbound.btn.receiptLong', N'入库实绩录入',   N'入庫實績錄入',   N'Inbound Receipt',  N'入庫実績入力', N'입고 실적 입력'),
  (N'wms.inbound.msg.noDetail',    N'明细0行，请添加', N'明細0行，請添加', N'No detail. Add a line.', N'WM-MSG-020：明細を追加してください', N'WM-MSG-020: 명세를 추가하세요'),
  (N'wms.inbound.msg.confirmAsk',  N'要确认此入库预定吗？', N'要確認此入庫預定嗎？', N'Confirm this inbound order?', N'この入庫予定を確定しますか？', N'이 입고 예정을 확정합니까?'),
  (N'wms.inbound.msg.cancelAsk',   N'要取消此入库预定吗？', N'要取消此入庫預定嗎？', N'Cancel this inbound order?',  N'この入庫予定を取消しますか？', N'이 입고 예정을 취소합니까?'),
  (N'wms.inbound.msg.deleteAsk',   N'要删除此入库预定吗？', N'要刪除此入庫預定嗎？', N'Delete this inbound order?',  N'この入庫予定を削除しますか？', N'이 입고 예정을 삭제합니까?');

INSERT INTO #i18n VALUES
  (N'wms.receipt.title',           N'入库实绩录入',   N'入庫實績錄入',   N'Inbound Receipt',  N'入庫実績 入力', N'입고 실적 입력'),
  (N'wms.receipt.modeRef',         N'参照模式',       N'參照模式',       N'Order Reference Mode', N'予定参照モード', N'예정 참조 모드'),
  (N'wms.receipt.modeDirect',      N'直接入库',       N'直接入庫',       N'Direct Mode',      N'直入モード',   N'직접 입고'),
  (N'wms.receipt.fld.refNo',       N'参照预定NO',     N'參照預定NO',     N'Reference Order',  N'入庫予定NO',   N'참조 예정NO'),
  (N'wms.receipt.fld.refNoHint',   N'（留空=直入）',  N'（留空=直入）',  N'(empty=direct)',   N'（空欄=直入）', N'（비어 있음=직접）'),
  (N'wms.receipt.fld.sourceType',  N'入库来源',       N'入庫來源',       N'Source Type',      N'入庫元区分',   N'입고 원천 구분'),
  (N'wms.receipt.fld.receiveDateTime', N'收货日时',   N'收貨日時',       N'Receive DateTime', N'受入日時',     N'입고 일시'),
  (N'wms.receipt.fld.workOrderNo', N'制造指令NO',     N'製造指令NO',     N'Work Order',       N'関連製造指図', N'관련 제조 지시'),
  (N'wms.receipt.fld.workOrderHint', N'（生产时填入）', N'（生產時填入）', N'(fill if PRODUCTION)', N'（PRODUCTION 時）', N'（PRODUCTION 시）'),
  (N'wms.receipt.col.refLine',     N'参照行',         N'參照行',         N'Ref Line',         N'参照行',       N'참조 행'),
  (N'wms.receipt.col.receivedQty', N'入库数量',       N'入庫數量',       N'Received Qty',     N'受入数量',     N'입고 수량'),
  (N'wms.receipt.col.locCd',       N'存放货位',       N'存放庫位',       N'Location',         N'配置ロケーション', N'배치 로케이션'),
  (N'wms.receipt.col.lotHint',     N'（留空=无批次）', N'（留空=無批次）', N'(empty=no-lot)',   N'（空=no-lot）', N'（비어 있음=no-lot）'),
  (N'wms.receipt.source.purchase', N'采购入库',       N'採購入庫',       N'Purchase',         N'購買入庫',     N'구매 입고'),
  (N'wms.receipt.source.production', N'生产完成品',   N'生產完成品',     N'Production',       N'製造完成品',   N'생산 완성품'),
  (N'wms.receipt.source.rma',      N'退货收货',       N'退貨收貨',       N'RMA Return',       N'返品受入',     N'RMA 반품'),
  (N'wms.receipt.source.manual',   N'直接入库(其他)', N'直接入庫(其他)', N'Manual',           N'直入(その他)', N'직접(기타)'),
  (N'wms.receipt.btn.loadOrder',   N'读取',           N'讀取',           N'Load',             N'読込',         N'읽기'),
  (N'wms.receipt.btn.confirmReceive', N'入库登记 + 库存反映', N'入庫登記 + 庫存反映', N'Confirm + Apply Stock', N'受入登録 + 在庫反映', N'입고 등록 + 재고 반영'),
  (N'wms.receipt.msg.loadOk',      N'读取了 {n} 行', N'讀取了 {n} 行', N'Loaded {n} rows', N'{n} 行を読み込みました', N'{n} 행을 읽었습니다'),
  (N'wms.receipt.msg.invalidStatus', N'参照预定不在确认状态', N'參照預定不在確認狀態', N'Order not in confirmed/partial status', N'予定が確定済 or 入庫中ではありません', N'예정이 확정 or 입고 중이 아닙니다'),
  (N'wms.receipt.msg.requireAll',  N'所有行: 产品CD/货位/数量(>0) 必填', N'所有行: 產品CD/庫位/數量(>0) 必填', N'All rows: Product/Loc/Qty(>0) required', N'全行で 製品CD/ロケーション/受入数量(>0) は必須', N'전 행에 제품CD/로케이션/수량(>0) 필수');

/* ============================================================
 * 6. 出庫指示（wms.outbound.*）
 * ============================================================ */
INSERT INTO #i18n VALUES
  (N'wms.outbound.title',          N'出库指令',       N'出庫指令',       N'Outbound Order',   N'出庫指示',     N'출고 지시'),
  (N'wms.outbound.titleList',      N'出库指令列表',   N'出庫指令列表',   N'Outbound Order List', N'出庫指示 一覧', N'출고 지시 목록'),
  (N'wms.outbound.titleNew',       N'出库指令 新建',  N'出庫指令 新增',  N'New Outbound Order',  N'出庫指示 新規', N'출고 지시 신규'),
  (N'wms.outbound.fld.no',         N'出库指令NO',     N'出庫指令NO',     N'Outbound No',      N'出庫指示NO',   N'출고 지시NO'),
  (N'wms.outbound.fld.workOrderNo', N'制造指令NO',    N'製造指令NO',     N'Work Order',       N'関連指図NO',   N'관련 지시NO'),
  (N'wms.outbound.fld.webOrderNo', N'订单NO',         N'訂單NO',         N'Order No',         N'関連受注NO',   N'관련 수주NO'),
  (N'wms.outbound.fld.customerCd', N'客户CD',         N'客戶CD',         N'Customer CD',      N'客先CD',       N'고객CD'),
  (N'wms.outbound.fld.customerName', N'客户名',       N'客戶名',         N'Customer Name',    N'客先名',       N'고객명'),
  (N'wms.outbound.fld.warehouseCd', N'出库仓库CD',    N'出庫倉庫CD',     N'Outbound Wh',      N'出庫倉庫CD',   N'출고 창고CD'),
  (N'wms.outbound.fld.plannedDate', N'计划日',        N'計劃日',         N'Planned Date',     N'計画日',       N'계획일'),
  (N'wms.outbound.fld.priority',   N'优先级',         N'優先級',         N'Priority',         N'優先度',       N'우선순위'),
  (N'wms.outbound.fld.carrierCd',  N'承运商CD',       N'承運商CD',       N'Carrier',          N'配送業者CD',   N'운송업체CD'),
  (N'wms.outbound.fld.shipToAddress', N'收货地址',    N'收貨地址',       N'Ship To Address',  N'配送先住所',   N'배송지 주소'),
  (N'wms.outbound.type.material',  N'生产领料',       N'生產領料',       N'Material',         N'材料出庫',     N'생산 출고'),
  (N'wms.outbound.type.shipping',  N'出货',           N'出貨',           N'Shipping',         N'出荷',         N'출하'),
  (N'wms.outbound.type.transfer',  N'内部调拨',       N'內部調撥',       N'Internal Transfer', N'社内振替',    N'내부 이동'),
  (N'wms.outbound.type.other',     N'其他',           N'其他',           N'Other',            N'その他',       N'기타'),
  (N'wms.outbound.status.draft',   N'草稿',           N'草稿',           N'Draft',            N'下書き',       N'초안'),
  (N'wms.outbound.status.confirmed', N'已确认',       N'已確認',         N'Confirmed',        N'確定済',       N'확정됨'),
  (N'wms.outbound.status.allocated', N'已分配',       N'已分配',         N'Allocated',        N'引当済',       N'할당됨'),
  (N'wms.outbound.status.picking', N'拣货中',         N'揀貨中',         N'Picking',          N'ピッキング',   N'피킹 중'),
  (N'wms.outbound.status.completed', N'已完成',       N'已完成',         N'Completed',        N'完了',         N'완료'),
  (N'wms.outbound.status.cancelled', N'已取消',       N'已取消',         N'Cancelled',        N'取消',         N'취소됨'),
  (N'wms.outbound.priority.normal', N'普通',          N'普通',           N'Normal',           N'通常',         N'보통'),
  (N'wms.outbound.priority.urgent', N'紧急',          N'緊急',           N'Urgent',           N'急ぎ',         N'긴급'),
  (N'wms.outbound.priority.express', N'特急',         N'特急',           N'Express',          N'特急',         N'특급'),
  (N'wms.outbound.col.requiredQty', N'需求数量',      N'需求數量',       N'Required',         N'必要数量',     N'필요 수량'),
  (N'wms.outbound.col.allocatedQty', N'已分配',       N'已分配',         N'Allocated',        N'引当数',       N'할당 수량'),
  (N'wms.outbound.col.shippedQty', N'已出库',         N'已出庫',         N'Shipped',          N'出庫済',       N'출고됨'),
  (N'wms.outbound.bridge.title',   N'自动展开',       N'自動展開',       N'Auto Expansion',   N'自動展開',     N'자동 전개'),
  (N'wms.outbound.bridge.fromWo',  N'从制造指令展开', N'從製造指令展開', N'From Work Order',  N'製造指図NO',   N'제조 지시NO에서'),
  (N'wms.outbound.bridge.fromOrder', N'从订单展开',   N'從訂單展開',     N'From Order',       N'受注NO',       N'수주NO에서'),
  (N'wms.outbound.bridge.expanded', N'展开完成: {no}', N'展開完成: {no}', N'Expanded: {no}', N'展開完了：{no}', N'전개 완료: {no}'),
  (N'wms.outbound.btn.confirm',    N'确认',           N'確認',           N'Confirm',          N'確定',         N'확정'),
  (N'wms.outbound.btn.allocate',   N'执行分配',       N'執行分配',       N'Allocate',         N'引当実行',     N'할당 실행'),
  (N'wms.outbound.btn.ship',       N'出库确认',       N'出庫確認',       N'Ship',             N'出庫確定',     N'출고 확정'),
  (N'wms.outbound.btn.cancel',     N'取消',           N'取消',           N'Cancel',           N'取消',         N'취소'),
  (N'wms.outbound.btn.delete',     N'删除',           N'刪除',           N'Delete',           N'削除',         N'삭제'),
  (N'wms.outbound.dlg.ship',       N'出库确认',       N'出庫確認',       N'Ship Confirmation', N'出庫確定',    N'출고 확정'),
  (N'wms.outbound.dlg.caseQty',    N'箱数',           N'箱數',           N'Case Qty',         N'ケース数',     N'박스 수'),
  (N'wms.outbound.dlg.totalWeight', N'总重量(kg)',    N'總重量(kg)',     N'Total Weight(kg)', N'総重量(kg)',   N'총중량(kg)'),
  (N'wms.outbound.dlg.totalVolume', N'总容积(m³)',    N'總容積(m³)',     N'Total Volume(m³)', N'総容積(m³)',   N'총용적(m³)'),
  (N'wms.outbound.dlg.trackingNo', N'追踪号',         N'追蹤號',         N'Tracking No',      N'追跡番号',     N'추적번호'),
  (N'wms.outbound.dlg.materialNote', N'按材料出库确认（无打包）', N'按材料出庫確認（無打包）', N'Ship as material (no package)', N'材料出庫として確定します（梱包なし）', N'자재 출고로 확정합니다（포장 없음）'),
  (N'wms.outbound.msg.allocateAsk', N'FIFO + 期限优先 执行分配？', N'FIFO + 期限優先 執行分配？', N'Execute FIFO + FEFO allocation?', N'FIFO + 期限優先で引当を実行しますか？', N'FIFO + 기한 우선으로 할당하시겠습니까?'),
  (N'wms.outbound.msg.allocateOk', N'已分配（已发出 RSV 事务）', N'已分配（已發出 RSV 事務）', N'Allocated (RSV issued)', N'引当しました（RSV トランザクション発行）', N'할당 완료（RSV 트랜잭션 발행）'),
  (N'wms.outbound.msg.shipOkPkg',  N'出库完成 + 打包NO: {no}', N'出庫完成 + 打包NO: {no}', N'Shipped + Package: {no}', N'出庫確定 + 梱包採番：{no}', N'출고 확정 + 포장NO: {no}'),
  (N'wms.outbound.msg.shipOkMat',  N'材料出库已确认', N'材料出庫已確認', N'Material ship confirmed', N'材料出庫確定（OUT トランザクション発行）', N'자재 출고 확정（OUT 트랜잭션 발행）'),
  (N'wms.outbound.msg.cancelAsk',  N'取消会解除已分配，确认？', N'取消會解除已分配，確認？', N'Cancel will release allocation. Proceed?', N'取消すると引当も解除されます。よろしいですか？', N'취소하면 할당도 해제됩니다. 계속하시겠습니까?');

/* ============================================================
 * 7. 棚卸（wms.stocktake.*）
 * ============================================================ */
INSERT INTO #i18n VALUES
  (N'wms.stocktake.title',         N'盘点',           N'盤點',           N'Stocktake',        N'棚卸',         N'재고 실사'),
  (N'wms.stocktake.titleList',     N'盘点列表',       N'盤點列表',       N'Stocktake List',   N'棚卸 一覧',    N'재고 실사 목록'),
  (N'wms.stocktake.fld.no',        N'盘点NO',         N'盤點NO',         N'Stocktake No',     N'棚卸NO',       N'재고 실사NO'),
  (N'wms.stocktake.fld.plannedDate', N'计划日',       N'計劃日',         N'Planned Date',     N'計画日',       N'계획일'),
  (N'wms.stocktake.fld.actualDate', N'实施日',        N'實施日',         N'Actual Date',      N'実施日',       N'실시일'),
  (N'wms.stocktake.fld.completedDate', N'完成日',     N'完成日',         N'Completed Date',   N'完了日',       N'완료일'),
  (N'wms.stocktake.fld.targetWh',  N'对象仓库',       N'對象倉庫',       N'Target Wh',        N'対象倉庫',     N'대상 창고'),
  (N'wms.stocktake.fld.targetLocPrefix', N'对象货位前缀', N'對象庫位前綴', N'Location Prefix', N'対象ロケ前方一致', N'대상 로케 접두사'),
  (N'wms.stocktake.fld.targetProduct', N'对象产品',   N'對象產品',       N'Target Product',   N'対象製品',     N'대상 제품'),
  (N'wms.stocktake.fld.approver',  N'审批人',         N'審批人',         N'Approver',         N'承認者',       N'승인자'),
  (N'wms.stocktake.fld.threshold', N'差异金额阈值',   N'差異金額閾值',   N'Diff Threshold',   N'差異金額閾値', N'차이 금액 임계값'),
  (N'wms.stocktake.type.full',     N'全盘',           N'全盤',           N'Full',             N'全棚卸',       N'전수 실사'),
  (N'wms.stocktake.type.cycle',    N'循环盘',         N'循環盤',         N'Cycle',            N'サイクル',     N'사이클'),
  (N'wms.stocktake.type.adhoc',    N'临时盘',         N'臨時盤',         N'AdHoc',            N'臨時',         N'임시'),
  (N'wms.stocktake.status.planned', N'计划',          N'計劃',           N'Planned',          N'計画',         N'계획'),
  (N'wms.stocktake.status.counting', N'清点中',       N'清點中',         N'Counting',         N'カウント中',   N'카운트 중'),
  (N'wms.stocktake.status.diffReview', N'差异确认中', N'差異確認中',     N'Diff Review',      N'差異確認中',   N'차이 확인 중'),
  (N'wms.stocktake.status.awaitingApproval', N'待审批', N'待審批',       N'Awaiting Approval', N'承認待ち',    N'승인 대기'),
  (N'wms.stocktake.status.completed', N'已完成',      N'已完成',         N'Completed',        N'完了',         N'완료'),
  (N'wms.stocktake.status.cancelled', N'已取消',      N'已取消',         N'Cancelled',        N'取消',         N'취소됨'),
  (N'wms.stocktake.col.book',      N'账面数',         N'帳面數',         N'Book Qty',         N'帳簿数',       N'장부 수량'),
  (N'wms.stocktake.col.counted',   N'实盘数',         N'實盤數',         N'Counted',          N'実棚数',       N'실사 수량'),
  (N'wms.stocktake.col.diff',      N'差异',           N'差異',           N'Diff',             N'差異',         N'차이'),
  (N'wms.stocktake.col.diffAmount', N'差异金额',      N'差異金額',       N'Diff Amount',      N'差異金額',     N'차이 금액'),
  (N'wms.stocktake.col.reason',    N'差异原因',       N'差異原因',       N'Reason',           N'差異理由',     N'차이 사유'),
  (N'wms.stocktake.approval.pending', N'未审批',      N'未審批',         N'Pending',          N'未承認',       N'미승인'),
  (N'wms.stocktake.approval.auto', N'自动通过',       N'自動通過',       N'Auto-Approved',    N'自動承認',     N'자동 승인'),
  (N'wms.stocktake.approval.approved', N'已批准',     N'已批准',         N'Approved',         N'承認済',       N'승인됨'),
  (N'wms.stocktake.approval.rejected', N'已驳回',     N'已駁回',         N'Rejected',         N'却下',         N'반려됨'),
  (N'wms.stocktake.btn.snapshot',  N'生成快照',       N'生成快照',       N'Create Snapshot',  N'スナップショット作成', N'스냅샷 생성'),
  (N'wms.stocktake.btn.startCount', N'开始清点',      N'開始清點',       N'Start Counting',   N'カウント開始', N'카운트 시작'),
  (N'wms.stocktake.btn.submitReview', N'提交审批',    N'提交審批',       N'Submit for Review', N'差異確認へ',  N'심사 제출'),
  (N'wms.stocktake.btn.approve',   N'批准',           N'批准',           N'Approve',          N'承認',         N'승인'),
  (N'wms.stocktake.btn.adjust',    N'反映调整',       N'反映調整',       N'Apply Adjust',     N'反映',         N'반영');

/* ============================================================
 * 8. ダッシュボード（wms.dashboard.*）
 * ============================================================ */
INSERT INTO #i18n VALUES
  (N'wms.dashboard.title',         N'WMS 仪表盘',     N'WMS 儀表盤',     N'WMS Dashboard',    N'WMSダッシュボード', N'WMS 대시보드'),
  (N'wms.dashboard.kpi.totalValue', N'总库存金额',    N'總庫存金額',     N'Total Stock Value', N'総在庫金額',  N'총 재고 금액'),
  (N'wms.dashboard.kpi.skuCount',  N'SKU 数',         N'SKU 數',         N'SKU Count',        N'SKU 数',       N'SKU 수'),
  (N'wms.dashboard.kpi.turnoverDays', N'平均周转天数', N'平均週轉天數',  N'Avg Turnover Days', N'平均回転日数', N'평균 회전 일수'),
  (N'wms.dashboard.kpi.dormant',   N'滞留品数',       N'滯留品數',       N'Dormant SKU',      N'滞留品件数',   N'잔류품 건수'),
  (N'wms.dashboard.kpi.todayInbound', N'今日入库预定', N'今日入庫預定',  N'Today Inbound',    N'今日入庫予定', N'오늘 입고 예정'),
  (N'wms.dashboard.kpi.todayShipping', N'今日出货预定', N'今日出貨預定', N'Today Shipping',   N'今日出荷予定', N'오늘 출하 예정'),
  (N'wms.dashboard.chart.trend',   N'近 30 天入出库趋势', N'近 30 天入出庫趨勢', N'30-Day In/Out Trend', N'30 日入出庫トレンド', N'30일 입출고 추세'),
  (N'wms.dashboard.chart.warehouseValue', N'各仓库库存金额', N'各倉庫庫存金額', N'Value by Warehouse', N'倉庫別在庫金額', N'창고별 재고 금액'),
  (N'wms.dashboard.alert.title',   N'告警',           N'告警',           N'Alerts',           N'アラート',     N'경보'),
  (N'wms.dashboard.alert.expiry',  N'有效期临近',     N'有效期臨近',     N'Expiring Soon',    N'賞味期限間近', N'유통기한 임박'),
  (N'wms.dashboard.alert.diff',    N'盘点差异未审批', N'盤點差異未審批', N'Stocktake Diff',   N'棚卸差異未承認', N'재고 차이 미승인'),
  (N'wms.dashboard.alert.delayed', N'入库预定延期',   N'入庫預定延期',   N'Inbound Delayed',  N'入庫予定遅延', N'입고 예정 지연');

/* ============================================================
 * 9. Placeholder（wms.placeholder.*）
 * ============================================================ */
INSERT INTO #i18n VALUES
  (N'wms.placeholder.comingSoon', N'即将到来', N'即將到來', N'Coming Soon', N'実装予定', N'구현 예정'),
  (N'wms.placeholder.inDev',      N'此页面正在开发中。', N'此頁面正在開發中。', N'This page is under development.', N'このページはまだ開発中です。', N'이 페이지는 개발 중입니다.'),
  (N'wms.placeholder.toDashboard', N'前往 WMS 仪表盘', N'前往 WMS 儀表盤', N'Go to WMS Dashboard', N'WMS ダッシュボードへ', N'WMS 대시보드로'),
  (N'wms.placeholder.back',       N'返回', N'返回', N'Back', N'戻る', N'뒤로');

/* ============================================================
 * MERGE 実行
 * ============================================================ */
DECLARE @actionLog TABLE (act nvarchar(10));

MERGE Sys_Langs AS tgt
USING #i18n AS src
   ON tgt.LangKey = src.LangKey
WHEN MATCHED THEN UPDATE SET
    tgt.ZhCN = src.ZhCN, tgt.ZhTW = src.ZhTW, tgt.En = src.En, tgt.Ja = src.Ja, tgt.Ko = src.Ko
WHEN NOT MATCHED BY TARGET THEN
    INSERT (LangKey, ZhCN, ZhTW, En, Ja, Ko)
    VALUES (src.LangKey, src.ZhCN, src.ZhTW, src.En, src.Ja, src.Ko)
OUTPUT $action INTO @actionLog;

DECLARE @ins INT = (SELECT COUNT(*) FROM @actionLog WHERE act = 'INSERT');
DECLARE @upd INT = (SELECT COUNT(*) FROM @actionLog WHERE act = 'UPDATE');
PRINT N'  追加: ' + CAST(@ins AS nvarchar(10)) + N' 件 / 更新: ' + CAST(@upd AS nvarchar(10)) + N' 件';

DROP TABLE #i18n;

PRINT '=== WMS Views i18n シード完了 ===';
PRINT '';
PRINT N'  ※ Cache-Aside で 1時間キャッシュされる為、即時反映には API 再起動が必要。';
