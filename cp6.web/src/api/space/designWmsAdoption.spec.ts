import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '@/api/http'
import { designWmsAdoptionApi } from './designWmsAdoption'

vi.mock('@/api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
  },
}))

describe('designWmsAdoptionApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(http.get).mockResolvedValue({})
    vi.mocked(http.post).mockResolvedValue({})
  })

  it('refreshes and pages the version-scoped WMS catalog', async () => {
    await designWmsAdoptionApi.refresh('version-1')
    await designWmsAdoptionApi.list('version-1', {
      status: 'Unbound',
      limit: 100,
      cursor: 'next-page',
    })

    expect(http.post).toHaveBeenCalledWith(
      '/space/design/v1/versions/version-1/wms-adoption/refresh',
    )
    expect(http.get).toHaveBeenCalledWith(
      '/space/design/v1/versions/version-1/wms-adoption/locations',
      {
        params: {
          status: 'Unbound',
          limit: 100,
          cursor: 'next-page',
        },
      },
    )
  })

  it('sends rowversion-protected single, batch, and place commands', async () => {
    await designWmsAdoptionApi.bind(
      'version-1',
      'adoption-1',
      'location-1',
      'rowversion-1',
    )
    await designWmsAdoptionApi.bindBatch('version-1', [
      {
        adoptionId: 'adoption-2',
        locationLogicalId: 'location-2',
        expectedRowVersion: 'rowversion-2',
      },
    ])
    await designWmsAdoptionApi.place('version-1', 'adoption-3', {
      floorLogicalId: 'floor-1',
      rackLogicalId: 'rack-1',
      column: 2,
      level: 3,
      depth: 1,
      expectedRowVersion: 'rowversion-3',
    })

    expect(vi.mocked(http.post).mock.calls).toEqual([
      [
        '/space/design/v1/versions/version-1/wms-adoption/locations/adoption-1/bind',
        {
          locationLogicalId: 'location-1',
          expectedRowVersion: 'rowversion-1',
        },
      ],
      [
        '/space/design/v1/versions/version-1/wms-adoption/bindings:batch',
        {
          items: [
            {
              adoptionId: 'adoption-2',
              locationLogicalId: 'location-2',
              expectedRowVersion: 'rowversion-2',
            },
          ],
        },
      ],
      [
        '/space/design/v1/versions/version-1/wms-adoption/locations/adoption-3/place',
        {
          floorLogicalId: 'floor-1',
          rackLogicalId: 'rack-1',
          column: 2,
          level: 3,
          depth: 1,
          expectedRowVersion: 'rowversion-3',
        },
      ],
    ])
  })
})
