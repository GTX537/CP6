import http from '../http'
import type { ApiResp } from '@/types/fin/fin'
import type {
  Budget, BudgetVersion, BudgetLineGridRow, BudgetLineDto,
  BudgetVsActualReport, BudgetWarningDto, BudgetImportPreviewResult,
  CostCenter,
} from '@/types/fin/budget'
import type { JournalEntry } from '@/types/fin/fin'

// ── 预算方案 ──
export const budgetApi = {
  /** 列表：所有方案（按财年降序）*/
  list() {
    return http.get<any, ApiResp<Budget[]>>('/fin/budget')
  },
  /** 创建方案 */
  create(d: Pick<Budget, 'name' | 'fiscalYear' | 'description'>) {
    return http.post<any, ApiResp<Budget>>('/fin/budget', d)
  },
  /** 更新方案基本信息 */
  update(id: string, d: { name: string; description?: string | null }) {
    return http.put<any, ApiResp<unknown>>(`/fin/budget/${id}`, d)
  },
  /** 停用方案 */
  deactivate(id: string) {
    return http.post<any, ApiResp<unknown>>(`/fin/budget/${id}/deactivate`)
  },
}

// ── 预算版本 ──
export const budgetVersionApi = {
  /** 列出某方案下所有版本 */
  list(budgetId: string) {
    return http.get<any, ApiResp<BudgetVersion[]>>(`/fin/budget/${budgetId}/versions`)
  },
  /** 新建版本（可选：从已有版本或上年实际复制）*/
  create(budgetId: string, d: {
    name: string
    copyFromVersionId?: string | null
    copyFromActualFiscalYear?: number | null
  }) {
    return http.post<any, ApiResp<BudgetVersion>>(`/fin/budget/${budgetId}/versions`, d)
  },
  /** 更新版本（仅草稿）：名称/控制模式/控制口径 */
  update(versionId: string, d: {
    name: string
    defaultControlMode: number
    defaultControlBasis: number
  }) {
    return http.put<any, ApiResp<unknown>>(`/fin/budget/versions/${versionId}`, d)
  },
  /** 提交审批 */
  submit(versionId: string) {
    return http.post<any, ApiResp<unknown>>(`/fin/budget/versions/${versionId}/submit`)
  },
  /** 激活已批准版本 */
  activate(versionId: string) {
    return http.post<any, ApiResp<unknown>>(`/fin/budget/versions/${versionId}/activate`)
  },
  /** 删除草稿版本 */
  remove(versionId: string) {
    return http.delete<any, ApiResp<unknown>>(`/fin/budget/versions/${versionId}`)
  },
}

// ── 预算行 ──
export const budgetLineApi = {
  /** 列出某版本下所有预算行（含 12 期）*/
  list(versionId: string) {
    return http.get<any, ApiResp<BudgetLineGridRow[]>>('/fin/budget/lines', { params: { versionId } })
  },
  /** 新建或更新预算行（按维度桶 upsert）*/
  upsert(d: BudgetLineDto) {
    return http.post<any, ApiResp<unknown>>('/fin/budget/lines', d)
  },
  /** 删除预算行 */
  remove(lineId: string, lineRowVersion: string, versionRowVersion: string) {
    return http.delete<any, ApiResp<unknown>>(`/fin/budget/lines/${lineId}`, {
      params: { lineRowVersion, versionRowVersion },
    })
  },
  /** Excel 导入预览（multipart file）*/
  importPreview(versionId: string, file: File) {
    const fd = new FormData(); fd.append('file', file)
    return http.post<any, ApiResp<BudgetImportPreviewResult>>(
      `/fin/budget/lines/import/preview?versionId=${versionId}`, fd)
  },
  /** Excel 导入确认（multipart file）*/
  importConfirm(versionId: string, versionRowVersion: string, file: File) {
    const fd = new FormData(); fd.append('file', file)
    return http.post<any, ApiResp<unknown>>(
      '/fin/budget/lines/import/confirm', fd, { params: { versionId, versionRowVersion } })
  },
}

// ── 成本中心查找 ──
export const costCenterApi = {
  /** 成本中心列表（A5 D2 lookup）*/
  list(includeInactive = false) {
    return http.get<any, ApiResp<CostCenter[]>>('/fin/cost-center', { params: { includeInactive } })
  },
}

// ── 报告 ──
export const budgetReportApi = {
  /** 预算 vs 实际对比报告 */
  vsActual(p: { fiscalYear: number; versionId?: string | null; from?: number; to?: number }) {
    return http.get<any, ApiResp<BudgetVsActualReport>>('/fin/budget/report/vs-actual', { params: p })
  },
  /** 过账前预检（全量 Warn+Block 评估）*/
  preCheck(entry: JournalEntry) {
    return http.post<any, ApiResp<BudgetWarningDto[]>>('/fin/budget/report/pre-check', entry)
  },
}
