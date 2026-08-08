// Space 编辑器 VO 类型 — 镜像后端 DTOs（ch01 §E-1）

import type { SpaceDataSource } from './dataSource'

export interface SiteVO {
  id?: string
  siteCode: string
  siteName: string
  address?: string | null
  lng?: number | null
  lat?: number | null
  enable: boolean
  warehouseCd?: string | null
}

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
  source: SpaceDataSource
  floor: FloorVO
  zones: ZoneVO[]
  aisles: AisleVO[]
  racks: RackVO[]
  locations: LocationVO[]
  markers: MarkerVO[]
}

export interface SceneExport {
  source: SpaceDataSource
  meta: Record<string, unknown>
  zones: unknown[]
  aisles: unknown[]
  racks: unknown[]
}

export interface TemplateVO {
  id?: string
  templateCode: string
  templateName: string
  templateType: number
  params: string
}

/** 差量删除的实体种类——决定 id 进 deletes 的哪个桶（后端 Deletes 五列表镜像） */
export type DeleteKind = 'rack' | 'aisle' | 'zone' | 'marker' | 'location'

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
    locations?: string[]
  }
}

export interface UnplacedLocationDto {
  id: string
  locationCode: string
  status: number
}

export type Envelope<T> = { code: number; message: string; data: T }

// ── 生命周期（ch03 编码规则 / ch04 发布）VO —— 镜像后端 CodeRuleDtos.cs ──

/** 编码段定义（CodeRuleDtos.cs:7，11 字段 camelCase 对齐）。 */
export interface CodeSegmentDef {
  key: string
  name: string
  source: string
  width: number
  pad: string
  start: number
  step: number
  sep: string
  upper: boolean
  fixedValue: string
  optional: boolean
}

/** 编码规则 VO（segments 归一后恒为数组；对应后端 CodeRuleDto）。 */
export interface CodeRuleVO {
  id?: string
  ruleName: string
  scopeType: number
  scopeId?: string | null
  segments: CodeSegmentDef[]
  isDefault: boolean
}

/** 实时预览响应（ch03 §8）。 */
export interface CodePreviewResp {
  structure: { key: string; name: string; source: string; optional: boolean }[]
  samples: string[]
  variableLen: { withAisle: string; withoutAisle: string }
  precheck: { ok: boolean; errors: string[] }
}

/** 发布前编码预检响应（ch03 §9.2）。 */
export interface CodePrecheckResp {
  emptyCodeCount: number
  duplicateGroups: string[][]
  precheckErrors: string[]
  unplacedDraftCount: number
}

/** SPACE→WMS 集成事件 VO（ch04 §2）。 */
export interface SpaceEventVO {
  id: string
  hookName: string
  sourceNo: string
  targetModule: string
  status: string
  attempts: number
  createDate: string
  correlationId: string
  jobId?: string | null
  publishAttemptId?: string | null
  safeErrorCode?: string | null
}
