// 采购（Pur）模块前端类型 —— 对应后端 CP6.Entity.DomainModels.Pur / Services.Pur

export interface ApiResp<T> {
  code: number
  message: string
  data: T
}

/** 供应商×物料 阶梯价（采购 章01 §3/§4） */
export interface SupplierPrice {
  id?: string
  supplierId: string
  itemId: string
  price: number
  currencyCd?: string | null
  minQty: number
  validFrom: string
  validTo?: string | null
  source?: string | null
}

/** 采购订单行（含★三累计锚 ReceivedQty/AcceptedQty/InvoicedQty） */
export interface PurchaseOrderLine {
  id?: string
  poNo?: string
  lineNo?: number
  itemId: string
  qty: number
  unitPrice: number
  taxCodeId?: string | null
  taxRate?: number
  netAmount?: number
  taxAmount?: number
  requiredDate?: string | null
  receivedQty?: number
  acceptedQty?: number
  invoicedQty?: number
  matchStatus?: number
  status?: number
}

/** 采购订单头（发注书） */
export interface PurchaseOrder {
  id?: string
  poNo?: string
  supplierId: string
  supplierName?: string | null
  type: number
  currencyCd?: string | null
  fxRate?: number
  postingBasis?: string
  status?: number
  orderDate?: string
  netAmount?: number
  taxAmount?: number
  grossAmount?: number
  sourceRfqNo?: string | null
  approvalRef?: string | null
  remarks?: string | null
  lines: PurchaseOrderLine[]
}

/** 建 PO 明细行入参 */
export interface PoLineCreateForm {
  itemId: string
  qty: number
  unitPrice: number | null
  taxCodeId: string | null
  requiredDate: string | null
}
/** 建 PO 入参 */
export interface PoCreateForm {
  supplierId: string
  type: number
  orderDate: string | null
  remarks: string | null
  lines: PoLineCreateForm[]
}

/** 收货单行 */
export interface GoodsReceiptLine {
  id?: string
  grNo?: string
  lineNo?: number
  poLineNo: number
  itemId: string
  receivedQty: number
  acceptedQty?: number
  rejectedQty?: number
  qcStatus?: string
  wmsReceiptDetailRef?: string | null
}
/** 收货单头（GR） */
export interface GoodsReceipt {
  id?: string
  grNo?: string
  poNo: string
  supplierId?: string
  receiptDate?: string
  status?: number
  wmsInboundNo?: string | null
  postingBasis?: string
  warehouseCd?: string | null
  remarks?: string | null
  lines: GoodsReceiptLine[]
}
/** 确认收货明细行入参 */
export interface GrLineCreateForm {
  poLineNo: number
  receivedQty: number
}
/** 确认收货入参 */
export interface GrCreateForm {
  poNo: string
  receiptDate: string | null
  warehouseCd: string | null
  remarks: string | null
  lines: GrLineCreateForm[]
}

/** 三单匹配明细 */
export interface ThreeWayMatchLine {
  id?: string
  matchNo?: string
  lineNo?: number
  poLineNo: number
  itemId: string
  qty: number
  unitPrice: number
  taxCodeId?: string | null
  priceVarPct?: number
  remainAccepted?: number
  withinTolerance?: boolean
}
/** 三单匹配单（★MVP 核心） */
export interface ThreeWayMatch {
  id?: string
  matchNo?: string
  poNo: string
  supplierInvoiceNo: string
  matchDate?: string
  status?: number
  maxQtyVarPct?: number
  maxPriceVarPct?: number
  apInvoiceNo?: string | null
  apInvoiceId?: string | null
  note?: string | null
  handledBy?: string | null
  lines: ThreeWayMatchLine[]
}
/** 匹配发票明细行入参 */
export interface MatchInvoiceLineForm {
  poLineNo: number
  qty: number
  unitPrice: number
  taxCodeId: string | null
}
/** 匹配发票入参 */
export interface MatchInvoiceForm {
  poNo: string
  supplierInvoiceNo: string
  invoiceDate: string | null
  lines: MatchInvoiceLineForm[]
}
/** 匹配结果 */
export interface MatchResult {
  match: ThreeWayMatch
  apCreated: boolean
  apInvoiceNo?: string | null
}

/** 采购申请行（章05；SuggestSupplierId 有→PR转PO，空→走询价RFQ；ConvertedPoNo 回填） */
export interface PurchaseRequestLine {
  id?: string
  prNo?: string
  lineNo?: number
  itemId: string
  qty: number
  unitCd?: string | null
  requiredDate?: string | null
  estPrice?: number | null
  suggestSupplierId?: string | null
  convertedPoNo?: string | null
  status?: number
}
/** 采购申请头（需求入口：手工/缺料反流/工单驱动） */
export interface PurchaseRequest {
  id?: string
  prNo?: string
  requesterId?: string | null
  deptId?: string | null
  requestDate?: string
  status?: number
  source?: string
  sourceRefNo?: string | null
  approvalRef?: string | null
  remarks?: string | null
  lines: PurchaseRequestLine[]
}
/** 建 PR 明细行入参 */
export interface PrLineCreateForm {
  itemId: string
  qty: number
  unitCd: string | null
  requiredDate: string | null
  estPrice: number | null
  suggestSupplierId: string | null
}
/** 建 PR 入参 */
export interface PrCreateForm {
  requestDate: string | null
  remarks: string | null
  lines: PrLineCreateForm[]
}

