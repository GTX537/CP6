// MSBBWM WMS Phase 1 — TypeScript 型定義

export interface WmsApi<T> {
  code: number
  message: string
  data: T
}

export interface WmsPaged<T> {
  total: number
  page: number
  pageSize: number
  items: T[]
}

// ───────── 倉庫マスタ ─────────

export interface Warehouse {
  id?: string
  warehouseCd: string
  warehouseName: string
  warehouseType: number // 1=原材料 2=半製品 3=完成品 4=不良品 5=外注
  baseCd?: string
  addressText?: string
  managerCd?: string
  allowNegative: boolean
  remarks?: string
  createDate?: string
  modifyDate?: string
  isDeleted?: boolean
}

// ───────── ロケーション ─────────

export interface WmsLocation {
  id?: string
  locationCd: string
  warehouseCd: string
  parentLocationCd?: string
  locationLevel: number // 1~5
  locationName?: string
  xCoord?: number
  yCoord?: number
  zCoord?: number
  capacityQty: number
  allowedProductType?: string
  isPickable: boolean
  isBlocked: boolean
  barcode?: string
}

// ───────── 在庫 ─────────

export interface Stock {
  id: string
  warehouseCd: string
  locationCd: string
  productCd: string
  lotNo: string
  physicalQty: number
  allocatedQty: number
  availableQty: number
  unitCd?: string
  receiveDate?: string
  expiryDate?: string
  unitPrice?: number
  recallFlag: boolean
  ownerType: string // SELF / CUSTOMER
  ownerCd?: string
  paperRollNo?: string
}

export interface StockTransaction {
  id: string
  txnNo: string
  txnType: 'IN' | 'OUT' | 'MOVE' | 'ADJ' | 'RSV' | 'UNRSV'
  txnDateTime: string
  warehouseCd: string
  locationCd: string
  productCd: string
  lotNo: string
  qty: number
  unitPrice?: number
  relatedNo?: string
  relatedType?: string
  operatorCd?: string
  remark?: string
}

// ───────── リクエスト DTO ─────────

export interface StockSearchQuery {
  warehouseCd?: string
  locationCd?: string
  productCd?: string
  lotNo?: string
  ownerType?: string
  ownerCd?: string
  hasStockOnly?: boolean
  page?: number
  pageSize?: number
}

export interface StockMovementRequest {
  txnType: 'IN' | 'OUT' | 'ADJ' | 'RSV' | 'UNRSV'
  warehouseCd: string
  locationCd: string
  productCd: string
  lotNo: string
  qty: number
  unitCd?: string
  unitPrice?: number
  relatedNo?: string
  relatedType?: string
  operatorCd?: string
  remark?: string
  expiryDate?: string
  receiveDate?: string
  ownerType?: string
  ownerCd?: string
  paperRollNo?: string
}

export interface StockMoveRequest {
  warehouseCd: string
  fromLocationCd: string
  toLocationCd: string
  productCd: string
  lotNo: string
  qty: number
  operatorCd?: string
  remark?: string
}

// ───────── 入庫予定（WM030） ─────────

export interface InboundOrder {
  id?: string
  inboundNo?: string
  inboundType: number // 1=購買 2=外注戻 3=返品 9=その他
  supplierCd?: string
  supplierName?: string
  poNo?: string
  expectedArrivalDate: string
  warehouseCd: string
  status: number // 0/1/2/3/9
  remarks?: string
  details: InboundOrderDetail[]
}

export interface InboundOrderDetail {
  lineNo: number
  productCd: string
  productName?: string
  lotNo?: string
  expectedQty: number
  receivedQty: number
  unitCd?: string
  expectedLocationCd?: string
  unitPrice?: number
  remarks?: string
}

export interface InboundOrderSearchQuery {
  inboundNo?: string
  supplierCd?: string
  warehouseCd?: string
  status?: number
  arrivalFrom?: string
  arrivalTo?: string
  page?: number
  pageSize?: number
}

// ───────── 入庫実績（WM040） ─────────

export interface InboundReceipt {
  id?: string
  receiptNo?: string
  inboundNo?: string
  sourceType: 'PURCHASE' | 'PRODUCTION' | 'RMA' | 'MANUAL'
  workOrderNo?: string
  receiveDateTime: string
  operatorCd?: string
  warehouseCd: string
  status: number
  remarks?: string
  details: InboundReceiptDetail[]
}

export interface InboundReceiptDetail {
  lineNo: number
  refOrderLineNo?: number
  productCd: string
  productName?: string
  lotNo: string
  receivedQty: number
  unitCd?: string
  locationCd: string
  unitPrice?: number
  expiryDate?: string
  paperRollNo?: string
  stockTxnNo?: string
  remarks?: string
}

export interface InboundReceiptSearchQuery {
  receiptNo?: string
  inboundNo?: string
  workOrderNo?: string
  warehouseCd?: string
  status?: number
  dateFrom?: string
  dateTo?: string
  page?: number
  pageSize?: number
}

