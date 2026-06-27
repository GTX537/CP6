import { Raycaster, Vector2 } from 'three'
import type { Camera, Intersection, InstancedMesh } from 'three'
import type { InstancedBuckets } from '../build/InstancedBuckets'
import type { SceneRoot } from '../core/SceneRoot'
import type { PickResult } from '@/types/space/viewer'

/** Pure mapping: given a raycast intersection on an InstancedMesh, resolve to PickResult or null. */
export function resolveIntersection(
  intersection: Intersection & { instanceId?: number },
  buckets: InstancedBuckets,
  sceneRoot: SceneRoot,
): PickResult | null {
  if (intersection.instanceId === undefined || intersection.instanceId === null) return null
  const mesh = intersection.object as InstancedMesh
  const locationId = buckets.instanceToLocation(mesh.id, intersection.instanceId)
  if (!locationId) return null
  return {
    kind: 'location',
    locationId,
    worldPoint: intersection.point,
    dataPoint: sceneRoot.worldToData(intersection.point),
  }
}

export class Picker {
  private readonly _raycaster = new Raycaster()

  pick(
    ndcX: number,
    ndcY: number,
    camera: Camera,
    buckets: InstancedBuckets,
    sceneRoot: SceneRoot,
  ): PickResult | null {
    this._raycaster.setFromCamera(new Vector2(ndcX, ndcY), camera)
    const ray = this._raycaster.ray

    // Stage 1: coarse bbox cull — find candidate buckets whose world bbox intersects the ray
    const candidates: InstancedMesh[] = []
    for (const mesh of buckets.meshes) {
      if (!mesh.visible) continue
      const localBox = mesh.boundingBox
      if (!localBox) {
        candidates.push(mesh)
        continue
      }
      const worldBox = localBox.clone().applyMatrix4(mesh.matrixWorld)
      if (ray.intersectsBox(worldBox)) candidates.push(mesh)
    }

    // Stage 2: precise raycast against each candidate InstancedMesh
    let closestDist = Infinity
    let closestResult: PickResult | null = null

    for (const mesh of candidates) {
      const hits = this._raycaster.intersectObject(mesh)
      for (const hit of hits) {
        if (hit.distance < closestDist) {
          const result = resolveIntersection(hit as Intersection & { instanceId?: number }, buckets, sceneRoot)
          if (result) {
            closestDist = hit.distance
            closestResult = result
          }
        }
      }
    }

    return closestResult
  }
}
