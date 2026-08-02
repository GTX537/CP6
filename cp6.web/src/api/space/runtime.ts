import http from '../http'
import type {
  SpaceRuntimeInventoryLocateQuery,
  SpaceRuntimeInventoryLocateResponse,
  SpaceRuntimeInventoryResponse,
  SpacePersonnelCurrentPage,
  SpacePersonnelTrajectoryResponse,
  SpaceDeviceCurrentPage,
  SpaceRuntimeTaskPathResponse,
  SpaceWarehouseOverviewResponse,
} from '@/types/space/runtime'

export const spaceRuntimeApi = {
  warehouseOverview(siteId: string, abcWindowDays = 90) {
    const params = new URLSearchParams({ abcWindowDays: String(abcWindowDays) })
    return http.get<unknown, SpaceWarehouseOverviewResponse>(
      `/space/design/v1/sites/${siteId}/runtime/overview`,
      { params },
    )
  },
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
    const ownerId = criteria.ownerId?.trim().toUpperCase()
    if (materialNumber) params.set('materialNumber', materialNumber)
    if (lotNumber) params.set('lotNumber', lotNumber)
    if (containerNumber) params.set('containerNumber', containerNumber)
    if (ownerId) params.set('ownerId', ownerId)
    return http.get<unknown, SpaceRuntimeInventoryLocateResponse>(
      `/space/design/v1/sites/${siteId}/runtime/inventory/locate`,
      { params },
    )
  },
  taskPath(siteId: string, taskId: string) {
    const normalized = taskId.trim().toUpperCase()
    const params = new URLSearchParams({ taskId: normalized })
    return http.get<unknown, SpaceRuntimeTaskPathResponse>(
      `/space/design/v1/sites/${siteId}/runtime/tasks/path`,
      { params },
    )
  },
  currentPersonnel(
    siteId: string,
    floorLogicalId: string,
    limit = 500,
    cursor?: string,
  ) {
    const params = new URLSearchParams({
      floorLogicalId,
      limit: String(limit),
    })
    if (cursor) params.set('cursor', cursor)
    return http.get<unknown, SpacePersonnelCurrentPage>(
      `/space/design/v1/sites/${siteId}/personnel`,
      { params },
    )
  },
  currentDevices(
    siteId: string,
    floorLogicalId: string,
    limit = 500,
    cursor?: string,
  ) {
    const params = new URLSearchParams({
      floorLogicalId,
      limit: String(limit),
    })
    if (cursor) params.set('cursor', cursor)
    return http.get<unknown, SpaceDeviceCurrentPage>(
      `/space/design/v1/sites/${siteId}/devices`,
      { params },
    )
  },
  personnelTrajectory(
    siteId: string,
    sourceId: string,
    personExternalId: string,
    fromUtc: string,
    toUtc: string,
    limit = 500,
    cursor?: string,
  ) {
    const normalizedSourceId = sourceId.trim().toUpperCase()
    const normalizedPersonId = personExternalId.trim().toUpperCase()
    const params = new URLSearchParams({
      sourceId: normalizedSourceId,
      personExternalId: normalizedPersonId,
      fromUtc,
      toUtc,
      limit: String(limit),
    })
    if (cursor) params.set('cursor', cursor)
    return http.get<unknown, SpacePersonnelTrajectoryResponse>(
      `/space/design/v1/sites/${siteId}/personnel/trajectory`,
      { params },
    )
  },
}
