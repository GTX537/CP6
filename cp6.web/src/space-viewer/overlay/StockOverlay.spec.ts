// cp6.web/src/space-viewer/overlay/StockOverlay.spec.ts
import { describe, it, expect, vi } from 'vitest'
import { StockOverlay } from './StockOverlay'
import { binStatusToHex } from './stockModel'
import type { WmsStockDto } from '@/types/space/overlay'

function fakeViewer() {
  // getLocationIdByCode: identity (test treats code as the GUID) so apply() resolves then colors
  return { setInstanceColor: vi.fn(), requestRender: vi.fn(), getLocationIdByCode: (c: string) => c }
}
const dto = (locationCode: string, binStatus: number): WmsStockDto =>
  ({ locationCode, binStatus, qty: 1, allocatedQty: 0, capacity: 10, topMaterial: null, productKinds: 1 })

describe('StockOverlay', () => {
  it('applies status colors per location in status mode', () => {
    const v = fakeViewer()
    const o = new StockOverlay(v as any)
    o.setSnapshot([dto('A-01', 0), dto('A-02', 2)])
    o.setMode('status')
    o.apply()
    expect(v.setInstanceColor).toHaveBeenCalledWith('A-01', binStatusToHex(0))
    expect(v.setInstanceColor).toHaveBeenCalledWith('A-02', binStatusToHex(2))
    expect(v.requestRender).toHaveBeenCalled()
  })

  it('resolves code -> location GUID before coloring (not the code)', () => {
    const v = { setInstanceColor: vi.fn(), requestRender: vi.fn(),
      getLocationIdByCode: (c: string) => (c === 'A-01' ? 'guid-1' : null) }
    const o = new StockOverlay(v as any)
    o.setSnapshot([dto('A-01', 2), dto('A-99', 1)])  // A-99 has no GUID → skipped
    o.setMode('status')
    o.apply()
    expect(v.setInstanceColor).toHaveBeenCalledWith('guid-1', binStatusToHex(2))
    expect(v.setInstanceColor).toHaveBeenCalledTimes(1)  // A-99 skipped (no id)
  })

  it('off mode does not color (caller resets to grey)', () => {
    const v = fakeViewer()
    const o = new StockOverlay(v as any)
    o.setSnapshot([dto('A-01', 0)])
    o.setMode('off')
    o.apply()
    expect(v.setInstanceColor).not.toHaveBeenCalled()
  })

  it('getStock returns cached dto by code', () => {
    const v = fakeViewer()
    const o = new StockOverlay(v as any)
    o.setSnapshot([dto('A-01', 1)])
    expect(o.getStock('A-01')?.binStatus).toBe(1)
    expect(o.getStock('GHOST')).toBeNull()
  })
})
