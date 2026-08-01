import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '@/api/http'
import { spaceRuntimeApi } from '../runtime'

vi.mock('@/api/http', () => ({
  default: {
    get: vi.fn(),
  },
}))

describe('spaceRuntimeApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(http.get).mockResolvedValue({})
  })

  it('serializes the current floor scope as repeated logical-id parameters', async () => {
    await spaceRuntimeApi.inventory('site-1', ['location-1', 'location-2', 'location-1'])

    expect(http.get).toHaveBeenCalledTimes(1)
    const [url, config] = vi.mocked(http.get).mock.calls[0]!
    expect(url).toBe('/space/design/v1/sites/site-1/runtime/inventory')
    expect(config?.params).toBeInstanceOf(URLSearchParams)
    expect((config?.params as URLSearchParams).getAll('locationLogicalId')).toEqual([
      'location-1',
      'location-2',
    ])
  })

  it('serializes normalized material, lot, and container locate criteria', async () => {
    await spaceRuntimeApi.locateInventory('site-1', {
      materialNumber: ' SKU-01 ',
      lotNumber: ' LOT-01 ',
      containerNumber: ' BOX-01 ',
    })

    const [url, config] = vi.mocked(http.get).mock.calls[0]!
    expect(url).toBe('/space/design/v1/sites/site-1/runtime/inventory/locate')
    const params = config?.params as URLSearchParams
    expect(params.get('materialNumber')).toBe('SKU-01')
    expect(params.get('lotNumber')).toBe('LOT-01')
    expect(params.get('containerNumber')).toBe('BOX-01')
  })

  it('omits blank locate criteria so the server can reject an empty request', async () => {
    await spaceRuntimeApi.locateInventory('site-1', {
      materialNumber: ' ',
      lotNumber: '',
    })

    const [, config] = vi.mocked(http.get).mock.calls[0]!
    expect([...(config?.params as URLSearchParams).keys()]).toEqual([])
  })
})
