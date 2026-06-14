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
  source: number        // VoucherSource: 0手工 1AP 2AR 3成本 4结转 5红冲
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

// ── 枚举 → 中文标签（中文即 i18n key，视图用 t(label) 翻译）──
export const ACCOUNT_TYPE_LABEL: Record<number, string> = { 1: '资产', 2: '负债', 3: '权益', 4: '收入', 5: '费用' }
export const ACCOUNT_SIDE_LABEL: Record<number, string> = { 1: '借', 2: '贷' }
export const VOUCHER_SOURCE_LABEL: Record<number, string> = { 0: '手工', 1: '应付', 2: '应收', 3: '成本', 4: '结转', 5: '红冲' }
export const JOURNAL_STATUS_LABEL: Record<number, string> = { 0: '草稿', 1: '待复核', 2: '已过账', 3: '已驳回', 4: '已红冲' }
export const JOURNAL_STATUS_TAG: Record<number, '' | 'info' | 'success' | 'warning' | 'danger'> =
  { 0: 'info', 1: 'warning', 2: 'success', 3: 'danger', 4: '' }
export const PERIOD_STATUS_LABEL: Record<number, string> = { 0: '开启', 1: '已结账' }

export const SCHEME_OPTIONS = [
  { value: 'CN-GAAP', label: '中国企业会计准则' },
  { value: 'INTL', label: '国际通用' },
]
