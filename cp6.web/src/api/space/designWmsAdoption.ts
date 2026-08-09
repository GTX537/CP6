import http from '@/api/http'
import type {
  IRefreshSpaceWmsAdoptionResponse,
  ISpacePageOfSpaceWmsAdoptionDto,
  ISpaceWmsAdoptionCommandResponse,
} from '../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

const root = '/space/design/v1'

export interface WmsAdoptionListQuery {
  status?: string
  differenceCode?: string
  limit?: number
  cursor?: string
}

export interface WmsAdoptionBinding {
  adoptionId: string
  locationLogicalId: string
  expectedRowVersion: string
}

export interface WmsAdoptionPlacement {
  floorLogicalId: string
  rackLogicalId: string
  column: number
  level: number
  depth: number
  expectedRowVersion: string
}

export const designWmsAdoptionApi = {
  refresh(versionId: string) {
    return http.post<unknown, IRefreshSpaceWmsAdoptionResponse>(
      `${root}/versions/${versionId}/wms-adoption/refresh`,
    )
  },

  list(versionId: string, query: WmsAdoptionListQuery = {}) {
    return http.get<unknown, ISpacePageOfSpaceWmsAdoptionDto>(
      `${root}/versions/${versionId}/wms-adoption/locations`,
      { params: query },
    )
  },

  bind(
    versionId: string,
    adoptionId: string,
    locationLogicalId: string,
    expectedRowVersion: string,
  ) {
    return http.post<unknown, ISpaceWmsAdoptionCommandResponse>(
      `${root}/versions/${versionId}/wms-adoption/locations/${adoptionId}/bind`,
      {
        locationLogicalId,
        expectedRowVersion,
      },
    )
  },

  bindBatch(versionId: string, items: readonly WmsAdoptionBinding[]) {
    return http.post<unknown, ISpaceWmsAdoptionCommandResponse>(
      `${root}/versions/${versionId}/wms-adoption/bindings:batch`,
      { items },
    )
  },

  place(
    versionId: string,
    adoptionId: string,
    request: WmsAdoptionPlacement,
  ) {
    return http.post<unknown, ISpaceWmsAdoptionCommandResponse>(
      `${root}/versions/${versionId}/wms-adoption/locations/${adoptionId}/place`,
      request,
    )
  },
}
