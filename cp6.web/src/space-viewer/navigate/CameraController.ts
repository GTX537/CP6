import { Box3, PerspectiveCamera, OrthographicCamera, Sphere, Vector3 } from 'three'
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls'
import type { WebGLRenderer } from 'three'

export type Easing = (t: number) => number

export function easeInOutCubic(t: number): number {
  return t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2
}

export function easeLinear(t: number): number { return t }

type FlyState = {
  startPos: Vector3
  startTarget: Vector3
  endPos: Vector3
  endTarget: Vector3
  duration: number
  elapsed: number
  easing: Easing
  onDone?: () => void
}

export class CameraController {
  private readonly _perspective: PerspectiveCamera
  private readonly _ortho: OrthographicCamera
  private _isOrtho = false
  private readonly _controls: OrbitControls
  private _fly: FlyState | null = null
  private readonly _requestRender: () => void

  constructor(perspective: PerspectiveCamera, renderer: WebGLRenderer, requestRender: () => void) {
    this._perspective = perspective
    this._requestRender = requestRender

    const w = renderer.domElement.clientWidth || renderer.domElement.width
    const h = renderer.domElement.clientHeight || renderer.domElement.height || 1
    const aspect = w / h
    this._ortho = new OrthographicCamera(-50 * aspect, 50 * aspect, 50, -50, 0.1, 2000)

    this._controls = new OrbitControls(perspective, renderer.domElement)
    this._controls.enableDamping = true
    this._controls.dampingFactor = 0.05
    this._controls.minDistance = 2
    this._controls.maxDistance = 500
    this._controls.maxPolarAngle = Math.PI / 2 + 0.15

    this._controls.addEventListener('change', requestRender)
  }

  get controls(): OrbitControls { return this._controls }

  get activeCamera(): PerspectiveCamera | OrthographicCamera {
    return this._isOrtho ? this._ortho : this._perspective
  }

  toggleProjection(): void {
    this._isOrtho = !this._isOrtho
    const pos = this._perspective.position.clone()
    const target = this._controls.target.clone()
    if (this._isOrtho) {
      this._ortho.position.copy(pos)
      this._ortho.lookAt(target)
      this._ortho.updateProjectionMatrix()
    } else {
      this._perspective.position.copy(this._ortho.position)
      this._perspective.lookAt(target)
      this._perspective.updateProjectionMatrix()
    }
    this._requestRender()
  }

  setPreset(preset: 'top' | 'iso' | 'front' | 'home'): void {
    const target = new Vector3(0, 0, 0)
    let pos: Vector3
    switch (preset) {
      case 'top':   pos = new Vector3(0, 100, 0.01); break
      case 'iso':   pos = new Vector3(60, 60, 60);   break
      case 'front': pos = new Vector3(0, 10, 100);   break
      default:      pos = new Vector3(0, 50, 80)
    }
    this.flyTo(pos, target, 600, easeInOutCubic)
  }

  flyTo(camPos: Vector3, target: Vector3, duration: number, easing: Easing = easeInOutCubic, onDone?: () => void): void {
    const cam = this.activeCamera
    this._fly = {
      startPos: cam.position.clone(),
      startTarget: this._controls.target.clone(),
      endPos: camPos.clone(),
      endTarget: target.clone(),
      duration,
      elapsed: 0,
      easing,
      onDone,
    }
  }

  /** Call every render frame. Returns true while flyTo is animating. */
  update(dt: number): boolean {
    this._controls.update()  // advance damping
    if (!this._fly) return false
    this._fly.elapsed += dt
    const raw = Math.min(this._fly.elapsed / this._fly.duration, 1)
    const t = this._fly.easing(raw)
    const cam = this.activeCamera
    cam.position.lerpVectors(this._fly.startPos, this._fly.endPos, t)
    this._controls.target.lerpVectors(this._fly.startTarget, this._fly.endTarget, t)
    if (raw >= 1) {
      this._fly.onDone?.()
      this._fly = null
    } else {
      this._requestRender()
    }
    return true
  }

  /** Fly camera to an oblique view of box3 (world space). */
  focusObject(box3: Box3): void {
    const center = new Vector3()
    box3.getCenter(center)
    const sphere = new Sphere()
    box3.getBoundingSphere(sphere)
    const radius = Math.max(sphere.radius, 0.5)
    const dist = radius * 3.5
    const camPos = new Vector3(
      center.x + dist * 0.6,
      center.y + dist * 0.8,
      center.z + dist * 0.6,
    )
    this.flyTo(camPos, center, 600, easeInOutCubic)
  }

  /** Reset camera to home preset. */
  home(): void { this.setPreset('home') }

  /** Fly to orthographic-style top-down overview of the floor. */
  overview(): void {
    const pos = new Vector3(0, 200, 0.01)
    const target = new Vector3(0, 0, 0)
    this.flyTo(pos, target, 700, easeInOutCubic)
  }

  dispose(): void {
    this._controls.dispose()
  }
}
