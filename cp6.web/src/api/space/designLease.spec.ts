import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '@/api/http'
import { designLeaseApi } from './designLease'

vi.mock('@/api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
  },
}))

describe('designLeaseApi', () => {
  beforeEach(() => vi.clearAllMocks())

  it('uses the frozen acquire and renew routes', async () => {
    vi.mocked(http.post).mockResolvedValue({})
    await designLeaseApi.acquire('version-1', 'floor-1', 'client-1')
    await designLeaseApi.renew(
      'version-1',
      'floor-1',
      'lease-1',
      'client-1',
    )

    expect(http.post).toHaveBeenNthCalledWith(
      1,
      '/space/design/v1/versions/version-1/floors/floor-1/lease',
      { clientInstanceId: 'client-1' },
    )
    expect(http.post).toHaveBeenNthCalledWith(
      2,
      '/space/design/v1/versions/version-1/floors/floor-1/lease/lease-1:renew',
      { clientInstanceId: 'client-1' },
    )
  })

  it('sends a reason when taking over a lease', async () => {
    vi.mocked(http.post).mockResolvedValue({})
    await designLeaseApi.takeover(
      'version-1',
      'floor-1',
      'client-1',
      'Owner approved handover',
    )

    expect(http.post).toHaveBeenCalledWith(
      '/space/design/v1/versions/version-1/floors/floor-1/lease:takeover',
      { clientInstanceId: 'client-1', reason: 'Owner approved handover' },
    )
  })
})
