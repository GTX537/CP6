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

export interface BridgeHealthMetrics {
  windowStartUtc: string
  windowEndUtc: string
  hooks: BridgeHookStats[]
  queueDepth: number
  deadLetterCount: number
  deadLetters: DeadLetterItem[]
}

export interface BridgeHookStats {
  hookName: string
  sourceModule: string
  targetModule: string
  totalCount: number
  successCount: number
  skippedCount: number
  failedCount: number
  deadLetterCount: number
  successRate: number
}

export interface DeadLetterItem {
  eventId: string
  hookName: string
  sourceModule: string
  targetModule: string
  sourceNo: string
  targetNo?: string
  attempts: number
  lastError?: string
  createDate: string
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
  /** Phase 7 Gap 1.3 QC ステータス: PENDING / PASSED / FAILED / HOLD */
  qcStatus?: string
}

export type StockQcStatusKey = 'PENDING' | 'PASSED' | 'FAILED' | 'HOLD'

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

// ───────── クロスドック（WM130） ─────────

export interface CrossDockOrder {
  id?: string
  xdockNo?: string
  inboundNo?: string
  outboundNo?: string
  productCd: string
  productName?: string
  qty: number
  supplierCd?: string
  customerCd?: string
  fromDock?: string
  toDock?: string
  warehouseCd: string
  tempLocationCd: string
  lotNo: string
  status: number  // 0/1/9
  inTxnNo?: string
  outTxnNo?: string
  executedAt?: string
  operatorCd?: string
  remarks?: string
}

export interface CrossDockSearchQuery {
  xdockNo?: string
  productCd?: string
  inboundNo?: string
  outboundNo?: string
  status?: number
  page?: number
  pageSize?: number
}

// ───────── 補充指示（WM120） ─────────

export interface ReplenishOrder {
  id?: string
  replenishNo?: string
  priority: number  // 1=至急 2=通常
  productCd: string
  productName?: string
  warehouseCd: string
  fromLocationCd: string
  toLocationCd: string
  lotNo: string
  qty: number
  unitCd?: string
  triggerType: 'BATCH' | 'MANUAL' | 'ALERT'
  status: number  // 0/1/9
  outTxnNo?: string
  inTxnNo?: string
  executedAt?: string
  operatorCd?: string
  remarks?: string
}

export interface ReplenishSearchQuery {
  replenishNo?: string
  productCd?: string
  warehouseCd?: string
  status?: number
  priority?: number
  page?: number
  pageSize?: number
}

// ───────── スロッティング（WM110） ─────────

export interface SlottingPlan {
  id?: string
  slottingPlanNo: string
  warehouseCd: string
  analysisDays: number
  txnSampleCount: number
  analyzedAt?: string
  status: number  // 0/1/2/9
  recommendationCount: number
  approverCd?: string
  recommendationsJson?: string
  remarks?: string
}

export interface SlottingRecommendation {
  productCd: string
  outCount: number
  outQty: number
  abcRank: 'A' | 'B' | 'C'
  currentLocationCd?: string
  recommendedLocationPattern?: string
  needsRelocation: boolean
}

export interface SlottingPlanResult {
  plan: SlottingPlan
  recommendations: SlottingRecommendation[]
}

// ───────── 原紙ロール（WM200） ─────────

export interface PaperRoll {
  id?: string
  rollNo: string
  paperGrade: string
  widthMm: number
  basisWeight: number
  grainDirection: 'T' | 'Y'
  originalLengthM: number
  remainingLengthM: number
  coreDiameterInch: number
  mfgDate?: string
  mfgLotNo?: string
  supplierRollNo?: string
  locationCd: string
  warehouseCd: string
  status: number  // 0/1/2/3
  parentRollNo?: string
  disposeThresholdM?: number
  remarks?: string
}

export interface PaperRollSearchQuery {
  rollNo?: string
  paperGrade?: string
  widthMm?: number
  grainDirection?: string
  status?: number
  warehouseCd?: string
  page?: number
  pageSize?: number
}

export interface SlitRequest {
  parentRollNo: string
  childWidths: number[]
  keepRemnant: boolean
}

// ───────── インキ（WM230） ─────────

export interface InkLot {
  id?: string
  inkLotNo: string
  colorCode: string
  inkType: string
  openStatus: 'UNOPENED' | 'OPENED'
  expiryDate: string
  quantity: number
  unitCd: string
  viscosityCp?: number
  solidContent?: number
  parentLotNoA?: string
  parentLotNoB?: string
  locationCd?: string
  supplierCd?: string
  remarks?: string
}

export interface InkLotSearchQuery {
  inkLotNo?: string
  colorCode?: string
  inkType?: string
  openStatus?: string
  expiringWithin30Days?: boolean
  page?: number
  pageSize?: number
}

