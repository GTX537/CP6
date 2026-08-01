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
})
