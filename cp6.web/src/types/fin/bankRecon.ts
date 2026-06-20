export type { ApiResp } from '@/types/fin/fin'

export interface BankStatement {
  id?: string
  no: string
  bankAccountId: string
  fiscalPeriodId: string
  periodStart?: string
  periodEnd?: string
  statementDate?: string | null
  currencyCd?: string | null
  openingBalance: number
  closingBalance: number
  status: number          // 0 Open / 1 Locked
  importFileName?: string | null
  lockedReconciledDiff?: number | null
  lockedBankAdjustedBalance?: number | null
  lockedBookAdjustedBalance?: number | null
  lockedStatementInternalDiff?: number | null
  lockedAt?: string | null
  lockedBy?: string | null
  rowVersion?: string | null
}

export interface BankStatementLine {
  id?: string
  statementId: string
  lineNo: number
  txnDate: string
  direction: number       // 1 Deposit / 2 Withdrawal
  amount: number
  signedAmount?: number
  currencyCd?: string | null
  description?: string | null
  counterpartyName?: string | null
  refNo?: string | null
  balanceAfter?: number | null
  source: number          // 1 Imported / 2 Manual
  matchStatus: number     // 0 Unmatched / 1 Matched / 2 MarkedPending
  category: number
  matchGroupId?: string | null
  generatedJournalEntryId?: string | null
  rowVersion?: string | null
}

export interface BankCandidateLine {
  journalLineId: string
  journalEntryId: string
  entryNo: string
  voucherDate: string
  bankSignedAmount: number
  currencyCd?: string | null
  partnerId?: string | null
  memo?: string | null
  rank: number
}

export interface ReconciliationStatement {
  statementId: string
  currencyCd?: string | null
  openingBalance: number
  closingBalance: number
  totalDeposit: number
  totalWithdrawal: number
  glBankEndingBalance: number
  bookOnlyDepositInTransit: number
  bookOnlyOutstandingPayment: number
  bankOnlyDepositNotBooked: number
  bankOnlyWithdrawalNotBooked: number
  statementInternalDiff: number
  bankAdjustedBalance: number
  bookAdjustedBalance: number
  reconciledDiff: number
  bookOnlyDetails: ReconLineDetail[]
  bankOnlyDetails: ReconLineDetail[]
}
export interface ReconLineDetail { kind: string; date: string; signedAmount: number; reference?: string | null }

export interface BankImportProfile {
  id?: string
  name: string
  bankAccountId?: string | null
  fileFormat: number      // 1 Csv / 2 Excel
  encoding: string
  delimiter: string
  skipHeaderRows: number
  dateField: string
  dateFormat: string
  amountMode: number      // 1 SignedSingle / 2 DepositWithdrawalColumns
  amountField?: string | null
  depositAmountField?: string | null
  withdrawalAmountField?: string | null
  signRule: number
  descriptionField?: string | null
  counterpartyField?: string | null
  refNoField?: string | null
  balanceField?: string | null
  decimalSeparator: string
  thousandsSeparator: string
  isActive: boolean
  rowVersion?: string | null
}

export interface BankOnlyLineResult { lineId: string; ok: boolean; code?: string | null; journalEntryId?: string | null }
export interface BankImportPreviewResult {
  successCount: number; failedCount: number; strongDupCount: number; suspectedDupCount: number
  importBatchNo: string; rows: any[]; errors: { sourceLineNo: number; code: string; rawText: string; reason: string }[]
}

export const MATCH_STATUS_LABEL: Record<number, string> = { 0: '未匹配', 1: '已匹配', 2: '标记未达' }
export const DIRECTION_LABEL: Record<number, string> = { 1: '入款', 2: '出款' }
