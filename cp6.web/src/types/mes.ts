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
