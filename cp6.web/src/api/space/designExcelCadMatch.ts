import http from '../http'
import type {
  ISpaceExcelCadMatchDto,
  IStartSpaceExcelCadMatchRequest,
  IStartSpaceExcelCadMatchResponse,
} from '../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

const root = '/space/design/v1'

export interface SpaceExcelCadMatchFilters {
  disposition?: string
  rackCode?: string
  sourceRef?: string
  onlyLocatable?: boolean
  limit?: number
  cursor?: string
}

export const designExcelCadMatchApi = {
  start(
    versionId: string,
    request: IStartSpaceExcelCadMatchRequest,
    idempotencyKey: string,
  ) {
    return http.post<unknown, IStartSpaceExcelCadMatchResponse>(
      `${root}/versions/${versionId}/excel-cad-matches`,
      request,
      { headers: { 'Idempotency-Key': idempotencyKey } },
    )
  },

  get(
    versionId: string,
    jobId: string,
    filters: SpaceExcelCadMatchFilters = {},
  ) {
    return http.get<unknown, ISpaceExcelCadMatchDto>(
      `${root}/versions/${versionId}/excel-cad-matches/${jobId}`,
      { params: filters },
    )
  },
}
