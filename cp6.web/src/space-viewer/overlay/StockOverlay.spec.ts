// cp6.web/src/space-viewer/overlay/StockOverlay.spec.ts
import { describe, it, expect, vi } from 'vitest'
import { StockOverlay } from './StockOverlay'
import { binStatusToHex, utilizationToHex } from './stockModel'
import type { WmsStockDto } from '@/types/space/overlay'

function fakeViewer() {
  // getLocationIdByCode: identity (test treats code as the GUID) so apply() resolves then colors
  return {
    setInstanceColor: vi.fn(), resetInstanceColors: vi.fn(), refreshHighlights: vi.fn(),
    requestRender: vi.fn(), getLocationIdByCode: (c: string) => c,
  }
}
const dto = (locationCode: string, binStatus: number): WmsStockDto =>
  ({ locationCode, binStatus, qty: 1, allocatedQty: 0, capacity: 10, capacityUom: 1,
    capacitySource: 'wms-bin', topMaterial: null, productKinds: 1, productCodes: ['P1'] })

describe('StockOverlay', () => {
  it('applies status colors per location in status mode', () => {
    const v = fakeViewer()
    const o = new StockOverlay(v as any)
    o.setSnapshot([dto('A-01', 0), dto('A-02', 2)])
    o.setMode('status')
    o.apply()
    expect(v.setInstanceColor).toHaveBeenCalledWith('A-01', binStatusToHex(0))
    expect(v.setInstanceColor).toHaveBeenCalledWith('A-02', binStatusToHex(2))
    expect(v.refreshHighlights).toHaveBeenCalledTimes(1)
    expect(v.refreshHighlights.mock.invocationCallOrder[0])
      .toBeGreaterThan(v.setInstanceColor.mock.invocationCallOrder.at(-1)!)
    expect(v.requestRender).toHaveBeenCalled()
  })

  it('resolves code -> location GUID before coloring (not the code)', () => {
    const v = { setInstanceColor: vi.fn(), resetInstanceColors: vi.fn(), requestRender: vi.fn(),
      getLocationIdByCode: (c: string) => (c === 'A-01' ? 'guid-1' : null) }
    const o = new StockOverlay(v as any)
    o.setSnapshot([dto('A-01', 2), dto('A-99', 1)])  // A-99 has no GUID → skipped
    o.setMode('status')
    o.apply()
    expect(v.setInstanceColor).toHaveBeenCalledWith('guid-1', binStatusToHex(2))
    expect(v.setInstanceColor).toHaveBeenCalledTimes(1)  // A-99 skipped (no id)
  })

  it('structure mode resets to base color and does not apply an overlay color', () => {
    const v = fakeViewer()
    const o = new StockOverlay(v as any)
    o.setSnapshot([dto('A-01', 0)])
    o.setMode('structure')
    o.apply()
    expect(v.resetInstanceColors).toHaveBeenCalledTimes(1)
    expect(v.setInstanceColor).not.toHaveBeenCalled()
  })

  it('resets before every mode so colors cannot bleed between status and ABC', () => {
    const v = fakeViewer()
    const o = new StockOverlay(v as any)
    o.setSnapshot([dto('A-01', 2)])
    o.setMode('status')
    o.apply()
    expect(v.setInstanceColor).toHaveBeenLastCalledWith('A-01', binStatusToHex(2))

    v.setInstanceColor.mockClear()
    o.setAbc([{ locationCode: 'A-01', abcRank: 'C' }] as any)
    o.setMode('abc')
    o.apply()
    expect(v.resetInstanceColors).toHaveBeenCalledTimes(2)
    expect(v.setInstanceColor).toHaveBeenCalledWith('A-01', 0x64748b)
    expect(v.setInstanceColor).not.toHaveBeenCalledWith('A-01', binStatusToHex(2))
  })

  it('applies utilization and storage-type analytics colors', () => {
    const v = fakeViewer()
    const o = new StockOverlay(v as any)
    o.setUtilization([{ locationCode: 'A-01', utilization: 0.5 }] as any)
    o.setMode('utilization')
    o.apply()
    expect(v.setInstanceColor).toHaveBeenLastCalledWith('A-01', utilizationToHex(0.5))

    o.setStorageTypes([{ locationCode: 'A-01', color: '#123456' }] as any)
    o.setMode('storageType')
    o.apply()
    expect(v.setInstanceColor).toHaveBeenLastCalledWith('A-01', 0x123456)
    expect(v.refreshHighlights).toHaveBeenCalledTimes(2)
  })

  it('getStock returns cached dto by code', () => {
    const v = fakeViewer()
    const o = new StockOverlay(v as any)
    o.setSnapshot([dto('A-01', 1)])
    expect(o.getStock('A-01')?.binStatus).toBe(1)
    expect(o.getStock('GHOST')).toBeNull()
  })
})
