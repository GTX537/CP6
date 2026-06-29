import { describe, it, expect, vi } from 'vitest'
import { Group } from 'three'
import { DeviceLayer } from './DeviceLayer'
import type { DeviceDto } from '@/types/space/advanced'

function fakeViewer() {
  const root = new Group()
  return { root, getSceneRoot: () => root, requestRender: vi.fn() }
}
const dev = (id: string, x: number | null): DeviceDto =>
  ({ deviceId: id, type: 'AGV', status: 0, locationCode: 'A-01', absX: x, absY: x, absZ: 500 })

describe('DeviceLayer', () => {
  it('setDevices renders boxes for devices with coords', () => {
    const v = fakeViewer()
    const dl = new DeviceLayer(v as any)
    dl.setDevices([dev('D1', 100), dev('D2', 200)])
    expect(dl.count).toBe(2)
    expect(v.root.children.length).toBe(1)             // 设备 Group
    expect(v.root.children[0]!.children.length).toBe(2) // 2 盒
    expect(v.requestRender).toHaveBeenCalled()
  })

  it('skips devices without coords', () => {
    const v = fakeViewer()
    const dl = new DeviceLayer(v as any)
    dl.setDevices([dev('D1', null)])
    expect(dl.count).toBe(0)
    expect(v.root.children.length).toBe(0)
  })

  it('clear removes the group and resets count', () => {
    const v = fakeViewer()
    const dl = new DeviceLayer(v as any)
    dl.setDevices([dev('D1', 100)])
    dl.clear()
    expect(dl.count).toBe(0)
    expect(v.root.children.length).toBe(0)
  })
})
