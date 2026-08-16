import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../http'
import { designProjectApi } from './designProject'

vi.mock('../http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
  },
}))

describe('designProjectApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('loads the active design project and its floors', async () => {
    vi.mocked(http.get).mockResolvedValue({} as never)

    await designProjectApi.getModel('site/1')
    await designProjectApi.getVersion('version/1')
    await designProjectApi.getFloors('version/1')

    expect(http.get).toHaveBeenNthCalledWith(
      1,
      '/space/design/v1/sites/site%2F1/model',
    )
    expect(http.get).toHaveBeenNthCalledWith(
      2,
      '/space/design/v1/versions/version%2F1',
    )
    expect(http.get).toHaveBeenNthCalledWith(
      3,
      '/space/design/v1/versions/version%2F1/floors',
    )
  })

  it('creates an explicit blank version and floor with idempotency fences', async () => {
    vi.mocked(http.post).mockResolvedValue({} as never)

    await designProjectApi.createBlankVersion(
      'site-1',
      'Blank warehouse',
      'version-key',
    )
    await designProjectApi.createFloor(
      'version-1',
      {
        floorCode: 'F1',
        name: 'Ground floor',
        level: 1,
        elevation: 0,
        height: 6000,
        expectedContentRevision: 0,
      },
      'floor-key',
    )

    expect(http.post).toHaveBeenNthCalledWith(
      1,
      '/space/design/v1/sites/site-1/versions',
      {
        name: 'Blank warehouse',
        basedOnVersionId: null,
        createMode: 'Blank',
      },
      { headers: { 'Idempotency-Key': 'version-key' } },
    )
    expect(http.post).toHaveBeenNthCalledWith(
      2,
      '/space/design/v1/versions/version-1/floors',
      {
        floorCode: 'F1',
        name: 'Ground floor',
        level: 1,
        elevation: 0,
        height: 6000,
        expectedContentRevision: 0,
      },
      { headers: { 'Idempotency-Key': 'floor-key' } },
    )
  })

  it('loads and previews the immutable warehouse template catalog', async () => {
    vi.mocked(http.get).mockResolvedValue({} as never)
    vi.mocked(http.post).mockResolvedValue({} as never)

    await designProjectApi.getWarehouseTemplates('System')
    await designProjectApi.previewWarehouseTemplate(
      'template/1',
      'version-1',
    )

    expect(http.get).toHaveBeenCalledWith(
      '/space/design/v1/templates',
      { params: { scope: 'System' } },
    )
    expect(http.post).toHaveBeenCalledWith(
      '/space/design/v1/templates/template%2F1/instantiate',
      { templateVersionId: 'version-1' },
    )
  })

  it('creates a tenant warehouse template with an idempotency key', async () => {
    vi.mocked(http.post).mockResolvedValue({} as never)
    const request = {
      templateCode: 'PRIVATE-01',
      name: 'Private warehouse',
      schemaVersion: 1,
      floors: [],
      zones: [],
      aisles: [],
      racks: [],
    }

    await designProjectApi.createTenantWarehouseTemplate(
      request,
      'tenant-template-key',
    )

    expect(http.post).toHaveBeenCalledWith(
      '/space/design/v1/templates',
      request,
      { headers: { 'Idempotency-Key': 'tenant-template-key' } },
    )
  })

  it('applies one sealed template floor to a leased Draft floor', async () => {
    vi.mocked(http.post).mockResolvedValue({} as never)
    const request = {
      schemaVersion: 1,
      siteId: 'site-1',
      templateVersionId: 'template-version-1',
      proposalHash: 'a'.repeat(64),
      templateFloorKey: 'F1',
      commandBatchId: 'batch-1',
      clientInstanceId: 'client-1',
      leaseId: 'lease-1',
      expectedFloorRevision: 2,
      expectedContentRevision: 3,
    }

    await designProjectApi.applyWarehouseTemplateFloor(
      'version/1',
      'floor/1',
      'template/1',
      request,
    )

    expect(http.post).toHaveBeenCalledWith(
      '/space/design/v1/versions/version%2F1/floors/floor%2F1/' +
        'templates/template%2F1:apply',
      request,
    )
  })
})
