import http from '../http'
import type {
  Stock,
  StockTransaction,
  StockSearchQuery,
  StockMovementRequest,
  StockMoveRequest,
  WmsApi,
  WmsPaged,
} from '@/types/wms'

export const stockApi = {
  /** 在庫照会 */
  search(query: StockSearchQuery = {}) {
    return http.get<any, WmsApi<WmsPaged<Stock>>>('/wms/stock', { params: query })
  },

  /** 在庫の変動履歴 */
  history(stockId: string, days = 90) {
    return http.get<any, WmsApi<{ stock: Stock; transactions: StockTransaction[] }>>(
      `/wms/stock/${stockId}/history`,
      { params: { days } },
    )
  },

  /** 在庫変動 1 件適用 */
  apply(req: StockMovementRequest) {
    return http.post<any, WmsApi<{ txnNo: string }>>('/wms/stock/apply', req)
  },

  /** 棚移動 */
  move(req: StockMoveRequest) {
    return http.post<any, WmsApi<{ outTxnNo: string; inTxnNo: string }>>('/wms/stock/move', req)
  },

  /** トランザクション一覧 */
  transactions(query: {
    productCd?: string
    lotNo?: string
    txnType?: string
    from?: string
    to?: string
    page?: number
    pageSize?: number
  } = {}) {
    return http.get<any, WmsApi<WmsPaged<StockTransaction>>>('/wms/stock/transactions', {
      params: query,
    })
  },
}
