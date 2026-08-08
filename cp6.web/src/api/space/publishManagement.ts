import http from '../http'
import type {
  ICreateSpacePublishAttemptRequest,
  ICreateSpacePublishAttemptResponse,
  ICreateSpaceValidationResponse,
  IRetrySpacePublishAttemptRequest,
  IRetrySpacePublishAttemptResponse,
  ISpaceHistoricalRepublishDto,
  ISpaceModelDto,
  ISpacePageOfSpaceVersionDto,
  ISpacePublishAttemptDto,
  ISpacePublishPreviewDto,
  ISpaceValidationRunDto,
  IStartSpaceHistoricalRepublishRequest,
  IStartSpaceHistoricalRepublishResponse,
} from '../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

const root = '/space/design/v1'

export interface SpacePublishAttemptSummary {
  id: string
  siteId: string
  targetVersionId: string
  targetVersionNo: string
  targetVersionName: string
  baseVersionId?: string
  status: string
  currentStep: string
  startedAtUtc: string
  finishedAtUtc?: string
  approvalReference?: string
  lastErrorCode?: string
  summary?: string
  jobId?: string
  jobStatus: string
  jobAttemptCount: number
  jobMaxAttempts: number
  nextAttemptAtUtc?: string
  openReconciliationIssueCount: number
  historicalRepublishId?: string
  historicalVersionId?: string
}

export interface SpacePublishActivityPage {
  items: SpacePublishAttemptSummary[]
  nextCursor?: string
}

export interface PublishPreviewFilters {
  floorLogicalId?: string
  objectType?: string
  action?: string
  impactCode?: string
  includeNoOp?: boolean
  limit?: number
  cursor?: string
}

export const publishManagementApi = {
  getModel(siteId: string) {
    return http.get<unknown, ISpaceModelDto>(`${root}/sites/${siteId}/model`)
  },

  getVersions(siteId: string, status?: string, limit = 100, cursor?: string) {
    return http.get<unknown, ISpacePageOfSpaceVersionDto>(`${root}/sites/${siteId}/versions`, {
      params: { status, limit, cursor },
    })
  },

  getActivities(siteId: string, status?: string, limit = 20, cursor?: string) {
    return http.get<unknown, SpacePublishActivityPage>(`${root}/sites/${siteId}/publish-attempts`, {
      params: { status, limit, cursor },
    })
  },

  createValidation(versionId: string) {
    return http.post<unknown, ICreateSpaceValidationResponse>(
      `${root}/versions/${versionId}/validations`,
    )
  },

  getValidation(validationId: string) {
    return http.get<unknown, ISpaceValidationRunDto>(`${root}/validations/${validationId}`)
  },

  getPreview(versionId: string, filters: PublishPreviewFilters = {}) {
    return http.get<unknown, ISpacePublishPreviewDto>(
      `${root}/versions/${versionId}/publish-preview`,
      { params: filters },
    )
  },

  createAttempt(versionId: string, request: ICreateSpacePublishAttemptRequest, idempotencyKey: string) {
    return http.post<unknown, ICreateSpacePublishAttemptResponse>(
      `${root}/versions/${versionId}/publish-attempts`,
      request,
      { headers: { 'Idempotency-Key': idempotencyKey } },
    )
  },

  getAttempt(attemptId: string) {
    return http.get<unknown, ISpacePublishAttemptDto>(`${root}/publish-attempts/${attemptId}`)
  },

  retryAttempt(attemptId: string, request: IRetrySpacePublishAttemptRequest, idempotencyKey: string) {
    return http.post<unknown, IRetrySpacePublishAttemptResponse>(
      `${root}/publish-attempts/${attemptId}/retry`,
      request,
      { headers: { 'Idempotency-Key': idempotencyKey } },
    )
  },

  startRepublish(
    historicalVersionId: string,
    request: IStartSpaceHistoricalRepublishRequest,
    idempotencyKey: string,
  ) {
    return http.post<unknown, IStartSpaceHistoricalRepublishResponse>(
      `${root}/versions/${historicalVersionId}/republish`,
      request,
      { headers: { 'Idempotency-Key': idempotencyKey } },
    )
  },

  getRepublish(republishId: string) {
    return http.get<unknown, ISpaceHistoricalRepublishDto>(`${root}/republishes/${republishId}`)
  },
}
