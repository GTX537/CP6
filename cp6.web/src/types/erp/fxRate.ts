export interface FxRate {
  id?: string
  currencyCd: string
  rateDate: string
  rate: number
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
