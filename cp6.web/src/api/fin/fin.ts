import http from '../http'
import type {
  ApiResp, GlAccount, JournalEntry, FiscalPeriod, TrialBalance,
  ApInvoice, Payment, SettlementApply, ApAgingRow, ApReconcileResult,
  BankAccount, TaxCode,
} from '@/types/fin/fin'

// 财务 API（/api/fin）。http 拦截器已返 body：{ code, message, data }，调用方取 res.data。

/** 会计科目 /api/fin/account */
export const glAccountApi = {
  list(scheme?: string, includeInactive = false) {
    return http.get<any, ApiResp<GlAccount[]>>('/fin/account', { params: { scheme, includeInactive } })
  },
  get(id: string) {
    return http.get<any, ApiResp<GlAccount>>(`/fin/account/${id}`)
  },
  create(data: GlAccount) {
    return http.post<any, ApiResp<{ id: string }>>('/fin/account', data)
  },
  update(id: string, data: GlAccount) {
    return http.put<any, ApiResp<unknown>>(`/fin/account/${id}`, data)
  },
  deactivate(id: string) {
    return http.post<any, ApiResp<unknown>>(`/fin/account/${id}/deactivate`)
  },
  importTemplate(scheme: string) {
    return http.post<any, ApiResp<{ count: number }>>('/fin/account/import', null, { params: { scheme } })
  },
}

/** 记账凭证 /api/fin/journal（凭证不可改不可删，只有状态机动作） */
export const journalApi = {
  list(periodId?: string, status?: number) {
    return http.get<any, ApiResp<JournalEntry[]>>('/fin/journal', { params: { periodId, status } })
  },
  get(id: string) {
    return http.get<any, ApiResp<JournalEntry>>(`/fin/journal/${id}`)
  },
  create(data: JournalEntry) {
    return http.post<any, ApiResp<{ id: string }>>('/fin/journal', data)
  },
  submit(id: string) {
    return http.post<any, ApiResp<unknown>>(`/fin/journal/${id}/submit`)
  },
  post(id: string) {
    return http.post<any, ApiResp<unknown>>(`/fin/journal/${id}/post`)
  },
  reject(id: string, reason: string) {
    return http.post<any, ApiResp<unknown>>(`/fin/journal/${id}/reject`, { reason })
  },
  reverse(id: string, reason: string) {
    return http.post<any, ApiResp<unknown>>(`/fin/journal/${id}/reverse`, { reason })
  },
}

/** 试算平衡表 /api/fin/trial-balance */
export const trialBalanceApi = {
  build(periodId: string) {
    return http.get<any, ApiResp<TrialBalance>>(`/fin/trial-balance/${periodId}`)
  },
}

/** 应付发票 /api/fin/ap/invoice */
export const apInvoiceApi = {
  list(supplierId?: string, status?: number) {
    return http.get<any, ApiResp<ApInvoice[]>>('/fin/ap/invoice', { params: { supplierId, status } })
  },
  get(id: string) {
    return http.get<any, ApiResp<ApInvoice>>(`/fin/ap/invoice/${id}`)
  },
  create(data: ApInvoice) {
    return http.post<any, ApiResp<{ id: string; no: string }>>('/fin/ap/invoice', data)
  },
  post(id: string) {
    return http.post<any, ApiResp<unknown>>(`/fin/ap/invoice/${id}/post`)
  },
  aging(asOf?: string, supplierId?: string) {
    return http.get<any, ApiResp<ApAgingRow[]>>('/fin/ap/invoice/aging', { params: { asOf, supplierId } })
  },
  reconcile() {
    return http.get<any, ApiResp<ApReconcileResult>>('/fin/ap/invoice/reconcile')
  },
}

/** 付款 /api/fin/ap/payment */
export const paymentApi = {
  list(supplierId?: string, status?: number) {
    return http.get<any, ApiResp<Payment[]>>('/fin/ap/payment', { params: { supplierId, status } })
  },
  get(id: string) {
    return http.get<any, ApiResp<Payment>>(`/fin/ap/payment/${id}`)
  },
  pay(data: Payment) {
    return http.post<any, ApiResp<{ id: string; no: string }>>('/fin/ap/payment', data)
  },
  reverse(id: string, reason: string) {
    return http.post<any, ApiResp<unknown>>(`/fin/ap/payment/${id}/reverse`, { reason })
  },
  settle(id: string, applies: SettlementApply[]) {
    return http.post<any, ApiResp<unknown>>(`/fin/ap/payment/${id}/settle`, { applies })
  },
}

/** 应付主数据（银行账户/税码）/api/fin/ap/master */
export const apMasterApi = {
  bankAccounts(includeInactive = false) {
    return http.get<any, ApiResp<BankAccount[]>>('/fin/ap/master/bank-account', { params: { includeInactive } })
  },
  createBankAccount(data: BankAccount) {
    return http.post<any, ApiResp<{ id: string }>>('/fin/ap/master/bank-account', data)
  },
  taxCodes(includeInactive = false) {
    return http.get<any, ApiResp<TaxCode[]>>('/fin/ap/master/tax-code', { params: { includeInactive } })
  },
  createTaxCode(data: TaxCode) {
    return http.post<any, ApiResp<{ id: string }>>('/fin/ap/master/tax-code', data)
  },
}

/** 会计期间 / 月结 /api/fin/period */
export const periodApi = {
  list(year?: number) {
    return http.get<any, ApiResp<FiscalPeriod[]>>('/fin/period', { params: { year } })
  },
  preCloseCheck(id: string) {
    return http.get<any, ApiResp<unknown>>(`/fin/period/${id}/pre-close-check`)
  },
  close(id: string) {
    return http.post<any, ApiResp<unknown>>(`/fin/period/${id}/close`)
  },
  reopen(id: string) {
    return http.post<any, ApiResp<unknown>>(`/fin/period/${id}/reopen`)
  },
}
