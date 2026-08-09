import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '@/api/http'
import {
  planningComparisonApi,
  planningScenarioApi,
  planningSimulationApi,
} from '../planningScenario'

vi.mock('@/api/http', () => ({
  default: {
    get: vi.fn(),
    put: vi.fn(),
  },
}))

describe('planningScenarioApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(http.get).mockResolvedValue({})
    vi.mocked(http.put).mockResolvedValue({})
  })

  it('reads the production model and isolated branches', async () => {
    await planningScenarioApi.getModel('site/1')
    await planningScenarioApi.list('site/1', 25)

    expect(http.get).toHaveBeenNthCalledWith(
      1,
      '/space/design/v1/sites/site%2F1/model',
    )
    expect(http.get).toHaveBeenNthCalledWith(
      2,
      '/space/planning/v1/sites/site%2F1/scenario-branches',
      { params: { limit: 25 } },
    )
  })

  it('uses a caller-stable branch identity and pinned base', async () => {
    const request = {
      basePublishedVersionId: 'published-1',
      name: 'Peak season',
    }
    await planningScenarioApi.create('site/1', 'branch/1', request)

    expect(http.put).toHaveBeenCalledWith(
      '/space/planning/v1/sites/site%2F1/scenario-branches/branch%2F1',
      request,
    )
  })

  it('downloads a branch-scoped standard GLB exchange', async () => {
    await planningScenarioApi.downloadGlb('site/1', 'branch/1')

    expect(http.get).toHaveBeenCalledWith(
      '/space/planning/v1/sites/site%2F1' +
        '/scenario-branches/branch%2F1/exports/gltf',
      {
        responseType: 'blob',
        headers: { Accept: 'model/gltf-binary' },
      },
    )
  })

  it('uses branch-scoped caller-stable simulation run identities', async () => {
    const request = {
      name: 'Peak baseline',
      datasetId: 'dataset-1',
      defaultQuantityCapacity: 100,
      defaultConcurrentTaskCapacity: 1,
      throughputWindowMinutes: 60,
      distanceCostPerMeter: 1,
      laborCostPerHour: 20,
      congestionCostPerTaskHour: 5,
      currencyCode: 'CNY',
      locationCapacities: [],
    }

    await planningSimulationApi.list('site/1', 'branch/1', 25)
    await planningSimulationApi.get('site/1', 'branch/1', 'run/1')
    await planningSimulationApi.create(
      'site/1',
      'branch/1',
      'run/1',
      request,
    )

    const root = '/space/planning/v1/sites/site%2F1' +
      '/scenario-branches/branch%2F1/simulation-runs'
    expect(http.get).toHaveBeenNthCalledWith(
      1,
      root,
      { params: { limit: 25 } },
    )
    expect(http.get).toHaveBeenNthCalledWith(2, `${root}/run%2F1`)
    expect(http.put).toHaveBeenCalledWith(`${root}/run%2F1`, request)
  })

  it('uses site-scoped comparison and append-only decision identities', async () => {
    const comparisonRequest = {
      name: 'Peak options',
      baselineRunId: 'run-1',
      runIds: ['run-1', 'run-2'],
      minimumDistanceCoveragePercent: 95,
      maximumPeakCapacityUtilizationPercent: 100,
      maximumCongestionTaskHours: 1,
      maximumTotalCost: 10_000,
    }
    const decisionRequest = {
      outcome: 'Selected' as const,
      selectedRunId: 'run-2',
      rationale: 'Lower congestion within accepted cost.',
      supersedesDecisionId: 'decision-0',
    }
    const root = '/space/planning/v1/sites/site%2F1/comparisons'

    await planningComparisonApi.list('site/1', 25)
    await planningComparisonApi.get('site/1', 'comparison/1')
    await planningComparisonApi.create(
      'site/1',
      'comparison/1',
      comparisonRequest,
    )
    await planningComparisonApi.listDecisions('site/1', 'comparison/1', 20)
    await planningComparisonApi.getDecision(
      'site/1',
      'comparison/1',
      'decision/1',
    )
    await planningComparisonApi.createDecision(
      'site/1',
      'comparison/1',
      'decision/1',
      decisionRequest,
    )

    expect(http.get).toHaveBeenNthCalledWith(1, root, { params: { limit: 25 } })
    expect(http.get).toHaveBeenNthCalledWith(2, `${root}/comparison%2F1`)
    expect(http.put).toHaveBeenNthCalledWith(
      1,
      `${root}/comparison%2F1`,
      comparisonRequest,
    )
    expect(http.get).toHaveBeenNthCalledWith(
      3,
      `${root}/comparison%2F1/decisions`,
      { params: { limit: 20 } },
    )
    expect(http.get).toHaveBeenNthCalledWith(
      4,
      `${root}/comparison%2F1/decisions/decision%2F1`,
    )
    expect(http.put).toHaveBeenNthCalledWith(
      2,
      `${root}/comparison%2F1/decisions/decision%2F1`,
      decisionRequest,
    )
  })
})
