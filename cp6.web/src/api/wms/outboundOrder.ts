import http from '../http'
import type {
  OutboundOrder, OutboundOrderSearchQuery, ShipRequest, WmsApi,
} from '@/types/wms'

export const outboundOrderApi = {
  search(query: OutboundOrderSearchQuery = {}) {
    return http.get<any, WmsApi<OutboundOrder[]>>('/wms/outbound-order', { params: query })
  },
  get(no: string) {
    return http.get<any, WmsApi<OutboundOrder>>(`/wms/outbound-order/${encodeURIComponent(no)}`)
  },
  create(dto: OutboundOrder) {
    return http.post<any, WmsApi<{ outboundNo: string }>>('/wms/outbound-order', dto)
  },
  update(no: string, dto: OutboundOrder) {
    return http.put<any, WmsApi<void>>(`/wms/outbound-order/${encodeURIComponent(no)}`, dto)
  },
  delete(no: string) {
    return http.delete<any, WmsApi<void>>(`/wms/outbound-order/${encodeURIComponent(no)}`)
  },
  confirm(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/outbound-order/${encodeURIComponent(no)}/confirm`)
  },
  cancel(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/outbound-order/${encodeURIComponent(no)}/cancel`)
  },
  /** 引当（FIFO + 期限優先） */
  allocate(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/outbound-order/${encodeURIComponent(no)}/allocate`)
  },
  /** ピッキング開始（Allocated → Picking） */
  startPicking(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/outbound-order/${encodeURIComponent(no)}/start-picking`)
  },
  /** 出庫確定（OUT + 梱包採番） */
  ship(no: string, req: ShipRequest = {}) {
    return http.post<any, WmsApi<{ packageNo: string | null }>>(
      `/wms/outbound-order/${encodeURIComponent(no)}/ship`, req)
  },
  /** MES 製造指図から材料出庫指示を自動展開 */
  fromWorkOrder(workOrderNo: string) {
    return http.post<any, WmsApi<{ outboundNo: string }>>(
      `/wms/outbound-order/from-work-order/${encodeURIComponent(workOrderNo)}`)
  },
  /** PA 受注から出荷指示を自動展開 */
  fromOrder(webOrderNo: string) {
    return http.post<any, WmsApi<{ outboundNo: string }>>(
      `/wms/outbound-order/from-order/${encodeURIComponent(webOrderNo)}`)
  },
}
