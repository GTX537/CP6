import type { WmsStockDto } from './overlay'

export interface AnalyticsConfig {
  windowDays: number
  metric: 'quantity' | 'frequency'
  thresholdA: number
  thresholdB: number
  staleAfterHours: number
  scheduledHourLocal: number
  enableScheduledSnapshot: boolean
}

export interface AnalyticsWarning {
  code: string
  message: string
  locationCode: string | null
  severity: 'warning' | 'error' | string
}

export interface UtilizationItem {
  locationId: string
  locationCode: string
  rackId: string | null
  rackCode: string | null
  zoneId: string | null
  zoneCode: string | null
  zoneName: string | null
  zoneType: number | null
  qty: number | null
  capacity: number | null
  capacityUom: number | null
  capacitySource: string | null
  utilization: number | null
  binStatus: number | null
  stockAvailable: boolean
  includedInAggregate: boolean
  warningCode: string | null
}

export interface UtilizationAggregate {
  entityId: string | null
  code: string
  name: string
  capacityUom: number
  locationCount: number
  qty: number
  capacity: number
  utilization: number
  overCapacityCount: number
}

export interface UtilizationResponse {
  floorId: string
  siteId: string
  warehouseCd: string
  timestamp: string
  stockAvailable: boolean
  items: UtilizationItem[]
  racks: UtilizationAggregate[]
  zones: UtilizationAggregate[]
  warnings: AnalyticsWarning[]
}

export interface StorageTypeItem {
  locationId: string
  locationCode: string
  zoneId: string | null
  zoneCode: string | null
  zoneName: string | null
  zoneType: number
  typeKey: string
  color: string
}

export interface StorageTypeSummary {
  zoneType: number
  typeKey: string
  color: string
  locationCount: number
  percentage: number
}

export interface StorageTypeResponse {
  floorId: string
  totalLocations: number
  items: StorageTypeItem[]
  summary: StorageTypeSummary[]
}

export interface AbcSnapshotMeta {
  snapshotId: string
  siteId: string
  warehouseCd: string
  calculatedAt: string
  windowFrom: string
  windowTo: string
  windowDays: number
  metric: string
  thresholdA: number
  thresholdB: number
  itemCount: number
  trigger: string
}

export interface AbcProduct {
  productCd: string
  outCount: number
  outQty: number
  score: number
  cumulativeRatio: number
  abcRank: 'A' | 'B' | 'C'
}

export interface AbcLocation {
  locationId: string
  locationCode: string
  qty: number | null
  productCodes: string[]
  abcRank: 'A' | 'B' | 'C' | null
  absX: number | null
  absY: number | null
}

export interface SpacePoint { x: number; y: number }

export interface AbcResponse {
  floorId: string
  siteId: string
  hasSnapshot: boolean
  isStale: boolean
  stockAvailable: boolean
  snapshot: AbcSnapshotMeta | null
  products: AbcProduct[]
  items: AbcLocation[]
  shippingTargets: SpacePoint[]
  averageAShippingDistanceMm: number | null
  distanceMethod: string | null
  warnings: AnalyticsWarning[]
}

export interface TowerUtilization {
  capacityUom: number
  qty: number
  capacity: number
  utilization: number
  locationCount: number
}

export interface TowerFloor {
  floorId: string
  floorCode: string
  floorName: string
  level: number
  totalLocations: number
  occupiedLocations: number
  alertCount: number
  locations: Array<{ locationCode: string; utilization: number | null }>
}

export interface ControlTower {
  siteId: string
  siteCode: string
  siteName: string
  warehouseCd: string
  generatedAt: string
  stockAvailable: boolean
  totalLocations: number
  occupiedLocations: number
  emptyLocations: number
  fullOrOverCapacityLocations: number
  anomalyCount: number
  todayInboundCount: number
  todayOutboundCount: number
  abcProductCounts: Record<'A' | 'B' | 'C', number>
  abcSnapshot: AbcSnapshotMeta | null
  utilizationByUom: TowerUtilization[]
  floors: TowerFloor[]
  alerts: AnalyticsWarning[]
}

export interface StockDelta {
  items: WmsStockDto[]
  requested: number
  matched: number
  ts: string
}
