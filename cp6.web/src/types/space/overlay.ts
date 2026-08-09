// cp6.web/src/types/space/overlay.ts —— 对齐后端 WmsStockDto/WmsLocationHit
import type { SpaceDataSource } from './dataSource'

export interface WmsStockDto {
  locationCode: string
  binStatus: number    // 0空 1有货 2满 3锁定 4在拣
  qty: number
  allocatedQty: number
  capacity: number | null
  capacityUom?: number | null
  capacitySource?: string | null
  topMaterial: string | null
  productKinds: number
  productCodes?: string[]
}

export interface FloorStockSnapshot {
  items: WmsStockDto[]
  source: SpaceDataSource
  ts: string           // 服务器快照时间戳
}

export interface WmsLocationHit {
  locationCode: string
  qty: number
  lot: string | null
}

export type OverlayMode = 'structure' | 'status' | 'utilization' | 'storageType' | 'abc' | 'off'
