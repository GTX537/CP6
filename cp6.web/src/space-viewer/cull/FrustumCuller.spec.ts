import { describe, it, expect } from 'vitest'
import { Box3, Frustum, Matrix4, PerspectiveCamera, Vector3 } from 'three'
import { boxInFrustum } from './FrustumCuller'

// Camera at z=10, looking at origin (-Z direction), near=0.1, far=100.
// Visible world z range: [-90, 9.9]
function makeFrustum(): Frustum {
  const cam = new PerspectiveCamera(60, 1, 0.1, 100)
  cam.position.set(0, 0, 10)
  cam.lookAt(0, 0, 0)
  cam.updateMatrixWorld()
  const m = new Matrix4().multiplyMatrices(cam.projectionMatrix, cam.matrixWorldInverse)
  return new Frustum().setFromProjectionMatrix(m)
}

describe('boxInFrustum', () => {
  it('returns true for box inside frustum', () => {
    const frustum = makeFrustum()
    const box = new Box3(new Vector3(-1, -1, -1), new Vector3(1, 1, 5))
    expect(boxInFrustum(box, frustum)).toBe(true)
  })

  it('returns false for box entirely behind near plane (z > 9.9)', () => {
    const frustum = makeFrustum()
    const box = new Box3(new Vector3(-1, -1, 15), new Vector3(1, 1, 20))
    expect(boxInFrustum(box, frustum)).toBe(false)
  })

  it('returns false for box beyond far plane (z < -90)', () => {
    const frustum = makeFrustum()
    const box = new Box3(new Vector3(-1, -1, -150), new Vector3(1, 1, -120))
    expect(boxInFrustum(box, frustum)).toBe(false)
  })
})
