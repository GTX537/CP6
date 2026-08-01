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

export interface SpaceRuntimeTaskItem {
  taskId: string
  taskType: string
  status: string
  sequenceNo: number
  locationLogicalId: string
  wmsLogicalId: string
  spaceLocationCode: string
  wmsLocationCode: string
  codeMatches: boolean
  floorLogicalId: string
  floorCode: string
  floorName: string
  floorLevel: number
  zoneLogicalId: string | null
  zoneCode: string | null
  rackLogicalId: string | null
  rackCode: string | null
  anchorXMillimeters: number | null
  anchorYMillimeters: number | null
  anchorZMillimeters: number | null
  quantity: number | null
  materialNumber: string | null
}

export interface SpaceRuntimeTaskFloor {
  floorLogicalId: string
  floorCode: string
  floorName: string
  floorLevel: number
  elevationMillimeters: number
  heightMillimeters: number
  stopCount: number
  totalQuantity: number
}

export interface SpaceRuntimeTaskWorkload {
  floorLogicalId: string
  floorCode: string
  zoneLogicalId: string | null
  zoneCode: string | null
  stopCount: number
  totalQuantity: number
}

export interface SpaceRuntimeTaskAisle {
  floorLogicalId: string
  zoneLogicalId: string
  aisleLogicalId: string
  aisleCode: string
  centerlineJson: string
}

export interface SpaceRuntimeTaskPathResponse {
  siteId: string
  publishedVersionId: string
  warehouseCode: string
  source: SpaceRuntimeSource
  taskId: string
  stopCount: number
  locatedStopCount: number
  floorCount: number
  zoneCount: number
  floorTransitionCount: number
  zoneTransitionCount: number
  totalQuantity: number
  crossFloor: boolean
  crossZone: boolean
  actualStops: SpaceRuntimeTaskItem[]
  floors: SpaceRuntimeTaskFloor[]
  workloads: SpaceRuntimeTaskWorkload[]
  aisles: SpaceRuntimeTaskAisle[]
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
