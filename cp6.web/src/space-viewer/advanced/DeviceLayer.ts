import { BoxGeometry, Group, Mesh, MeshBasicMaterial } from 'three'
import type { ViewerHandle } from '../api/ViewerHandle'
import type { DeviceDto } from '@/types/space/advanced'

const DEVICE_SIZE = 1000   // mm
const DEVICE_COLOR = 0x7e57c2
const DEVICE_Z = 500       // 缺 absZ 时的默认高度

/** 设备图层 v1 占位：参数化盒体挂 SceneRoot（mm 数据空间）；真接实时流 = P3+。 */
export class DeviceLayer {
  private _viewer: ViewerHandle
  private _group = new Group()
  private _count = 0

  constructor(viewer: ViewerHandle) { this._viewer = viewer }

  get count(): number { return this._count }

  setDevices(devices: DeviceDto[]): void {
    this.clear()
    for (const d of devices) {
      if (d.absX == null || d.absY == null) continue
      const mesh = new Mesh(
        new BoxGeometry(DEVICE_SIZE, DEVICE_SIZE, DEVICE_SIZE),
        new MeshBasicMaterial({ color: DEVICE_COLOR }),
      )
      mesh.position.set(d.absX, d.absY, d.absZ ?? DEVICE_Z)
      this._group.add(mesh)
      this._count++
    }
    if (this._count > 0) {
      this._viewer.getSceneRoot().add(this._group)
    }
    this._viewer.requestRender()
  }

  clear(): void {
    this._group.clear()
    if (this._group.parent) this._group.parent.remove(this._group)
    this._count = 0
    this._viewer.requestRender()
  }
}
