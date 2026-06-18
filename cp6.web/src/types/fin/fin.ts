// 财务（Fin）模块前端类型 —— 对应后端 CP6.Entity.DomainModels.Fin / Services.Fin

export interface ApiResp<T> {
  code: number
  message: string
  data: T
}

/** 会计科目 */
export interface GlAccount {
  id?: string
  code: string
  name: string
  type: number          // AccountType: 1资产 2负债 3权益 4收入 5费用
  normalSide: number    // AccountSide: 1借 2贷
  parentId?: string | null
  level: number
  isLeaf: boolean
  isControl: boolean
  subLedgerType?: string | null
  requirePartner: boolean
  role?: string | null
  standardScheme: string
  isActive: boolean
  currencyCd?: string | null
}

/** 凭证分录行 */
export interface JournalLine {
  id?: string
  entryId?: string
  lineNo?: number
  accountId: string
  debit: number
  credit: number
  partnerId?: string | null
  costObjectType?: string | null
  costObjectId?: string | null
  costCenterId?: string | null
  currencyCd?: string | null
  memo?: string | null
}

/** 记账凭证头 */
export interface JournalEntry {
  id?: string
  no?: string
  voucherDate: string
  periodId?: string
  source: number        // VoucherSource: 0手工 1AP 2AR 3成本 4结转 5红冲 6汇兑重估
  sourceDocNo?: string | null
  status: number        // JournalStatus: 0草稿 1待复核 2已过账 3已驳回 4已红冲
  description: string
  makerId?: string
  makerAt?: string
  checkerId?: string | null
  checkerAt?: string | null
  rejectReason?: string | null
  autoPosted?: boolean
  lines: JournalLine[]
}

/** 会计期间 */
export interface FiscalPeriod {
  id: string
  fiscalYear: number
  year: number
  month: number
  periodNo: number
  periodStart: string
  periodEnd: string
  status: number        // PeriodStatus: 0开启 1已结账
  closedAt?: string | null
  closedBy?: string | null
}

/** 试算平衡表 */
export interface TrialBalanceRow {
  code: string
  name: string
  openBal: number
  periodDebit: number
  periodCredit: number
  closeBal: number
  normalSide: number
}
export interface TrialBalance {
  periodId: string
  rows: TrialBalanceRow[]
  movementBalanced: boolean
  closingBalanced: boolean
  isBalanced: boolean
}

// ── 三大报表（章08）──
/** 报表一行（科目级） */
export interface FinReportLine { code: string; name: string; amount: number }
/** 资产负债表（期末余额时点数；资产 = 负债+权益+本年利润 必平） */
export interface BalanceSheet {
  periodId: string
  assets: FinReportLine[]
  liabilities: FinReportLine[]
  equity: FinReportLine[]
  currentProfit: number
  totalAssets: number
  totalLiabilities: number
  totalEquity: number
  totalLiabEquity: number
  isBalanced: boolean
}
/** 损益表（本期发生区间数；毛利=收入−COGS,净利润=毛利−营业费用） */
export interface IncomeStatement {
  periodId: string
  revenueLines: FinReportLine[]
  costLines: FinReportLine[]
  expenseLines: FinReportLine[]
  revenue: number
  cost: number
  grossProfit: number
  operatingExpense: number
  netProfit: number
}

// ── 成本核算（章06）──
/** 成本归集明细行（A2 §3.7：工/费行承载工时追溯字段） */
export interface CostSheetLine {
  id?: string
  lineNo: number
  element: number       // CostElement: 1直接材料 2直接人工 3制造费用
  processCd?: string | null
  materialCd?: string | null
  materialName?: string | null
  planQty: number
  actualQty: number
  unitPrice: number
  actualAmount: number
  standardAmount: number
  // ── A2 §3.7 工时追溯（料行多为空，工/费行承载）──
  hours?: number | null
  standardHours?: number | null
  wgCd?: string | null
  taskCd?: string | null
  rateValidFrom?: string | null
  hourSource?: string | null
  calcNote?: string | null
  warningCode?: string | null
}
/** 成本单（料吃MES真实消耗×BOM单价；工费工时×费率做真；计算属性由后端序列化） */
export interface CostSheet {
  id?: string
  no: string
  workOrderNo: string
  productCd?: string | null
  completedQty: number
  materialActual: number
  materialStandard: number
  laborActual: number       // A2 §3.6：实际直接人工（工时×费率归集）
  laborStandard: number     // A2 §3.6：标准直接人工（标准工时×费率）
  overheadActual: number    // A2 §3.6：实际制造费用（机时×费率归集）
  overheadStandard: number  // A2 §3.6：标准制造费用（标准机时×费率）
  status: number        // CostSheetStatus: 0草稿 1已归集 2已结转
  totalActual: number
  standardCost: number
  variance: number
  fgUnitCost: number
  lines?: CostSheetLine[]
}
export const COST_SHEET_STATUS_LABEL: Record<number, string> = { 0: '草稿', 1: '已归集', 2: '已结转' }
export const COST_SHEET_STATUS_TAG: Record<number, '' | 'info' | 'success' | 'warning' | 'danger'> = { 0: 'info', 1: 'warning', 2: 'success' }
export const COST_ELEMENT_LABEL: Record<number, string> = { 1: '直接材料', 2: '直接人工', 3: '制造费用' }

