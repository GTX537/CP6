// MSBBPA070 / 080 / 090 Web 受注 类型定义
// 对应后端 CP6.Entity/DTOs/OrderDto.cs

import type { ApiResult } from '@/types/estimateCalc'

/** PA070 操作种别 */
export enum OrderOperationType {
  New = 10,
  Edit = 20,
  Delete = 30,
  View = 40,
}

/** PA070 受注 1 个完整 DTO（ヘッダー + 多明細 + 多工程 + 多材料） */
export interface OrderDto {
  webOrderNo?: string

  // ───── ヘッダー ─────
  customerCd: string
  orderType: string
  orderDepartment?: string
  orderDate?: string
  customerDeliveryDate?: string
  quantity?: number
  orderSheetNo?: string
  customerContact?: string
  addressee?: string
  carrier?: string
  shipDateTime?: string
  shipCondition?: string
  salesPriceDiv?: string
  memo1?: string
  memo2?: string
  memo3?: string

  details: OrderDetailDto[]

  status: number
  mcTransferFlg: boolean
  mcOrderNo?: string
  rowVersion?: string
}

/** 受注明細 1 行（製品単位）— 約 130 項目 */
export interface OrderDetailDto {
  webOrderNo?: string
  webOrderDetailNo: number

  haibaiNo1?: string
  haibaiNo2?: string
  haibaiNo3?: string
  haibaiNo4?: string

  productCd: string
  itemCd?: string
  branch1?: string
  branch2?: string
  branch3?: string

  productCatBig?: string
  productCatMid?: string
  productCatSml?: string

  customerItemName1?: string
  customerItemName2?: string
  customerPartNo?: string
  cpItemName1?: string
  cpItemName2?: string
  janCode?: string

  qtyUnit?: string
  quantity?: number
  specialPriceFlg?: string
  unitPriceUnit?: string
  setUnitPrice?: number
  individualUnitPrice?: number
  amount?: number

  deliveryCd?: string
  deliveryName?: string
  customerDeliveryDate?: string
  logisticsGroup?: string
  haibaiKbn?: string

  consignedSalesFlg?: string
  salesReason?: string
  consignedSalesQty?: number

  fscOrderType?: string
  fscProductDiv?: string
  fscMaterialDiv?: string
  fscManagementNo?: string
  foodSafety?: string

  shipInspection?: string
  fixedShipment?: string
  deliveryReserve?: number
  salesSample?: number
  salesAvailable?: string

  // 構成情報（13 フィールド）
  sheetFlute?: string
  paperCdF?: string
  paperCdC?: string
  paperCdB?: string
  printCdF?: string
  printCdC?: string
  printCdB?: string
  embossCdF?: string
  embossCdC?: string
  embossCdB?: string
  makerCdF?: string
  makerCdC?: string
  makerCdB?: string

  // 寸法
  sheetPrint?: string
  bladeWidth?: number
  bladeFlow?: number
  gutterFb?: number
  gutterLr?: number
  sheetDimW?: number
  sheetDimF?: number
  salesWidth?: number
  finalMachineProcess?: string

  // 備考
  printNote?: string
  mfgNote?: string
  remfgNote?: string
  slipNote?: string
  deliveryNote?: string
  shipNote1?: string
  shipNote2?: string

  // 仕入情報
  defectiveHaibaiNo?: string
  purchaseVendor?: string
  rollMeter?: number
  purchaseUnitPrice?: number
  purchaseUnit?: string

  // 案件・見積
  projectNoParent?: string
  projectNoChild?: string
  projectNoMaterial?: string
  quotationNo?: string
  estimateCalcNo?: string
  refEstimateCalcNo?: string

  // セット品
  setProductCd?: string
  setProductName?: string
  parentChildDiv?: string
  setRatio?: number
  orderType?: string

  // 製品属性スナップショット
  productUsage?: string
  distributionDiv?: string
  confidentialInfo?: string
  seizureDiv?: string
  importanceDiv?: string
  mChange?: string
  qualityDiv?: string
  productShape?: string
  unescoMark?: string
  origamiMark?: string
  fourMContract?: string
  tkpWrinkleStd?: string
  recyclingPayment?: string

  paperUsageG?: number
  plasticUsageG?: number
  glassUsageG?: number
  petUsageG?: number
  packPaperUsageG?: number
  packPlasticUsageG?: number

  designProposalNo?: string
  salesPriceDiv?: string
  freightBilling?: string

