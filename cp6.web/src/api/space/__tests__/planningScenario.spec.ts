import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '@/api/http'
import { planningScenarioApi, planningSimulationApi } from '../planningScenario'

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
})
