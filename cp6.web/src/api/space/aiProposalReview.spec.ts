import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '@/api/http'
import { aiProposalReviewApi } from './aiProposalReview'

vi.mock('@/api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
  },
}))

describe('aiProposalReviewApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(http.get).mockResolvedValue({})
    vi.mocked(http.post).mockResolvedValue({})
  })

  it('loads a review and a bounded proposal page', async () => {
    await aiProposalReviewApi.getRun('run-1')
    await aiProposalReviewApi.getReview('run-1')
    await aiProposalReviewApi.getProposals('run-1', {
      status: 'Proposed',
      confidenceBand: 'High',
      limit: 200,
    })

    expect(http.get).toHaveBeenNthCalledWith(
      1,
      '/space/design/v1/generation-runs/run-1',
    )
    expect(http.get).toHaveBeenNthCalledWith(
      2,
      '/space/design/v1/generation-runs/run-1/review',
    )
    expect(http.get).toHaveBeenNthCalledWith(
      3,
      '/space/design/v1/generation-runs/run-1/proposals',
      { params: { status: 'Proposed', confidenceBand: 'High', limit: 200 } },
    )
  })

  it('queues an atomic apply with the frozen review preconditions', async () => {
    await aiProposalReviewApi.apply(
      'run-1',
      {
        expectedContentRevision: 42,
        expectedRunRowVersion: 'run-row-version',
        reviewEtag: 'review-etag',
      },
      'apply-key',
    )

    expect(http.post).toHaveBeenCalledWith(
      '/space/design/v1/generation-runs/run-1/apply',
      {
        expectedContentRevision: 42,
        expectedRunRowVersion: 'run-row-version',
        reviewEtag: 'review-etag',
      },
      { headers: { 'Idempotency-Key': 'apply-key' } },
    )
  })

  it('sends row-version decisions with Idempotency-Key', async () => {
    await aiProposalReviewApi.decide(
      'run-1',
      {
        proposalId: 'proposal-1',
        decision: 'Accept',
        expectedProposalRowVersion: 'row-version',
      },
      'decision-key',
    )

    expect(http.post).toHaveBeenCalledWith(
      '/space/design/v1/generation-runs/run-1/decisions',
      expect.objectContaining({
        proposalId: 'proposal-1',
        expectedProposalRowVersion: 'row-version',
      }),
      { headers: { 'Idempotency-Key': 'decision-key' } },
    )
  })

  it('sends batch rejection with the current review etag', async () => {
    await aiProposalReviewApi.decideBatch(
      'run-1',
      {
        proposalIds: ['proposal-1', 'proposal-2'],
        decision: 'Reject',
        reviewEtag: 'review-etag',
      },
      'batch-key',
    )

    expect(http.post).toHaveBeenCalledWith(
      '/space/design/v1/generation-runs/run-1/decisions:batch',
      expect.objectContaining({
        decision: 'Reject',
        reviewEtag: 'review-etag',
      }),
      { headers: { 'Idempotency-Key': 'batch-key' } },
    )
  })
})
