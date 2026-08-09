// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import ElementPlus, { ElMessage } from 'element-plus'
import {
  planningDatasetApi,
  planningScenarioApi,
} from '@/api/space/planningScenario'
import PlanningScenarioPanel from './PlanningScenarioPanel.vue'
import type {
  CreateSpacePlanningScenarioBranchResponse,
  SpacePlanningScenarioBranch,
} from '@/api/space/planningScenario'

vi.mock('@/api/space/planningScenario', () => ({
  planningScenarioApi: {
    list: vi.fn(),
    create: vi.fn(),
    downloadGlb: vi.fn(),
  },
  planningDatasetApi: {
    list: vi.fn(),
    get: vi.fn(),
    create: vi.fn(),
  },
}))

const branch: SpacePlanningScenarioBranch = {
  branchId: 'branch-1',
  siteId: 'site-1',
  modelId: 'model-1',
  basePublishedVersionId: 'published-1',
  baseVersionNo: 'v0007',
  scenarioVersionId: 'scenario-1',
  scenarioVersionNo: 'v0008',
  name: 'Peak season',
  branchStatus: 'Ready',
  scenarioVersionStatus: 'Draft',
  cloneJobId: 'job-123456789',
  cloneJobStatus: 'Succeeded',
  createdAtUtc: '2026-07-29T19:00:00Z',
  createdBy: 'actor-1',
  definitionVersion: 'space-planning-scenario-v1',
  productionIsolated: true,
  limitations: [
    'SCENARIO_VERSION_CANNOT_ENTER_PRODUCTION_PUBLISH_LIFECYCLE',
  ],
}

describe('PlanningScenarioPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(planningScenarioApi.list)
      .mockResolvedValue({ items: [branch], isTruncated: false })
    vi.mocked(planningScenarioApi.create)
      .mockResolvedValue({
        outcome: 'Created',
        branch,
      } satisfies CreateSpacePlanningScenarioBranchResponse)
    vi.mocked(planningScenarioApi.downloadGlb)
      .mockResolvedValue(new Blob(['glTF']))
    vi.mocked(planningDatasetApi.list)
      .mockResolvedValue({ items: [], isTruncated: false })
    vi.spyOn(ElMessage, 'success').mockImplementation(() => undefined as never)
  })

  it('shows pinned lineage and explicit production isolation', async () => {
    const wrapper = mountPanel()
    await flushPromises()

    expect(planningScenarioApi.list).toHaveBeenCalledWith('site-1')
    expect(wrapper.text()).toContain('不会占用生产草稿')
    expect(wrapper.text()).toContain('v0007')
    expect(wrapper.text()).toContain('v0008')
    expect(wrapper.text()).toContain('Isolated')
  })

  it('creates a caller-identified branch from the current production version', async () => {
    const wrapper = mountPanel()
    await flushPromises()

    await wrapper.find('input').setValue('Automation option')
    await wrapper.find('[data-test="create-scenario"]').trigger('click')
    await flushPromises()

    expect(planningScenarioApi.create).toHaveBeenCalledWith(
      'site-1',
      expect.stringMatching(
        /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/,
      ),
      {
        basePublishedVersionId: 'published-1',
        name: 'Automation option',
      },
    )
  })

  it('opens historical imports only for a ready isolated clone', async () => {
    vi.mocked(planningScenarioApi.list).mockResolvedValue({
      items: [
        branch,
        {
          ...branch,
          branchId: 'branch-pending',
          branchStatus: 'Cloning',
          cloneJobStatus: 'Running',
        },
      ],
      isTruncated: false,
    })
    const wrapper = mountPanel()
    await flushPromises()

    const buttons = wrapper.findAll('[data-test="open-datasets"]')
    expect(buttons).toHaveLength(1)
    await buttons[0]!.trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-test="historical-dataset-panel"]').exists())
      .toBe(true)
    expect(planningDatasetApi.list).toHaveBeenCalledWith('site-1', 'branch-1')
  })

  it('downloads GLB only for a ready isolated branch', async () => {
    const createObjectUrl = vi.fn(() => 'blob:exchange')
    const revokeObjectUrl = vi.fn()
    vi.stubGlobal('URL', {
      createObjectURL: createObjectUrl,
      revokeObjectURL: revokeObjectUrl,
    })
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click')
      .mockImplementation(() => undefined)
    const wrapper = mountPanel()
    await flushPromises()

    const button = wrapper.find('[data-test="download-exchange"]')
    expect(button.exists()).toBe(true)
    await button.trigger('click')
    await flushPromises()

    expect(planningScenarioApi.downloadGlb)
      .toHaveBeenCalledWith('site-1', 'branch-1')
    expect(createObjectUrl).toHaveBeenCalled()
    expect(click).toHaveBeenCalled()
    expect(revokeObjectUrl).toHaveBeenCalledWith('blob:exchange')
  })
})

function mountPanel() {
  return mount(PlanningScenarioPanel, {
    props: {
      siteId: 'site-1',
      basePublishedVersionId: 'published-1',
    },
    global: {
      plugins: [
        ElementPlus,
        createI18n({
          legacy: false,
          locale: 'zh-CN',
          messages: { 'zh-CN': {} },
        }),
      ],
      directives: {
        permission: {},
      },
      stubs: {
        PlanningComparisonPanel: true,
      },
    },
  })
}
