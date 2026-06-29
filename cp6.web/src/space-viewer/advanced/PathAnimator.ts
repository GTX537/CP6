// cp6.web/src/space-viewer/advanced/PathAnimator.ts
import {
  BoxGeometry, BufferGeometry, Float32BufferAttribute, Group, Line, LineBasicMaterial,
  Mesh, MeshBasicMaterial,
} from 'three'
import type { ViewerHandle } from '../api/ViewerHandle'
import type { Pt } from './PickPathPlanner'
import { polylineLength, pointAtDistance } from './pathModel'

const GROUND_Z = 200       // mm，路径线离地高度
const CART_SIZE = 600      // mm 立方
const PATH_COLOR = 0x00e5ff
const CART_COLOR = 0xff4081
const COMPARE_COLOR = 0x76ff03   // 绿，优化对比线
const DEFAULT_SPEED = 4000 // mm/s

export class PathAnimator {
  private _viewer: ViewerHandle
  private _group = new Group()
  private _points: Pt[] = []
  private _length = 0
  private _cart: Mesh | null = null
  private _compareLine: Line | null = null
  private _dist = 0
  private _speed = DEFAULT_SPEED
  private _playing = false
  private _raf = 0
  private _lastTs = 0

  constructor(viewer: ViewerHandle) { this._viewer = viewer }

  get playing(): boolean { return this._playing }
  get progress(): number { return this._length > 0 ? Math.min(1, this._dist / this._length) : 0 }

  setPath(points: Pt[]): void {
    this.clear()
    this._points = points.slice()
    this._length = polylineLength(points)
    if (points.length < 2) return

    const arr: number[] = []
    for (const p of points) arr.push(p.x, p.y, GROUND_Z)
    const geom = new BufferGeometry()
    geom.setAttribute('position', new Float32BufferAttribute(arr, 3))
    this._group.add(new Line(geom, new LineBasicMaterial({ color: PATH_COLOR })))

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

  /** 静态对比线（优化序，无小车不参与动画）；null 清除。挂在同一 _group 下。 */
  setComparisonPath(points: Pt[] | null): void {
    this._clearCompareLine()
    if (points && points.length >= 2) {
      const arr: number[] = []
      for (const p of points) arr.push(p.x, p.y, GROUND_Z + 20)  // +20mm 防 z-fight
      const geom = new BufferGeometry()
      geom.setAttribute('position', new Float32BufferAttribute(arr, 3))
      this._compareLine = new Line(geom, new LineBasicMaterial({ color: COMPARE_COLOR }))
      this._group.add(this._compareLine)
    }
    this._viewer.requestRender()
  }

  private _positionCart(): void {
    if (!this._cart) return
    const p = pointAtDistance(this._points, this._dist)
    this._cart.position.set(p.x, p.y, GROUND_Z + CART_SIZE / 2)
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
      acc += Math.hypot(this._points[i]!.x - this._points[i - 1]!.x, this._points[i]!.y - this._points[i - 1]!.y)
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
    this._group.clear()
    if (this._group.parent) this._group.parent.remove(this._group)
    this._cart = null
    this._points = []
    this._length = 0
    this._dist = 0
    this._viewer.requestRender()
  }
}
