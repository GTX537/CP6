export interface OutboundRoutingRule {
  id?: string
  ruleName: string
  sortOrder: number
  customerCd?: string | null
  productCdPrefix?: string | null
  outboundType?: number | null
  targetWarehouseCd: string
  enabled: boolean
  remarks?: string | null
  creator?: string
  createDate?: string
  modifier?: string
  modifyDate?: string
}

export interface WmsApi<T> {
  code: number
  message: string
  data: T
}
