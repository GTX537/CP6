import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '@/api/http'
import { designSourcesApi } from './designSources'

vi.mock('@/api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
  },
}))

describe('designSourcesApi', () => {
  beforeEach(() => vi.clearAllMocks())

  it('loads the removal preview from Design V1', async () => {
    vi.mocked(http.get).mockResolvedValue({})

    await designSourcesApi.getRemovalPreview('version-1', 'source-1')

    expect(http.get).toHaveBeenCalledWith(
      '/space/design/v1/versions/version-1/sources/source-1/removal-preview',
    )
  })

  it('removes with both revision fences and an idempotency key', async () => {
    vi.mocked(http.post).mockResolvedValue({})

    await designSourcesApi.remove(
      'version-1',
      'source-1',
      {
        expectedContentRevision: 12,
        expectedSourceRowVersion: 'AAAAAAAAAAE=',
      },
      'remove-key',
    )

    expect(http.post).toHaveBeenCalledWith(
      '/space/design/v1/versions/version-1/sources/source-1:remove',
      {
        expectedContentRevision: 12,
        expectedSourceRowVersion: 'AAAAAAAAAAE=',
      },
      { headers: { 'Idempotency-Key': 'remove-key' } },
    )
  })
})
