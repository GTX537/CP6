import http from '../http'
import type { FormQueryFilter, FormQueryItem } from '../../types/oa/advanced'

export interface LegacyFormHistoryFilter extends FormQueryFilter {
  timeZoneId?: string
  snapshotToken?: string
}

interface LegacyFormHistoryPage {
  items: FormQueryItem[]
  total: number
  page: number
  snapshotAsOf: string
  snapshotToken: string
}

const unwrap = <T>(value: any): T => value?.data ?? value
export const queryApi = {
  search: (filter: FormQueryFilter) => http.post('/oa/query/search', filter),
  historyContext: async (signal?: AbortSignal) =>
    unwrap<{ timeZoneId: string }>(await http.get('/oa/query/history/context', { signal })),
  history: async (filter: LegacyFormHistoryFilter, signal?: AbortSignal) =>
    unwrap<LegacyFormHistoryPage>(await http.post('/oa/query/history/search', filter, { signal })),
}
