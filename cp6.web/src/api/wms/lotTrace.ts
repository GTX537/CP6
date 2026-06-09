import http from '../http'
import type { LotTraceResult, LotStockSummary, WmsApi } from '@/types/wms/wms'

export const lotTraceApi = {
  forward(productCd: string, lotNo: string) {
    return http.get<any, WmsApi<LotTraceResult>>('/wms/lot-trace/forward', { params: { productCd, lotNo } })
  },
  backward(productCd: string, lotNo: string) {
    return http.get<any, WmsApi<LotTraceResult>>('/wms/lot-trace/backward', { params: { productCd, lotNo } })
  },
  summary(productCd: string, lotNo: string) {
    return http.get<any, WmsApi<LotStockSummary>>('/wms/lot-trace/summary', { params: { productCd, lotNo } })
  },
  recall(productCd: string, lotNo: string, flag: boolean) {
    return http.post<any, WmsApi<{ affectedStocks: number }>>('/wms/lot-trace/recall', { productCd, lotNo, flag })
  },
}
