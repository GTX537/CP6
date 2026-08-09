import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '@/api/http'
import { designExcelPreflightApi } from './designExcelPreflight'

vi.mock('@/api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
  },
}))

describe('designExcelPreflightApi', () => {
  beforeEach(() => vi.clearAllMocks())

  it('uploads the workbook as bounded multipart data', async () => {
    const file = new File(['xlsx'], 'warehouse.xlsx', {
      type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    })

    await designExcelPreflightApi.upload('version-1', file)

    const call = vi.mocked(http.post).mock.calls[0]
    expect(call).toBeDefined()
    const [url, form, options] = call!
    expect(url).toBe('/space/design/v1/versions/version-1/excel-sources')
    expect(form).toBeInstanceOf(FormData)
    const uploaded = (form as FormData).get('file') as File
    expect(uploaded.name).toBe(file.name)
    expect(uploaded.size).toBe(file.size)
    expect(options).toEqual({ timeout: 120_000 })
  })

  it('starts a pinned preflight with an idempotency key', async () => {
    const request = { mappingProfileId: 'profile-1', mappingProfileVersion: 3 }

    await designExcelPreflightApi.start(
      'version-1',
      'source-1',
      request,
      'preflight-1',
    )

    expect(http.post).toHaveBeenCalledWith(
      '/space/design/v1/versions/version-1/sources/source-1/excel-preflights',
      request,
      { headers: { 'Idempotency-Key': 'preflight-1' } },
    )
  })

  it('reads located issues and downloads the protected csv report', async () => {
    await designExcelPreflightApi.get('version-1', 'source-1', 'job-1', 50)
    await designExcelPreflightApi.downloadReport(
      'version-1',
      'source-1',
      'job-1',
    )

    const base =
      '/space/design/v1/versions/version-1/sources/source-1/excel-preflights/job-1'
    expect(http.get).toHaveBeenNthCalledWith(1, base, {
      params: { issueLimit: 50 },
    })
    expect(http.get).toHaveBeenNthCalledWith(2, `${base}/report`, {
      responseType: 'blob',
      headers: { Accept: 'text/csv' },
    })
  })
})
