import { Box3, Frustum, Matrix4 } from 'three'
import type { PerspectiveCamera, OrthographicCamera } from 'three'
import type { InstancedBuckets } from '../build/InstancedBuckets'

export function boxInFrustum(box: Box3, frustum: Frustum): boolean {
  return frustum.intersectsBox(box)
}

export class FrustumCuller {
  private readonly _frustum = new Frustum()
  private readonly _projScreenMatrix = new Matrix4()
  private readonly _worldBox = new Box3()

  /** Only hides meshes outside the frustum — never re-shows meshes already hidden by LOD. */
  update(camera: PerspectiveCamera | OrthographicCamera, buckets: InstancedBuckets): void {
    this._projScreenMatrix.multiplyMatrices(camera.projectionMatrix, camera.matrixWorldInverse)
    this._frustum.setFromProjectionMatrix(this._projScreenMatrix)

    for (const mesh of buckets.meshes) {
      if (!mesh.visible) continue
      const localBox = mesh.boundingBox
      if (!localBox) continue
      this._worldBox.copy(localBox).applyMatrix4(mesh.matrixWorld)
      if (!boxInFrustum(this._worldBox, this._frustum)) {
        mesh.visible = false
      }
    }
  }
}
