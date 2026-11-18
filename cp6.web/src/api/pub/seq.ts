import http from '../http'

export interface NumberRule {
  version: string; prefix: string; dateFormat: string | null; minimumWidth: number
  resetCycle: number; timeZone: string; culture: string; firstValue: number
}
export interface NumberManagement {
  scope: { ruleId: string; tenantId: string; sourceOwner: string; bizKey: string; businessScope: string
    company: string | null; warehouse: string | null; cutoverBasis: string; uniqueDomain: string }
  rule: NumberRule; allocatedCount: number; effectiveAtUtc: string | null; currentPeriod: string; originalAllocator: string
}
export interface NumberFormatDraft {
  expectedRuleVersion: string; prefix: string; dateFormat: string | null; minimumWidth: number
}
export interface NumberChange extends NumberFormatDraft { operationRef: string; newVersion: string }
export interface NumberChangeReceipt {
  changeId: string; ruleId: string; tenantId: string; sourceOwner: string; operationRef: string
  previousVersion: string; rule: NumberRule
}
export interface NumberPreview {
  sample: string; timeZone: string; periodKey: string; isReservation: false
  wouldStartPeriod: boolean; ruleVersion: string
}
export interface LegacyNumberPreview {
  sample: string; timeZone: string; periodKey: string; isReservation: false; wouldReset: boolean
}
interface ListParams { page: number; pageSize: number; keyword?: string; legacyOnly?: boolean }
const ownerPath = (id: string) => '/pub/seq/owner-v1/' + encodeURIComponent(id)

export const numberOwnerApi = {
  list: (params: ListParams) => http.get<unknown, { rows: { id: string; bizKey: string }[]; total: number }>(
    '/pub/seq/owner-v1', { params }),
  management: (id: string) => http.get<unknown, NumberManagement>(ownerPath(id) + '/management'),
  preview: (id: string, draft: NumberFormatDraft) => http.post<unknown, NumberPreview>(ownerPath(id) + '/preview', draft),
  change: (id: string, command: NumberChange) => http.post<unknown, NumberChangeReceipt>(ownerPath(id) + '/changes', command),
  original: (id: string, operationRef: string) => http.post<unknown, NumberChangeReceipt>(ownerPath(id) + '/changes/read', { operationRef }),
}

// 采番规则 CRUD（VolTable 通用接口）；后端返回 {rows,total}/实体，无信封。
export const seqApi = {
  getList(params: ListParams) {
    return http.get('/pub/seq', { params })
  },
  add(data: any) {
    return http.post('/pub/seq', data)
  },
  update(data: any) {
    return http.put('/pub/seq', data)
  },
  del(ids: string[]) {
    return http.delete('/pub/seq', { data: ids })
  },
  async preview(bizKey: string): Promise<string> {
    const result = await seqApi.previewDetails(bizKey)
    return result.sample
  },
  previewDetails(bizKey: string) {
    return http.get<unknown, LegacyNumberPreview>('/pub/seq/preview/' + encodeURIComponent(bizKey))
  },
}
