import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '@/api/http'
import {
  designExcelMappingApi,
  type SpaceExcelMappingDefinition,
} from './designExcelMapping'

vi.mock('@/api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
  },
}))

const definition: SpaceExcelMappingDefinition = {
  schemaVersion: 1,
  unknownColumnPolicy: 'Warning',
  emptyValuePolicy: 'Reject',
  duplicateRowPolicy: 'Reject',
  sheets: [],
}

describe('designExcelMappingApi', () => {
  beforeEach(() => vi.clearAllMocks())

  it('lists and retrieves immutable mapping versions', async () => {
    await designExcelMappingApi.listProfiles()
    await designExcelMappingApi.getProfile('profile/a', 3)

    expect(http.get).toHaveBeenNthCalledWith(
      1,
      '/space/design/v1/mapping-profiles/excel',
    )
    expect(http.get).toHaveBeenNthCalledWith(
      2,
      '/space/design/v1/mapping-profiles/excel/profile%2Fa',
      { params: { version: 3 } },
    )
  })

  it('previews header snapshots without sending cell bodies or files', async () => {
    const workbook = [{ sheetName: 'Vendor racks', headers: ['rack_code'] }]

    await designExcelMappingApi.preview(definition, workbook)

    expect(http.post).toHaveBeenCalledWith(
      '/space/design/v1/mapping-profiles/excel/preview',
      { definition, workbook },
    )
  })

  it('saves with an explicit idempotency key', async () => {
    const request = { name: 'Vendor A', definition }

    await designExcelMappingApi.save(request, 'mapping-request-1')

    expect(http.post).toHaveBeenCalledWith(
      '/space/design/v1/mapping-profiles/excel',
      request,
      { headers: { 'Idempotency-Key': 'mapping-request-1' } },
    )
  })
})
