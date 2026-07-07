import http from '../http'
import type { FloorVO, Envelope } from '@/types/space/scene'

export const floorApi = {
  list(siteId: string) {
    return http.get<unknown, Envelope<FloorVO[]>>(`/space/floor`, { params: { siteId } })
  },
  create(d: FloorVO) {
    return http.post<unknown, Envelope<{ id: string }>>(`/space/floor`, d)
  },
  update(id: string, d: FloorVO) {
    return http.put<unknown, Envelope<unknown>>(`/space/floor/${encodeURIComponent(id)}`, d)
  },
  remove(id: string) {
    return http.delete<unknown, Envelope<unknown>>(`/space/floor/${encodeURIComponent(id)}`)
  },
}
