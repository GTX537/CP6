import http from '../http'
import type { Envelope } from '@/types/space/scene'
import type {
  AbcResponse,
  AbcSnapshotMeta,
  AnalyticsConfig,
  ControlTower,
  StorageTypeResponse,
  UtilizationResponse,
} from '@/types/space/analytics'

export const analyticsApi = {
  config(siteId: string) {
    return http.get<unknown, Envelope<AnalyticsConfig>>(`/space/site/${siteId}/analytics/config`)
  },
  updateConfig(siteId: string, data: AnalyticsConfig) {
    return http.put<unknown, Envelope<AnalyticsConfig>>(`/space/site/${siteId}/analytics/config`, data)
  },
  rebuildAbc(siteId: string) {
    return http.post<unknown, Envelope<AbcSnapshotMeta>>(`/space/site/${siteId}/analytics/abc/rebuild`)
  },
  utilization(floorId: string) {
    return http.get<unknown, Envelope<UtilizationResponse>>(`/space/floor/${floorId}/analytics/utilization`)
  },
  storageTypes(floorId: string) {
    return http.get<unknown, Envelope<StorageTypeResponse>>(`/space/floor/${floorId}/analytics/storage-types`)
  },
  abc(floorId: string, includeProducts = true) {
    return http.get<unknown, Envelope<AbcResponse>>(`/space/floor/${floorId}/analytics/abc`, {
      params: { includeProducts },
    })
  },
  controlTower(siteId: string) {
    return http.get<unknown, Envelope<ControlTower>>(`/space/site/${siteId}/control-tower`)
  },
}
