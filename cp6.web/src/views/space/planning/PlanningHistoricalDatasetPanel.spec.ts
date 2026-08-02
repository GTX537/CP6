// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import ElementPlus, { ElMessage } from 'element-plus'
import { planningDatasetApi } from '@/api/space/planningScenario'
import PlanningHistoricalDatasetPanel from './PlanningHistoricalDatasetPanel.vue'
import type {
  CreateSpacePlanningHistoricalDatasetResponse,
  SpacePlanningHistoricalDataset,
} from '@/api/space/planningScenario'

vi.mock('@/api/space/planningScenario', () => ({
  planningDatasetApi: {
    list: vi.fn(),
    get: vi.fn(),
    create: vi.fn(),
  },
}))

const dataset: SpacePlanningHistoricalDataset = {
  datasetId: 'dataset-1',
  branchId: 'branch-1',
  siteId: 'site-1',
  scenarioVersionId: 'scenario-1',
  name: 'June replay',
  taskCount: 42,
  sourceDatasetHash: 'c'.repeat(64),
  definitionVersion: 'space-planning-historical-dataset-v1',
  deidentificationVersion: 'sha256-upstream-token-v1',
  deidentified: true,
  productionWriteAllowed: false,
  replayClock: {
    historicalFromUtc: '2026-06-01T00:00:00Z',
    historicalToUtc: '2026-06-02T00:00:00Z',
    replayStartUtc: '2026-07-29T12:00:00Z',
    replayEndUtc: '2026-07-29T15:00:00Z',
    replaySpeedFactor: 8,
    historicalDurationSeconds: 86_400,
    replayDurationSeconds: 10_800,
  },
  tasks: [],
  createdAtUtc: '2026-07-29T12:00:00Z',
  createdBy: 'actor-1',
  limitations: ['DATASET_AND_REPLAY_RESULTS_CANNOT_WRITE_PRODUCTION'],
}

describe('PlanningHistoricalDatasetPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(planningDatasetApi.list).mockResolvedValue({
      items: [{
        datasetId: dataset.datasetId,
        branchId: dataset.branchId,
        scenarioVersionId: dataset.scenarioVersionId,
        name: dataset.name,
        taskCount: dataset.taskCount,
        historicalFromUtc: dataset.replayClock.historicalFromUtc,
        historicalToUtc: dataset.replayClock.historicalToUtc,
        replayStartUtc: dataset.replayClock.replayStartUtc,
        replayEndUtc: dataset.replayClock.replayEndUtc,
        replaySpeedFactor: dataset.replayClock.replaySpeedFactor,
        createdAtUtc: dataset.createdAtUtc,
      }],
      isTruncated: false,
    })
    vi.mocked(planningDatasetApi.get).mockResolvedValue(dataset)
    vi.mocked(planningDatasetApi.create).mockResolvedValue({
      outcome: 'Created',
      dataset,
    } satisfies CreateSpacePlanningHistoricalDatasetResponse)
    vi.spyOn(ElMessage, 'success').mockImplementation(() => undefined as never)
  })

  it('shows immutable replay evidence with an explicit production guard', async () => {
    const wrapper = mountPanel()
    await flushPromises()

    expect(planningDatasetApi.list).toHaveBeenCalledWith('site-1', 'branch-1')
    expect(wrapper.text()).toContain('June replay')
    expect(wrapper.text()).toContain('永不写入生产')

    await wrapper.find('[data-test="view-dataset"]').trigger('click')
    await flushPromises()

    expect(planningDatasetApi.get).toHaveBeenCalledWith(
      'site-1',
      'branch-1',
      'dataset-1',
    )
    expect(wrapper.find('[data-test="dataset-evidence"]').text())
      .toContain('无生产回写')
  })

  it('imports only after the caller attests irreversible deidentification', async () => {
    const wrapper = mountPanel()
    await flushPromises()
    expect(wrapper.find('[data-test="create-dataset"]').attributes('disabled'))
      .toBeDefined()

    const payload = {
      name: 'July replay',
      historicalFromUtc: '2026-07-01T08:00:00Z',
      historicalToUtc: '2026-07-01T12:00:00Z',
      replayStartUtc: '2026-07-29T12:00:00Z',
      replaySpeedFactor: 8,
      sourceDatasetHash: 'c'.repeat(64),
      confirmDeidentified: false,
      tasks: [{
        taskToken: 'a'.repeat(64),
        workerToken: null,
        taskType: 'Pick',
        outcome: 'Completed',
        originalCreatedAtUtc: '2026-07-01T09:00:00Z',
        originalCompletedAtUtc: '2026-07-01T09:30:00Z',
        fromLocationLogicalId: null,
        toLocationLogicalId: '11111111-1111-1111-1111-111111111111',
        quantity: 1,
      }],
    }
    await wrapper.find('[data-test="dataset-json"]')
      .setValue(JSON.stringify(payload))
    await wrapper.find('[data-test="confirm-deidentified"] input')
      .setValue(true)
    await wrapper.find('[data-test="create-dataset"]').trigger('click')
    await flushPromises()

    expect(planningDatasetApi.create).toHaveBeenCalledWith(
      'site-1',
      'branch-1',
      expect.stringMatching(
        /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/,
      ),
      expect.objectContaining({
        name: 'July replay',
        confirmDeidentified: true,
        tasks: [expect.objectContaining({ taskToken: 'a'.repeat(64) })],
      }),
    )
  })
})

function mountPanel() {
  return mount(PlanningHistoricalDatasetPanel, {
    props: {
      siteId: 'site-1',
      branchId: 'branch-1',
      branchName: 'Peak season',
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
      directives: { permission: {} },
    },
  })
}
