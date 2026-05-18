// MES 製造執行系統 — TypeScript 型定義
// 対応：MSBBME010〜090

// ─────────────────────────────────────────────────────────────
// 製造指図ステータス（仕様書 §1.4）
// 0=下書き 1=確定済 2=発行済 3=着手中 4=完了 5=中断中 6=検査済 9=取消
// ─────────────────────────────────────────────────────────────
export const WORK_ORDER_STATUS = {
  Draft: 0,
  Confirmed: 1,
  Issued: 2,
  InProgress: 3,
  Completed: 4,
  Suspended: 5,
  Inspected: 6,
  Cancelled: 9,
} as const

export const WORK_ORDER_STATUS_OPTIONS = [
  { value: 0, label: '下書き', color: '#909399' },
  { value: 1, label: '確定済', color: '#409EFF' },
  { value: 2, label: '発行済', color: '#67C23A' },
  { value: 3, label: '着手中', color: '#E6A23C' },
  { value: 4, label: '完了', color: '#67C23A' },
  { value: 5, label: '中断中', color: '#F56C6C' },
  { value: 6, label: '検査済', color: '#36CFC9' },
  { value: 9, label: '取消', color: '#909399' },
]

export const PROCESS_STATUS_OPTIONS = [
  { value: 0, label: '未着手', color: '#909399' },
  { value: 1, label: '着手中', color: '#E6A23C' },
  { value: 2, label: '完了', color: '#67C23A' },
  { value: 3, label: '中断', color: '#F56C6C' },
  { value: 9, label: '取消', color: '#909399' },
]

export const PRIORITY_OPTIONS = [
  { value: 1, label: '通常' },
  { value: 2, label: '急ぎ' },
  { value: 3, label: '特急' },
]

export const RESULT_TYPE_OPTIONS = [
  { value: 1, label: '開始' },
  { value: 2, label: '中断' },
  { value: 3, label: '中断解除' },
  { value: 4, label: '完了' },
  { value: 5, label: '数量報告' },
]

// ─────────────────────────────────────────────────────────────
// DTO
// ─────────────────────────────────────────────────────────────

export interface WorkOrderProcessDto {
  id?: string
  workOrderNo: string
  processCd: string
  taskCd: string
  processName?: string | null
  sortOrder: number
  processStatus: number
  machineCd?: string | null
  wgCd?: string | null
  planStartTime?: string | null
  planEndTime?: string | null
  actualStartTime?: string | null
  actualEndTime?: string | null
  planQty?: number | null
  goodQty: number
  defectQty: number
  stdLossRate?: number | null
  leadTime?: number | null
  prevProcessCd?: string | null
  remarks?: string | null
}

export interface WorkOrderMaterialDto {
  id?: string
  workOrderNo: string
  processCd: string
  materialCd: string
  materialName?: string | null
  materialTypeDiv?: string | null
  planQty?: number | null
  actualQty: number
  unit?: string | null
  supplyStatus: number
  sortOrder: number
  remarks?: string | null
}

export interface WorkOrderDto {
  id?: string
  workOrderNo: string
  status: number
  orderNo1?: string | null
  orderNo2?: string | null
  orderNo3?: string | null
  webOrderNo?: string | null
  customerCd?: string | null
  customerName?: string | null
  productCd: string
  productName?: string | null
  productionQty: number
  completedQty: number
  defectQty: number
  deliveryDate?: string | null
  planStartDate?: string | null
  planEndDate?: string | null
  actualStartDate?: string | null
  actualEndDate?: string | null
  priority: number
  lotNo?: string | null
  baseCd?: string | null
  remarks?: string | null
  processCount: number
  completedProcessCount: number
  progressRate: number
  delayDays: number
  createDate?: string
  processes: WorkOrderProcessDto[]
  materials: WorkOrderMaterialDto[]
}