// ───────── 出庫指示（WM050/070） ─────────

export interface OutboundOrder {
  id?: string
  outboundNo?: string
  outboundType: number // 1=材料 2=出荷 3=社内振替 9=その他
  workOrderNo?: string
  webOrderNo?: string
  customerCd?: string
  customerName?: string
  warehouseCd: string
  plannedDate: string
  status: number // 0/1/2/3/4/9
  priority: number // 1=通常 2=急 3=特急
  shipToAddress?: string
  carrierCd?: string
  remarks?: string
  details: OutboundOrderDetail[]
}

export interface OutboundOrderDetail {
  lineNo: number
  productCd: string
  productName?: string
  requiredQty: number
  allocatedQty: number
  shippedQty: number
  lotNo?: string
  locationCd?: string
  unitCd?: string
  unitPrice?: number
  allocateTxnNo?: string
  shipTxnNo?: string
  remarks?: string
}

export interface OutboundOrderSearchQuery {
  outboundNo?: string
  outboundType?: number
  status?: number
  workOrderNo?: string
  webOrderNo?: string
  customerCd?: string
  warehouseCd?: string
  plannedFrom?: string
  plannedTo?: string
  page?: number
  pageSize?: number
}

export interface ShipRequest {
  caseQty?: number
  totalWeightKg?: number
  totalVolumeM3?: number
  carrierCd?: string
  trackingNo?: string
  remarks?: string
}

// ───────── 出荷梱包（WM080） ─────────

export interface ShippingPackage {
  id?: string
  packageNo: string
  outboundNo: string
  caseQty: number
  totalWeightKg?: number
  totalVolumeM3?: number
  carrierCd?: string
  trackingNo?: string
  departureTime?: string
  remarks?: string
}

// ───────── 賞味期限・FEFO（WM170） ─────────

export interface ExpiryStock {
  stockId: string
  warehouseCd: string
  locationCd: string
  productCd: string
  lotNo: string
  physicalQty: number
  availableQty: number
  unitPrice?: number
  expiryDate?: string
  daysUntilExpiry: number
  unitCd?: string
  lossAmount?: number
}

export interface ExpiryDisposeRequest {
  stockIds: string[]
  reason?: string
}

// ───────── QC 入荷検品（WM100） ─────────

export interface QcInspection {
  id?: string
  inspectionNo?: string
  inboundNo?: string
  supplierCd?: string
  supplierName?: string
  arrivalDateTime: string
  inspectorCd?: string
  status: number  // 0/1/2/9
  finalJudgement?: 'PASS' | 'CONDITIONAL' | 'HOLD' | 'FAIL' | 'RETURN'
  judgementReason?: string
  generatedReceiptNo?: string
  photoUrls?: string
  remarks?: string
  items: QcInspectionItem[]
}

export interface QcInspectionItem {
  lineNo: number
  productCd: string
  productName?: string
  expectedQty: number
  receivedQty: number
  acceptedQty: number
  rejectedQty: number
  pendingQty: number
  defectReasonCd?: string
  remarks?: string
}

export interface QcInspectionSearchQuery {
  inspectionNo?: string
  inboundNo?: string
  supplierCd?: string
  status?: number
  finalJudgement?: string
  dateFrom?: string
  dateTo?: string
  page?: number
  pageSize?: number
}

export interface QcJudgeRequest {
  finalJudgement: 'PASS' | 'CONDITIONAL' | 'HOLD' | 'FAIL' | 'RETURN'
  reason?: string
  acceptWarehouseCd?: string
  acceptLocations?: string[]
}

export interface QcJudgeResult {
  finalJudgement: string
  generatedReceiptNo?: string
}

// ───────── RMA 返品（WM150） ─────────

export interface RmaHeader {
  id?: string
  rmaNo?: string
  customerCd: string
  customerName?: string
  originalShippingNo?: string
  returnReason?: string
  appliedDate: string
  warehouseCd: string
  status: number  // 0/1/2/3/4/5/9
  operatorCd?: string
  remarks?: string
  details: RmaDetail[]
}

export interface RmaDetail {
  lineNo: number
  productCd: string
  productName?: string
  lotNo: string
  qty: number
  unitCd?: string
  conditionLevel: 'NEW' | 'OPEN' | 'DAMAGED'
  judgement?: 'RESELL' | 'REPAIR' | 'SCRAP' | 'SUPPLIER_RETURN'
  destLocationCd?: string
  inboundTxnNo?: string
  dispositionTxnNo?: string
  remarks?: string
}

export interface RmaSearchQuery {
  rmaNo?: string
  customerCd?: string
  originalShippingNo?: string
  status?: number
  appliedFrom?: string
  appliedTo?: string
  page?: number
  pageSize?: number
}

