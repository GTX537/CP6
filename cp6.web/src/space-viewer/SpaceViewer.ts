import { Scene, PerspectiveCamera, Color, type Vector3 } from 'three'
import { Renderer } from './core/Renderer'
import { Loop } from './core/Loop'
import { SceneRoot } from './core/SceneRoot'
import { SceneBuilder } from './build/SceneBuilder'
import { sceneApi } from '@/api/space/scene'
import type { ViewerHandle } from './api/ViewerHandle'
import type { InstancedBuckets } from './build/InstancedBuckets'

export class SpaceViewer implements ViewerHandle {
  private _renderer: Renderer
  private _scene: Scene
  private _sceneRoot: SceneRoot
  private _camera: PerspectiveCamera
  private _loop: Loop
  private _buckets: InstancedBuckets | null = null
  private _readyCbs: Array<() => void> = []
  private _progressCbs: Array<(done: number, total: number) => void> = []

  constructor(canvas: HTMLCanvasElement) {
    this._renderer = new Renderer(canvas)
    this._scene = new Scene()
    this._scene.background = new Color(0x1a1a2e)
    this._sceneRoot = new SceneRoot()
    this._scene.add(this._sceneRoot)

    const aspect = canvas.clientWidth / (canvas.clientHeight || 1)
    this._camera = new PerspectiveCamera(45, aspect, 0.1, 2000)
    this._camera.position.set(0, 50, 80)
    this._camera.lookAt(0, 0, 0)

    this._loop = new Loop((dt) => this._render(dt))

    window.addEventListener('resize', this._onResize)
  }

  private _render(_dt: number): void {
    this._renderer.gl.render(this._scene, this._camera)
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

  async load(floorId: string): Promise<void> {
    const env = await sceneApi.get(floorId)
    const editorScene = env.data

    const builder = new SceneBuilder()
    const result = builder.build(editorScene, {
      onProgress: (done, total) => {
        this._progressCbs.forEach((cb) => cb(done, total))
        this.requestRender()
      },
    })

    this._buckets = result.buckets

    for (const obj of result.objects) {
      this._sceneRoot.add(obj)
    }

    this.requestRender()
    this._readyCbs.forEach((cb) => cb())
  }

  dispose(): void {
    this._loop.stop()
    window.removeEventListener('resize', this._onResize)

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

  getSceneRoot() {
    return this._sceneRoot
  }

  worldToData(v: Vector3): { x: number; y: number; z: number } {
    return this._sceneRoot.worldToData(v)
  }

  dataToWorld(p: { x: number; y: number; z: number }): Vector3 {
    return this._sceneRoot.dataToWorld(p)
  }

  instanceToLocation(meshId: number, instanceId: number): string | null {
    return this._buckets?.instanceToLocation(meshId, instanceId) ?? null
  }

  setInstanceColor(locationId: string, hex: number): void {
    this._buckets?.setColor(locationId, hex)
    this.requestRender()
  }

  requestRender(): void {
    this._loop.markDirty()
  }

  onReady(cb: () => void): void {
    this._readyCbs.push(cb)
  }

  onProgress(cb: (done: number, total: number) => void): void {
    this._progressCbs.push(cb)
  }

  start(): void {
    this._loop.start()
  }
}
