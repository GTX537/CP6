import http from '../http'
import type { Envelope, EditorScene, SceneSaveDto } from '@/types/space/scene'

export const sceneApi = {
  get(floorId: string) {
    return http.get<any, Envelope<EditorScene>>(`/space/floor/${floorId}/scene`)
  },
  save(floorId: string, dto: SceneSaveDto) {
    return http.post<any, Envelope<{ idMap: Record<string, string> }>>(`/space/floor/${floorId}/scene`, dto)
  },
  exportScene(floorId: string) {
    return http.get<any, Envelope<any>>(`/space/floor/${floorId}/export`)
  },
  importScene(siteId: string, dto: unknown) {
    return http.post<any, Envelope<{ floorId: string }>>(`/space/site/${siteId}/import`, dto)
  },
  bindCodes(rackId: string, pairs: { locationId: string; col: number; level: number; depth: number }[]) {
    return http.post<any, Envelope<any>>(`/space/rack/${rackId}/bind-codes`, { pairs })
  },
}
