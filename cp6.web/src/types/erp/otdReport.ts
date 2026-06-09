export type OtdReportGroupBy = 'customer' | 'month'

export interface OtdReportQuery {
  dateFrom?: string
  dateTo?: string
  groupBy: OtdReportGroupBy
  customerCd?: string
}

export interface OtdReportRow {
  groupKey: string
  groupLabel: string
  totalShippedOrders: number
  onTimeCount: number
  lateCount: number
  onTimeRate: number
  avgLateDays: number
}

export interface OtdReportSummary {
  totalShippedOrders: number
  onTimeCount: number
  lateCount: number
  onTimeRate: number
  rows: OtdReportRow[]
}

export interface ApiResult<T> {
  code: number
  message: string
  data: T
}
