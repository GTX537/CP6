import http from '../http'
import type { ApiResp } from '@/types/fin/fin'
import type {
  BankStatement, BankStatementLine, BankCandidateLine,
  ReconciliationStatement, BankImportProfile, BankOnlyLineResult, BankImportPreviewResult,
} from '@/types/fin/bankRecon'

export const bankStatementApi = {
  list(p: { bankAccountId?: string; fiscalPeriodId?: string; status?: number }) {
    return http.get<any, ApiResp<BankStatement[]>>('/fin/bank-statement', { params: p })
  },
  get(id: string) { return http.get<any, ApiResp<{ statement: BankStatement; lines: BankStatementLine[] }>>(`/fin/bank-statement/${id}`) },
  create(d: Partial<BankStatement>) { return http.post<any, ApiResp<unknown>>('/fin/bank-statement', d) },
  preview(id: string, profileId: string, file: File) {
    const fd = new FormData(); fd.append('file', file)
    return http.post<any, ApiResp<BankImportPreviewResult>>(`/fin/bank-statement/${id}/import?profileId=${profileId}&dryRun=true`, fd)
  },
  confirm(id: string, profileId: string, file: File) {
    const fd = new FormData(); fd.append('file', file)
    return http.post<any, ApiResp<unknown>>(`/fin/bank-statement/${id}/import?profileId=${profileId}&dryRun=false`, fd)
  },
  addLine(id: string, line: Partial<BankStatementLine>) { return http.post<any, ApiResp<unknown>>(`/fin/bank-statement/${id}/line`, line) },
  updateLine(id: string, lineId: string, line: Partial<BankStatementLine>) { return http.put<any, ApiResp<unknown>>(`/fin/bank-statement/${id}/line/${lineId}`, line) },
  deleteLine(id: string, lineId: string) { return http.delete<any, ApiResp<unknown>>(`/fin/bank-statement/${id}/line/${lineId}`) },
}

export const bankReconApi = {
  candidates(statementId: string, lineId: string, widen = false) {
    return http.get<any, ApiResp<BankCandidateLine[]>>(`/fin/bank-recon/${statementId}/candidates`, { params: { lineId, widen } })
  },
  autoMatch(statementId: string) { return http.post<any, ApiResp<unknown>>(`/fin/bank-recon/${statementId}/auto-match`) },
  manualMatch(statementId: string, statementLineIds: string[], journalLineIds: string[], rowVersion?: string, note?: string) {
    return http.post<any, ApiResp<unknown>>(`/fin/bank-recon/${statementId}/manual-match`,
      { statementLineIds, journalLineIds, note }, { headers: rowVersion ? { 'X-Row-Version': rowVersion } : {} })
  },
  unmatch(groupId: string) { return http.post<any, ApiResp<unknown>>(`/fin/bank-recon/unmatch/${groupId}`) },
  generateVoucher(statementId: string, lineIds: string[], counterAccountId: string, counterRole?: string, partnerId?: string) {
    return http.post<any, ApiResp<BankOnlyLineResult[]>>(`/fin/bank-recon/${statementId}/generate-voucher`, { lineIds, counterAccountId, counterRole, partnerId })
  },
  markPending(statementId: string, lineIds: string[], category: number, rowVersion?: string) {
    return http.post<any, ApiResp<unknown>>(`/fin/bank-recon/${statementId}/mark-pending`, { lineIds, category, rowVersion })
  },
  reconStatement(statementId: string) { return http.get<any, ApiResp<ReconciliationStatement>>(`/fin/bank-recon/${statementId}/reconciliation-statement`) },
  lock(statementId: string) { return http.post<any, ApiResp<unknown>>(`/fin/bank-recon/${statementId}/lock`) },
  unlock(statementId: string, reason: string) { return http.post<any, ApiResp<unknown>>(`/fin/bank-recon/${statementId}/unlock`, { reason }) },
}

export const bankProfileApi = {
  list(bankAccountId?: string) { return http.get<any, ApiResp<BankImportProfile[]>>('/fin/bank-import-profile', { params: { bankAccountId } }) },
  save(d: BankImportProfile) { return http.post<any, ApiResp<unknown>>('/fin/bank-import-profile/upsert', d) },
  remove(id: string) { return http.delete<any, ApiResp<unknown>>(`/fin/bank-import-profile/${id}`) },
}