export interface InkColorMatchHistory {
  id?: string
  matchNo: string
  customerCd: string
  customerName?: string
  colorCode: string
  formulaJson?: string
  consumedQty: number
  matchedAt: string
  operatorCd?: string
  remarks?: string
}

export interface MixInkRequest {
  parentLotNoA: string
  parentQtyA: number
  parentLotNoB: string
  parentQtyB: number
  newColorCode?: string
  newLocationCd?: string
  remarks?: string
}

// ───────── パレット（WM240） ─────────

export interface Pallet {
  id?: string
  palletNo: string
  productCd: string
  productName?: string
  lotNo: string
  cartonQty: number
  weightKg?: number
  heightMm?: number
  maxStackLayers?: number
  warehouseCd: string
  locationCd: string
  status: number  // 0/1/2/3
  shippedOutboundNo?: string
  remarks?: string
}

export interface PalletSearchQuery {
  palletNo?: string
  productCd?: string
  lotNo?: string
  warehouseCd?: string
  status?: number
  page?: number
  pageSize?: number
}

// ───────── VMI（WM250） ─────────

export interface VmiCustomerSummary {
  customerCd: string
  customerName?: string
  skuCount: number
  totalPhysicalQty: number
  totalAllocatedQty: number
  totalAvailableQty: number
  estimatedValue?: number
}

export interface VmiStockDetail {
  productCd: string
  lotNo: string
  warehouseCd: string
  locationCd: string
  physicalQty: number
  availableQty: number
  receiveDate?: string
  expiryDate?: string
}

export interface VmiBilling {
  id?: string
  billingNo: string
  customerCd: string
  customerName?: string
  yearMonth: string
  skuCount: number
  beginQty: number
  endQty: number
  avgQty: number
  dailyStorageRate: number
  billingAmount: number
  calculatedAt: string
  confirmed: boolean
  remarks?: string
}

// ───────── 残材（WM210） ─────────

export interface RemnantMaterial {
  id?: string
  remnantNo: string
  materialType: string  // PAPER / FILM / OTHER
  materialGrade?: string
  widthMm: number
  lengthMm: number
  thicknessUm?: number
  quantity: number
  unitCd: string
  sourceWorkOrderNo?: string
  sourceRollNo?: string
  warehouseCd: string
  locationCd: string
  status: number  // 0/1/2/3
  reservedFor?: string
  registeredAt: string
  remarks?: string
}

export interface RemnantSearchQuery {
  remnantNo?: string
  materialType?: string
  materialGrade?: string
  status?: number
  warehouseCd?: string
  sourceWorkOrderNo?: string
  sourceRollNo?: string
  page?: number
  pageSize?: number
}

// ───────── 印版・木型（WM220） ─────────

export interface PlateMoldStock {
  id?: string
  plateNo: string
  plateType: string  // PLATE / MOLD / CYL / OTHER
  customerCd?: string
  customerName?: string
  productCd?: string
  productName?: string
  colorCount?: number
  sizeNote?: string
  madeDate?: string
  madeCost?: number
  maxShots?: number
  usedShots: number
  lastUsedAt?: string
  nextMaintenanceDate?: string
  warehouseCd?: string
  locationCd?: string
  status: number  // 0/1/2/3
  remarks?: string
}

export interface PlateMoldSearchQuery {
  plateNo?: string
  plateType?: string
  customerCd?: string
  productCd?: string
  status?: number
  page?: number
  pageSize?: number
}

// ───────── サンプル（WM260） ─────────

export interface SampleStock {
  id?: string
  sampleNo: string
  sampleType: string  // PROTO / COLOR / DUMMY / OTHER
  customerCd?: string
  customerName?: string
  productCd?: string
  productName?: string
  quantity: number
  unitCd: string
  warehouseCd?: string
  locationCd?: string
  status: number  // 0/1/2/3
  lentTo?: string
  lentAt?: string
  expectedReturnDate?: string
  returnedAt?: string
  registeredAt: string
  remarks?: string
}

export interface SampleSearchQuery {
  sampleNo?: string
  sampleType?: string
  customerCd?: string
  productCd?: string
  status?: number
  overdueOnly?: boolean
  page?: number
  pageSize?: number
}

// ───────── 帳票センター（WM900） ─────────

export interface MonthlyStockReportRow {
  yearMonth: string
  warehouseCd: string
  productCd: string
  physicalQty: number
  allocatedQty: number
  availableQty: number
  estimatedValue?: number
  lotCount: number
}

export interface AbcAnalysisRow {
  productCd: string
  outCount: number
  outQty: number
  cumulativeRatio?: number
  abcRank: string  // A / B / C
}

export interface DeadStockRow {
  warehouseCd: string
  locationCd: string
  productCd: string
  lotNo: string
  physicalQty: number
  lastMovedAt?: string
  idleDays: number
  estimatedValue?: number
}