export interface RmaDispositionInput {
  lineNo: number
  judgement: 'RESELL' | 'REPAIR' | 'SCRAP' | 'SUPPLIER_RETURN'
  destLocationCd?: string
}

// ───────── ロット追溯（WM160） ─────────

export interface LotTraceNode {
  txnNo: string
  txnType: string
  txnAt: string
  warehouseCd: string
  locationCd: string
  qty: number
  relatedNo?: string
  relatedType?: string
  operatorCd?: string
  remark?: string
}

export interface LotAffectedCustomer {
  outboundNo: string
  webOrderNo?: string
  customerCd?: string
  customerName?: string
  qty: number
  shippedAt: string
}

export interface LotAffectedSupplier {
  inboundNo: string
  supplierCd?: string
  supplierName?: string
  qty: number
  receivedAt: string
}

export interface LotTraceResult {
  productCd: string
  lotNo: string
  direction: 'FORWARD' | 'BACKWARD'
  nodes: LotTraceNode[]
  affectedCustomers: LotAffectedCustomer[]
  affectedSuppliers: LotAffectedSupplier[]
}

export interface LotStockSummary {
  productCd: string
  lotNo: string
  totalPhysicalQty: number
  totalAvailableQty: number
  locationCount: number
  recallFlag: boolean
  expiryDate?: string
}

// ───────── 棚卸（WM090） ─────────

export interface StockTake {
  id?: string
  stockTakeNo: string
  stockTakeType: number // 1=全 2=サイクル 3=臨時
  plannedDate: string
  actualDate?: string
  completedDate?: string
  status: number // 0/1/2/3/4/9
  targetWarehouseCd: string
  targetLocationPrefix?: string
  targetProductCd?: string
  approvalThresholdAmount?: number
  approverCd?: string
  remarks?: string
  details: StockTakeDetail[]
}

export interface StockTakeDetail {
  lineNo: number
  stockId: string
  warehouseCd: string
  locationCd: string
  productCd: string
  lotNo: string
  bookQty: number
  countedQty?: number
  diffQty?: number
  diffAmount?: number
  unitPrice?: number
  diffReasonCd?: string
  diffReasonText?: string
  approvalStatus: number // 0/1/2/9
  adjustTxnNo?: string
  countedByCd?: string
  remarks?: string
}

export interface StockTakePlanRequest {
  stockTakeType: number
  plannedDate: string
  targetWarehouseCd: string
  targetLocationPrefix?: string
  targetProductCd?: string
  approvalThresholdAmount?: number
  remarks?: string
}

export interface StockTakeCountInput {
  lineNo: number
  countedQty: number
  countedByCd?: string
  diffReasonCd?: string
  diffReasonText?: string
  remarks?: string
}

export interface StockTakeSearchQuery {
  stockTakeNo?: string
  status?: number
  stockTakeType?: number
  targetWarehouseCd?: string
  plannedFrom?: string
  plannedTo?: string
  page?: number
  pageSize?: number
}

// ───────── ダッシュボード ─────────

export interface WmsKpi {
  totalStockValue: number
  activeSkuCount: number
  totalPhysicalQty: number
  totalAllocatedQty: number
  stagnantSkuCount: number
  todayInboundPlanCount: number
  todayShippingPlanCount: number
  openStockTakeCount: number
}

export interface WmsTrendPoint {
  date: string
  inQty: number
  outQty: number
  adjQty: number
}

export interface WmsWarehouseValue {
  warehouseCd: string
  warehouseName?: string
  stockValue: number
  skuCount: number
}

export interface WmsAlerts {
  expiryAlerts: Array<{
    productCd: string
    lotNo: string
    warehouseCd: string
    locationCd: string
    expiryDate?: string
    daysUntilExpiry: number
    physicalQty: number
  }>
  delayedInbounds: Array<{
    inboundNo: string
    supplierName?: string
    expectedArrivalDate: string
    delayDays: number
    status: number
  }>
  pendingApprovalStockTakeCount: number
}

// ───────── キッティング（WM140） ─────────

export interface KitMaster {
  id?: string
  kitSku: string
  kitName: string
  defaultWarehouseCd?: string
  remarks?: string
  activeFlg: boolean
  components: KitMasterComponent[]
}

export interface KitMasterComponent {
  lineNo: number
  componentProductCd: string
  componentName?: string
  requiredQty: number
  unitCd?: string
  remarks?: string
}

export interface KitOrder {
  id?: string
  kitOrderNo?: string
  kitSku: string
  kitName?: string
  qty: number
  direction: 'ASSEMBLE' | 'DISASSEMBLE'
  warehouseCd: string
  kitLocationCd: string
  kitLotNo?: string
  status: number
  operatorCd?: string
  remarks?: string
  executedTxnNos?: string
  executedAt?: string
}

export interface KitOrderSearchQuery {
  kitOrderNo?: string
  kitSku?: string
  direction?: string
  status?: number
  page?: number
  pageSize?: number
}
