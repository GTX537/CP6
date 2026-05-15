import http from './http'
import type {
  WorkOrderDto,
  WorkOrderSearchQuery,
  ProductionResultDto,
  ProductionResultRequest,
  ProductionResultSearchQuery,
  ExpandFromOrderRequest,
  MesPagedResult,
} from '@/types/mes'

// 既存 http.ts は内部で {code,message,data} を data として返す
// ここでは response.data 自体を返り値として扱う
type Api<T> = { code: number; message: string; data: T }

// ─────────────────────────────────────────────────────────────
// MSBBME020 / 030 — 製造指図
// ─────────────────────────────────────────────────────────────
export const workOrderApi = {
  /** 採番取得 */
  getNextSequence() {
    return http.get<any, Api<{ sequence: string }>>('/mes/work-orders/next-seq')
  },

  /** ME030 — 一覧検索 */
  search(query: WorkOrderSearchQuery) {
    return http.get<any, Api<MesPagedResult<WorkOrderDto>>>('/mes/work-orders', {
      params: query,
      paramsSerializer: { indexes: null },
    })
  },

  /** ME020 — 詳細取得 */
  get(no: string) {
    return http.get<any, Api<WorkOrderDto>>(`/mes/work-orders/${encodeURIComponent(no)}`)
  },

  /** ME020 — 新建 */
  create(dto: WorkOrderDto) {
    return http.post<any, Api<{ workOrderNo: string }>>('/mes/work-orders', dto)
  },

  /** ME020 — 訂正 */
  update(no: string, dto: WorkOrderDto) {
    return http.put<any, Api<unknown>>(`/mes/work-orders/${encodeURIComponent(no)}`, dto)
  },

  /** ME030 — 削除 */
  delete(no: string, rowVersion?: Uint8Array | string | null) {
    return http.delete<any, Api<unknown>>(`/mes/work-orders/${encodeURIComponent(no)}`, {
      data: { rowVersion },
    })
  },

  /** ME020 — 指図発行（Status → 2） */
  issue(no: string) {
    return http.post<any, Api<unknown>>(`/mes/work-orders/${encodeURIComponent(no)}/issue`)
  },

  /** ME020 — 受注 → 指図 自動展開 */
  expandFromOrder(req: ExpandFromOrderRequest) {
    return http.post<any, Api<{ workOrderNos: string[] }>>('/mes/work-orders/expand-from-order', req)
  },
}

// ─────────────────────────────────────────────────────────────
// MSBBME040 / 050 — 製造実績
// ─────────────────────────────────────────────────────────────
export const productionResultApi = {
  /** ME050 — 一覧検索 */
  search(query: ProductionResultSearchQuery) {
    return http.get<any, Api<MesPagedResult<ProductionResultDto>>>('/mes/production-results', {
      params: query,
    })
  },

  /** ME040 — 指図サマリ取得（上部表示） */
  getWorkOrderSummary(no: string) {
    return http.get<any, Api<WorkOrderDto>>(
      `/mes/production-results/work-order/${encodeURIComponent(no)}`
    )
  },

  /** ME040 — 工程開始 */
  start(req: ProductionResultRequest) {
    return http.post<any, Api<{ resultNo: string }>>('/mes/production-results/start', req)
  },

  /** ME040 — 工程中断 */
  suspend(req: ProductionResultRequest) {
    return http.post<any, Api<{ resultNo: string }>>('/mes/production-results/suspend', req)
  },

  /** ME040 — 中断解除 */
  resume(req: ProductionResultRequest) {
    return http.post<any, Api<{ resultNo: string }>>('/mes/production-results/resume', req)
  },

  /** ME040 — 工程完了 */
  complete(req: ProductionResultRequest) {
    return http.post<any, Api<{ resultNo: string }>>('/mes/production-results/complete', req)
  },

  /** ME040 — 数量報告 */
  report(req: ProductionResultRequest) {
    return http.post<any, Api<{ resultNo: string }>>('/mes/production-results', req)
  },
}
