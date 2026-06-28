// cp6.web/src/api/space/stock.ts
import http from '../http'
import type { Envelope } from '@/types/space/scene'
import type { FloorStockSnapshot, WmsLocationHit } from '@/types/space/overlay'

export const stockApi = {
  floorStock(floorId: string) {
    return http.get<unknown, Envelope<FloorStockSnapshot>>(`/space/floor/${floorId}/stock`)
  },
  locate(params: { material?: string; lot?: string; container?: string }) {
    return http.get<unknown, Envelope<WmsLocationHit[]>>(`/space/stock/locate`, { params })
  },
}
