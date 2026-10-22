import http from '../http'
import type {
  Stock,
  StockTransaction,
  StockSearchQuery,
  StockMovementRequest,
  StockMoveRequest,
  StockOperationResult,
  StockOperation,
  StockOperationScope,
  WmsApi,
  WmsPaged,
} from '@/types/wms/wms'

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
  apply(req: StockMovementRequest & { operationId: string }) {
    return http.post<any, WmsApi<StockOperationResult>>('/wms/stock/commands/v1/apply', req)
  },

  /** 棚移動 */
  move(req: StockMoveRequest & { operationId: string }) {
    return http.post<any, WmsApi<StockOperationResult>>('/wms/stock/commands/v1/move', req)
  },

  operationScope() {
    return http.get<any, WmsApi<StockOperationScope>>('/wms/stock/operations/v1/scope')
  },
  // UNKNOWN is not proof of rollback. Retain the same key; never generate/retry a new operation here.
  readOperation(operationId: string) {
    return http.get<any, WmsApi<StockOperation>>(`/wms/stock/operations/v1/${encodeURIComponent(operationId)}`)
  },
  closeOperation(operationId: string) {
    return http.post<any, WmsApi<StockOperation>>(`/wms/stock/operations/v1/${encodeURIComponent(operationId)}/close`)
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

  // ─────── Phase 7 Gap 1.3 QC ステータス ───────
  /** 在庫 1 件の QC ステータスを設定（PENDING/PASSED/FAILED/HOLD） */
  setQcStatus(stockId: string, newStatus: string, reason?: string) {
    return http.post<any, WmsApi<{ stockId: string; qcStatus: string }>>(
      `/wms/stock-qc/${stockId}/set`,
      { newStatus, reason },
    )
  },

  /** 製造指図に紐づく全 Stock を一括 QC ステータス更新 */
  setQcStatusByWorkOrder(workOrderNo: string, newStatus: string, reason?: string) {
    return http.post<any, WmsApi<{ workOrderNo: string; affected: number }>>(
      `/wms/stock-qc/by-work-order/${encodeURIComponent(workOrderNo)}`,
      { newStatus, reason },
    )
  },
}
