import { describe, it, expect, vi } from 'vitest'
import { WorkloadHeatmap } from './WorkloadHeatmap'
import { workloadToHex } from './workloadModel'
import type { SpaceDataSource } from '@/types/space/dataSource'

const real: SpaceDataSource = {
  kind: 'Real',
  dataSourceId: 'CP6_WMS',
  observedAtUtc: '2026-07-25T00:00:00Z',
  isSimulated: false,
  isAvailable: true,
}

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
    h.setSnapshot([{ locationCode: 'A', opCount: 10 }, { locationCode: 'B', opCount: 5 }], real)
    h.setEnabled(true)
    h.apply()
    expect(v.setInstanceColor).toHaveBeenCalledWith('id-A', workloadToHex(1))
    expect(v.setInstanceColor).toHaveBeenCalledWith('id-B', workloadToHex(0.5))
    expect(v.requestRender).toHaveBeenCalled()
  })

  it('apply is a no-op when disabled', () => {
    const v = fakeViewer()
    const h = new WorkloadHeatmap(v as any)
    h.setSnapshot([{ locationCode: 'A', opCount: 1 }], real)
    h.apply()
    expect(v.setInstanceColor).not.toHaveBeenCalled()
  })

  it('getOpCount returns raw count by code', () => {
    const v = fakeViewer()
    const h = new WorkloadHeatmap(v as any)
    h.setSnapshot([{ locationCode: 'A', opCount: 7 }], real)
    expect(h.getOpCount('A')).toBe(7)
    expect(h.getOpCount('GHOST')).toBe(0)
  })

  it('does not render unavailable workload as an empty real heatmap', () => {
    const v = fakeViewer()
    const h = new WorkloadHeatmap(v as any)
    h.setSnapshot([{ locationCode: 'A', opCount: 7 }], {
      kind: 'Unavailable',
      dataSourceId: 'WMS_UNCONFIGURED',
      observedAtUtc: '2026-07-25T00:00:00Z',
      isSimulated: false,
      isAvailable: false,
    })
    h.setEnabled(true)
    h.apply()
    expect(h.source.kind).toBe('Unavailable')
    expect(h.getOpCount('A')).toBe(0)
    expect(v.setInstanceColor).not.toHaveBeenCalled()
  })
})
