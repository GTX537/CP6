import http from '../http'
import type { WmsApi } from '@/types/wms/wms'
import type { StockDwellQuery, StockDwellSummary } from '@/types/wms/stockDwell'

export const stockDwellApi = {
  summary(query: StockDwellQuery) {
    return http.post<any, WmsApi<StockDwellSummary>>('/wms/stock-dwell/summary', query)
  },
}
