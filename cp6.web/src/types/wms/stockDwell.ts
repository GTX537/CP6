export type StockDwellGroupBy = 'product' | 'customer'

export interface StockDwellQuery {
  groupBy: StockDwellGroupBy
  warehouseCd?: string
  productCd?: string
  ownerCd?: string
  asOfDate?: string
}

export interface StockDwellRow {
  groupKey: string
  groupLabel: string
  totalQty: string
  referenceValue: string | null
  bucket0To30Qty: string
  bucket31To60Qty: string
  bucket61To90Qty: string
  bucketOver90Qty: string
  oldestReceiveDate?: string
  oldestAgeDays: number
}

export interface StockDwellSummary {
  version: 'INVENTORY-OBSERVATION-OWNER/1'
  referenceReadable: boolean
  customerNameReadable: boolean
  missingPriceCount: number | null
  valuationStatus: 'UNKNOWN'
  currency: null
  asOfDate: string
  groupBy: StockDwellGroupBy
  totalQty: string
  referenceValue: string | null
  over90Qty: string
  over90ReferenceValue: string | null
  rows: StockDwellRow[]
}
