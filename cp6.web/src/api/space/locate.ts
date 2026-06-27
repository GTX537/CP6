import http from '../http'
import type { LocationDetail, LocateResult } from '@/types/space/viewer'
import type { Envelope } from '@/types/space/scene'

export const locateApi = {
  detail(id: string) {
    return http.get<unknown, Envelope<LocationDetail>>(`/space/location/${id}/detail`)
  },
  // validateStatus allows 400 through without throwing — Locator checks env.data for null
  locate(code: string) {
    return http.get<unknown, Envelope<LocateResult | null>>(`/space/location/locate`, {
      params: { code },
      validateStatus: (s) => (s >= 200 && s < 300) || s === 400,
    })
  },
  search(prefix: string, floorId?: string) {
    return http.get<unknown, Envelope<LocateResult[]>>(`/space/location/search`, {
      params: floorId ? { prefix, floorId } : { prefix },
    })
  },
}
