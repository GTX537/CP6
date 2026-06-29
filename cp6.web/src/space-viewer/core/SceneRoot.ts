import { Group, Vector3 } from 'three'

export class SceneRoot extends Group {
  constructor() {
    super()
    this.scale.setScalar(0.001)      // mm → meters
    this.rotation.x = -Math.PI / 2  // Z-up data → Y-up Three: data(x,y,z) → world(x, z, -y)
    this.updateMatrixWorld(true)
  }

  dataToWorld(p: { x: number; y: number; z: number }): Vector3 {
    return this.localToWorld(new Vector3(p.x, p.y, p.z))
  }

  worldToData(v: Vector3): { x: number; y: number; z: number } {
    const l = this.worldToLocal(v.clone())
    return { x: l.x, y: l.y, z: l.z }
  }
}
