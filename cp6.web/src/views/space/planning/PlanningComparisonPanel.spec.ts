// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { nextTick } from 'vue'
import ElementPlus, { ElMessage } from 'element-plus'
import {
  planningComparisonApi,
  planningSimulationApi,
} from '@/api/space/planningScenario'
import PlanningComparisonPanel from './PlanningComparisonPanel.vue'
import type {
  SpacePlanningComparison,
  SpacePlanningDecision,
  SpacePlanningScenarioBranch,
  SpacePlanningSimulationRunSummary,
} from '@/api/space/planningScenario'

vi.mock('@/api/space/planningScenario', () => ({
  planningComparisonApi: {
    list: vi.fn(),
    get: vi.fn(),
    create: vi.fn(),
    listDecisions: vi.fn(),
    createDecision: vi.fn(),
  },
  planningSimulationApi: {
    list: vi.fn(),
  },
}))

const branch = (id: string): SpacePlanningScenarioBranch => ({
  branchId: id,
  siteId: 'site-1',
  modelId: 'model-1',
  basePublishedVersionId: 'published-1',
  baseVersionNo: 'v0007',
  scenarioVersionId: `version-${id}`,
  scenarioVersionNo: `v-${id}`,
  name: `Option ${id}`,
  branchStatus: 'Ready',
  scenarioVersionStatus: 'Draft',
  cloneJobId: `job-${id}`,
  cloneJobStatus: 'Succeeded',
  createdAtUtc: '2026-08-02T12:00:00Z',
  createdBy: 'actor-1',
  definitionVersion: 'space-planning-scenario-v1',
  productionIsolated: true,
  limitations: [],
})

const runSummary = (id: string): SpacePlanningSimulationRunSummary => ({
  runId: id,
  datasetId: 'dataset-1',
  scenarioContentRevision: 7,
  name: `Run ${id}`,
  status: 'Completed',
  currencyCode: 'CNY',
  taskCount: 42,
  distanceCoveragePercent: 100,
  totalDistanceMeters: id === 'run-1' ? 1_000 : 900,
  overloadedLocationCount: id === 'run-1' ? 1 : 0,
  averageCompletedTasksPerHour: id === 'run-1' ? 10 : 12,
  totalCost: id === 'run-1' ? 2_000 : 2_100,
  createdAtUtc: '2026-08-02T13:00:00Z',
})

const comparison: SpacePlanningComparison = {
  comparisonId: 'comparison-1',
  siteId: 'site-1',
  modelId: 'model-1',
  basePublishedVersionId: 'published-1',
  baselineRunId: 'run-1',
  name: 'Peak options',
  status: 'Completed',
  definitionVersion: 'space-planning-comparison-v1',
  requestHash: 'a'.repeat(64),
  comparisonHash: 'b'.repeat(64),
  sourceDatasetHash: 'c'.repeat(64),
  currencyCode: 'CNY',
  historicalFromUtc: '2026-07-01T00:00:00Z',
  historicalToUtc: '2026-07-02T00:00:00Z',
  thresholds: {
    minimumDistanceCoveragePercent: 95,
    maximumPeakCapacityUtilizationPercent: 100,
    maximumCongestionTaskHours: 1,
    maximumTotalCost: 3_000,
  },
  entries: [
    entry('run-1', true, 1_000, 0, 2_000),
    entry('run-2', false, 900, -100, 2_100),
  ],
  automatedRanking: false,
  productionWriteAllowed: false,
  createdAtUtc: '2026-08-02T14:00:00Z',
  createdBy: 'actor-1',
  limitations: ['NO_AUTOMATED_RANKING_OR_RECOMMENDATION'],
}

const previousDecision: SpacePlanningDecision = {
  decisionId: 'decision-0',
  siteId: 'site-1',
  comparisonId: comparison.comparisonId,
  selectedRunId: 'run-1',
  supersedesDecisionId: null,
  outcome: 'Selected',
  rationale: 'Initial operational choice.',
  comparisonHash: comparison.comparisonHash,
  definitionVersion: comparison.definitionVersion,
  humanDecision: true,
  automatedRecommendation: false,
  productionWriteAllowed: false,
  createdAtUtc: '2026-08-02T15:00:00Z',
  createdBy: 'actor-1',
}

