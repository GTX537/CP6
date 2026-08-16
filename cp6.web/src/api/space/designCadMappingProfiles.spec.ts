import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '@/api/http'
import { designCadMappingProfileApi } from './designCadMappingProfiles'

vi.mock('@/api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
  },
}))

describe('designCadMappingProfileApi', () => {
  beforeEach(() => vi.clearAllMocks())

  it('lists, reads and saves versioned CAD mapping profiles', async () => {
    vi.mocked(http.get).mockResolvedValue({})
    vi.mocked(http.post).mockResolvedValue({})
    const request = {
      name: 'Tenant CAD',
      isEnabled: true,
      rules: [],
      copyFromProfileId: 'system-1',
      copyFromVersion: 1,
    }

    await designCadMappingProfileApi.listProfiles()
    await designCadMappingProfileApi.getProfile('profile/a', 2)
    await designCadMappingProfileApi.save(request, 'idempotency-1')

    expect(http.get).toHaveBeenNthCalledWith(
      1,
      '/space/design/v1/mapping-profiles/cad',
    )
    expect(http.get).toHaveBeenNthCalledWith(
      2,
      '/space/design/v1/mapping-profiles/cad/profile%2Fa',
      { params: { version: 2 } },
    )
    expect(http.post).toHaveBeenCalledWith(
      '/space/design/v1/mapping-profiles/cad',
      request,
      { headers: { 'Idempotency-Key': 'idempotency-1' } },
    )
  })
})