  mcOrderNo?: string
  mcOrderDetailNo?: string
  status: number
  wfApprovalFlg: boolean
  mcTransferFlg: boolean

  provisionalPriceFlg: boolean
  priceChangeReason?: string
  approvalStatus?: number

  processes: OrderProcessDto[]
  processNotes: OrderProcessNoteDto[]
  materials: OrderMaterialDto[]
}

/** 受注加工工程 1 行 */
export interface OrderProcessDto {
  productCd: string
  operationCd: string
  processCd: string

  topItemCd?: string
  topBranch1?: string
  topBranch2?: string
  topBranch3?: string
  itemCd?: string
  branch1?: string
  branch2?: string
  branch3?: string

  workingGroupCd?: string
  machineOrVendor?: string
  machineFixedFlg: boolean
  cpDeliveryDiv?: string

  spec01?: string
  spec02?: string
  spec03?: string
  spec04?: string
  spec05?: string
  spec06?: string
  spec07?: string
  spec08?: string
  spec09?: string
  spec10?: string

  qtyUnit?: string
  plateNo1?: string
  plateNo2?: string
  plateNo3?: string
  consumable1?: string
  consumable2?: string
  consumable3?: string

  purchaseUnitPrice?: number
  fixedPrice?: number
  lossRate: number
  machineCount: number
  leadTimeDays: number
  storageLocation?: string

  sortOrder: number

  priorityItem1?: string
  priorityItem2?: string
  priorityItem3?: string
  priorityItem4?: string
  priorityItem5?: string
  priorityItem6?: string
  priorityItem7?: string
  priorityItem8?: string

  scheduledDate?: string
}

export interface OrderProcessNoteDto {
  productCd: string
  operationCd: string
  note1?: string
  note2?: string
}

export interface OrderMaterialDto {
  productCd: string
  processCd: string
  materialCd: string
  materialTypeDiv: string
  itemCd?: string
  branch1?: string
  branch2?: string
  branch3?: string
  supplyDiv: string
  supplyUnitPrice: number
  sortOrder: number
}

// ─────────────────── PA080 一覧 ───────────────────

export interface OrderQueryDto {
  baseCd?: string
  salesPersonCd?: string
  customerCd?: string
  customerCdTo?: string
  orderType?: string
  productCatBig?: string
  productCatMid?: string
  productCatSml?: string
  orderDateFrom?: string
  orderDateTo?: string
  deliveryDateFrom?: string
  deliveryDateTo?: string
  haibaiNo1From?: string
  haibaiNo1To?: string
  haibaiNo2From?: string
  haibaiNo2To?: string
  haibaiNo3From?: string
  haibaiNo3To?: string
  orderSheetNo?: string
  defectiveHaibaiNo?: string
  mcOrderNo?: string
  productCd?: string
  itemCd?: string
  customerItemName?: string
  customerPartNo?: string
  sheetFlute?: string
  paperCd?: string
  printCd?: string
  embossCd?: string
  makerCd?: string
  fscOrderType?: string
  salesPriceDiv?: string
  productShape?: string
  distributionDiv?: string
  carrier?: string
  onlyConsignedSales?: boolean
  onlyMcUntransferred?: boolean
  includeDeleted?: boolean
  page?: number
  pageSize?: number
  maxRows?: number
  sortField?: string
  sortOrder?: string
}

export interface OrderListItemDto {
  rowNo: number
  customerCd?: string
  salesPersonCd?: string
  customerName?: string
  salesPersonName?: string
  orderSheetNo?: string
  haibaiNo1?: string
  defectiveHaibaiNo?: string
  mcOrderNo?: string
  orderDate?: string
  customerDeliveryDate?: string
  productCd?: string
  itemCd?: string
  productCatBig?: string
  productCatBigName?: string
  cpItemOrComposition?: string
  sheetFlute?: string
  compositionF?: string
  compositionC?: string
  compositionB?: string
  compositionItemName?: string
  fullSheetWidth?: number
  fullSheetFlow?: number
  slitterWidth?: number
  slitterFlow?: number
  qtyUnit?: string
  quantity?: number
  individualUnitPrice?: number
  setUnitPrice?: number
  unitPriceUnit?: string
  amount?: number
  consignedSalesFlg?: string
  slipNote?: string
  deliveryNote?: string
  webOrderNo: string
  webOrderDetailNo: number
}

// ─────────────────── PA090 単価訂正 ───────────────────