describe('PlanningComparisonPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(planningComparisonApi.list).mockResolvedValue({
      items: [{
        comparisonId: comparison.comparisonId,
        baselineRunId: comparison.baselineRunId,
        name: comparison.name,
        currencyCode: comparison.currencyCode,
        runCount: 2,
        riskCount: 1,
        createdAtUtc: comparison.createdAtUtc,
      }],
      isTruncated: false,
    })
    vi.mocked(planningComparisonApi.get).mockResolvedValue(comparison)
    vi.mocked(planningComparisonApi.listDecisions).mockResolvedValue({
      items: [],
      isTruncated: false,
    })
    vi.mocked(planningSimulationApi.list).mockImplementation(
      async (_siteId, branchId) => ({
        items: [runSummary(branchId === 'branch-1' ? 'run-1' : 'run-2')],
        isTruncated: false,
      }),
    )
    vi.mocked(planningComparisonApi.create).mockResolvedValue({
      outcome: 'Created',
      comparison,
    })
    vi.mocked(planningComparisonApi.createDecision).mockResolvedValue({
      outcome: 'Created',
      decision: { ...previousDecision, decisionId: 'decision-1' },
    })
    vi.spyOn(ElMessage, 'success').mockImplementation(() => undefined as never)
  })

  it('loads completed runs across isolated branches and renders unranked evidence', async () => {
    const wrapper = mountPanel()
    await flushPromises()

    expect(planningSimulationApi.list).toHaveBeenCalledTimes(2)
    await wrapper.find('[data-test="view-comparison"]').trigger('click')
    await flushPromises()

    const evidence = wrapper.find('[data-test="comparison-evidence"]')
    expect(evidence.text()).toContain('无自动排名')
    expect(evidence.text()).toContain('无生产回写')
    expect(evidence.text()).toContain('Run run-1')
    expect(evidence.text()).toContain('Run run-2')
    expect(evidence.text()).toContain('-100')
  })

  it('creates a comparison with an explicit baseline and caller thresholds', async () => {
    const wrapper = mountPanel()
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      name: string
      selectedRunIds: string[]
      baselineRunId: string
      createComparison: () => Promise<void>
    }
    vm.name = 'Review options'
    vm.selectedRunIds = ['run-1', 'run-2']
    vm.baselineRunId = 'run-1'
    await nextTick()
    await vm.createComparison()

    expect(planningComparisonApi.create).toHaveBeenCalledWith(
      'site-1',
      expect.stringMatching(
        /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/,
      ),
      {
        name: 'Review options',
        baselineRunId: 'run-1',
        runIds: ['run-1', 'run-2'],
        minimumDistanceCoveragePercent: 95,
        maximumPeakCapacityUtilizationPercent: 100,
        maximumCongestionTaskHours: 0,
        maximumTotalCost: null,
      },
    )
  })

  it('appends a human decision that supersedes the current head', async () => {
    vi.mocked(planningComparisonApi.listDecisions).mockResolvedValue({
      items: [previousDecision],
      isTruncated: false,
    })
    const wrapper = mountPanel()
    await flushPromises()
    await wrapper.find('[data-test="view-comparison"]').trigger('click')
    await flushPromises()

    const vm = wrapper.vm as unknown as {
      decisionOutcome: 'Selected' | 'Deferred' | 'RejectedAll'
      selectedDecisionRunId: string
      rationale: string
      createDecision: () => Promise<void>
    }
    vm.decisionOutcome = 'Selected'
    vm.selectedDecisionRunId = 'run-2'
    vm.rationale = 'Accept modest cost increase to remove overload risk.'
    await nextTick()
    await vm.createDecision()

    expect(planningComparisonApi.createDecision).toHaveBeenCalledWith(
      'site-1',
      'comparison-1',
      expect.stringMatching(
        /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/,
      ),
      {
        outcome: 'Selected',
        selectedRunId: 'run-2',
        rationale: 'Accept modest cost increase to remove overload risk.',
        supersedesDecisionId: 'decision-0',
      },
    )
  })
})

function entry(
  runId: string,
  isBaseline: boolean,
  distance: number,
  distanceDelta: number,
  cost: number,
) {
  return {
    sequenceNo: isBaseline ? 1 : 2,
    runId,
    branchId: runId === 'run-1' ? 'branch-1' : 'branch-2',
    scenarioVersionId: `version-${runId}`,
    scenarioContentRevision: 7,
    runName: `Run ${runId}`,
    runResultHash: runId.repeat(16).slice(0, 64),
    isBaseline,
    metrics: {
      distanceCoveragePercent: 100,
      totalDistanceMeters: distance,
      congestionTaskSeconds: 0,
      congestionTaskHours: 0,
      overloadedLocationCount: isBaseline ? 1 : 0,
      peakCapacityUtilizationPercent: isBaseline ? 110 : 90,
      averageCompletedTasksPerHour: isBaseline ? 10 : 12,
      peakCompletedTasksPerHour: isBaseline ? 15 : 18,
      totalCost: cost,
    },
    deltaFromBaseline: {
      distanceMeters: distanceDelta,
      congestionTaskSeconds: 0,
      overloadedLocationCount: isBaseline ? 0 : -1,
      peakCapacityUtilizationPercentagePoints: isBaseline ? 0 : -20,
      averageCompletedTasksPerHour: isBaseline ? 0 : 2,
      totalCost: isBaseline ? 0 : 100,
    },
    risks: isBaseline
      ? [{ code: 'CAPACITY_THRESHOLD_EXCEEDED', severity: 'Critical' as const }]
      : [],
  }
}

function mountPanel() {
  return mount(PlanningComparisonPanel, {
    props: {
      siteId: 'site-1',
      branches: [branch('branch-1'), branch('branch-2')],
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
