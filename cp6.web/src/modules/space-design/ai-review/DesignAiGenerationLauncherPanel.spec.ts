import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { aiProposalReviewApi } from '@/api/space/aiProposalReview'
import {
  SpaceAiGenerationRunLinksDto,
  SpaceSourceDto,
} from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import DesignAiGenerationLauncherPanel from './DesignAiGenerationLauncherPanel.vue'

const { confirm, success } = vi.hoisted(() => ({
  confirm: vi.fn(),
  success: vi.fn(),
}))

vi.mock('@/api/space/aiProposalReview', () => ({
  aiProposalReviewApi: {
    getVersion: vi.fn(),
    getSources: vi.fn(),
    createGenerationRun: vi.fn(),
  },
}))

vi.mock('element-plus', () => ({
  ElMessage: { success, warning: vi.fn(), error: vi.fn() },
  ElMessageBox: { confirm },
}))

const global = {
  directives: { loading: {}, permission: {} },
  stubs: {
    ElAlert: true,
    ElButton: {
      props: ['disabled', 'loading'],
      emits: ['click'],
      template: '<button :disabled="disabled" @click="$emit(\'click\')"><slot /></button>',
    },
    ElForm: { template: '<form><slot /></form>' },
    ElFormItem: { template: '<label><slot /></label>' },
    ElOption: true,
    ElSelect: true,
  },
}

describe('DesignAiGenerationLauncherPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    confirm.mockResolvedValue(undefined)
    vi.mocked(aiProposalReviewApi.getVersion).mockResolvedValue({
      id: 'version-1',
      status: 'Draft',
      contentRevision: 42,
      rowVersion: 'version-row-version',
    })
    vi.mocked(aiProposalReviewApi.getSources).mockResolvedValue({
      items: [new SpaceSourceDto({
        id: 'source-1',
        modelVersionId: 'version-1',
        sourceType: 'Dwg',
        displayName: 'warehouse.dwg',
        state: 'PreviewReady',
        mappingProfileId: 'mapping-1',
        mappingProfileVersion: 3,
        sha256: 'a'.repeat(64),
      })],
    })
    vi.mocked(aiProposalReviewApi.createGenerationRun).mockResolvedValue({
      schemaVersion: 1,
      runId: 'run-1',
      jobId: 'job-1',
      status: 'Queued',
      baseContentRevision: 42,
      sourceId: 'source-1',
      sourceHash: 'a'.repeat(64),
      mode: 'RuleOnly',
      policy: 'Disabled',
      links: new SpaceAiGenerationRunLinksDto({
        self: '/run-1',
        proposals: '/run-1/proposals',
      }),
      reused: false,
      idempotentReplay: false,
    })
  })

  it('creates a RuleOnly run with frozen Draft and CAD preconditions', async () => {
    const wrapper = mount(DesignAiGenerationLauncherPanel, {
      props: { versionId: 'version-1', currentContentRevision: 42 },
      global,
    })
    await flushPromises()

    const button = wrapper.get('[data-test="create-rule-only-run"]')
    expect(button.attributes('disabled')).toBeUndefined()
    await button.trigger('click')
    await flushPromises()

    expect(aiProposalReviewApi.createGenerationRun).toHaveBeenCalledWith(
      'version-1',
      {
        sourceId: 'source-1',
        mappingProfileVersionId: 'mapping-1',
        rackGenerationProfileVersionId: null,
        mode: 'RuleOnly',
        expectedContentRevision: 42,
      },
      'version-row-version',
      expect.any(String),
    )
    expect(wrapper.emitted('created')?.[0]).toEqual(['run-1'])
    expect(success).toHaveBeenCalledWith('规则生成任务已排队')
  })

  it('does not offer raster or unconfirmed sources', async () => {
    vi.mocked(aiProposalReviewApi.getSources).mockResolvedValue({
      items: [new SpaceSourceDto({
        id: 'source-pdf',
        sourceType: 'Pdf',
        state: 'PreviewReady',
        mappingProfileId: 'mapping-1',
        mappingProfileVersion: 1,
      })],
    })
    const wrapper = mount(DesignAiGenerationLauncherPanel, {
      props: { versionId: 'version-1', currentContentRevision: 42 },
      global,
    })
    await flushPromises()

    expect(wrapper.get('[data-test="create-rule-only-run"]').attributes('disabled'))
      .toBeDefined()
  })
})
