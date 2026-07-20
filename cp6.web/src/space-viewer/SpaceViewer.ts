import { Box3, Matrix4, Quaternion, Scene, PerspectiveCamera, Color, Vector3, Group } from 'three'
import { CSS2DRenderer } from 'three/examples/jsm/renderers/CSS2DRenderer'
import { Renderer } from './core/Renderer'
import { Loop } from './core/Loop'
import { SceneRoot } from './core/SceneRoot'
import { SceneBuilder } from './build/SceneBuilder'
import { FrustumCuller } from './cull/FrustumCuller'
import { LodController } from './cull/LodController'
import { LabelVirtualizer } from './labels/LabelVirtualizer'
import { CameraController, easeInOutCubic } from './navigate/CameraController'
import { Picker } from './navigate/Picker'
import { Highlighter } from './navigate/Highlighter'
import { sceneApi } from '@/api/space/scene'
import type { ViewerHandle } from './api/ViewerHandle'
import type { InstancedBuckets } from './build/InstancedBuckets'
import type { PickResult } from '@/types/space/viewer'

export class SpaceViewer implements ViewerHandle {
  private _renderer: Renderer
  private _scene: Scene
  private _sceneRoot: SceneRoot
  private _labelGroup: Group
  private _camera: PerspectiveCamera
  private _loop: Loop

  private _buckets: InstancedBuckets | null = null
  private _locationCodes = new Map<string, string>()
  private _codeToId = new Map<string, string>()
  private _currentFloorId = ''
  private _selectedLocationId: string | null = null

  private _frustumCuller = new FrustumCuller()
  private _lodController = new LodController()
  private _labelVirtualizer: LabelVirtualizer | null = null
  private _cameraController: CameraController | null = null
  private _css2dRenderer: CSS2DRenderer | null = null
  private _picker = new Picker()
  private _highlighter = new Highlighter()
  private _loadGeneration = 0

  private _readyCbs: Array<() => void> = []
  private _progressCbs: Array<(done: number, total: number) => void> = []

  constructor(canvas: HTMLCanvasElement) {
    this._renderer = new Renderer(canvas)
    this._scene = new Scene()
    this._scene.background = new Color(0x1a1a2e)
    this._sceneRoot = new SceneRoot()
    this._scene.add(this._sceneRoot)

    // Label objects live directly in scene (world space positioning)
    this._labelGroup = new Group()
    this._scene.add(this._labelGroup)

    const aspect = canvas.clientWidth / (canvas.clientHeight || 1)
    this._camera = new PerspectiveCamera(45, aspect, 0.1, 2000)
    this._camera.position.set(0, 50, 80)
    this._camera.lookAt(0, 0, 0)

    this._loop = new Loop((dt) => this._render(dt))

    // Camera controller (OrbitControls + flyTo)
    this._cameraController = new CameraController(this._camera, this._renderer.gl, () => this.requestRender())

    // CSS2DRenderer for labels
    const css2dEl = this._setupCSS2DRenderer(canvas)
    if (css2dEl) {
      this._css2dRenderer = new CSS2DRenderer({ element: css2dEl })
      this._css2dRenderer.setSize(
        canvas.clientWidth || canvas.width,
        canvas.clientHeight || canvas.height,
      )
    }

    // Label virtualizer — pool objects added to scene for CSS2D rendering
    this._labelVirtualizer = new LabelVirtualizer(200)
    for (const obj of this._labelVirtualizer.pool.objects) {
      this._labelGroup.add(obj)
    }

    // Throttled per-bucket LOD + frustum cull + label update (every 100 ms)
    this._loop.addThrottledTask((_dt) => {
      if (!this._buckets) return
      const cam = this._cameraController?.activeCamera ?? this._camera
      const target = this._cameraController?.controls.target ?? new Vector3(0, 0, 0)
      const dist = cam.position.distanceTo(target)
      const tier = this._lodController.update(dist)

      // Step 1: LOD — show/hide location buckets
      for (const mesh of this._buckets.meshes) {
        mesh.visible = tier !== 'far'
      }

      // Step 2: frustum cull — only further restrict visible buckets
      if (tier !== 'far') {
        this._frustumCuller.update(cam, this._buckets)
      }

      // Step 3: label virtualizer
      if (this._labelVirtualizer && this._css2dRenderer) {
        this._labelVirtualizer.update(cam, this._buckets, tier, this._locationCodes, this._renderer.gl)
      }

      this.requestRender()
    }, 100)

    window.addEventListener('resize', this._onResize)
  }

  private _setupCSS2DRenderer(canvas: HTMLCanvasElement): HTMLElement | null {
    const parent = canvas.parentElement
    if (!parent) return null
    const div = document.createElement('div')
    div.style.cssText = 'position:absolute;top:0;left:0;width:100%;height:100%;pointer-events:none;overflow:hidden'
    parent.style.position = 'relative'
    parent.appendChild(div)
    return div
  }

  private _render(dt: number): void {
    const animating = this._cameraController?.update(dt)
    if (animating) this._loop.markDirty()

    const cam = this._cameraController?.activeCamera ?? this._camera
    this._renderer.gl.render(this._scene, cam)
    if (this._css2dRenderer) this._css2dRenderer.render(this._scene, cam)
  }

  private _onResize = (): void => {
    const canvas = this._renderer.gl.domElement
    const w = canvas.clientWidth
    const h = canvas.clientHeight || 1
    this._camera.aspect = w / h
    this._camera.updateProjectionMatrix()
    this._renderer.resize(w, h)
    this._css2dRenderer?.setSize(w, h)
    this.requestRender()
  }

