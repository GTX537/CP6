import http from '../http'
import type { Envelope, ZoneVO } from '@/types/space/scene'

export const zoneApi = {
  // 列出楼层下全部库区
  list(floorId: string) {
    return http.get<unknown, Envelope<ZoneVO[]>>('/space/zone', { params: { floorId } })
  },
}