export interface WorkOrderSearchQuery {
  baseCd?: string
  workOrderNo?: string
  orderNo?: string
  productCd?: string
  customerCd?: string
  deliveryDateFrom?: string
  deliveryDateTo?: string
  planStartDateFrom?: string
  planStartDateTo?: string
  statuses?: number[]
  priority?: number
  processCd?: string
  wgCd?: string
  delayedOnly?: boolean
  pageIndex?: number
  pageSize?: number
}

export interface ProductionResultDto {
  id?: string
  resultNo: string
  workOrderNo: string
  productName?: string | null
  processCd: string
  processName?: string | null
  taskCd?: string | null
  resultType: number
  operatorCd: string
  operatorName?: string | null
  actualStartTime?: string | null
  actualEndTime?: string | null
  goodQty: number
  defectQty: number
  actualLossRate?: number | null
  defectReasonCd?: string | null
  suspendReasonCd?: string | null
  machineCd?: string | null
  resultNote?: string | null
  createDate?: string
}

export interface ProductionResultRequest {
  workOrderNo: string
  processCd: string
  taskCd?: string | null
  operatorCd: string
  operatorName?: string | null
  actualStartTime?: string | null
  actualEndTime?: string | null
  goodQty: number
  defectQty: number
  defectReasonCd?: string | null
  suspendReasonCd?: string | null
  machineCd?: string | null
  resultNote?: string | null
}

export interface ProductionResultSearchQuery {
  workOrderNo?: string
  processCd?: string
  operatorCd?: string
  dateFrom?: string
  dateTo?: string
  resultType?: number
  pageIndex?: number
  pageSize?: number
}

export interface ExpandFromOrderRequest {
  webOrderNo: string
  webOrderDetailNos?: number[]
  baseCd?: string
  priority?: number
}

export interface MesPagedResult<T> {
  total: number
  pageIndex: number
  pageSize: number
  items: T[]
}

// ─────────────────────────────────────────────────────────────
// MSBBME060 / 070 — 品質検査
// ─────────────────────────────────────────────────────────────

export const INSPECTION_TYPE_OPTIONS = [
  { value: 1, label: '受入' },
  { value: 2, label: '工程内' },
  { value: 3, label: '最終' },
  { value: 4, label: '出荷前' },
]

export const OVERALL_RESULT_OPTIONS = [
  { value: 1, label: '合格', color: '#67C23A' },
  { value: 2, label: '不合格', color: '#F56C6C' },
  { value: 9, label: '保留', color: '#E6A23C' },
]

export const DISPOSITION_OPTIONS = [
  { value: 1, label: '手直し' },
  { value: 2, label: '特別採用' },
  { value: 3, label: '返品' },
  { value: 4, label: '廃棄' },
]

export const ITEM_RESULT_OPTIONS = [
  { value: 1, label: '合格' },
  { value: 2, label: '不合格' },
  { value: 9, label: '保留' },
]

export interface QualityInspectionItemDto {
  id?: string
  inspectionNo: string
  itemSeqNo: number
  itemName?: string | null
  inspectionMethod?: string | null
  standardValue?: number | null
  upperLimit?: number | null
  lowerLimit?: number | null
  measuredValue?: number | null
  measuredText?: string | null
  result?: number | null
  unit?: string | null
  remarks?: string | null
}

export interface QualityInspectionDto {
  id?: string
  inspectionNo: string
  workOrderNo: string
  productCd?: string | null
  productName?: string | null
  processCd?: string | null
  processName?: string | null
  inspectionDate: string
  inspectorCd: string
  inspectorName?: string | null
  inspectionType: number
  templateCd?: string | null
  inspectionQty?: number | null
  sampleQty?: number | null
  overallResult?: number | null
  dispositionAction?: number | null
  judgmentReason?: string | null
  remarks?: string | null
  createDate?: string
  itemCount: number
  passCount: number
  passRate: number
  items: QualityInspectionItemDto[]
}

