import { beforeEach, describe, expect, it, vi } from 'vitest'
import { spaceRuntimeApi } from '@/api/space/runtime'
import { StockOverlay } from './StockOverlay'
import { binStatusToHex } from './stockModel'
import type {
  RuntimeLocationRef,
  RuntimeStockItem,
  SpaceRuntimeInventoryResponse,
  SpaceRuntimeSource,
  SpaceWarehouseAbcRank,
} from '@/types/space/runtime'

vi.mock('@/api/space/runtime', () => ({
  spaceRuntimeApi: {
    inventory: vi.fn(),
  },
}))

function fakeViewer() {
  return {
    setInstanceColor: vi.fn(),
    setInstanceColors: vi.fn(),
    requestRender: vi.fn(),
  }
}

const dto = (locationLogicalId: string, locationCode: string, binStatus: 0 | 1): RuntimeStockItem => ({
  locationLogicalId,
  locationCode,
  binStatus,
  qty: binStatus,
  allocatedQty: 0,
  capacity: null,
  topMaterial: null,
  productKinds: binStatus,
})

const real: SpaceRuntimeSource = {
  kind: 'Real',
  adapterId: 'cp6-wms-v1',
  dataSourceId: 'CP6_WMS',
  observedAtUtc: '2026-08-01T00:00:00Z',
  receivedAtUtc: '2026-08-01T00:00:02Z',
  delayMilliseconds: 2000,
  clockSkewMilliseconds: 0,
  isSimulated: false,
  isAvailable: true,
}

const simulated: SpaceRuntimeSource = {
  ...real,
  kind: 'Simulated',
  adapterId: 'space-standard-simulator-v1',
  dataSourceId: 'SPACE_STANDARD_SIMULATOR',
  isSimulated: true,
}

