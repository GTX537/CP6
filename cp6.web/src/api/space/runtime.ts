import http from '../http'
import type { SpaceRuntimeInventoryResponse } from '@/types/space/runtime'

export const spaceRuntimeApi = {
  inventory(siteId: string, locationLogicalIds: readonly string[]) {
    const params = new URLSearchParams()
    for (const locationLogicalId of new Set(locationLogicalIds)) {
      params.append('locationLogicalId', locationLogicalId)
    }
    return http.get<unknown, SpaceRuntimeInventoryResponse>(
      `/space/design/v1/sites/${siteId}/runtime/inventory`,
      { params },
    )
  },
}
