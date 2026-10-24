import http from '../http'
import type { KitMaster, KitOrder, KitOrderSearchQuery, WmsApi } from '@/types/wms/wms'
import type { KitQuery, KitMasterRow, KitMasterDetail, KitOrderRow, KitPage } from '@/types/wms/kittingOwner'

export const kittingApi = {
  searchMastersOwner(query: KitQuery) { return http.get<any, WmsApi<KitPage<KitMasterRow>>>('/wms/kit/owner-v1/masters', { params: query }) },
  getMasterOwner(sku: string) { return http.get<any, WmsApi<KitMasterDetail>>(`/wms/kit/owner-v1/masters/${encodeURIComponent(sku)}`) },
  searchOrdersOwner(query: KitQuery) { return http.get<any, WmsApi<KitPage<KitOrderRow>>>('/wms/kit/owner-v1/orders', { params: query }) },
  getOrderOwner(no: string) { return http.get<any, WmsApi<KitOrderRow>>(`/wms/kit/owner-v1/orders/${encodeURIComponent(no)}`) },
  searchMasters(keyword?: string) {
    return http.get<any, WmsApi<KitMaster[]>>('/wms/kit/masters', { params: { keyword } })
  },
  getMaster(kitSku: string) {
    return http.get<any, WmsApi<KitMaster>>(`/wms/kit/masters/${encodeURIComponent(kitSku)}`)
  },
  createMaster(dto: KitMaster) {
    return http.post<any, WmsApi<void>>('/wms/kit/masters', dto)
  },
  updateMaster(kitSku: string, dto: KitMaster) {
    return http.put<any, WmsApi<void>>(`/wms/kit/masters/${encodeURIComponent(kitSku)}`, dto)
  },
  deleteMaster(kitSku: string) {
    return http.delete<any, WmsApi<void>>(`/wms/kit/masters/${encodeURIComponent(kitSku)}`)
  },

  searchOrders(query: KitOrderSearchQuery = {}) {
    return http.get<any, WmsApi<KitOrder[]>>('/wms/kit/orders', { params: query })
  },
  getOrder(no: string) {
    return http.get<any, WmsApi<KitOrder>>(`/wms/kit/orders/${encodeURIComponent(no)}`)
  },
  createOrder(dto: KitOrder) {
    return http.post<any, WmsApi<{ kitOrderNo: string }>>('/wms/kit/orders', dto)
  },
  execute(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/kit/orders/${encodeURIComponent(no)}/execute`)
  },
  cancel(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/kit/orders/${encodeURIComponent(no)}/cancel`)
  },
}
