export const materialShortageVersion = 'MATERIAL-SHORTAGE-OWNER/1'
export type MaterialShortageStatus = 'OPEN' | 'RESOLVED' | 'DISMISSED'
export interface MaterialShortage {
  id: string
  rowVersion: string
  workOrderNo: string
  relatedOutboundNo?: string | null
  productCd: string
  lotNo?: string | null
  requiredQty: string
  availableQty: string
  shortQty: string
  detectedAt: string
  resolvedAt?: string | null
  status: MaterialShortageStatus
  remark?: string | null
}
export interface MaterialShortageQuery {
  status?: MaterialShortageStatus | ''
  workOrderNo?: string
  page?: number
  pageSize?: number
}
export interface MaterialShortageActionRequest {
  rowVersion: string
  setRemark: boolean
  remark?: string | null
}
export interface MaterialShortagePaged {
  version: typeof materialShortageVersion
  total: number
  openCount: number
  page: number
  pageSize: number
  remarkReadable: boolean
  remarkWritable: boolean
  rows: MaterialShortage[]
}
export interface WmsApi<T> { code: number; message: string; data: T }
