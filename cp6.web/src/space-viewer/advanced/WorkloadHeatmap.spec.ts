import { describe, it, expect, vi } from 'vitest'
import { WorkloadHeatmap } from './WorkloadHeatmap'
import { workloadToHex } from './workloadModel'

function fakeViewer() {
  return {
    getLocationIdByCode: (c: string) => (c === 'GHOST' ? null : `id-${c}`),
    setInstanceColor: vi.fn(),
    requestRender: vi.fn(),
  }
}

describe('WorkloadHeatmap', () => {
  it('apply colors busy locations hot when enabled', () => {
    const v = fakeViewer()
    const h = new WorkloadHeatmap(v as any)
    h.setSnapshot([{ locationCode: 'A', opCount: 10 }, { locationCode: 'B', opCount: 5 }])
    h.setEnabled(true)
    h.apply()
    expect(v.setInstanceColor).toHaveBeenCalledWith('id-A', workloadToHex(1))
    expect(v.setInstanceColor).toHaveBeenCalledWith('id-B', workloadToHex(0.5))
    expect(v.requestRender).toHaveBeenCalled()
  })

  it('apply is a no-op when disabled', () => {
    const v = fakeViewer()
    const h = new WorkloadHeatmap(v as any)
    h.setSnapshot([{ locationCode: 'A', opCount: 1 }])
    h.apply()
    expect(v.setInstanceColor).not.toHaveBeenCalled()
  })

  it('getOpCount returns raw count by code', () => {
    const v = fakeViewer()
    const h = new WorkloadHeatmap(v as any)
    h.setSnapshot([{ locationCode: 'A', opCount: 7 }])
    expect(h.getOpCount('A')).toBe(7)
    expect(h.getOpCount('GHOST')).toBe(0)
  })
})
