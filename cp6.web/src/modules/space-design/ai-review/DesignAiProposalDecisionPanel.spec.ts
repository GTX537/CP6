import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { aiProposalReviewApi } from '@/api/space/aiProposalReview'
import {
  SpaceAiGenerationReviewSummaryDto,
  SpaceAiGenerationRunLinksDto,
} from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import DesignAiProposalDecisionPanel from './DesignAiProposalDecisionPanel.vue'

const { confirm, success } = vi.hoisted(() => ({
  confirm: vi.fn(),
  success: vi.fn(),
}))

vi.mock('@/api/space/aiProposalReview', () => ({
  aiProposalReviewApi: {
    getVersion: vi.fn(),
    getRun: vi.fn(),
    getReview: vi.fn(),
    getProposals: vi.fn(),
    decide: vi.fn(),
    decideBatch: vi.fn(),
    apply: vi.fn(),
    cancel: vi.fn(),
    retry: vi.fn(),
    discard: vi.fn(),
    reconcile: vi.fn(),
    createGenerationRun: vi.fn(),
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
        sourceId: 'source-1',
        mappingProfileVersionId: 'mapping-1',
        status: 'AwaitingReview',
        progress: 90,
        baseContentRevision: 42,
        cancellationPending: false,
        retryable: false,
        recoveryAction: 'complete-review-or-discard',
        applyCommitState: 'NotStarted',
        rowVersion: 'run-row-version',
      })
      .mockResolvedValueOnce({
        schemaVersion: 1,
        runId: 'run-1',
        siteId: 'site-1',
        modelVersionId: 'version-1',
        sourceId: 'source-1',
        mappingProfileVersionId: 'mapping-1',
        status: 'Succeeded',
        progress: 100,
        baseContentRevision: 42,
        appliedContentRevision: 43,
        applyJobStatus: 'Succeeded',
        cancellationPending: false,
        retryable: false,
        recoveryAction: 'open-updated-draft',
        applyCommitState: 'Committed',
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

  it('shows queued progress without requesting review resources early', async () => {
    vi.mocked(aiProposalReviewApi.getRun).mockReset().mockResolvedValue({
      schemaVersion: 1,
      runId: 'run-queued',
      siteId: 'site-1',
      modelVersionId: 'version-1',
      sourceId: 'source-1',
      mappingProfileVersionId: 'mapping-1',
      status: 'Queued',
      progress: 10,
      baseContentRevision: 42,
      cancellationPending: false,
      retryable: false,
      recoveryAction: 'wait-for-generation',
      applyCommitState: 'NotStarted',
      rowVersion: 'run-row-version',
    })

    const wrapper = mount(DesignAiProposalDecisionPanel, {
      props: { runId: 'run-queued', currentContentRevision: 42 },
      global: {
        directives: { loading: {}, permission: {} },
        stubs: {
          ElAlert: { props: ['title'], template: '<div>{{ title }}</div>' },
          ElButton: true,
          ElCheckbox: true,
          ElDialog: true,
          ElForm: true,
          ElFormItem: true,
          ElInput: true,
          ElOption: true,
          ElSelect: true,
          ElTag: true,
          ElTooltip: true,
        },
      },
    })
    await flushPromises()

    expect(aiProposalReviewApi.getReview).not.toHaveBeenCalled()
    expect(aiProposalReviewApi.getProposals).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('规则生成正在进行：Queued（10%）')
    expect(wrapper.text()).toContain('生成完成后将自动进入人工审查')
    wrapper.unmount()
  })

  it('rebuilds a failed run through the unified create contract', async () => {
    vi.mocked(aiProposalReviewApi.getRun).mockReset().mockResolvedValue({
      schemaVersion: 1,
      runId: 'run-failed',
      siteId: 'site-1',
      modelVersionId: 'version-1',
      sourceId: 'source-1',
      mappingProfileVersionId: 'mapping-1',
      status: 'Failed',
      progress: 40,
      baseContentRevision: 42,
      cancellationPending: false,
      retryable: false,
      recoveryAction: 'use-rule-only-or-retry-later',
      applyCommitState: 'NotStarted',
      rowVersion: 'failed-row-version',
    })
    vi.mocked(aiProposalReviewApi.getVersion).mockResolvedValue({
      id: 'version-1',
      status: 'Draft',
      contentRevision: 43,
      rowVersion: 'version-row-version',
    })
    vi.mocked(aiProposalReviewApi.createGenerationRun).mockResolvedValue({
      schemaVersion: 1,
      runId: 'run-replacement',
      jobId: 'job-2',
      status: 'Queued',
      baseContentRevision: 43,
      sourceId: 'source-1',
      sourceHash: 'a'.repeat(64),
      mode: 'RuleOnly',
      policy: 'Disabled',
      links: new SpaceAiGenerationRunLinksDto({
        self: '/run-replacement',
        proposals: '/run-replacement/proposals',
      }),
      reused: false,
      idempotentReplay: false,
    })

    const wrapper = mount(DesignAiProposalDecisionPanel, {
      props: { runId: 'run-failed', currentContentRevision: 43 },
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
          ElTag: true,
          ElTooltip: true,
        },
      },
    })
    await flushPromises()

    const rebuild = wrapper.findAll('button').find(button => button.text().includes('规则降级重建'))
    expect(rebuild).toBeDefined()
    await rebuild!.trigger('click')
    await flushPromises()

    expect(aiProposalReviewApi.createGenerationRun).toHaveBeenCalledWith(
      'version-1',
      {
        sourceId: 'source-1',
        mappingProfileVersionId: 'mapping-1',
        rackGenerationProfileVersionId: null,
        basedOnRunId: 'run-failed',
        expectedContentRevision: 43,
        expectedBasedOnRunRowVersion: 'failed-row-version',
        mode: 'RuleOnly',
      },
      'version-row-version',
      expect.any(String),
    )
    expect(wrapper.emitted('recovered')?.[0]).toEqual(['run-replacement'])
  })
})
