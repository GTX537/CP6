import http from '../http'
import type { BridgeHealthMetrics, WmsApi } from '@/types/wms'

export const bridgeHealthApi = {
  metrics() {
    return http.get<any, WmsApi<BridgeHealthMetrics>>('/bridge-health/metrics')
  },

  compensate(eventId: string) {
    return http.post<any, WmsApi<{ eventId: string; status: string }>>(
      `/bridge-health/compensate/${encodeURIComponent(eventId)}`,
    )
  },
}
