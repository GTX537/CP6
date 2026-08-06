import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { aiProposalReviewApi } from '@/api/space/aiProposalReview'
import { SpaceAiGenerationReviewSummaryDto } from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import DesignAiProposalDecisionPanel from './DesignAiProposalDecisionPanel.vue'

const { confirm, success } = vi.hoisted(() => ({
  confirm: vi.fn(),
  success: vi.fn(),
}))

vi.mock('@/api/space/aiProposalReview', () => ({
  aiProposalReviewApi: {
    getRun: vi.fn(),
    getReview: vi.fn(),
    getProposals: vi.fn(),
    decide: vi.fn(),
    decideBatch: vi.fn(),
    apply: vi.fn(),
  },
}))

vi.mock('element-plus', () => ({
  ElMessage: {
    error: vi.fn(),
    success,
    warning: vi.fn(),
  },
  ElMessageBox: { confirm },
}))

describe('DesignAiProposalDecisionPanel atomic apply', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    confirm.mockResolvedValue(undefined)
    vi.mocked(aiProposalReviewApi.getReview).mockResolvedValue({
      runId: 'run-1',
      status: 'AwaitingReview',
      baseContentRevision: 42,
      runRowVersion: 'run-row-version',
      reviewEtag: 'review-etag',
      reviewCompleted: true,
      summary: new SpaceAiGenerationReviewSummaryDto({
        totalCount: 1,
        proposedCount: 0,
        acceptedCount: 1,
        rejectedCount: 0,
        modifiedCount: 0,
        obsoleteCount: 0,
        blockingProposalCount: 0,
        openRunBlockingIssueCount: 0,
        openProposalBlockingIssueCount: 0,
      }),
    })
    vi.mocked(aiProposalReviewApi.getProposals).mockResolvedValue({
      items: [],
    })
    vi.mocked(aiProposalReviewApi.getRun)
      .mockResolvedValueOnce({
        schemaVersion: 1,
        runId: 'run-1',
        siteId: 'site-1',
        modelVersionId: 'version-1',
        status: 'AwaitingReview',
        progress: 90,
        baseContentRevision: 42,
        rowVersion: 'run-row-version',
      })
      .mockResolvedValueOnce({
        schemaVersion: 1,
        runId: 'run-1',
        siteId: 'site-1',
        modelVersionId: 'version-1',
        status: 'Succeeded',
        progress: 100,
        baseContentRevision: 42,
        appliedContentRevision: 43,
        applyJobStatus: 'Succeeded',
        rowVersion: 'applied-row-version',
      })
    vi.mocked(aiProposalReviewApi.apply).mockResolvedValue({
      schemaVersion: 1,
      runId: 'run-1',
      jobId: 'job-1',
      status: 'Queued',
      expectedContentRevision: 42,
      reviewEtag: 'review-etag',
      idempotentReplay: false,
    })
  })

  it('queues the frozen review and emits applied after terminal polling', async () => {
    const wrapper = mount(DesignAiProposalDecisionPanel, {
      props: { runId: 'run-1' },
      global: {
        directives: { loading: {}, permission: {} },
        stubs: {
          ElAlert: true,
          ElButton: {
            props: ['disabled', 'loading'],
            emits: ['click'],
            template: '<button :disabled="disabled" @click="$emit(\'click\')"><slot /></button>',
          },
          ElCheckbox: true,
          ElDialog: true,
          ElForm: true,
          ElFormItem: true,
          ElInput: true,
          ElOption: true,
          ElSelect: true,
          ElTag: { template: '<span><slot /></span>' },
          ElTooltip: true,
        },
      },
    })
    await flushPromises()

    const applyButton = wrapper
      .get('[data-test="ai-proposal-apply"] button')
    expect(applyButton.attributes('disabled')).toBeUndefined()
    await applyButton.trigger('click')
    await flushPromises()

    expect(aiProposalReviewApi.apply).toHaveBeenCalledWith(
      'run-1',
      {
        expectedContentRevision: 42,
        expectedRunRowVersion: 'run-row-version',
        reviewEtag: 'review-etag',
      },
      expect.any(String),
    )
    expect(wrapper.emitted('applied')?.[0]?.[0]).toEqual(
      expect.objectContaining({
        status: 'Succeeded',
        appliedContentRevision: 43,
      }),
    )
    expect(success).toHaveBeenCalledWith('AI 提案应用任务已排队')
  })
})
