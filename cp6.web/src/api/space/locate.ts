import http from '../http'
import type { LocationDetail, LocateResult } from '@/types/space/viewer'
import type { Envelope } from '@/types/space/scene'

export const locateApi = {
  detail(id: string) {
    return http.get<unknown, Envelope<LocationDetail>>(`/space/location/${id}/detail`)
  },
  locate(code: string) {
    return http.get<unknown, Envelope<LocateResult>>(`/space/location/locate`, { params: { code } })
  },
  search(prefix: string, floorId?: string) {
    return http.get<unknown, Envelope<LocateResult[]>>(`/space/location/search`, {
      params: floorId ? { prefix, floorId } : { prefix },
    })
  },
}
