import http from '../http'
import type {
  SpaceRuntimeInventoryLocateQuery,
  SpaceRuntimeInventoryLocateResponse,
  SpaceRuntimeInventoryResponse,
} from '@/types/space/runtime'

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
  locateInventory(siteId: string, criteria: SpaceRuntimeInventoryLocateQuery) {
    const params = new URLSearchParams()
    const materialNumber = criteria.materialNumber?.trim()
    const lotNumber = criteria.lotNumber?.trim()
    const containerNumber = criteria.containerNumber?.trim()
    if (materialNumber) params.set('materialNumber', materialNumber)
    if (lotNumber) params.set('lotNumber', lotNumber)
    if (containerNumber) params.set('containerNumber', containerNumber)
    return http.get<unknown, SpaceRuntimeInventoryLocateResponse>(
      `/space/design/v1/sites/${siteId}/runtime/inventory/locate`,
      { params },
    )
  },
}
