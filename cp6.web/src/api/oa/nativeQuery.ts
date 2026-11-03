import http from '@/api/http'
import type { NativeDetail } from './nativeInbox'
export interface QueryFilter {
  starterId?: string; handlerId?: string; flowKey?: string; keyword?: string; status?: number; from?: string; to?: string
  page: number; pageSize: number; formKey?: string; formVersion?: number; hasInstance?: boolean
  submittedFrom?: string; submittedTo?: string; timeZoneId?: string; snapshotAsOf?: string
}
export type QueryApplication = NonNullable<NativeDetail['business']> & {
  applicationReceiptId: string | null; receiptRevision: string | null; businessResultRef: { owner: string; id: string; version: string } | null; pendingReceiptGap: boolean
}
export interface QueryItem {
  id: string; formDataId: string | null; instanceId: string | null; formKey: string | null; formVersion: number | null
  formVersionId: string | null; formName: string | null; flowKey: string | null; flowVersion: number | null
  flowVersionId: string | null; flowName: string | null; title: string | null; starterRef: string; submittedAtUtc: string
  instanceStatus: number | null; submissionState: string; applicationSummary: QueryApplication | null
  evidenceState: string; timeSource: string; detailRef: string
}
export interface QueryPage { items: QueryItem[]; total: number; page: number; pageSize: number; snapshotAsOf: string; timeZoneId: string | null; actualUserId: string; effectiveUserId: string }
export interface QueryDetail { item: QueryItem; form: NativeDetail['form']; instance: NativeDetail | null; canEdit: false }
const unwrap = <T>(value: any): T => value?.data ?? value
export const nativeQueryApi = {
  context: async (signal?: AbortSignal) => unwrap<{ timeZoneId: string }>(await http.get('/oa/v2/query/context', { signal })),
  search: async (filter: QueryFilter, signal?: AbortSignal) => unwrap<QueryPage>(await http.post('/oa/v2/query/search', filter, { signal })),
  instance: async (id: string, signal?: AbortSignal) => unwrap<QueryDetail>(await http.get(`/oa/v2/query/instances/${encodeURIComponent(id)}`, { signal })),
  form: async (id: string, signal?: AbortSignal) => unwrap<QueryDetail>(await http.get(`/oa/v2/form-data/${encodeURIComponent(id)}`, { signal })),
}
