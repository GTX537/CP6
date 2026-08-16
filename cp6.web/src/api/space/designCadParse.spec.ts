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

  it('loads the server-owned CAD capability for a Site', async () => {
    vi.mocked(http.get).mockResolvedValue({})
    await designCadParseApi.getCadCapability('site-1')

    expect(http.get).toHaveBeenCalledWith(
      '/space/design/v1/sites/site-1/cad-capability',
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

  it.each([
    ['warehouse.dwg', 'Dwg'],
    ['warehouse.dxf', 'Dxf'],
  ])('uploads %s with the explicit %s format', async (fileName, sourceFormat) => {
    vi.mocked(http.post).mockResolvedValue({})
    const file = new File(['cad'], fileName)
    await designCadParseApi.upload('version-1', file)

    const upload = vi.mocked(http.post).mock.calls[0]!
    expect(upload[0]).toBe('/space/design/v1/versions/version-1/cad-sources')
    expect(upload[1]).toBeInstanceOf(FormData)
    expect((upload[1] as FormData).get('SourceFormat')).toBe(sourceFormat)
  })

  it('rejects unsupported extensions before sending an upload request', () => {
    const file = new File(['cad'], 'warehouse.pdf')

    expect(() => designCadParseApi.upload('version-1', file))
      .toThrow('CAD 导入仅支持 .dwg 或 .dxf 文件')
    expect(http.post).not.toHaveBeenCalled()
  })

  it('retries with an idempotency key', async () => {
    vi.mocked(http.post).mockResolvedValue({})
    await designCadParseApi.retry('version-1', 'source-1', 'job-1', 'retry-1')

    expect(http.post).toHaveBeenNthCalledWith(
      1,
      '/space/design/v1/versions/version-1/sources/source-1/cad-parses/job-1:retry',
      undefined,
      { headers: { 'Idempotency-Key': 'retry-1' } },
    )
  })

  it('previews a server-known mapping and starts only with the sealed request', async () => {
    vi.mocked(http.get).mockResolvedValue({})
    vi.mocked(http.post).mockResolvedValue({})
    const previewRequest = {
      floorLogicalId: 'floor-1',
      confirmedUnit: 'Millimeter',
      sourceOriginInSourceUnits: { x: 0, y: 0 },
      floorOriginMillimeters: { x: 0, y: 0, z: 0 },
      rotationZDegrees: 0,
      mappingProfileId: 'profile-1',
      mappingProfileVersion: 1,
      layerOverrides: [],
    }
    const startRequest = {
      preparationId: 'preparation-1',
      floorLogicalId: 'floor-1',
      confirmedUnit: 'Millimeter',
      confirmedScaleToMillimeters: 1,
      coordinateMetadataJson: '{}',
      coordinateTransformSha256: 'a'.repeat(64),
      mappingProfileId: 'profile-1',
      mappingProfileVersion: 1,
      mappingDefinitionSha256: 'b'.repeat(64),
      mappingPreviewSha256: 'c'.repeat(64),
    }

    await designCadParseApi.getPreparationStatus('version-1', 'source-1')
    await designCadParseApi.listMappingProfiles('version-1')
    await designCadParseApi.previewPreparation(
      'version-1',
      'source-1',
      previewRequest,
    )
    await designCadParseApi.start(
      'version-1',
      'source-1',
      startRequest,
      'start-1',
    )

    expect(http.get).toHaveBeenNthCalledWith(
      1,
      '/space/design/v1/versions/version-1/sources/source-1/cad-preparations/status',
    )
    expect(http.get).toHaveBeenNthCalledWith(
      2,
      '/space/design/v1/versions/version-1/cad-mapping-profiles',
    )
    expect(http.post).toHaveBeenNthCalledWith(
      1,
      '/space/design/v1/versions/version-1/sources/source-1/cad-preparations:preview',
      previewRequest,
    )
    expect(http.post).toHaveBeenNthCalledWith(
      2,
      '/space/design/v1/versions/version-1/sources/source-1/cad-parses',
      startRequest,
      { headers: { 'Idempotency-Key': 'start-1' } },
    )
  })
})
