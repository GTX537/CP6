import type { Vector3 } from 'three'

export interface PickResult {
  kind: 'location' | 'rack' | 'zone' | 'marker'
  locationId?: string
  locationCode?: string
  rackId?: string
  rackCode?: string
  zoneId?: string
  worldPoint: Vector3
  dataPoint: { x: number; y: number; z: number }
}

export interface LocateResult {
  locationId: string
  locationCode?: string
  floorId: string
  absX: number
  absY: number
  absZ: number
  placed: boolean
  status: number
}

export interface LocationDetail {
  locationId: string
  locationCode: string
  path: {
    siteCode?: string
    floorLevel: number
    zoneCode?: string
    aisleCode?: string | null
    rackCode?: string
    col: number
    level: number
    depth: number
  }
  status: number
  codeOrigin: number
  absX: number
  absY: number
  absZ: number
}
