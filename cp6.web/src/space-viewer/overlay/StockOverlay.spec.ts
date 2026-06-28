// cp6.web/src/space-viewer/overlay/StockOverlay.spec.ts
import { describe, it, expect, vi } from 'vitest'
import { StockOverlay } from './StockOverlay'
import { binStatusToHex } from './stockModel'
import type { WmsStockDto } from '@/types/space/overlay'

function fakeViewer() {
  return { setInstanceColor: vi.fn(), requestRender: vi.fn() }
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
