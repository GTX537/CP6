import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '@/api/http'
import { designCadParseApi } from './designCadParse'

vi.mock('@/api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
  },
}))

describe('designCadParseApi', () => {
  beforeEach(() => vi.clearAllMocks())

  it('loads a server-built review workspace from the parse chain', async () => {
    vi.mocked(http.get).mockResolvedValue({})
    await designCadParseApi.getReviewWorkspace('version-1', 'source-1', 'job-1')

    expect(http.get).toHaveBeenCalledWith(
      '/space/design/v1/versions/version-1/sources/source-1/cad-parses/job-1/review-workspace',
    )
  })

  it('cancels the selected parse job without mutating the draft', async () => {
    vi.mocked(http.post).mockResolvedValue({})
    await designCadParseApi.cancel('version-1', 'source-1', 'job-1')

    expect(http.post).toHaveBeenCalledWith(
      '/space/design/v1/versions/version-1/sources/source-1/cad-parses/job-1:cancel',
    )
  })
})
