import http from '../http'
import type { SiteVO, Envelope } from '@/types/space/scene'

export const siteApi = {
  list() {
    return http.get<unknown, Envelope<SiteVO[]>>(`/space/site`)
  },
  create(d: SiteVO) {
    return http.post<unknown, Envelope<{ id: string }>>(`/space/site`, d)
  },
  update(id: string, d: SiteVO) {
    return http.put<unknown, Envelope<unknown>>(`/space/site/${encodeURIComponent(id)}`, d)
  },
  remove(id: string) {
    return http.delete<unknown, Envelope<unknown>>(`/space/site/${encodeURIComponent(id)}`)
  },
}
