// cp6.web/src/types/space/advanced.ts —— 对齐 SpaceAdvancedController 响应
import type { SpaceDataSource } from './dataSource'

export interface PickStopVO {
  seq: number
  locationCode: string
  qty: number
  materialNo: string | null
  absX: number | null
  absY: number | null
  absZ: number | null
}

export interface AisleCenterlineVO {
  aisleCode: string
  centerline: string   // JSON [[x,y],...]（mm）
}

export interface FloorPickPath {
  taskNo: string
  stops: PickStopVO[]
  aisles: AisleCenterlineVO[]
  source: SpaceDataSource
}

export interface WorkloadItem {
  locationCode: string
  opCount: number
}

export interface FloorWorkload {
  items: WorkloadItem[]
  from: string
  to: string
  source: SpaceDataSource
}

export interface SiteFloorVO { floorId: string; floorCode: string; level: number; height: number; z: number }
export interface SitePickStopVO { seq: number; locationCode: string; qty: number; materialNo: string | null; floorId: string | null; absX: number | null; absY: number | null; absZ: number | null }
export interface SiteAisleVO { floorId: string; aisleCode: string; centerline: string }
export interface SiteConnectorVO { connectorCode: string; type: number; waitSec: number; travelSecPerFloor: number; stops: Array<{ floorId: string; x: number; y: number }> }
export interface SitePickPath { taskNo: string; floors: SiteFloorVO[]; stops: SitePickStopVO[]; aisles: SiteAisleVO[]; connectors: SiteConnectorVO[]; source: SpaceDataSource }
