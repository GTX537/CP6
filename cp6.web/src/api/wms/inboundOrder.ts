import http from '../http'
import type { InboundOrder, InboundOrderSearchQuery, WmsApi } from '@/types/wms'

export const inboundOrderApi = {
  /** 検索 */
  search(query: InboundOrderSearchQuery = {}) {
    return http.get<any, WmsApi<InboundOrder[]>>('/wms/inbound-order', { params: query })
  },

  /** 詳細取得 */
  get(no: string) {
    return http.get<any, WmsApi<InboundOrder>>(`/wms/inbound-order/${encodeURIComponent(no)}`)
  },

  /** 新規 */
  create(dto: InboundOrder) {
    return http.post<any, WmsApi<{ inboundNo: string }>>('/wms/inbound-order', dto)
  },

  /** 更新 */
  update(no: string, dto: InboundOrder) {
    return http.put<any, WmsApi<void>>(`/wms/inbound-order/${encodeURIComponent(no)}`, dto)
  },

  /** 論理削除 */
  delete(no: string) {
    return http.delete<any, WmsApi<void>>(`/wms/inbound-order/${encodeURIComponent(no)}`)
  },

  /** 確定（→1） */
  confirm(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/inbound-order/${encodeURIComponent(no)}/confirm`)
  },

  /** 取消（→9） */
  cancel(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/inbound-order/${encodeURIComponent(no)}/cancel`)
  },
}
