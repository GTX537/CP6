export type MaterialShortageStatus = 'OPEN' | 'RESOLVED' | 'DISMISSED'

export interface MaterialShortage {
  id: string
  workOrderNo: string
  relatedOutboundNo?: string
  productCd: string
  lotNo?: string
  requiredQty: number
  availableQty: number
  detectedAt: string
  resolvedAt?: string
  status: MaterialShortageStatus
  remark?: string
  creator?: string
  createDate?: string
  modifier?: string
  modifyDate?: string
}

export interface MaterialShortageQuery {
  status?: MaterialShortageStatus | ''
  workOrderNo?: string
  page?: number
  pageSize?: number
}

export interface MaterialShortageActionRequest {
  remark?: string
}

export interface MaterialShortagePaged {
  total?: number
  Total?: number
  pageIndex?: number
  PageIndex?: number
  pageSize?: number
  PageSize?: number
  items?: MaterialShortage[]
  Items?: MaterialShortage[]
}

export interface WmsApi<T> {
  code: number
  message: string
  data: T
}