/** 询价行（章06 §2；行级追溯回 PR） */
export interface RfqLine {
  id?: string
  rfqNo?: string
  lineNo?: number
  itemId: string
  qty: number
  unitCd?: string | null
  requiredDate?: string | null
  sourcePrNo?: string | null
  sourcePrLineNo?: number | null
}
/** 被邀供应商 */
export interface RfqSupplier {
  id?: string
  rfqNo?: string
  supplierId: string
  supplierName?: string | null
  inviteStatus?: number
}
/** 报价（供应商×行 矩阵；含比价 Rank / 选中标记） */
export interface RfqQuote {
  id?: string
  rfqNo?: string
  supplierId: string
  lineNo: number
  quotedPrice: number
  currencyCd?: string | null
  leadDays?: number | null
  validUntil?: string | null
  isSelected?: boolean
  rank?: number
}
/** 询价单头（一头三身：行 + 被邀供应商 + 报价矩阵） */
export interface Rfq {
  id?: string
  rfqNo?: string
  rfqDate?: string
  dueDate?: string | null
  status?: number
  buyer?: string | null
  sourcePrNo?: string | null
  remarks?: string | null
  lines: RfqLine[]
  suppliers: RfqSupplier[]
  quotes: RfqQuote[]
}
/** 报价录入行入参（一行对应一个 RfqLine.LineNo） */
export interface RfqQuoteLineForm {
  lineNo: number
  quotedPrice: number
  currencyCd?: string | null
  leadDays?: number | null
  validUntil?: string | null
}
/** 选定入参（给某询价行选定某供应商的报价） */
export interface RfqSelectionForm {
  lineNo: number
  supplierId: string
}

// ── 枚举标签 / 颜色 ──
export const PO_TYPE_LABEL: Record<number, string> = { 1: '标准采购', 2: '外注委托' }
export const PO_TYPE_OPTIONS = [
  { value: 1, label: '标准采购' },
  { value: 2, label: '外注委托' },
]
export const PO_STATUS_LABEL: Record<number, string> =
  { 0: '草稿', 1: '送审中', 2: '已确认', 3: '部分收货', 4: '收货完毕', 5: '部分开票', 6: '关闭', 9: '已取消' }
export const PO_STATUS_TAG: Record<number, '' | 'info' | 'success' | 'warning' | 'danger'> =
  { 0: 'info', 1: 'warning', 2: '', 3: 'warning', 4: '', 5: 'warning', 6: 'success', 9: 'danger' }

export const GR_STATUS_LABEL: Record<number, string> =
  { 0: '草稿', 1: '已收货', 2: '检验中', 3: '已完成', 9: '已取消' }
export const GR_STATUS_TAG: Record<number, '' | 'info' | 'success' | 'warning' | 'danger'> =
  { 0: 'info', 1: '', 2: 'warning', 3: 'success', 9: 'danger' }

export const MATCH_STATUS_LABEL: Record<number, string> =
  { 0: '通过', 1: '差异挂起', 2: '人工放行', 3: '拒绝' }
export const MATCH_STATUS_TAG: Record<number, '' | 'info' | 'success' | 'warning' | 'danger'> =
  { 0: 'success', 1: 'warning', 2: 'success', 3: 'danger' }

export const QC_STATUS_LABEL: Record<string, string> =
  { NONE: '免检', PENDING: '待检', PASS: '合格', FAIL: '不良' }
export const POSTING_BASIS_LABEL: Record<string, string> = { '1': '着荷基准', '2': '检收基准' }

export const RFQ_STATUS_LABEL: Record<number, string> =
  { 0: '草稿', 1: '邀请中', 2: '报价中', 3: '已选定', 4: '已转PO', 8: '关闭', 9: '已取消' }
export const RFQ_STATUS_TAG: Record<number, '' | 'info' | 'success' | 'warning' | 'danger'> =
  { 0: 'info', 1: 'warning', 2: 'warning', 3: '', 4: 'success', 8: 'info', 9: 'danger' }
export const RFQ_INVITE_LABEL: Record<number, string> =
  { 0: '待邀请', 1: '已邀请', 2: '已报价' }

export const PR_STATUS_LABEL: Record<number, string> =
  { 0: '草稿', 1: '已提交', 2: '已批准', 3: '已拒绝', 4: '已转PO', 9: '关闭' }
export const PR_STATUS_TAG: Record<number, '' | 'info' | 'success' | 'warning' | 'danger'> =
  { 0: 'info', 1: 'warning', 2: 'success', 3: 'danger', 4: '', 9: 'info' }
export const PR_SOURCE_LABEL: Record<string, string> =
  { manual: '手工录入', shortage: '缺料反流', workorder: '工单驱动' }
export const PR_SOURCE_OPTIONS = [
  { value: 'manual', label: '手工录入' },
  { value: 'shortage', label: '缺料反流' },
  { value: 'workorder', label: '工单驱动' },
]

// ───── 外注加工（章07）─────

/** 有償支給材（外注 PO 子表） */
export interface PoConsignMaterial {
  id?: string
  poNo?: string
  lineNo?: number
  consignItemId: string
  consignQty: number
  consignUnitCost: number
  issuedQty?: number
  wmsIssueNo?: string | null
}

/** 登记支給材入参 */
export interface ConsignMaterialForm {
  consignItemId: string
  consignQty: number
  consignUnitCost: number
}

/** 分批发料入参 */
export interface ConsignIssueForm {
  consignItemId: string
  qty: number
}

/** 成品成本核算结果 */
export interface SubcontractCostResult {
  processingFee: number
  consignCost: number
  finishedCost: number
  costVoucherNo?: string | null
}

/** 防吞料对账逐料明细 */
export interface ConsignReconcileLine {
  consignItemId: string
  issuedQty: number
  expectedQty: number
  variance: number
  allowedVariance: number
  isAnomaly: boolean
}

/** 防吞料对账报表 */
export interface ConsignReconcileResult {
  poNo?: string
  lineNo?: number
  finishedQty: number
  tolerancePct: number
  hasAnomaly: boolean
  lines: ConsignReconcileLine[]
}
