import http from '../http'
import type { InboundReceipt, InboundReceiptSearchQuery, WmsApi } from '@/types/wms/wms'

export const inboundReceiptApi = {
  search(query: InboundReceiptSearchQuery = {}) {
    return http.get<any, WmsApi<InboundReceipt[]>>('/wms/inbound-receipt', { params: query })
  },

  get(no: string) {
    return http.get<any, WmsApi<InboundReceipt>>(`/wms/inbound-receipt/${encodeURIComponent(no)}`)
  },

  /** 受入登録（同時に確定 + 在庫反映） */
  confirm(dto: InboundReceipt) {
    return http.post<any, WmsApi<{ receiptNo: string }>>('/wms/inbound-receipt', dto)
  },
}
