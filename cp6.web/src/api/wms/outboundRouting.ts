import http from '../http'
import type { OutboundRoutingRule, WmsApi } from '@/types/wms/outboundRouting'

export const outboundRoutingApi = {
  list(includeDisabled = true) {
    return http.get<any, WmsApi<OutboundRoutingRule[]>>('/wms/outbound-routing', {
      params: { includeDisabled },
    })
  },

  preview(params: { productCd: string; customerCd?: string; outboundType?: number; fallbackWarehouseCd?: string }) {
    return http.get<any, WmsApi<string[]>>('/wms/outbound-routing/preview', { params })
  },

  create(rule: OutboundRoutingRule) {
    return http.post<any, WmsApi<OutboundRoutingRule>>('/wms/outbound-routing', rule)
  },

  update(id: string, rule: OutboundRoutingRule) {
    return http.put<any, WmsApi<unknown>>(`/wms/outbound-routing/${encodeURIComponent(id)}`, rule)
  },

  remove(id: string) {
    return http.delete<any, WmsApi<unknown>>(`/wms/outbound-routing/${encodeURIComponent(id)}`)
  },
}