// ── 枚举 → 中文标签（中文即 i18n key，视图用 t(label) 翻译）──
export const ACCOUNT_TYPE_LABEL: Record<number, string> = { 1: '资产', 2: '负债', 3: '权益', 4: '收入', 5: '费用' }
export const ACCOUNT_SIDE_LABEL: Record<number, string> = { 1: '借', 2: '贷' }
export const VOUCHER_SOURCE_LABEL: Record<number, string> = { 0: '手工', 1: '应付', 2: '应收', 3: '成本', 4: '结转', 5: '红冲', 6: '汇兑重估' }
export const JOURNAL_STATUS_LABEL: Record<number, string> = { 0: '草稿', 1: '待复核', 2: '已过账', 3: '已驳回', 4: '已红冲' }
export const JOURNAL_STATUS_TAG: Record<number, '' | 'info' | 'success' | 'warning' | 'danger'> =
  { 0: 'info', 1: 'warning', 2: 'success', 3: 'danger', 4: '' }
export const PERIOD_STATUS_LABEL: Record<number, string> = { 0: '开启', 1: '已结账' }

export const SCHEME_OPTIONS = [
  { value: 'CN-GAAP', label: '中国企业会计准则' },
  { value: 'INTL', label: '国际通用' },
]

// ───────── 应付 AP（章03）─────────

/** 应付发票行 */
export interface ApInvoiceLine {
  id?: string
  invoiceId?: string
  lineNo?: number
  itemId?: string | null
  qty: number
  unitPrice: number
  amount: number
  taxCodeId?: string | null
  taxAmount?: number
  expenseAccountId: string
  costCenterId?: string | null
}

/** 应付发票 */
export interface ApInvoice {
  id?: string
  no?: string
  supplierInvoiceNo?: string | null
  supplierId: string
  invoiceDate: string
  dueDate: string
  currencyCd?: string | null
  fxRate: number
  netAmount?: number
  taxAmount?: number
  grossAmount?: number
  settledAmount?: number
  status?: number       // ApInvoiceStatus: 0草稿 1已过账 2部分核销 3已核销 4已红冲
  journalEntryId?: string | null
  purchaseOrderId?: string | null
  isCreditMemo?: boolean
  lines: ApInvoiceLine[]
}

/** 付款单 */
export interface Payment {
  id?: string
  no?: string
  supplierId: string
  payDate: string
  currencyCd?: string | null
  fxRate: number
  amount: number
  settledAmount?: number
  method: number        // PaymentMethod: 1转账 2现金 3票据 9其他
  bankAccountId: string
  isPrepayment?: boolean
  status?: number       // PaymentStatus: 1已过账 2已撤销
  journalEntryId?: string | null
}

/** 核销应用一行 */
export interface SettlementApply {
  invoiceId: string
  appliedAmount: number
  discountAmount?: number
  discountAccountId?: string | null
}

/** 账龄一行 */
export interface ApAgingRow {
  supplierId: string
  notDue: number
  days1To30: number
  days31To60: number
  days60Plus: number
  total: number
  overdue: number
}

/** 子账↔GL 对账结果 */
export interface ApReconcileResult {
  subLedger: number
  glBalance: number
  isMatched: boolean
}

export const AP_INVOICE_STATUS_LABEL: Record<number, string> = { 0: '草稿', 1: '已过账', 2: '部分核销', 3: '已核销', 4: '已红冲' }
export const AP_INVOICE_STATUS_TAG: Record<number, '' | 'info' | 'success' | 'warning' | 'danger'> =
  { 0: 'info', 1: 'success', 2: 'warning', 3: '', 4: 'danger' }
