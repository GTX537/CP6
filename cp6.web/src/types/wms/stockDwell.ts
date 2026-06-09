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
  totalQty: number
  totalValue: number
  bucket0To30Qty: number
  bucket31To60Qty: number
  bucket61To90Qty: number
  bucketOver90Qty: number
  oldestReceiveDate?: string
  oldestAgeDays: number
}

export interface StockDwellSummary {
  asOfDate: string
  groupBy: StockDwellGroupBy
  totalQty: number
  totalValue: number
  over90Qty: number
  over90Value: number
  rows: StockDwellRow[]
}
