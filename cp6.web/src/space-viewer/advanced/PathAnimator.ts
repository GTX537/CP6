// cp6.web/src/space-viewer/advanced/PathAnimator.ts
import {
  BoxGeometry, BufferGeometry, Float32BufferAttribute, Group, Line, LineBasicMaterial,
  Mesh, MeshBasicMaterial,
} from 'three'
import type { ViewerHandle } from '../api/ViewerHandle'
import type { Pt3 } from './multiFloor'
import { polylineLength3, pointAtDistance3 } from './pathModel'

const GROUND_Z = 200       // mm，路径线离地高度
const CART_SIZE = 600      // mm 立方
const PATH_COLOR = 0x00e5ff
const CART_COLOR = 0xff4081
const COMPARE_COLOR = 0x76ff03   // 绿，优化对比线
const DEFAULT_SPEED = 4000 // mm/s

/** 接受 Pt（无 z）或 Pt3（有 z）；z 缺省归零，保持单层调用者零回归。 */
type Pt3Like = { x: number; y: number; z?: number }

function normalizePt3(p: Pt3Like): Pt3 {
  return { x: p.x, y: p.y, z: p.z ?? 0 }
}

export class PathAnimator {
  private _viewer: ViewerHandle
  private _group = new Group()
  private _points: Pt3[] = []
  private _length = 0
  private _cart: Mesh | null = null
  private _pathLine: Line | null = null
  private _compareLine: Line | null = null
  private _dist = 0
  private _speed = DEFAULT_SPEED
  private _playing = false
  private _raf = 0
  private _lastTs = 0

  constructor(viewer: ViewerHandle) { this._viewer = viewer }

  get playing(): boolean { return this._playing }
  get progress(): number { return this._length > 0 ? Math.min(1, this._dist / this._length) : 0 }

  setPath(points: Pt3Like[]): void {
    this.clear()
    this._points = points.map(normalizePt3)
    this._length = polylineLength3(this._points)
    if (this._points.length < 2) return

    const arr: number[] = []
    for (const p of this._points) arr.push(p.x, p.y, p.z + GROUND_Z)
    const geom = new BufferGeometry()
    geom.setAttribute('position', new Float32BufferAttribute(arr, 3))
    this._pathLine = new Line(geom, new LineBasicMaterial({ color: PATH_COLOR }))
    this._group.add(this._pathLine)

    this._cart = new Mesh(new BoxGeometry(CART_SIZE, CART_SIZE, CART_SIZE), new MeshBasicMaterial({ color: CART_COLOR }))
    this._group.add(this._cart)
    this._dist = 0
    this._positionCart()

    this._viewer.getSceneRoot().add(this._group)
    this._viewer.requestRender()
  }

  /** 移除并释放对比线（GPU geometry/material dispose，防 toggle 泄漏）。 */
  private _clearCompareLine(): void {
    if (!this._compareLine) return
    this._group.remove(this._compareLine)
    this._compareLine.geometry.dispose()
    ;(this._compareLine.material as LineBasicMaterial).dispose()
    this._compareLine = null
  }

  /** 移除并释放主路径线 + 小车（GPU geometry/material dispose，防 reload 泄漏）。 */
  private _clearMainPath(): void {
    if (this._pathLine) {
      this._group.remove(this._pathLine)
      this._pathLine.geometry.dispose()
      ;(this._pathLine.material as LineBasicMaterial).dispose()
      this._pathLine = null
    }
    if (this._cart) {
      this._group.remove(this._cart)
      this._cart.geometry.dispose()
      ;(this._cart.material as MeshBasicMaterial).dispose()
      this._cart = null
    }
  }

  /** 静态对比线（优化序，无小车不参与动画）；null 清除。挂在同一 _group 下。 */
  setComparisonPath(points: Pt3Like[] | null): void {
    this._clearCompareLine()
    if (points && points.length >= 2) {
      const pts3 = points.map(normalizePt3)
      const arr: number[] = []
      for (const p of pts3) arr.push(p.x, p.y, p.z + GROUND_Z + 20)  // +20mm 防 z-fight
      const geom = new BufferGeometry()
      geom.setAttribute('position', new Float32BufferAttribute(arr, 3))
      this._compareLine = new Line(geom, new LineBasicMaterial({ color: COMPARE_COLOR }))
      this._group.add(this._compareLine)
    }
    this._viewer.requestRender()
  }

  private _positionCart(): void {
    if (!this._cart) return
    const p = pointAtDistance3(this._points, this._dist)
    this._cart.position.set(p.x, p.y, p.z + GROUND_Z + CART_SIZE / 2)
  }

  play(): void {
    if (this._playing || this._points.length < 2) return
    this._playing = true
    this._lastTs = 0
    const tick = (ts: number): void => {
      if (!this._playing) return
      if (this._lastTs === 0) this._lastTs = ts
      const dt = (ts - this._lastTs) / 1000
      this._lastTs = ts
      this._dist += this._speed * dt
      if (this._dist >= this._length) { this._dist = this._length; this._playing = false }
      this._positionCart()
      this._viewer.requestRender()
      if (this._playing) this._raf = requestAnimationFrame(tick)
    }
    this._raf = requestAnimationFrame(tick)
  }

  pause(): void {
    this._playing = false
    if (this._raf) cancelAnimationFrame(this._raf)
    this._raf = 0
  }

  /** 步进到下一折线顶点。 */
  stepNext(): void {
    this.pause()
    let acc = 0
    for (let i = 1; i < this._points.length; i++) {
      const a = this._points[i - 1]!
      const b = this._points[i]!
      acc += Math.hypot(b.x - a.x, b.y - a.y, b.z - a.z)
      if (acc > this._dist + 1) { this._dist = acc; break }
    }
    if (this._dist > this._length) this._dist = this._length
    this._positionCart()
    this._viewer.requestRender()
  }

  setSpeed(mmPerSec: number): void { this._speed = Math.max(100, mmPerSec) }

  replay(): void {
    this.pause()
    this._dist = 0
    this._positionCart()
    this._viewer.requestRender()
    this.play()
  }

  clear(): void {
    this.pause()
    this._clearCompareLine()
    this._clearMainPath()
    this._group.clear()
    if (this._group.parent) this._group.parent.remove(this._group)
    this._points = []
    this._length = 0
    this._dist = 0
    this._viewer.requestRender()
  }
}
