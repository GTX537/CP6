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

  it('applies a selected changeset with lease and revision fences', async () => {
    vi.mocked(http.post).mockResolvedValue({})
    const request = {
      commandBatchId: 'batch-1',
      clientInstanceId: 'client-1',
      leaseId: 'lease-1',
      expectedFloorRevision: 7,
      expectedContentRevision: 11,
      expectedContentHash: 'a'.repeat(64),
      workspaceSha256: 'b'.repeat(64),
      changeIds: ['change-1'],
    }

    await designCadParseApi.applyReviewChanges(
      'version-1',
      'source-1',
      'job-1',
      request,
    )

    expect(http.post).toHaveBeenCalledWith(
      '/space/design/v1/versions/version-1/sources/source-1/cad-parses/job-1/review-workspace:apply',
      request,
    )
  })

  it('cancels the selected parse job without mutating the draft', async () => {
    vi.mocked(http.post).mockResolvedValue({})
    await designCadParseApi.cancel('version-1', 'source-1', 'job-1')

    expect(http.post).toHaveBeenCalledWith(
      '/space/design/v1/versions/version-1/sources/source-1/cad-parses/job-1:cancel',
    )
  })

  it('uploads DWG/DXF and retries with an idempotency key', async () => {
    vi.mocked(http.post).mockResolvedValue({})
    const file = new File(['cad'], 'warehouse.dxf')
    await designCadParseApi.upload('version-1', file)
    await designCadParseApi.retry('version-1', 'source-1', 'job-1', 'retry-1')

    const upload = vi.mocked(http.post).mock.calls[0]!
    expect(upload[0]).toBe('/space/design/v1/versions/version-1/cad-sources')
    expect(upload[1]).toBeInstanceOf(FormData)
    expect((upload[1] as FormData).get('SourceFormat')).toBe('Dxf')
    expect(http.post).toHaveBeenNthCalledWith(
      2,
      '/space/design/v1/versions/version-1/sources/source-1/cad-parses/job-1:retry',
      undefined,
      { headers: { 'Idempotency-Key': 'retry-1' } },
    )
  })
})
