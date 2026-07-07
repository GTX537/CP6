import http from '../http'
import type { FloorVO, Envelope } from '@/types/space/scene'

export const floorApi = {
  list(siteId: string) {
    return http.get<unknown, Envelope<FloorVO[]>>(`/space/floor`, { params: { siteId } })
  },
  // create 载荷无 id（后端生成 Guid）；收窄为 Partial 以匹配「新建时无 id」的现实
  // （照 api/space/template.ts 的 Partial 惯例；FloorVO 本身不改——编辑器代码假定 id 存在）。
  create(d: Partial<FloorVO>) {
    return http.post<unknown, Envelope<{ id: string }>>(`/space/floor`, d)
  },
  update(id: string, d: FloorVO) {
    return http.put<unknown, Envelope<unknown>>(`/space/floor/${encodeURIComponent(id)}`, d)
  },
  remove(id: string) {
    return http.delete<unknown, Envelope<unknown>>(`/space/floor/${encodeURIComponent(id)}`)
  },
}
