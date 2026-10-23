import http from '../http'
import type { WmsApi } from '@/types/wms/wms'
import type { LotQuery, LotSnapshot, LotRecallCommand, LotRecallResult } from '@/types/wms/lotTraceOwner'
export const lotTraceApi = {
  read(query: LotQuery) {
    return http.get<any, WmsApi<LotSnapshot>>('/wms/lot-trace/owner-v1', { params: query })
  },
  recall(command: LotRecallCommand) {
    return http.post<any, WmsApi<LotRecallResult>>('/wms/lot-trace/owner-v1/recall', command)
  },
}
