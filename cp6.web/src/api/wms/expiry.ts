import http from '../http'
import type { ExpiryStock, ExpiryDisposeRequest, WmsApi } from '@/types/wms'

export const expiryApi = {
  expiring(days = 30, warehouseCd?: string) {
    return http.get<any, WmsApi<ExpiryStock[]>>('/wms/expiry/expiring', { params: { days, warehouseCd } })
  },
  dispose(req: ExpiryDisposeRequest) {
    return http.post<any, WmsApi<{ disposed: number }>>('/wms/expiry/dispose', req)
  },
}
