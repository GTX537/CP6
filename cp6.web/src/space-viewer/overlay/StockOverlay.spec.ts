import { describe, it, expect, vi } from 'vitest'
import { StockOverlay } from './StockOverlay'
import { binStatusToHex } from './stockModel'
import type { WmsStockDto } from '@/types/space/overlay'
import type { SpaceDataSource } from '@/types/space/dataSource'

function fakeViewer() {
  return {
    setInstanceColor: vi.fn(),
    requestRender: vi.fn(),
    getLocationIdByCode: (code: string) => code,
  }
}

const dto = (locationCode: string, binStatus: number): WmsStockDto => ({
  locationCode,
  binStatus,
  qty: 1,
  allocatedQty: 0,
  capacity: 10,
  topMaterial: null,
  productKinds: 1,
})

const real: SpaceDataSource = {
  kind: 'Real',
  dataSourceId: 'CP6_WMS',
  observedAtUtc: '2026-07-25T00:00:00Z',
  isSimulated: false,
  isAvailable: true,
}

const simulated: SpaceDataSource = {
  ...real,
  kind: 'Simulated',
  dataSourceId: 'SPACE_SIMULATOR',
  isSimulated: true,
}

describe('StockOverlay', () => {
  it('applies status colors per location in status mode', () => {
    const viewer = fakeViewer()
    const overlay = new StockOverlay(viewer as any)
    overlay.setSnapshot([dto('A-01', 0), dto('A-02', 2)], real)
    overlay.setMode('status')
    overlay.apply()
    expect(viewer.setInstanceColor).toHaveBeenCalledWith('A-01', binStatusToHex(0))
    expect(viewer.setInstanceColor).toHaveBeenCalledWith('A-02', binStatusToHex(2))
    expect(viewer.requestRender).toHaveBeenCalled()
  })

  it('resolves code to location id before coloring', () => {
    const viewer = {
      setInstanceColor: vi.fn(),
      requestRender: vi.fn(),
      getLocationIdByCode: (code: string) => (code === 'A-01' ? 'guid-1' : null),
    }
    const overlay = new StockOverlay(viewer as any)
    overlay.setSnapshot([dto('A-01', 2), dto('A-99', 1)], real)
    overlay.setMode('status')
    overlay.apply()
    expect(viewer.setInstanceColor).toHaveBeenCalledWith('guid-1', binStatusToHex(2))
    expect(viewer.setInstanceColor).toHaveBeenCalledTimes(1)
  })

  it('off mode does not color', () => {
    const viewer = fakeViewer()
    const overlay = new StockOverlay(viewer as any)
    overlay.setSnapshot([dto('A-01', 0)], real)
    overlay.setMode('off')
    overlay.apply()
    expect(viewer.setInstanceColor).not.toHaveBeenCalled()
  })

  it('getStock returns cached dto by code', () => {
    const overlay = new StockOverlay(fakeViewer() as any)
    overlay.setSnapshot([dto('A-01', 1)], real)
    expect(overlay.getStock('A-01')?.binStatus).toBe(1)
    expect(overlay.getStock('GHOST')).toBeNull()
  })

  it('uses simulated stock while preserving its marker', () => {
    const viewer = fakeViewer()
    const overlay = new StockOverlay(viewer as any)
    overlay.setSnapshot([dto('A-01', 1)], simulated)
    overlay.apply()
    expect(overlay.source.kind).toBe('Simulated')
    expect(overlay.getStock('A-01')).not.toBeNull()
    expect(viewer.setInstanceColor).toHaveBeenCalled()
  })

  it('does not treat unavailable source as empty real stock', () => {
    const viewer = fakeViewer()
    const overlay = new StockOverlay(viewer as any)
    overlay.setSnapshot([dto('A-01', 0)], {
      kind: 'Unavailable',
      dataSourceId: 'WMS_UNCONFIGURED',
      observedAtUtc: '2026-07-25T00:00:00Z',
      isSimulated: false,
      isAvailable: false,
    })
    overlay.apply()
    expect(overlay.getStock('A-01')).toBeNull()
    expect(viewer.setInstanceColor).not.toHaveBeenCalled()
    expect(overlay.source.kind).toBe('Unavailable')
  })
})
