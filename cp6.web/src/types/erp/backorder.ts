export interface BackorderQueueQuery {
  customerCd?: string
  dateFrom?: string
  dateTo?: string
}

export interface BackorderQueueItem {
  webOrderNo: string
  customerCd: string
  customerName?: string
  detailNo: number
  productCd: string
  orderedQty: number
  shippedQty: number
  backorderQty: number
  remainingQty: number
  lastShipDate?: string
}

export interface BackorderActionRequest {
  reason: string
}

export interface BackorderActionResult {
  webOrderNo: string
  detailNo: number
  backorderQty: number
  remainingQty: number
  newWebOrderNo?: string
}

export interface ApiResult<T> {
  code: number
  message: string
  data: T
}