export interface OrderPriceCorrectionQueryDto {
  baseCd?: string
  salesPersonCd?: string
  customerCd?: string
  customerCdTo?: string
  orderDateFrom?: string
  orderDateTo?: string
  deliveryDateFrom?: string
  deliveryDateTo?: string
  haibaiNo1?: string
  orderSheetNo?: string
  productCd?: string
  customerItemName?: string
  qtyFrom?: number
  qtyTo?: number
  amountFrom?: number
  amountTo?: number
  onlyProvisional?: boolean
  approvalStatus?: string
  page?: number
  pageSize?: number
  maxRows?: number
}

export interface OrderPriceCorrectionItemDto {
  rowNo: number
  approvalStatus?: number
  customerCd?: string
  customerName?: string
  salesPersonCd?: string
  salesPersonName?: string
  orderSheetNo?: string
  haibaiNo1?: string
  defectiveHaibaiNo?: string
  productCd?: string
  itemCd?: string
  cpItemName1?: string
  cpItemName2?: string
  orderDate?: string
  customerDeliveryDate?: string
  unit?: string
  cpItemOrComposition?: string
  compositionF?: string
  compositionC?: string
  compositionB?: string
  sheetFlute?: string
  fullSheetWidth?: number
  fullSheetFlow?: number
  slitterWidth?: number
  slitterFlow?: number
  quantity?: number
  qtyUnit?: string
  individualUnitPriceBefore?: number
  setUnitPriceBefore?: number
  unitPriceUnit?: string
  individualUnitPriceAfter?: number
  setUnitPriceAfter?: number
  specialPriceFlg?: string
  provisionalPriceFlg: boolean
  priceChangeReason?: string
  amount?: number
  consignedSalesFlg?: string
  slipNote?: string
  deliveryNote?: string
  webOrderNo: string
  webOrderDetailNo: number
  selected?: boolean
  rowVersion?: string
}

export interface OrderPriceCorrectionUpdateDto {
  webOrderNo: string
  webOrderDetailNo: number
  individualUnitPriceAfter?: number
  setUnitPriceAfter?: number
  specialPriceFlg?: string
  priceChangeReason?: string
  rowVersion?: string
}

export interface OrderPriceCorrectionBatchUpdateDto {
  items: OrderPriceCorrectionUpdateDto[]
}

export interface OrderPriceCorrectionBatchResultDto {
  updatedCount: number
  wfRequestedCount: number
  conflictedKeys: string[]
}

// ─────────────────── 業務ルール結果 ───────────────────

export interface IsEditableResultDto {
  isEditable: boolean
  reason?: string
}

export interface OrderWipCheckResultDto {
  level: number
  message?: string
}

export interface CreditCheckResultDto {
  isOver: boolean
  creditLimit: number
  balance: number
  message?: string
}

export interface LeadTimeRequestDto {
  shipDateTime?: string
  leadTimeDays?: number[]
  wgCd?: string
}

// ─────── Phase 6 受注取消 ───────

export type CancelOutcome = 'Cancelled' | 'NeedsDecision' | 'Rejected' | 'PartiallyCancelled'

export interface WorkOrderProbe {
  workOrderNo: string
  status: number
  autoCancellable: boolean
  cancelled?: boolean | null
  message?: string | null
}

export interface OutboundProbe {
  outboundNo: string
  status: number
  outboundType: number
  autoCancellable: boolean
  cancelled?: boolean | null
  message?: string | null
}

export interface OrderCancelResult {
  outcome: CancelOutcome
  message?: string | null
  relatedWorkOrders: WorkOrderProbe[]
  relatedOutbounds: OutboundProbe[]
  correlationId: string
}

// ─────── Phase 8 受注済未出荷 Dashboard ───────

export interface UnshippedOrderItemDto {
  webOrderNo: string
  customerCd: string
  customerName?: string | null
  orderDate?: string | null
  customerDeliveryDate?: string | null
  orderStatus: string  // CONFIRMED / IN_PRODUCTION / PARTIALLY_CANCELLED
  shipStatus: number   // 0 / 5 / 9
  orderedQty: number
  shippedQty: number
  remainingQty: number
  isOverdue: boolean
  daysUntilDue?: number | null
  mesStatusSummary?: string | null
  wmsStatusSummary?: string | null
}

export interface UnshippedOrderQuery {
  customerCd?: string
  onlyOverdue?: boolean
  page: number
  pageSize: number
  sortField?: string  // "deliveryDate" / "orderDate" / "remainingQty"
  sortOrder?: 'asc' | 'desc'
}

export interface PagedResult<T> {
  rows: T[]
  total: number
}

export type { ApiResult }
