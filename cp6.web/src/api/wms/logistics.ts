import http from '../http'
import type {
  CrossDockOrder, CrossDockSearchQuery,
  ReplenishOrder, ReplenishSearchQuery,
  SlottingPlan, SlottingPlanResult, WmsApi,
} from '@/types/wms/wms'

export const crossDockApi = {
  search(q: CrossDockSearchQuery = {}) {
    return http.get<any, WmsApi<CrossDockOrder[]>>('/wms/cross-dock', { params: q })
  },
  get(no: string) {
    return http.get<any, WmsApi<CrossDockOrder>>(`/wms/cross-dock/${encodeURIComponent(no)}`)
  },
  create(dto: CrossDockOrder) {
    return http.post<any, WmsApi<{ xdockNo: string }>>('/wms/cross-dock', dto)
  },
  execute(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/cross-dock/${encodeURIComponent(no)}/execute`)
  },
  cancel(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/cross-dock/${encodeURIComponent(no)}/cancel`)
  },
}

export const replenishApi = {
  search(q: ReplenishSearchQuery = {}) {
    return http.get<any, WmsApi<ReplenishOrder[]>>('/wms/replenish', { params: q })
  },
  get(no: string) {
    return http.get<any, WmsApi<ReplenishOrder>>(`/wms/replenish/${encodeURIComponent(no)}`)
  },
  create(dto: ReplenishOrder) {
    return http.post<any, WmsApi<{ replenishNo: string }>>('/wms/replenish', dto)
  },
  generateBatch(warehouseCd: string, minQty: number) {
    return http.post<any, WmsApi<{ generated: number }>>('/wms/replenish/generate-batch', { warehouseCd, minQty })
  },
  execute(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/replenish/${encodeURIComponent(no)}/execute`)
  },
  cancel(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/replenish/${encodeURIComponent(no)}/cancel`)
  },
}

export const slottingApi = {
  search(warehouseCd?: string, status?: number) {
    return http.get<any, WmsApi<SlottingPlan[]>>('/wms/slotting', { params: { warehouseCd, status } })
  },
  get(no: string) {
    return http.get<any, WmsApi<SlottingPlanResult>>(`/wms/slotting/${encodeURIComponent(no)}`)
  },
  analyze(warehouseCd: string, analysisDays: number) {
    return http.post<any, WmsApi<{ slottingPlanNo: string }>>('/wms/slotting/analyze', { warehouseCd, analysisDays })
  },
  approve(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/slotting/${encodeURIComponent(no)}/approve`)
  },
  cancel(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/slotting/${encodeURIComponent(no)}/cancel`)
  },
}
