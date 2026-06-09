export type PlanAchievementGroupBy = 'product' | 'month' | 'customer'

export interface PlanAchievementQuery {
  dateFrom?: string
  dateTo?: string
  groupBy: PlanAchievementGroupBy
  productCd?: string
  customerCd?: string
  onlyCompleted?: boolean
}

export interface PlanAchievementRow {
  groupKey: string
  groupLabel: string
  workOrderCount: number
  plannedQty: number
  goodQty: number
  defectQty: number
  achievementRate: number
  defectRate: number
  onTargetCount: number
}

export interface PlanAchievementSummary {
  totalWorkOrders: number
  totalPlannedQty: number
  totalGoodQty: number
  totalDefectQty: number
  achievementRate: number
  defectRate: number
  onTargetCount: number
  rows: PlanAchievementRow[]
}

export interface ApiResult<T> {
  code: number
  message: string
  data: T
}
