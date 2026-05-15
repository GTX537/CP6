import http from './http'
import type {
  WorkOrderDto,
  WorkOrderSearchQuery,
  ProductionResultDto,
  ProductionResultRequest,
  ProductionResultSearchQuery,
  ExpandFromOrderRequest,
  QualityInspectionDto,
  QualityInspectionSearchQuery,
  InspectionTemplateDto,
  DefectRecordDto,
  DefectRecordSearchQuery,
  DefectCategoryDto,
  PlanningBarDto,
  PlanningBoardQuery,
  PlanningKpiDto,
  RescheduleRequest,
  AutoArrangeRequest,
  MesDashboardSummaryDto,
  ProcessProgressDto,
  DelayAlertDto,
  DailyTrendDto,
  DefectTop5Dto,
  RecentCompletedDto,
  MachineHeatmapDto,
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

// ─────────────────────────────────────────────────────────────
// MSBBME060 / 070 — 品質検査
// ─────────────────────────────────────────────────────────────
export const qualityInspectionApi = {
  /** ME070 — 一覧 */
  search(query: QualityInspectionSearchQuery) {
    return http.get<any, Api<MesPagedResult<QualityInspectionDto>>>('/mes/inspections', {
      params: query,
    })
  },

  /** ME060 — 単条 */
  get(no: string) {
    return http.get<any, Api<QualityInspectionDto>>(`/mes/inspections/${encodeURIComponent(no)}`)
  },

  /** ME060 — 新規登録 */
  create(dto: QualityInspectionDto) {
    return http.post<any, Api<{ inspectionNo: string }>>('/mes/inspections', dto)
  },

  /** ME060 — 訂正 */
  update(no: string, dto: QualityInspectionDto) {
    return http.put<any, Api<unknown>>(`/mes/inspections/${encodeURIComponent(no)}`, dto)
  },

  /** ME060 — 検査項目テンプレート取得 */
  getTemplate(cd: string) {
    return http.get<any, Api<InspectionTemplateDto[]>>(
      `/mes/inspections/templates/${encodeURIComponent(cd)}`
    )
  },

  /** ME060 — 全テンプレートCD一覧 */
  getAllTemplateCodes() {
    return http.get<any, Api<string[]>>('/mes/inspections/templates')
  },
}

// ─────────────────────────────────────────────────────────────
// MSBBME080 — 不良品管理
// ─────────────────────────────────────────────────────────────
export const defectRecordApi = {
  search(query: DefectRecordSearchQuery) {
    return http.get<any, Api<MesPagedResult<DefectRecordDto>>>('/mes/defects', {
      params: query,
      paramsSerializer: { indexes: null },
    })
  },

  get(no: string) {
    return http.get<any, Api<DefectRecordDto>>(`/mes/defects/${encodeURIComponent(no)}`)
  },

  create(dto: DefectRecordDto) {
    return http.post<any, Api<{ defectNo: string }>>('/mes/defects', dto)
  },

  update(no: string, dto: DefectRecordDto) {
    return http.put<any, Api<unknown>>(`/mes/defects/${encodeURIComponent(no)}`, dto)
  },

  delete(no: string) {
    return http.delete<any, Api<unknown>>(`/mes/defects/${encodeURIComponent(no)}`)
  },

  /** 不良分類マスタ */
  getCategories() {
    return http.get<any, Api<DefectCategoryDto[]>>('/mes/defects/categories')
  },
}

// ─────────────────────────────────────────────────────────────
// MSBBME010 — 生産計画ボード
// ─────────────────────────────────────────────────────────────
export const planningBoardApi = {
  getBars(query: PlanningBoardQuery) {
    return http.get<any, Api<PlanningBarDto[]>>('/mes/planning-board', {
      params: query,
      paramsSerializer: { indexes: null },
    })
  },
  getKpi(query: PlanningBoardQuery) {
    return http.get<any, Api<PlanningKpiDto>>('/mes/planning-board/kpi', {
      params: query,
    })
  },
  reschedule(req: RescheduleRequest) {
    return http.put<any, Api<unknown>>('/mes/planning-board/reschedule', req)
  },
  autoArrange(req: AutoArrangeRequest) {
    return http.post<any, Api<{ changed: number }>>('/mes/planning-board/auto-arrange', req)
  },
}

// ─────────────────────────────────────────────────────────────
// MSBBME090 — MES ダッシュボード
// ─────────────────────────────────────────────────────────────
export const mesDashboardApi = {
  summary() {
    return http.get<any, Api<MesDashboardSummaryDto>>('/mes/dashboard/summary')
  },
  processProgress() {
    return http.get<any, Api<ProcessProgressDto[]>>('/mes/dashboard/process-progress')
  },
  delayAlerts(top = 50) {
    return http.get<any, Api<DelayAlertDto[]>>('/mes/dashboard/delay-alerts', { params: { top } })
  },
  dailyTrend(days = 30) {
    return http.get<any, Api<DailyTrendDto[]>>('/mes/dashboard/daily-trend', { params: { days } })
  },
  defectTop5() {
    return http.get<any, Api<DefectTop5Dto[]>>('/mes/dashboard/defect-top5')
  },
  recentCompleted(top = 10) {
    return http.get<any, Api<RecentCompletedDto[]>>('/mes/dashboard/recent-completed', { params: { top } })
  },
  machineHeatmap(days = 7) {
    return http.get<any, Api<MachineHeatmapDto[]>>('/mes/dashboard/machine-heatmap', { params: { days } })
  },
}
