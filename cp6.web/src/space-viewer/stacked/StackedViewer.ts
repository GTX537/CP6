import { Box3, Color, Group, PerspectiveCamera, Scene } from 'three'
import { Renderer } from '../core/Renderer'
import { Loop } from '../core/Loop'
import { SceneRoot } from '../core/SceneRoot'
import { SceneBuilder } from '../build/SceneBuilder'
import { CameraController } from '../navigate/CameraController'
import type { InstancedBuckets } from '../build/InstancedBuckets'
import { publishedFloorId, toPublishedFloorView } from '@/api/space/designPublishedScene'
import type { ISpaceDesignSceneDto } from '../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

// ── Pure helper (exported for TDD) ────────────────────────────────────────────

/**
 * Given a list of floors (each with id, level, height in mm), returns a Map
 * from floorId to the cumulative Z elevation (mm) at which that floor's ground
 * plane sits.  Floors are sorted ascending by level; level-1 gets z=0.
 */
export function accumulateFloorZ(
  floors: Array<{ id: string; level: number; height: number }>,
): Map<string, number> {
  const ordered = [...floors].sort((a, b) => a.level - b.level)
  const z = new Map<string, number>()
  let acc = 0
  for (const f of ordered) {
    z.set(f.id, acc)
    acc += f.height
  }
  return z
}

// ── StackedViewer ─────────────────────────────────────────────────────────────

/**
 * A Three.js viewer that renders ALL floors of a site stacked at their
 * cumulative Z elevation.  Constructor wiring mirrors SpaceViewer exactly
 * (Renderer + Scene + SceneRoot + PerspectiveCamera + Loop + CameraController).
 *
 * Key difference from SpaceViewer:
 *  - loadPublished() accepts only server-selected Published Design V1 scenes
 *    and places each floor inside a Group at cumulative elevation (mm,
 *    data-space — SceneRoot's 0.001 scale converts to world metres).
 *  - No _clearSceneData between floors: all floor groups coexist.
 */
export class StackedViewer {
  private _renderer: Renderer
  private _scene: Scene
  private _sceneRoot: SceneRoot
  private _camera: PerspectiveCamera
  private _loop: Loop
  private _cameraController: CameraController

  /** floorId → cumulative Z elevation in data-space mm */
  private _floorZ = new Map<string, number>()
  /** floorId → Three.js Group holding that floor's objects */
  private _floorGroups = new Map<string, Group>()
  /** floorId → InstancedBuckets for that floor (for future picking) */
  private _floorBuckets = new Map<string, InstancedBuckets>()
  private _floorDisposers = new Map<string, () => void>()
  /** locationCode → floor/location identity for analytics overlays and realtime refresh. */
  private _codeToLocation = new Map<string, { floorId: string; locationId: string }>()

  constructor(canvas: HTMLCanvasElement) {
    // ── Renderer ──────────────────────────────────────────────────────────────
    this._renderer = new Renderer(canvas)

    // ── Scene ─────────────────────────────────────────────────────────────────
    this._scene = new Scene()
    this._scene.background = new Color(0x1a1a2e)
    this._sceneRoot = new SceneRoot()
    this._scene.add(this._sceneRoot)

    // ── Camera ────────────────────────────────────────────────────────────────
    const aspect = canvas.clientWidth / (canvas.clientHeight || 1)
    this._camera = new PerspectiveCamera(45, aspect, 0.1, 2000)
    this._camera.position.set(0, 50, 80)
    this._camera.lookAt(0, 0, 0)

    // ── Loop with render function (mirrors SpaceViewer._render) ───────────────
    this._loop = new Loop((dt) => this._render(dt))

    // ── CameraController (same args as SpaceViewer) ───────────────────────────
    this._cameraController = new CameraController(
      this._camera,
      this._renderer.gl,
      () => this.requestRender(),
    )

    window.addEventListener('resize', this._onResize)
  }

  // ── Render loop ──────────────────────────────────────────────────────────────

