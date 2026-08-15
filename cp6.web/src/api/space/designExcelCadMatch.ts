import http from '../http'
import type {
  ICompensateSpaceExcelCadApplyRequest,
  ICompensateSpaceExcelCadApplyResponse,
  IConfirmSpaceExcelCadMatchRequest,
  IConfirmSpaceExcelCadMatchResponse,
  ISpaceExcelCadApplyDto,
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

  confirm(
    versionId: string,
    matchJobId: string,
    request: IConfirmSpaceExcelCadMatchRequest,
    idempotencyKey: string,
  ) {
    return http.post<unknown, IConfirmSpaceExcelCadMatchResponse>(
      `${root}/versions/${versionId}/excel-cad-matches/${matchJobId}/confirmations`,
      request,
      { headers: { 'Idempotency-Key': idempotencyKey } },
    )
  },

  getConfirmation(
    versionId: string,
    matchJobId: string,
    applyJobId: string,
  ) {
    return http.get<unknown, ISpaceExcelCadApplyDto>(
      `${root}/versions/${versionId}/excel-cad-matches/${matchJobId}`
      + `/confirmations/${applyJobId}`,
    )
  },

  compensate(
    versionId: string,
    matchJobId: string,
    applyJobId: string,
    request: ICompensateSpaceExcelCadApplyRequest,
    idempotencyKey: string,
  ) {
    return http.post<unknown, ICompensateSpaceExcelCadApplyResponse>(
      `${root}/versions/${versionId}/excel-cad-matches/${matchJobId}`
      + `/confirmations/${applyJobId}:compensate`,
      request,
      { headers: { 'Idempotency-Key': idempotencyKey } },
    )
  },
}