  private _clearSceneData(): void {
    this._highlighter.clear()
    this._selectedLocationId = null
    if (this._buckets) {
      this._buckets.dispose()
      this._buckets = null
    }
    this._locationCodes.clear()
    this._codeToId.clear()
    while (this._sceneRoot.children.length > 0) {
      const child = this._sceneRoot.children[0]
      if (child) this._sceneRoot.remove(child)
    }
  }

  async load(floorId: string): Promise<void> {
    const generation = ++this._loadGeneration
    this._clearSceneData()
    this._currentFloorId = floorId
    const env = await sceneApi.get(floorId)
    if (generation !== this._loadGeneration) return
    const editorScene = env.data

    const builder = new SceneBuilder()
    const result = builder.build(editorScene, {
      onProgress: (done, total) => {
        this._progressCbs.forEach((cb) => cb(done, total))
        this.requestRender()
      },
    })

    this._buckets = result.buckets
    this._locationCodes = result.locationCodes
    this._codeToId = new Map()
    for (const [id, code] of result.locationCodes) this._codeToId.set(code, id)
    this._highlighter.attach(this, this._buckets)

    for (const obj of result.objects) {
      this._sceneRoot.add(obj)
    }

    this.requestRender()
    this._readyCbs.forEach((cb) => cb())
  }

  dispose(): void {
    this._loadGeneration++
    this._loop.stop()
    window.removeEventListener('resize', this._onResize)
    this._cameraController?.dispose()
    this._highlighter.clear()

    if (this._buckets) {
      this._buckets.dispose()
      this._buckets = null
    }

    while (this._sceneRoot.children.length > 0) {
      const child = this._sceneRoot.children[0]
      if (child) this._sceneRoot.remove(child)
    }

    this._renderer.dispose()
  }

  // ── ViewerHandle ──────────────────────────────────────────────────────────

  getSceneRoot() { return this._sceneRoot }

  worldToData(v: Vector3) { return this._sceneRoot.worldToData(v) }
  dataToWorld(p: { x: number; y: number; z: number }) { return this._sceneRoot.dataToWorld(p) }

  instanceToLocation(meshId: number, instanceId: number): string | null {
    return this._buckets?.instanceToLocation(meshId, instanceId) ?? null
  }

  setInstanceColor(locationId: string, hex: number): void {
    this._buckets?.setColor(locationId, hex)
    this.requestRender()
  }

  resetInstanceColors(hex = 0x607d8b): void {
    this._buckets?.resetColors(hex)
    this.requestRender()
  }

  refreshHighlights(): void { this._highlighter.refresh() }

  requestRender(): void { this._loop.markDirty() }

  onReady(cb: () => void): void { this._readyCbs.push(cb) }
  onProgress(cb: (done: number, total: number) => void): void { this._progressCbs.push(cb) }

  start(): void { this._loop.start() }

  // ── Navigation helpers (used by FloorViewer) ──────────────────────────────

  pick(ndcX: number, ndcY: number): PickResult | null {
    if (!this._buckets) return null
    const cam = this._cameraController?.activeCamera ?? this._camera
    return this._picker.pick(ndcX, ndcY, cam, this._buckets, this._sceneRoot)
  }

  hover(pick: PickResult | null): void { this._highlighter.hover(pick) }

  select(pick: PickResult | null): string | null {
    const id = this._highlighter.select(pick)
    this._selectedLocationId = id
    return id
  }

  clearSelection(): void {
    this._highlighter.clear()
    this._selectedLocationId = null
  }

  setPreset(preset: 'top' | 'iso' | 'front' | 'home'): void {
    this._cameraController?.setPreset(preset)
    this.requestRender()
  }

  toggleProjection(): void {
    this._cameraController?.toggleProjection()
    this.requestRender()
  }

  // ── New N-4 navigation helpers ────────────────────────────────────────────

  getCurrentFloorId(): string { return this._currentFloorId }
  getLocationCode(locationId: string): string | null { return this._locationCodes.get(locationId) ?? null }
  getLocationIdByCode(code: string): string | null { return this._codeToId.get(code) ?? null }

  /** Fly camera to oblique view of a data-space point (mm, Z-up). */
  flyToData(p: { x: number; y: number; z: number }): void {
    const target = this._sceneRoot.dataToWorld(p)
    const dist = 25
    const camPos = new Vector3(
      target.x + dist * 0.6,
      target.y + dist * 0.8,
      target.z + dist * 0.6,
    )
    this._cameraController?.flyTo(camPos, target, 800, easeInOutCubic)
    this.requestRender()
  }

  home(): void {
    this._cameraController?.home()
    this.requestRender()
  }

  overview(): void {
    this._cameraController?.overview()
    this.requestRender()
  }

  /** Focus camera on the selected location's bounding box. */
  focusSelected(): void {
    if (!this._selectedLocationId || !this._buckets || !this._cameraController) return
    const ref = this._buckets.locationToInstance(this._selectedLocationId)
    if (!ref) return
    for (const mesh of this._buckets.meshes) {
      if (mesh.id !== ref.meshId) continue
      const mat = new Matrix4()
      mesh.getMatrixAt(ref.instanceId, mat)
      const pos = new Vector3()
      const q = new Quaternion()
      const scale = new Vector3()
      mat.decompose(pos, q, scale)
      // pos = data-mm, scale = mm dimensions; convert to world meters
      const worldCenter = this._sceneRoot.dataToWorld({ x: pos.x, y: pos.y, z: pos.z })
      const halfExtent = Math.max(scale.x, scale.y, scale.z) * 0.001 * 0.5
      const box = new Box3(
        worldCenter.clone().subScalar(halfExtent),
        worldCenter.clone().addScalar(halfExtent),
      )
      this._cameraController.focusObject(box)
      return
    }
  }

  /** Focus camera on the given world-space box. */
  focusBox(box3: Box3): void {
    this._cameraController?.focusObject(box3)
  }
}
