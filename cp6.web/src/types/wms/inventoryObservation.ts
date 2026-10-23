export interface ExpiryObservationQuery {
  asOfDate: string
  days: number
  warehouseCd?: string
  page: number
  pageSize: number
}
export interface ExpiryObservationRow {
  stockId: string
  warehouseCd: string
  locationCd: string
  productCd: string
  lotNo: string
  physicalQty: string
  availableQty: string
  unitPrice: string | null
  referenceValue: string | null
  expiryDate: string
  daysUntilExpiry: number
  unitCd: string | null
}
export interface ExpiryObservation {
  version: 'INVENTORY-OBSERVATION-OWNER/1'
  asOfDate: string
  days: number
  total: number
  page: number
  pageSize: number
  unitPriceReadable: boolean
  referenceReadable: boolean
  valuationStatus: 'UNKNOWN'
  currency: null
  rows: ExpiryObservationRow[]
}
