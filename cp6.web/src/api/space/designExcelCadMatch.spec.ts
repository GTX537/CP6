import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '@/api/http'
import { designExcelCadMatchApi } from './designExcelCadMatch'

vi.mock('@/api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
  },
}))

describe('designExcelCadMatchApi', () => {
  beforeEach(() => vi.clearAllMocks())

  it('starts only a pinned server-side match with an idempotency key', async () => {
    const request = {
      excelSourceId: 'excel-1',
      preflightJobId: 'preflight-1',
      cadSourceId: 'cad-1',
      cadParseJobId: 'cad-job-1',
      floorLogicalId: 'floor-1',
      expectedContentRevision: 7,
    }

    await designExcelCadMatchApi.start('version-1', request, 'match-1')

    expect(http.post).toHaveBeenCalledWith(
      '/space/design/v1/versions/version-1/excel-cad-matches',
      request,
      { headers: { 'Idempotency-Key': 'match-1' } },
    )
  })

  it('reads authoritative rows through protected server-side filters', async () => {
    await designExcelCadMatchApi.get('version-1', 'job-1', {
      disposition: 'Conflict',
      rackCode: 'R-001',
      onlyLocatable: true,
      limit: 50,
      cursor: 'next-1',
    })

    expect(http.get).toHaveBeenCalledWith(
      '/space/design/v1/versions/version-1/excel-cad-matches/job-1',
      {
        params: {
          disposition: 'Conflict',
          rackCode: 'R-001',
          onlyLocatable: true,
          limit: 50,
          cursor: 'next-1',
        },
      },
    )
  })

  it('confirms an exact authoritative artifact with an idempotency key', async () => {
    const request = {
      confirmed: true,
      artifactId: 'artifact-1',
      artifactPayloadSha256: 'a'.repeat(64),
      expectedContentRevision: 7,
    }

    await designExcelCadMatchApi.confirm(
      'version-1',
      'match-1',
      request,
      'apply-1',
    )

    expect(http.post).toHaveBeenCalledWith(
      '/space/design/v1/versions/version-1/excel-cad-matches/match-1/confirmations',
      request,
      { headers: { 'Idempotency-Key': 'apply-1' } },
    )
  })

  it('reads typed confirmation status from the protected match chain', async () => {
    await designExcelCadMatchApi.getConfirmation(
      'version-1',
      'match-1',
      'apply-1',
    )

    expect(http.get).toHaveBeenCalledWith(
      '/space/design/v1/versions/version-1/excel-cad-matches/match-1/confirmations/apply-1',
    )
  })
})
