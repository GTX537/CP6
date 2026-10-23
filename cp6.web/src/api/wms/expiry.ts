import http from '../http'
import type { ExpiryObservation, ExpiryObservationQuery } from '@/types/wms/inventoryObservation'
import type { ExpiryStock, ExpiryDisposeRequest, WmsApi } from '@/types/wms/wms'

export const expiryApi = {
  observation(query: ExpiryObservationQuery) {
    return http.post<any, WmsApi<ExpiryObservation>>('/wms/expiry/observation-v1', query)
  },
  expiring(days = 30, warehouseCd?: string) {
    return http.get<any, WmsApi<ExpiryStock[]>>('/wms/expiry/expiring', { params: { days, warehouseCd } })
  },
  dispose(req: ExpiryDisposeRequest) {
    return http.post<any, WmsApi<{ disposed: number }>>('/wms/expiry/dispose', req)
  },
}
