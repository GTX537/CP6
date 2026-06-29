import http from '../http'
import type { FloorVO, Envelope } from '@/types/space/scene'

export const floorApi = {
  list(siteId: string) {
    return http.get<unknown, Envelope<FloorVO[]>>(`/space/floor`, { params: { siteId } })
  },
}
