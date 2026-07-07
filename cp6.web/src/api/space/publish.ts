import http from '../http'
import type { Envelope, SpaceEventVO } from '@/types/space/scene'

export const publishApi = {
  // 整层/库区发布（zoneId 可空 = 整层）；返回发布库位数
  publishFloor(floorId: string, zoneId?: string) {
    return http.post<unknown, Envelope<{ published: number }>>(
      `/space/floor/${encodeURIComponent(floorId)}/publish`,
      { zoneId },
    )
  },
  // 停用已发布库位
  deactivate(locationId: string) {
    return http.put<unknown, Envelope<unknown>>(
      `/space/location/${encodeURIComponent(locationId)}/deactivate`,
    )
  },
  // 存量采纳导入
  adopt(items: { code: string; attrs?: Record<string, unknown> }[]) {
    return http.post<unknown, Envelope<{ imported: number; skipped: string[] }>>(
      '/space/location/adopt',
      { items },
    )
  },
  // SPACE→WMS 集成事件（分页，无 total）
  events(page: number, pageSize: number) {
    return http.get<unknown, Envelope<SpaceEventVO[]>>('/space/publish/events', {
      params: { page, pageSize },
    })
  },
}
