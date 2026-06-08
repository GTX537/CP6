export type OrderTraceStatus = 'PENDING' | 'SUCCESS' | 'SKIPPED' | 'FAILED' | 'DEAD' | 'COMPENSATED'

export interface OrderTraceTimelineItem {
  eventTime: string
  eventKind: string
  sourceModule: string
  targetModule: string
  hookName: string
  sourceNo: string
  targetNo?: string
  status: OrderTraceStatus | string
  message?: string
  correlationId: string
}

export interface OrderTraceSummary {
  totalEvents: number
  successCount: number
  failedCount: number
  skippedCount: number
  deadCount: number
  distinctCorrelationIds: number
}

export interface OrderTrace {
  webOrderNo: string
  customerName?: string
  orderDate?: string
  summary: OrderTraceSummary
  timeline: OrderTraceTimelineItem[]
}

export interface ApiResult<T> {
  code: number
  message: string
  data: T
}
