// Space 编辑器 VO 类型 — 镜像后端 DTOs（ch01 §E-1）

export interface FloorVO {
  id: string
  siteId: string
  level: number
  floorCode: string
  floorName: string
  height: number
  underlayImage?: string | null
  underlayScale?: number | null
  underlayOffsetX: number
  underlayOffsetY: number
  originX: number
  originY: number
}

export interface ZoneVO {
  id: string
  floorId: string
  zoneCode: string
  zoneName: string
  zoneType: number
  polygon: string
  color?: string | null
  enable?: boolean
}

export interface AisleVO {
  id: string
  zoneId: string
  aisleCode: string
  polygon: string
  centerline: string
}

export interface RackVO {
  id: string
  zoneId: string
  aisleId?: string | null
  floorId: string
  templateId?: string | null
  rackCode: string
  x: number
  y: number
  z: number
  rotationZ: number
  cols: number
  levels: number
  depthCount: number
  cellW: number
  cellH: number
  cellD: number
  enable?: boolean
  rowVersion?: string | null  // byte[] RowVersion serialized as base64 in JSON
}

export interface LocationVO {
  id: string
  rackId: string
  floorId: string
  locationCode: string | null
  codeOrigin: number
  col: number
  level: number
  depth: number
  absX: number
  absY: number
  absZ: number
  sizeW: number
  sizeH: number
  sizeD: number
  placed: boolean
  status: number
  version: number
}

export interface MarkerVO {
  id: string
  floorId: string
  x: number
  y: number
  z: number
  markerType: number
  text: string
  refRackId?: string | null
}

export interface EditorScene {
  floor: FloorVO
  zones: ZoneVO[]
  aisles: AisleVO[]
  racks: RackVO[]
  locations: LocationVO[]
  markers: MarkerVO[]
}

export interface TemplateVO {
  id?: string
  templateCode: string
  templateName: string
  templateType: number
  params: string
}

export interface SceneSaveDto {
  racks?: RackVO[]
  aisles?: AisleVO[]
  zones?: ZoneVO[]
  markers?: MarkerVO[]
  locations?: LocationVO[]
  deletes?: {
    racks?: string[]
    aisles?: string[]
    zones?: string[]
    markers?: string[]
  }
}

export interface UnplacedLocationDto {
  id: string
  locationCode: string
  status: number
}

export type Envelope<T> = { code: number; message: string; data: T }
