export type { ApiResp } from '@/types/fin/fin'

// ── 枚举 ──
/** 预算版本状态: 0草稿 1审批中 2已批准 3已驳回 4已归档 */
export type BudgetVersionStatus = 0 | 1 | 2 | 3 | 4
/** 控制模式: 0不控制 1预警 2硬控制 */
export type BudgetControlMode = 0 | 1 | 2
/** 控制口径: 0累计(YTD) 1单期 */
export type BudgetControlBasis = 0 | 1

export const BUDGET_VERSION_STATUS_LABEL: Record<number, string> = {
  0: '草稿', 1: '审批中', 2: '已批准', 3: '已驳回', 4: '已归档',
}
export const BUDGET_VERSION_STATUS_TAG: Record<number, '' | 'info' | 'success' | 'warning' | 'danger'> = {
  0: 'info', 1: 'warning', 2: 'success', 3: 'danger', 4: '',
}
export const BUDGET_CONTROL_MODE_LABEL: Record<number, string> = {
  0: '不控制', 1: '预警', 2: '硬控制',
}
export const BUDGET_CONTROL_BASIS_LABEL: Record<number, string> = {
  0: '累计', 1: '单期',
}

// ── 实体类型 ──

/** 预算方案（按财年，每财年唯一）*/
export interface Budget {
  id?: string
  no?: string
  name: string
  fiscalYear: number
  scope?: number           // BudgetScope: 1 PnL
  description?: string | null
  isActive: boolean
  creator?: string | null
  createDate?: string | null
  modifyDate?: string | null
}

/** 预算版本 */
export interface BudgetVersion {
  id?: string
  budgetId: string
  versionNo: number
  name: string
  status: BudgetVersionStatus
  isActive: boolean
  defaultControlMode: BudgetControlMode
  defaultControlBasis: BudgetControlBasis
  approvalInstanceId?: string | null
  approvalRef?: string | null
  submittedAt?: string | null
  submittedBy?: string | null
  approvedAt?: string | null
  approvedBy?: string | null
  rejectReason?: string | null
  rowVersion?: string | null
  creator?: string | null
  createDate?: string | null
  modifyDate?: string | null
}

/** 预算行网格行（含 12 期分摊）*/
export interface BudgetLineGridRow {
  id: string
  versionId?: string
  accountId: string
  costCenterId?: string | null
  costObjectType?: string | null
  costObjectId?: string | null
  annualAmount: number
  controlMode?: BudgetControlMode | null
  controlBasis?: BudgetControlBasis | null
  memo?: string | null
  periods: number[]         // length 12, index 0 = period 1
  rowVersion?: string | null
}

/** 新建/更新预算行 DTO（镜像 BudgetLineDto）*/
export interface BudgetLineDto {
  id?: string | null
  versionId: string
  accountId: string
  costCenterId?: string | null
  costObjectType?: string | null
  costObjectId?: string | null
  annualAmount: number
  spreadMode: 'even' | 'seasonal' | 'manual'
  periods?: number[] | null   // 12 values for manual/seasonal
  controlMode?: BudgetControlMode | null
  controlBasis?: BudgetControlBasis | null
  memo?: string | null
  rowVersion?: number[] | string | null
}

/** 预算 vs 实际对比行 */
export interface BudgetVsActualRow {
  accountId: string
  accountCode: string
  accountName: string
  costCenterId?: string | null
  costObjectType?: string | null
  costObjectId?: string | null
  budget: number
  actual: number
  variance: number
  variancePct?: number | null
  budgetPeriods: number[]     // length 12
  actualPeriods: number[]     // length 12
  isUnbudgeted: boolean
}

/** 预算 vs 实际对比报告 */
export interface BudgetVsActualReport {
  fiscalYear: number
  versionId?: string | null
  rows: BudgetVsActualRow[]
  totalBudget: number
  totalActual: number
}

/** 过账预检警告行（W 级：IsBlock=false 为预警，true 为即将硬控制）*/
export interface BudgetWarningDto {
  accountCode: string
  budget: number
  used: number
  incoming: number
  isBlock: boolean
}

/** 导入预览行 */
export interface BudgetImportRow {
  rowNo: number
  accountCode: string
  costCenterCode?: string | null
  costObjectType?: string | null
  costObjectId?: string | null
  periods: number[]           // length 12
  ok: boolean
  error?: string | null
}

/** 导入预览结果 */
export interface BudgetImportPreviewResult {
  rows: BudgetImportRow[]
  hasFatal: boolean
}
