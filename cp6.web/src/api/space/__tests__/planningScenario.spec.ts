import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '@/api/http'
import { planningScenarioApi } from '../planningScenario'

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
})
