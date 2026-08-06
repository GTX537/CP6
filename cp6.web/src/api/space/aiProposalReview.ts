import http from '../http'
import type {
  ICreateSpaceAiProposalBatchDecisionRequest,
  ICreateSpaceAiProposalDecisionRequest,
  ISpaceAiGenerationReviewDto,
  ISpaceAiProposalDecisionResponse,
  ISpaceAiProposalIssuePageDto,
  ISpaceAiProposalPageDto,
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
}
