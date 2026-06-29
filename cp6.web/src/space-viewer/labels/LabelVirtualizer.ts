import { Matrix4, Vector3 } from 'three'
import { CSS2DObject } from 'three/examples/jsm/renderers/CSS2DRenderer'
import type { PerspectiveCamera, OrthographicCamera, WebGLRenderer } from 'three'
import type { InstancedBuckets } from '../build/InstancedBuckets'
import type { LodTier } from '../cull/LodController'

/** Dedup candidates by screen grid cell. Returns locationIds to show (≤ maxTotal). */
export function gridDedup(
  candidates: Array<{ id: string; screenX: number; screenY: number }>,
  screenW: number,
  screenH: number,
  cellCount: number,
  maxPerCell: number,
  maxTotal: number,
): string[] {
  const cellW = screenW / cellCount
  const cellH = screenH / cellCount
  const cellCounts = new Map<string, number>()
  const result: string[] = []
  for (const c of candidates) {
    if (result.length >= maxTotal) break
    const cx = Math.floor(c.screenX / cellW)
    const cy = Math.floor(c.screenY / cellH)
    const key = `${cx},${cy}`
    const count = cellCounts.get(key) ?? 0
    if (count < maxPerCell) {
      cellCounts.set(key, count + 1)
      result.push(c.id)
    }
  }
  return result
}

export class LabelPool {
  private readonly _pool: CSS2DObject[] = []
  private readonly _active = new Set<CSS2DObject>()
  readonly maxSize: number

  constructor(max: number) {
    this.maxSize = max
    for (let i = 0; i < max; i++) {
      const div = document.createElement('div')
      div.className = 'space-label'
      div.style.cssText =
        'color:#fff;font-size:10px;padding:1px 3px;background:rgba(0,0,0,.55);border-radius:2px;pointer-events:none;white-space:nowrap'
      const obj = new CSS2DObject(div)
      obj.visible = false
      this._pool.push(obj)
    }
  }

  get objects(): readonly CSS2DObject[] { return this._pool }

  acquire(): CSS2DObject | null {
    for (const obj of this._pool) {
      if (!this._active.has(obj)) {
        this._active.add(obj)
        obj.visible = true
        return obj
      }
    }
    return null
  }

  release(obj: CSS2DObject): void {
    obj.visible = false
    this._active.delete(obj)
  }

  releaseAll(): void {
    for (const obj of this._active) obj.visible = false
    this._active.clear()
  }

  get activeCount(): number { return this._active.size }
}

const _tmpMatrix = new Matrix4()
const _tmpPos = new Vector3()

export class LabelVirtualizer {
  readonly pool: LabelPool

  constructor(maxLabels: number) {
    this.pool = new LabelPool(maxLabels)
  }

  update(
    camera: PerspectiveCamera | OrthographicCamera,
    buckets: InstancedBuckets,
    tier: LodTier,
    locationCodes: Map<string, string>,
    gl: WebGLRenderer,
  ): void {
    this.pool.releaseAll()
    if (tier !== 'near') return

    const w = gl.domElement.clientWidth || gl.domElement.width
    const h = gl.domElement.clientHeight || gl.domElement.height || 1

    type Candidate = { id: string; code: string; screenX: number; screenY: number; worldPos: Vector3 }
    const candidates: Candidate[] = []

    for (const mesh of buckets.meshes) {
      if (!mesh.visible) continue
      for (let i = 0; i < mesh.count; i++) {
        const locationId = buckets.instanceToLocation(mesh.id, i)
        if (!locationId) continue
        const code = locationCodes.get(locationId)
        if (!code) continue

        mesh.getMatrixAt(i, _tmpMatrix)
        _tmpPos.setFromMatrixPosition(_tmpMatrix).applyMatrix4(mesh.matrixWorld)

        const ndc = _tmpPos.clone().project(camera)
        if (ndc.z > 1) continue   // behind camera
        const screenX = ((ndc.x + 1) / 2) * w
        const screenY = ((-ndc.y + 1) / 2) * h
        if (screenX < 0 || screenX > w || screenY < 0 || screenY > h) continue

        candidates.push({ id: locationId, code, screenX, screenY, worldPos: _tmpPos.clone() })
      }
    }

    const visibleIds = gridDedup(candidates, w, h, 10, 1, this.pool.maxSize)
    const byId = new Map(candidates.map((c) => [c.id, c]))

    for (const id of visibleIds) {
      const cand = byId.get(id)
      if (!cand) continue
      const obj = this.pool.acquire()
      if (!obj) break
      obj.position.copy(cand.worldPos)
      ;(obj.element as HTMLElement).textContent = cand.code
    }
  }
}
