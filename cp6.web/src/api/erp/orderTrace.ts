import http from '../http'
import type { ApiResult, OrderTrace } from '@/types/orderTrace'

export const orderTraceApi = {
  get(webOrderNo: string) {
    return http.get<any, ApiResult<OrderTrace>>(`/order-trace/${encodeURIComponent(webOrderNo)}`)
  },
}