export const PAYMENT_METHOD_LABEL: Record<number, string> = { 1: '转账', 2: '现金', 3: '票据', 9: '其他' }
export const PAYMENT_METHOD_OPTIONS = [
  { value: 1, label: '转账' }, { value: 2, label: '现金' }, { value: 3, label: '票据' }, { value: 9, label: '其他' },
]
export const PAYMENT_STATUS_LABEL: Record<number, string> = { 1: '已过账', 2: '已撤销' }
export const PAYMENT_STATUS_TAG: Record<number, '' | 'info' | 'success' | 'warning' | 'danger'> = { 1: 'success', 2: 'danger' }

/** 银行账户 */
export interface BankAccount {
  id?: string
  code: string
  name: string
  bankName?: string | null
  accountNo?: string | null
  currencyCd?: string | null
  glAccountId: string
  isActive?: boolean
}

/** 税码 */
export interface TaxCode {
  id?: string
  code: string
  name: string
  rate: number
  direction: number   // 1进项 2销项
  recoverable: boolean
  isActive?: boolean
}

// ───────── 应收 AR（章04）─────────

/** 应收发票行 */
export interface ArInvoiceLine {
  id?: string
  invoiceId?: string
  lineNo?: number
  itemId?: string | null
  qty: number
  unitPrice: number
  amount: number
  taxCodeId?: string | null
  taxAmount?: number
  revenueAccountId?: string | null
  costCenterId?: string | null
}

/** 应收发票 */
export interface ArInvoice {
  id?: string
  no?: string
  customerId: string
  invoiceDate: string
  dueDate: string
  currencyCd?: string | null
  fxRate: number
  netAmount?: number
  taxAmount?: number
  grossAmount?: number
  settledAmount?: number
  costAmount?: number
  status?: number       // ArInvoiceStatus: 0草稿 1已过账 2部分核销 3已核销 4已红冲
  journalEntryId?: string | null
  costJournalEntryId?: string | null
  shipmentId?: string | null
  orderId?: string | null
  isCreditMemo?: boolean
  creditNoteId?: string | null
  lines: ArInvoiceLine[]
}

/** 收款单 */
export interface Receipt {
  id?: string
  no?: string
  customerId: string
  receiptDate: string
  currencyCd?: string | null
  fxRate: number
  amount: number
  settledAmount?: number
  method: number        // PaymentMethod: 1转账 2现金 3票据 9其他
  bankAccountId: string
  isAdvance?: boolean
  status?: number       // ReceiptStatus: 1已过账 2已撤销
  journalEntryId?: string | null
}

/** 应收账龄一行 */
export interface ArAgingRow {
  customerId: string
  notDue: number
  days1To30: number
  days31To60: number
  days60Plus: number
  total: number
  overdue: number
}

/** 应收子账↔GL 对账结果 */
export interface ArReconcileResult {
  subLedger: number
  glBalance: number
  isMatched: boolean
}

/** 信用校验结果 */
export interface CreditCheckResult {
  customerId: string
  creditLimit: number
  openAr: number
  orderAmount: number
  controlled: boolean
  exceeded: boolean
  available: number
}

/** 销售退货红字请求（接 CreditNote） */
export interface FinCreditMemoRequest {
  creditNoteId: string
  customerId: string
  invoiceDate: string
  dueDate: string
  currencyCd?: string | null
  fxRate?: number
  estimatedCost: number
  originInvoiceId?: string | null
  lines: { itemId?: string | null; qty: number; unitPrice: number; taxCodeId?: string | null; revenueAccountId?: string | null; costCenterId?: string | null }[]
}

export const AR_INVOICE_STATUS_LABEL: Record<number, string> = { 0: '草稿', 1: '已过账', 2: '部分核销', 3: '已核销', 4: '已红冲' }
export const AR_INVOICE_STATUS_TAG: Record<number, '' | 'info' | 'success' | 'warning' | 'danger'> =
  { 0: 'info', 1: 'success', 2: 'warning', 3: '', 4: 'danger' }
export const RECEIPT_STATUS_LABEL: Record<number, string> = { 1: '已过账', 2: '已撤销' }
export const RECEIPT_STATUS_TAG: Record<number, '' | 'info' | 'success' | 'warning' | 'danger'> = { 1: 'success', 2: 'danger' }