export interface InspectionTemplateDto {
  id?: string
  templateCd: string
  itemSeqNo: number
  templateName?: string | null
  itemName: string
  inspectionMethod?: string | null
  standardValue?: number | null
  upperLimit?: number | null
  lowerLimit?: number | null
  unit?: string | null
  processCd?: string | null
  requiredFlg: boolean
  activeFlg: boolean
}

export interface QualityInspectionSearchQuery {
  inspectionNo?: string
  workOrderNo?: string
  processCd?: string
  inspectorCd?: string
  inspectionType?: number
  overallResult?: number
  dateFrom?: string
  dateTo?: string
  pageIndex?: number
  pageSize?: number
}

// ─────────────────────────────────────────────────────────────
// MSBBME080 — 不良品管理
// ─────────────────────────────────────────────────────────────

export const DEFECT_STATUS_OPTIONS = [
  { value: 0, label: '起票済み', color: '#909399' },
  { value: 1, label: '分析中', color: '#409EFF' },
  { value: 2, label: '是正中', color: '#E6A23C' },
  { value: 3, label: '完了', color: '#67C23A' },
]

export interface DefectRecordDto {
  id?: string
  defectNo: string
  workOrderNo: string
  productCd?: string | null
  productName?: string | null
  processCd?: string | null
  processName?: string | null
  inspectionNo?: string | null
  occurDate: string
  reporterCd?: string | null
  reporterName?: string | null
  categoryCd: string
  categoryName?: string | null
  detailCd?: string | null
  detailName?: string | null
  defectQty: number
  defectDescription: string
  causeAnalysis?: string | null
  correctiveAction?: string | null
  assigneeCd?: string | null
  assigneeName?: string | null
  dueDate?: string | null
  completedDate?: string | null
  status: number
  remarks?: string | null
  createDate?: string
}

export interface DefectRecordSearchQuery {
  defectNo?: string
  workOrderNo?: string
  processCd?: string
  categoryCd?: string
  detailCd?: string
  statuses?: number[]
  assigneeCd?: string
  occurDateFrom?: string
  occurDateTo?: string
  pageIndex?: number
  pageSize?: number
}

export interface DefectCategoryDto {
  id?: string
  categoryCd: string
  detailCd: string
  categoryName?: string | null
  detailName?: string | null
  sortOrder: number
  activeFlg: boolean
}

// ─────────────────────────────────────────────────────────────
// MSBBME010 — 生産計画ボード
// ─────────────────────────────────────────────────────────────

export interface PlanningBarDto {
  id: string
  workOrderNo: string
  productCd: string
  productName?: string | null
  customerCd?: string | null
  processCd: string
  taskCd: string
  processName?: string | null
  machineCd?: string | null
  wgCd?: string | null
  sortOrder: number
  processStatus: number
  workOrderStatus: number
  priority: number
  planStartTime?: string | null
  planEndTime?: string | null
  actualStartTime?: string | null
  actualEndTime?: string | null
  deliveryDate?: string | null
  planQty?: number | null
  goodQty: number
  defectQty: number
}

export interface PlanningBoardQuery {
  dateFrom?: string
  dateTo?: string
  processCd?: string
  wgCd?: string
  machineCd?: string
  statuses?: number[]
  baseCd?: string
}

export interface PlanningKpiDto {
  totalOrders: number
  inProgressOrders: number
  completedOrders: number
  delayedOrders: number
  completionRate: number
  avgDelayDays: number
}

export interface RescheduleRequest {
  id: string
  planStartTime: string
  planEndTime: string
  machineCd?: string | null
}

export interface AutoArrangeRequest {
  baseDate: string
  wgCd?: string
  processCd?: string
  defaultHoursPerJob?: number
}

// ─────────────────────────────────────────────────────────────
// MSBBME090 — ダッシュボード
// ─────────────────────────────────────────────────────────────