  private _render(dt: number): void {
    const animating = this._cameraController.update(dt)
    if (animating) this._loop.markDirty()
    const cam = this._cameraController.activeCamera
    this._renderer.gl.render(this._scene, cam)
  }

  private _onResize = (): void => {
    const canvas = this._renderer.gl.domElement
    const w = canvas.clientWidth
    const h = canvas.clientHeight || 1
    this._camera.aspect = w / h
    this._camera.updateProjectionMatrix()
    this._renderer.resize(w, h)
    this.requestRender()
  }

  // ── Multi-floor loading ──────────────────────────────────────────────────────

  /**
   * Build the server-selected Published scenes at cumulative Z elevations.
   * Each floor's objects live in its own Group so setFloorVisible() can toggle
   * individual floors independently.
   */
  async loadPublished(scenes: readonly ISpaceDesignSceneDto[]): Promise<void> {
    this._clearPublishedData()
    const floors = scenes.map(toPublishedFloorView)

    // Compute Z elevation for each floor (sorted by level asc, bottom=0)
    this._floorZ = accumulateFloorZ(
      floors.map((f) => ({ id: f.id, level: f.level, height: f.height })),
    )

    try {
      // Build each floor's scene and add to sceneRoot
      for (const scene of scenes) {
        const floorId = publishedFloorId(scene)
        const result = new SceneBuilder().buildPublished(scene)

        const grp = new Group()
        // position.z is data-space mm; SceneRoot converts it to world metres.
        grp.position.z = this._floorZ.get(floorId) ?? 0

        for (const object of result.objects) grp.add(object)

        this._sceneRoot.add(grp)
        this._floorGroups.set(floorId, grp)
        this._floorBuckets.set(floorId, result.buckets)
        this._floorDisposers.set(floorId, result.dispose)
        for (const [locationId, code] of result.locationCodes) {
          this._codeToLocation.set(code, { floorId, locationId })
        }
      }
    } catch (error) {
      this._clearPublishedData()
      throw error
    }

    // Frame camera to the union bounding box of the whole stacked scene
    const box = new Box3().setFromObject(this._sceneRoot)
    if (!box.isEmpty()) {
      this._cameraController.focusObject(box)
    }

    this.requestRender()
  }

  // ── Floor visibility ─────────────────────────────────────────────────────────

  setFloorVisible(floorId: string, v: boolean): void {
    const grp = this._floorGroups.get(floorId)
    if (grp) {
      grp.visible = v
      this.requestRender()
    }
  }

  setInstanceColorByCode(locationCode: string, hex: number): void {
    const ref = this._codeToLocation.get(locationCode)
    if (!ref) return
    this._floorBuckets.get(ref.floorId)?.setColor(ref.locationId, hex)
    this.requestRender()
  }

  resetInstanceColors(hex = 0x607d8b): void {
    for (const buckets of this._floorBuckets.values()) buckets.resetColors(hex)
    this.requestRender()
  }

  getFloorIdByCode(locationCode: string): string | null {
    return this._codeToLocation.get(locationCode)?.floorId ?? null
  }

  // ── ViewerHandle-compatible surface ─────────────────────────────────────────

  getSceneRoot(): Group { return this._sceneRoot }

  getFloorZ(floorId: string): number {
    return this._floorZ.get(floorId) ?? 0
  }

  requestRender(): void { this._loop.markDirty() }

  start(): void { this._loop.start() }

  private _clearPublishedData(): void {
    for (const buckets of this._floorBuckets.values()) buckets.dispose()
    this._floorBuckets.clear()
    for (const dispose of this._floorDisposers.values()) dispose()
    this._floorDisposers.clear()
    this._floorGroups.clear()
    this._codeToLocation.clear()
    while (this._sceneRoot.children.length > 0) {
      const child = this._sceneRoot.children[0]
      if (child) this._sceneRoot.remove(child)
    }
  }

  dispose(): void {
    this._loop.stop()
    window.removeEventListener('resize', this._onResize)
    this._cameraController.dispose()

    this._clearPublishedData()

    this._renderer.dispose()
  }
}
