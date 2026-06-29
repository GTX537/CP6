import { Box3, Color, Group, PerspectiveCamera, Scene } from 'three'
import { Renderer } from '../core/Renderer'
import { Loop } from '../core/Loop'
import { SceneRoot } from '../core/SceneRoot'
import { SceneBuilder } from '../build/SceneBuilder'
import { CameraController } from '../navigate/CameraController'
import { floorApi } from '@/api/space/floor'
import { sceneApi } from '@/api/space/scene'
import type { InstancedBuckets } from '../build/InstancedBuckets'

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
 *  - loadSite() fetches every floor in sequence and places each floor's scene
 *    objects inside a Group at position.z = accumulateFloorZ elevation (mm,
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
   * Fetch all floors for `siteId`, build their scenes, and stack them at
   * cumulative Z elevations.  Each floor's objects live in their own Group so
   * setFloorVisible() can toggle individual floors independently.
   */
  async loadSite(siteId: string): Promise<void> {
    const floors = (await floorApi.list(siteId)).data

    // Compute Z elevation for each floor (sorted by level asc, bottom=0)
    this._floorZ = accumulateFloorZ(
      floors.map((f) => ({ id: f.id, level: f.level, height: f.height })),
    )

    // Build each floor's scene and add to sceneRoot
    for (const f of floors) {
      const editorScene = (await sceneApi.get(f.id)).data
      const result = new SceneBuilder().build(editorScene)

      const grp = new Group()
      // position.z in SceneRoot's local (data-space mm); SceneRoot's rotation+scale
      // transforms this to the correct world-space Y elevation (mm * 0.001 = metres).
      grp.position.z = this._floorZ.get(f.id) ?? 0

      for (const o of result.objects) {
        grp.add(o)
      }

      this._sceneRoot.add(grp)
      this._floorGroups.set(f.id, grp)
      this._floorBuckets.set(f.id, result.buckets)
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

  // ── ViewerHandle-compatible surface ─────────────────────────────────────────

  getSceneRoot(): Group { return this._sceneRoot }

  getFloorZ(floorId: string): number {
    return this._floorZ.get(floorId) ?? 0
  }

  requestRender(): void { this._loop.markDirty() }

  start(): void { this._loop.start() }

  dispose(): void {
    this._loop.stop()
    window.removeEventListener('resize', this._onResize)
    this._cameraController.dispose()

    for (const buckets of this._floorBuckets.values()) {
      buckets.dispose()
    }
    this._floorBuckets.clear()

    while (this._sceneRoot.children.length > 0) {
      const child = this._sceneRoot.children[0]
      if (child) this._sceneRoot.remove(child)
    }

    this._renderer.dispose()
  }
}
