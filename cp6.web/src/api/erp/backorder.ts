import http from '../http'
import type {
  ApiResult,
  BackorderActionRequest,
  BackorderActionResult,
  BackorderQueueItem,
  BackorderQueueQuery,
} from '@/types/erp/backorder'

export const backorderApi = {
  queue(query: BackorderQueueQuery) {
    return http.get<any, ApiResult<BackorderQueueItem[]>>('/backorder/queue', { params: query })
  },

  closeRemaining(webOrderNo: string, detailNo: number, request: BackorderActionRequest) {
    return http.post<any, ApiResult<BackorderActionResult>>(
      `/backorder/${encodeURIComponent(webOrderNo)}/${detailNo}/close-remaining`,
      request,
    )
  },

  splitToNewOrder(webOrderNo: string, detailNo: number, request: BackorderActionRequest) {
    return http.post<any, ApiResult<BackorderActionResult>>(
      `/backorder/${encodeURIComponent(webOrderNo)}/${detailNo}/split-to-new-order`,
      request,
    )
  },
}

export type { BackorderQueueItem }
