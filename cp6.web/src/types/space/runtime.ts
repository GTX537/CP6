import type { SpaceDataSource } from './dataSource'

export interface SpaceRuntimeSource extends SpaceDataSource {
  adapterId: string
  receivedAtUtc: string
  delayMilliseconds: number
  clockSkewMilliseconds: number
}

export interface SpaceRuntimeInventoryItem {
  locationLogicalId: string
  wmsLogicalId: string
  spaceLocationCode: string
  wmsLocationCode: string
  codeMatches: boolean
  floorLogicalId: string
  floorCode: string
  floorName: string
  floorLevel: number
  physicalQuantity: number
  allocatedQuantity: number
  materialNumber: string | null
  lotNumber: string | null
  containerNumber: string | null
  ownerId: string | null
}

export interface SpaceRuntimeInventoryResponse {
  siteId: string
  publishedVersionId: string
  warehouseCode: string
  source: SpaceRuntimeSource
  items: SpaceRuntimeInventoryItem[]
}

export interface SpaceRuntimeInventoryLocateQuery {
  materialNumber?: string
  lotNumber?: string
  containerNumber?: string
}

export interface SpaceRuntimeInventoryLocateCriteria {
  materialNumber: string | null
  lotNumber: string | null
  containerNumber: string | null
}

export interface SpaceRuntimeInventoryLocateHit {
  locationLogicalId: string
  wmsLogicalId: string
  spaceLocationCode: string
  wmsLocationCode: string
  codeMatches: boolean
  floorLogicalId: string
  floorCode: string
  floorName: string
  floorLevel: number
  physicalQuantity: number
  allocatedQuantity: number
  materialNumbers: string[]
  lotNumbers: string[]
  containerNumbers: string[]
}

export interface SpaceRuntimeInventoryLocateResponse {
  siteId: string
  publishedVersionId: string
  warehouseCode: string
  source: SpaceRuntimeSource
  criteria: SpaceRuntimeInventoryLocateCriteria
  locationCount: number
  floorCount: number
  items: SpaceRuntimeInventoryLocateHit[]
}

export interface RuntimeLocationRef {
  locationLogicalId: string
  locationCode: string
}

export interface RuntimeStockItem {
  locationLogicalId: string
  locationCode: string
  binStatus: 0 | 1
  qty: number
  allocatedQty: number
  capacity: null
  topMaterial: string | null
  productKinds: number
}
