import http from '../http'
import type { WmsApi } from '@/types/wms'
import type { StockDwellQuery, StockDwellSummary } from '@/types/stockDwell'

export const stockDwellApi = {
  summary(query: StockDwellQuery) {
    return http.post<any, WmsApi<StockDwellSummary>>('/wms/stock-dwell/summary', query)
  },
}
