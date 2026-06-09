import http from '../http'
import type {
  StockTake, StockTakePlanRequest, StockTakeCountInput, StockTakeSearchQuery, WmsApi,
} from '@/types/wms/wms'

export const stockTakeApi = {
  search(q: StockTakeSearchQuery = {}) {
    return http.get<any, WmsApi<StockTake[]>>('/wms/stock-take', { params: q })
  },
  get(no: string) {
    return http.get<any, WmsApi<StockTake>>(`/wms/stock-take/${encodeURIComponent(no)}`)
  },
  createPlan(req: StockTakePlanRequest) {
    return http.post<any, WmsApi<{ stockTakeNo: string }>>('/wms/stock-take/plan', req)
  },
  startCount(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/stock-take/${encodeURIComponent(no)}/start-count`)
  },
  updateCounts(no: string, inputs: StockTakeCountInput[]) {
    return http.put<any, WmsApi<void>>(`/wms/stock-take/${encodeURIComponent(no)}/counts`, inputs)
  },
  submit(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/stock-take/${encodeURIComponent(no)}/submit`)
  },
  approve(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/stock-take/${encodeURIComponent(no)}/approve`)
  },
  cancel(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/stock-take/${encodeURIComponent(no)}/cancel`)
  },
}
