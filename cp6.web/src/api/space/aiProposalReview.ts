import http from '../http'
import type {
  ICreateSpaceAiGenerationRecoveryRequest,
  ICreateSpaceAiProposalBatchDecisionRequest,
  ICreateSpaceAiProposalDecisionRequest,
  ICreateSpaceAiAtomicApplyRequest,
  ISpaceAiAtomicApplyAcceptedDto,
  ISpaceAiGenerationReviewDto,
  ISpaceAiGenerationRunDto,
  ISpaceAiGenerationRunActionDto,
  ISpaceAiProposalDecisionResponse,
  ISpaceAiProposalIssuePageDto,
  ISpaceAiProposalPageDto,
  ISpaceAiRunActionRequest,
} from '../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

const root = '/space/design/v1/generation-runs'

export interface SpaceAiProposalListQuery {
  status?: string
  confidenceBand?: string
  proposalType?: string
  hasBlockingIssue?: boolean
  cursor?: string
  limit?: number
}

export const aiProposalReviewApi = {
  getRun(runId: string) {
    return http.get<unknown, ISpaceAiGenerationRunDto>(
      `${root}/${runId}`,
    )
  },

  getReview(runId: string) {
    return http.get<unknown, ISpaceAiGenerationReviewDto>(
      `${root}/${runId}/review`,
    )
  },

  getProposals(runId: string, query: SpaceAiProposalListQuery) {
    return http.get<unknown, ISpaceAiProposalPageDto>(
      `${root}/${runId}/proposals`,
      { params: query },
    )
  },

  getIssues(runId: string, proposalId?: string) {
    return http.get<unknown, ISpaceAiProposalIssuePageDto>(
      `${root}/${runId}/issues`,
      { params: { proposalId, limit: 200 } },
    )
  },

  decide(
    runId: string,
    request: ICreateSpaceAiProposalDecisionRequest,
    idempotencyKey: string = crypto.randomUUID(),
  ) {
    return http.post<unknown, ISpaceAiProposalDecisionResponse>(
      `${root}/${runId}/decisions`,
      request,
      { headers: { 'Idempotency-Key': idempotencyKey } },
    )
  },

  decideBatch(
    runId: string,
    request: ICreateSpaceAiProposalBatchDecisionRequest,
    idempotencyKey: string = crypto.randomUUID(),
  ) {
    return http.post<unknown, ISpaceAiProposalDecisionResponse>(
      `${root}/${runId}/decisions:batch`,
      request,
      { headers: { 'Idempotency-Key': idempotencyKey } },
    )
  },

  apply(
    runId: string,
    request: ICreateSpaceAiAtomicApplyRequest,
    idempotencyKey: string = crypto.randomUUID(),
  ) {
    return http.post<unknown, ISpaceAiAtomicApplyAcceptedDto>(
      `${root}/${runId}/apply`,
      request,
      { headers: { 'Idempotency-Key': idempotencyKey } },
    )
  },

  cancel(
    runId: string,
    request: ISpaceAiRunActionRequest,
    idempotencyKey: string = crypto.randomUUID(),
  ) {
    return http.post<unknown, ISpaceAiGenerationRunActionDto>(
      `${root}/${runId}/cancel`,
      request,
      { headers: { 'Idempotency-Key': idempotencyKey } },
    )
  },

  retry(
    runId: string,
    request: ISpaceAiRunActionRequest,
    idempotencyKey: string = crypto.randomUUID(),
  ) {
    return http.post<unknown, ISpaceAiGenerationRunActionDto>(
      `${root}/${runId}/retry`,
      request,
      { headers: { 'Idempotency-Key': idempotencyKey } },
    )
  },

  discard(
    runId: string,
    request: ISpaceAiRunActionRequest,
    idempotencyKey: string = crypto.randomUUID(),
  ) {
    return http.post<unknown, ISpaceAiGenerationRunActionDto>(
      `${root}/${runId}/discard`,
      request,
      { headers: { 'Idempotency-Key': idempotencyKey } },
    )
  },

  reconcile(
    runId: string,
    request: ISpaceAiRunActionRequest,
    idempotencyKey: string = crypto.randomUUID(),
  ) {
    return http.post<unknown, ISpaceAiGenerationRunActionDto>(
      `${root}/${runId}/reconcile`,
      request,
      { headers: { 'Idempotency-Key': idempotencyKey } },
    )
  },

  recover(
    versionId: string,
    request: ICreateSpaceAiGenerationRecoveryRequest,
    idempotencyKey: string = crypto.randomUUID(),
  ) {
    return http.post<unknown, ISpaceAiGenerationRunActionDto>(
      `/space/design/v1/versions/${versionId}/generation-runs`,
      request,
      { headers: { 'Idempotency-Key': idempotencyKey } },
    )
  },
}
