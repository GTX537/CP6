// cp6.web/src/api/space/advanced.ts
import http from '../http'
import type { Envelope } from '@/types/space/scene'
import type { FloorPickPath, FloorWorkload, DeviceDto, SitePickPath } from '@/types/space/advanced'
import type { SourcedItems } from '@/types/space/dataSource'

export const advancedApi = {
  pickPath(floorId: string, taskNo: string) {
    return http.get<unknown, Envelope<FloorPickPath>>(`/space/floor/${floorId}/pick-path`, { params: { taskNo } })
  },
  workload(floorId: string, from: string, to: string) {
    return http.get<unknown, Envelope<FloorWorkload>>(`/space/floor/${floorId}/workload`, { params: { from, to } })
  },
  devices(floorId: string) {
    return http.get<unknown, Envelope<SourcedItems<DeviceDto>>>(`/space/floor/${floorId}/devices`)
  },
  sitePickPath(siteId: string, taskNo: string) {
    return http.get<unknown, Envelope<SitePickPath>>(`/space/site/${siteId}/pick-path`, { params: { taskNo } })
  },
}