describe('StockOverlay', () => {
  beforeEach(() => vi.clearAllMocks())

  it('applies status colors directly by Space logical identity', () => {
    const viewer = fakeViewer()
    const overlay = new StockOverlay(viewer as never)
    overlay.setSnapshot([
      dto('location-1', 'A-01', 0),
      dto('location-2', 'A-02', 1),
    ], real)
    overlay.setMode('status')
    overlay.apply()

    expect(viewer.setInstanceColors).toHaveBeenCalledWith([
      { locationId: 'location-1', hex: binStatusToHex(0) },
      { locationId: 'location-2', hex: binStatusToHex(1) },
    ])
    expect(viewer.requestRender).toHaveBeenCalled()
  })

  it('falls back to individual color writes for older viewer handles', () => {
    const viewer = {
      setInstanceColor: vi.fn(),
      requestRender: vi.fn(),
    }
    const overlay = new StockOverlay(viewer as never)
    overlay.setSnapshot([dto('location-1', 'A-01', 1)], real)

    overlay.apply()

    expect(viewer.setInstanceColor).toHaveBeenCalledWith('location-1', binStatusToHex(1))
    expect(viewer.requestRender).toHaveBeenCalled()
  })

  it('off mode does not color', () => {
    const viewer = fakeViewer()
    const overlay = new StockOverlay(viewer as never)
    overlay.setSnapshot([dto('location-1', 'A-01', 1)], real)
    overlay.setMode('off')
    overlay.apply()
    expect(viewer.setInstanceColors).not.toHaveBeenCalled()
  })

  it('keeps a spatial filter active across stock modes and marks excluded locations', () => {
    const viewer = fakeViewer()
    const overlay = new StockOverlay(viewer as never)
    overlay.setSnapshot([
      dto('location-1', 'A-01', 1),
      dto('location-2', 'A-02', 1),
    ], real)
    overlay.setMode('off')

    overlay.setSpatialFilter(['location-2'])

    expect(overlay.spatialFilterActive).toBe(true)
    expect(viewer.setInstanceColors).toHaveBeenLastCalledWith([
      { locationId: 'location-1', hex: StockOverlay.FILTER_EXCLUDED_HEX },
      { locationId: 'location-2', hex: StockOverlay.FILTER_MATCH_HEX },
    ])
    overlay.clearSpatialFilter()
    expect(overlay.spatialFilterActive).toBe(false)
  })

  it('applies ABC ranks and keeps empty locations visually neutral', () => {
    const viewer = fakeViewer()
    const overlay = new StockOverlay(viewer as never)
    overlay.setSnapshot([
      dto('location-1', 'A-01', 1),
      dto('location-2', 'A-02', 1),
      dto('location-3', 'A-03', 0),
    ], real)
    const ranks = new Map<string, SpaceWarehouseAbcRank>([
      ['location-1', 'A'],
      ['location-2', 'C'],
    ])

    overlay.setAbcOverlay(ranks)

    expect(overlay.abcOverlayActive).toBe(true)
    expect(viewer.setInstanceColors).toHaveBeenLastCalledWith([
      { locationId: 'location-1', hex: StockOverlay.ABC_COLORS.A },
      { locationId: 'location-2', hex: StockOverlay.ABC_COLORS.C },
      { locationId: 'location-3', hex: StockOverlay.ABC_EMPTY_HEX },
    ])
  })

  it('gives the spatial filter precedence over ABC until the filter is cleared', () => {
    const viewer = fakeViewer()
    const overlay = new StockOverlay(viewer as never)
    overlay.setSnapshot([
      dto('location-1', 'A-01', 1),
      dto('location-2', 'A-02', 1),
    ], real)
    overlay.setAbcOverlay(new Map([['location-1', 'A' as const]]))

    overlay.setSpatialFilter(['location-2'])
    expect(viewer.setInstanceColors).toHaveBeenLastCalledWith([
      { locationId: 'location-1', hex: StockOverlay.FILTER_EXCLUDED_HEX },
      { locationId: 'location-2', hex: StockOverlay.FILTER_MATCH_HEX },
    ])

    overlay.clearSpatialFilter()
    overlay.apply()
    expect(viewer.setInstanceColors).toHaveBeenLastCalledWith([
      { locationId: 'location-1', hex: StockOverlay.ABC_COLORS.A },
      { locationId: 'location-2', hex: StockOverlay.ABC_EMPTY_HEX },
    ])
  })

  it('keeps ABC ranks across stock refreshes until explicitly cleared', async () => {
    const viewer = fakeViewer()
    const overlay = new StockOverlay(viewer as never)
    overlay.setSnapshot([dto('location-1', 'A-01', 1)], real)
    overlay.setAbcOverlay(new Map([['location-1', 'B' as const]]))
    vi.mocked(spaceRuntimeApi.inventory).mockResolvedValueOnce({
      siteId: 'site-1',
      publishedVersionId: 'version-1',
      warehouseCode: 'WH1',
      source: real,
      items: [],
    })

    await overlay.refresh('site-1', [
      { locationLogicalId: 'location-1', locationCode: 'A-01' },
    ])

    expect(viewer.setInstanceColors).toHaveBeenLastCalledWith([
      { locationId: 'location-1', hex: StockOverlay.ABC_COLORS.B },
    ])
    overlay.clearAbcOverlay()
    expect(overlay.abcOverlayActive).toBe(false)
  })

  it('gets selected stock by logical identity', () => {
    const overlay = new StockOverlay(fakeViewer() as never)
    overlay.setSnapshot([dto('location-1', 'A-01', 1)], real)
    expect(overlay.getStock('location-1')?.locationCode).toBe('A-01')
    expect(overlay.getStock('missing')).toBeNull()
  })

  it('uses simulated stock while preserving its marker', () => {
    const viewer = fakeViewer()
    const overlay = new StockOverlay(viewer as never)
    overlay.setSnapshot([dto('location-1', 'A-01', 1)], simulated)
    overlay.apply()
    expect(overlay.source.kind).toBe('Simulated')
    expect(overlay.getStock('location-1')).not.toBeNull()
    expect(viewer.setInstanceColors).toHaveBeenCalled()
  })

  it('does not treat unavailable source as empty real stock', () => {
    const viewer = fakeViewer()
    const overlay = new StockOverlay(viewer as never)
    overlay.setSnapshot([dto('location-1', 'A-01', 0)], {
      ...real,
      kind: 'Unavailable',
      isAvailable: false,
    })
    overlay.apply()
    expect(overlay.getStock('location-1')).toBeNull()
    expect(viewer.setInstanceColors).not.toHaveBeenCalled()
    expect(overlay.source.kind).toBe('Unavailable')
  })

  it('keeps the last successful snapshot when a later refresh throws', async () => {
    const viewer = fakeViewer()
    const overlay = new StockOverlay(viewer as never)
    overlay.setSnapshot([dto('location-1', 'A-01', 1)], real)
    vi.mocked(spaceRuntimeApi.inventory).mockRejectedValueOnce(new Error('transport failed'))

    await expect(overlay.refresh('site-1', [
      { locationLogicalId: 'location-1', locationCode: 'A-01' },
    ])).rejects.toThrow('transport failed')

    expect(overlay.source).toEqual(real)
    expect(overlay.getStock('location-1')).not.toBeNull()
  })

  it('queries and aggregates the current floor scope from the runtime endpoint', async () => {
    const locations: RuntimeLocationRef[] = [
      { locationLogicalId: 'location-1', locationCode: 'A-01' },
      { locationLogicalId: 'location-2', locationCode: 'A-02' },
    ]
    const response: SpaceRuntimeInventoryResponse = {
      siteId: 'site-1',
      publishedVersionId: 'version-1',
      warehouseCode: 'WH1',
      source: real,
      items: [{
        locationLogicalId: 'location-1',
        wmsLogicalId: 'wms-1',
        spaceLocationCode: 'A-01',
        wmsLocationCode: 'WMS-A-01',
        codeMatches: false,
        floorLogicalId: 'floor-1',
        floorCode: 'F1',
        floorName: 'Floor 1',
        floorLevel: 1,
        physicalQuantity: 4,
        allocatedQuantity: 1,
        materialNumber: 'SKU-1',
        lotNumber: null,
        containerNumber: null,
        ownerId: null,
      }],
    }
    vi.mocked(spaceRuntimeApi.inventory).mockResolvedValueOnce(response)
    const overlay = new StockOverlay(fakeViewer() as never)

    await overlay.refresh('site-1', locations)

    expect(spaceRuntimeApi.inventory).toHaveBeenCalledWith(
      'site-1',
      ['location-1', 'location-2'],
    )
    expect(overlay.getStock('location-1')).toMatchObject({ qty: 4, binStatus: 1 })
    expect(overlay.getStock('location-2')).toMatchObject({ qty: 0, binStatus: 0 })
  })

  it('does not turn an empty floor scope into an unbounded site query', async () => {
    const overlay = new StockOverlay(fakeViewer() as never)

    await overlay.refresh('site-1', [])

    expect(spaceRuntimeApi.inventory).not.toHaveBeenCalled()
    expect(overlay.source).toMatchObject({
      kind: 'Unavailable',
      dataSourceId: 'EMPTY_FLOOR_SCOPE',
    })
  })

  it('ignores a slower snapshot after a newer floor refresh starts', async () => {
    let resolveFirst!: (value: SpaceRuntimeInventoryResponse) => void
    const first = new Promise<SpaceRuntimeInventoryResponse>((resolve) => {
      resolveFirst = resolve
    })
    const newerSource = {
      ...real,
      dataSourceId: 'NEWER_WMS',
      receivedAtUtc: '2026-08-01T00:00:10Z',
    }
    const response = (source: SpaceRuntimeSource): SpaceRuntimeInventoryResponse => ({
      siteId: 'site-1',
      publishedVersionId: 'version-1',
      warehouseCode: 'WH1',
      source,
      items: [],
    })
    vi.mocked(spaceRuntimeApi.inventory)
      .mockImplementationOnce(() => first)
      .mockResolvedValueOnce(response(newerSource))
    const overlay = new StockOverlay(fakeViewer() as never)
    const oldRefresh = overlay.refresh('site-1', [
      { locationLogicalId: 'old-location', locationCode: 'OLD' },
    ])

    const newRefresh = overlay.refresh('site-1', [
      { locationLogicalId: 'new-location', locationCode: 'NEW' },
    ])
    expect(await newRefresh).toBe(true)
    resolveFirst(response(real))

    expect(await oldRefresh).toBe(false)
    expect(overlay.source.dataSourceId).toBe('NEWER_WMS')
    expect(overlay.getStock('new-location')).not.toBeNull()
    expect(overlay.getStock('old-location')).toBeNull()
  })
})
