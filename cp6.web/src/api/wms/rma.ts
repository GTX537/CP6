import http from '../http'
import type { RmaHeader, RmaSearchQuery, RmaDispositionInput, WmsApi } from '@/types/wms/wms'

export const rmaApi = {
  search(query: RmaSearchQuery = {}) {
    return http.get<any, WmsApi<RmaHeader[]>>('/wms/rma', { params: query })
  },
  get(no: string) {
    return http.get<any, WmsApi<RmaHeader>>(`/wms/rma/${encodeURIComponent(no)}`)
  },
  create(dto: RmaHeader) {
    return http.post<any, WmsApi<{ rmaNo: string }>>('/wms/rma', dto)
  },
  receive(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/rma/${encodeURIComponent(no)}/receive`)
  },
  startInspection(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/rma/${encodeURIComponent(no)}/start-inspection`)
  },
  judge(no: string, inputs: RmaDispositionInput[]) {
    return http.post<any, WmsApi<void>>(`/wms/rma/${encodeURIComponent(no)}/judge`, inputs)
  },
  close(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/rma/${encodeURIComponent(no)}/close`)
  },
  cancel(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/rma/${encodeURIComponent(no)}/cancel`)
  },
}
