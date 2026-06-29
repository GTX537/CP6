// cp6.web/src/types/space/advanced.ts —— 对齐 SpaceAdvancedController 响应
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
}

export interface WorkloadItem {
  locationCode: string
  opCount: number
}

export interface FloorWorkload {
  items: WorkloadItem[]
  from: string
  to: string
}

export interface DeviceDto {
  deviceId: string
  type: string
  status: number
  locationCode: string | null
  absX: number | null
  absY: number | null
  absZ: number | null
}