export interface InboundHistoryRow {
  txnNo: string
  txnDateTime: string
  warehouseCd: string
  locationCd: string
  productCd: string
  lotNo: string
  qty: number
  relatedNo?: string
  relatedType?: string
  operatorCd?: string
}

export interface OutboundHistoryRow {
  txnNo: string
  txnDateTime: string
  warehouseCd: string
  locationCd: string
  productCd: string
  lotNo: string
  qty: number
  relatedNo?: string
  relatedType?: string
  operatorCd?: string
}

// ───────── WCS タスク（WM310） ─────────

export interface WcsTask {
  id?: string
  taskNo: string
  taskType: string  // MOVE/PICK/PUT/COUNT
  priority: number
  status: number    // 0/1/2/3/9
  deviceCd?: string
  relatedNo?: string
  relatedType?: string
  fromWarehouseCd?: string
  fromLocationCd?: string
  toWarehouseCd?: string
  toLocationCd?: string
  productCd?: string
  lotNo?: string
  qty?: number
  unitCd?: string
  createdAt: string
  dispatchedAt?: string
  startedAt?: string
  completedAt?: string
  errorMessage?: string
  remarks?: string
}

export interface WcsTaskSearchQuery {
  taskNo?: string
  taskType?: string
  deviceCd?: string
  status?: number
  relatedNo?: string
  page?: number
  pageSize?: number
}

// ───────── Carrier 配送業者（WM320） ─────────

export interface CarrierShipment {
  id?: string
  shipmentNo: string
  packageNo: string
  carrierCd: string  // YAMATO/SAGAWA/JP/SELF/OTHER
  trackingNo: string
  serviceType?: string
  status: number     // 0/1/2/3/9
  customerCd?: string
  shipToAddress?: string
  shipToName?: string
  shipToTel?: string
  weightKg?: number
  carrierFee?: number
  pickedUpAt?: string
  deliveredAt?: string
  eventsJson?: string
  apiRefId?: string
  remarks?: string
}

export interface CarrierSearchQuery {
  shipmentNo?: string
  packageNo?: string
  trackingNo?: string
  carrierCd?: string
  customerCd?: string
  status?: number
  page?: number
  pageSize?: number
}

export interface CarrierEvent {
  ts: string
  location?: string
  status?: string
  message?: string
}

// ───────── IoT センサ（WM330） ─────────

export interface IotSensor {
  id?: string
  sensorId: string
  sensorType: string  // TEMP/HUMID/SHOCK/SHELF
  sensorName?: string
  warehouseCd: string
  locationCd?: string
  unit?: string
  minThreshold?: number
  maxThreshold?: number
  isEnabled: boolean
  lastValue?: number
  lastReadAt?: string
  remarks?: string
}

export interface IotSensorReading {
  id?: string
  sensorId: string
  readAt: string
  value: number
  isAlert: boolean
  alertMessage?: string
}

export interface IotAlert {
  sensorId: string
  sensorName?: string
  sensorType: string
  warehouseCd: string
  locationCd?: string
  lastValue: number
  lastReadAt: string
  alertMessage?: string
}

// ───────── モバイル作業指示（WM300） ─────────

export interface MobileTask {
  id?: string
  mobileTaskNo: string
  taskType: string            // RECEIVE/PUTAWAY/PICK/COUNT/MOVE/LABEL
  assignedTo?: string
  priority: number            // 1=至急 2=通常
  status: number              // 0=未着手 1=進行中 2=完了 9=取消
  relatedNo?: string
  relatedType?: string
  productCd?: string
  productName?: string
  lotNo?: string
  warehouseCd?: string
  fromLocationCd?: string
  toLocationCd?: string
  qty: number
  scannedQty: number
  unitCd?: string
  instruction?: string
  startedAt?: string
  doneAt?: string
  remarks?: string
}

export interface MobileTaskQuery {
  assignedTo?: string
  taskType?: string
  status?: number
  openOnly?: boolean
  page?: number
  pageSize?: number
}

export interface MobileStockLine {
  warehouseCd: string
  locationCd: string
  productCd: string
  lotNo: string
  physicalQty: number
  availableQty: number
  unitCd?: string
  expiryDate?: string
}

export interface MobileScanResult {
  kind: string                // LOCATION/PRODUCT/UNKNOWN
  barcode: string
  locationCd?: string
  locationName?: string
  isBlocked?: boolean
  productCd?: string
  stocks: MobileStockLine[]
  matched?: boolean
  message?: string
}

export interface MobileScanRequest {
  barcode: string
  taskNo?: string
  warehouseCd?: string
}

export interface MobileCompleteRequest {
  scannedQty?: number
  toLocationCd?: string
  remarks?: string
}