export interface MesDashboardSummaryDto {
  inProgressCount: number
  completedCount: number
  totalGoodQty: number
  totalDefectQty: number
  defectRate: number
  delayedCount: number
}

export interface ProcessProgressDto {
  processCd: string
  processName?: string | null
  notStarted: number
  inProgress: number
  completed: number
}

export interface DelayAlertDto {
  workOrderNo: string
  productCd?: string | null
  productName?: string | null
  customerCd?: string | null
  planEndDate?: string | null
  deliveryDate?: string | null
  delayDays: number
  status: number
  progressRate: number
}

export interface DailyTrendDto {
  date: string
  goodQty: number
  defectQty: number
}

export interface DefectTop5Dto {
  categoryCd: string
  categoryName?: string | null
  count: number
  qty: number
}

export interface RecentCompletedDto {
  workOrderNo: string
  productCd?: string | null
  productName?: string | null
  productionQty: number
  completedQty: number
  actualEndDate?: string | null
}

export interface MachineHeatmapDto {
  machineCd: string
  hour: number
  count: number
}

// ─────────────────────────────────────────────────────────────
// MES Phase 4 — 設備管理 / OEE
// ─────────────────────────────────────────────────────────────

export const MACHINE_STATUS_OPTIONS = [
  { value: 0, label: '停止', color: '#909399', light: 'gray' },
  { value: 1, label: '稼働中', color: '#67C23A', light: 'green' },
  { value: 2, label: '故障', color: '#F56C6C', light: 'red' },
  { value: 3, label: 'メンテ', color: '#E6A23C', light: 'yellow' },
  { value: 4, label: '段取り', color: '#409EFF', light: 'blue' },
]

export const DOWNTIME_TYPE_OPTIONS = [
  { value: 1, label: '計画停止' },
  { value: 2, label: '故障' },
  { value: 3, label: '材料待ち' },
  { value: 4, label: '作業者不在' },
  { value: 9, label: 'その他' },
]

export interface MachineDto {
  id?: string
  machineCd: string
  machineName: string
  machineType?: string | null
  processCd?: string | null
  wgCd?: string | null
  baseCd?: string | null
  status: number
  plannedRunMinutesPerDay: number
  standardCycleSec?: number | null
  capacityPerHour?: number | null
  installDate?: string | null
  lastMaintenanceDate?: string | null
  nextMaintenanceDate?: string | null
  manufacturer?: string | null
  model?: string | null
  remarks?: string | null
  activeFlg: boolean
  currentWorkOrderNo?: string | null
  currentProcessCd?: string | null
  todayOee?: number | null
}

export interface MachineDowntimeDto {
  id?: string
  downtimeNo: string
  machineCd: string
  machineName?: string | null
  workOrderNo?: string | null
  startTime: string
  endTime?: string | null
  downtimeMinutes?: number | null
  downtimeType: number
  reasonCd?: string | null
  description?: string | null
  operatorCd?: string | null
  recoveryOperatorCd?: string | null
  remarks?: string | null
  createDate?: string
}

export interface MachineSearchQuery {
  machineCd?: string
  processCd?: string
  wgCd?: string
  baseCd?: string
  statuses?: number[]
  activeOnly?: boolean
}

export interface DowntimeSearchQuery {
  machineCd?: string
  dateFrom?: string
  dateTo?: string
  downtimeType?: number
  onlyOpen?: boolean
  pageIndex?: number
  pageSize?: number
}

export interface OeeDailyDto {
  id?: string
  oeeDate: string
  machineCd: string
  machineName?: string | null
  plannedRunMinutes: number
  actualRunMinutes: number
  downtimeMinutes: number
  goodQty: number
  defectQty: number
  availability: number
  performance: number
  quality: number
  oee: number
  remarks?: string | null
}

export interface OeeSearchQuery {
  machineCd?: string
  dateFrom?: string
  dateTo?: string
}

export interface OeeRecalcRequest {
  targetDate: string
  machineCd?: string
}
