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

export interface SpacePersonnelCurrentPage {
  siteId: string
  asOfUtc: string
  freshnessThresholdSeconds: number
  items: SpacePersonnelCurrent[]
  nextCursor: string | null
}

export interface SpacePersonnelCurrent {
  sourceId: string
  sourceKind: 'Real' | 'Simulated'
  personExternalId: string
  workState: 'Unknown' | 'Offline' | 'Idle' | 'Busy' | 'Break'
  floorLogicalId: string | null
  locationLogicalId: string | null
  xMillimeters: number | null
  yMillimeters: number | null
  zMillimeters: number | null
  accuracyMillimeters: number | null
  positionOccurredAtUtc: string | null
  positionReceivedAtUtc: string | null
  positionEventId: string | null
  positionSourceEventId: string | null
  workStateOccurredAtUtc: string | null
  workStateReceivedAtUtc: string | null
  workStateEventId: string | null
  workStateSourceEventId: string | null
  positionAgeMilliseconds: number | null
  workStateAgeMilliseconds: number | null
  hasPosition: boolean
  positionIsStale: boolean
  workStateIsStale: boolean
  isSimulated: boolean
}

export interface SpacePersonnelTrajectoryResponse {
  siteId: string
  sourceId: string
  sourceKind: 'Real' | 'Simulated'
  personExternalId: string
  fromUtc: string
  toUtc: string
  retentionCutoffUtc: string
  items: SpacePersonnelTrajectoryPoint[]
  nextCursor: string | null
}

export interface SpacePersonnelTrajectoryPoint {
  eventId: string
  sourceEventId: string
  floorLogicalId: string | null
  locationLogicalId: string | null
  xMillimeters: number | null
  yMillimeters: number | null
  zMillimeters: number | null
  accuracyMillimeters: number | null
  sourceSequence: number | null
  occurredAtUtc: string
  receivedAtUtc: string
  ingestDelayMilliseconds: number
}
