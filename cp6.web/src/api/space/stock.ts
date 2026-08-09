// cp6.web/src/api/space/stock.ts
import http from '../http'
import type { Envelope } from '@/types/space/scene'
import type { FloorStockSnapshot, WmsLocationHit } from '@/types/space/overlay'
import type { SourcedItems } from '@/types/space/dataSource'
import type { StockDelta } from '@/types/space/analytics'

export const stockApi = {
  floorStock(floorId: string) {
    return http.get<unknown, Envelope<FloorStockSnapshot>>(`/space/floor/${floorId}/stock`)
  },
  async floorStockDelta(floorId: string, locationCodes: string[]): Promise<Envelope<StockDelta>> {
    // Keep each GET comfortably below proxy URL limits and the backend's 200-code guard.
    const codes = [...new Set(locationCodes.filter(Boolean))]
    if (codes.length === 0) {
      return { code: 0, message: 'OK', data: { items: [], requested: 0, matched: 0, ts: new Date().toISOString() } }
    }
    const batches: string[][] = []
    for (let i = 0; i < codes.length; i += 50) batches.push(codes.slice(i, i + 50))
    const responses = await Promise.all(batches.map((batch) => {
      const params = new URLSearchParams()
      for (const code of batch) params.append('locationCodes', code)
      return http.get<unknown, Envelope<StockDelta>>(`/space/floor/${floorId}/stock/delta?${params.toString()}`)
    }))
    return {
      code: 0,
      message: 'OK',
      data: {
        items: responses.flatMap((response) => response.data.items),
        requested: responses.reduce((sum, response) => sum + response.data.requested, 0),
        matched: responses.reduce((sum, response) => sum + response.data.matched, 0),
        ts: responses.at(-1)!.data.ts,
      },
    }
  },
  locate(params: { material?: string; lot?: string; container?: string }) {
    return http.get<unknown, Envelope<SourcedItems<WmsLocationHit>>>(`/space/stock/locate`, { params })
  },
}
