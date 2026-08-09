import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '@/api/http'
import { designModelingTemplateApi } from './designModelingTemplate'

vi.mock('@/api/http', () => ({
  default: {
    get: vi.fn(),
  },
}))

describe('designModelingTemplateApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(http.get).mockResolvedValue(new Blob())
  })

  it('downloads the global standard Excel template as a blob', async () => {
    await designModelingTemplateApi.downloadStandardExcel()

    expect(http.get).toHaveBeenCalledWith(
      '/space/design/v1/modeling-templates/excel/standard',
      {
        responseType: 'blob',
        headers: {
          Accept:
            'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
        },
      },
    )
  })
})
