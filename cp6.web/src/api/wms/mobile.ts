import http from '../http'
import type {
  MobileTask, MobileTaskQuery,
  MobileScanRequest, MobileScanResult, MobileCompleteRequest,
  WmsApi,
} from '@/types/wms'

// ───────── モバイル作業指示（WM300） ─────────

export const mobileApi = {
  tasks(q: MobileTaskQuery = {}) {
    return http.get<any, WmsApi<MobileTask[]>>('/wms/mobile/task', { params: q })
  },
  get(no: string) {
    return http.get<any, WmsApi<MobileTask>>(`/wms/mobile/task/${encodeURIComponent(no)}`)
  },
  create(dto: Partial<MobileTask>) {
    return http.post<any, WmsApi<{ mobileTaskNo: string }>>('/wms/mobile/task', dto)
  },
  start(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/mobile/task/${encodeURIComponent(no)}/start`)
  },
  scan(req: MobileScanRequest) {
    return http.post<any, WmsApi<MobileScanResult>>('/wms/mobile/scan', req)
  },
  done(no: string, req: MobileCompleteRequest = {}) {
    return http.post<any, WmsApi<void>>(`/wms/mobile/task/${encodeURIComponent(no)}/done`, req)
  },
  cancel(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/mobile/task/${encodeURIComponent(no)}/cancel`)
  },
}
