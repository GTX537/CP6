import { InstancedMesh, Color } from 'three'
import { UNIT_BOX, locMaterial, makeInstanceMatrix } from './BoxFactory'

export interface EnrichedLocation {
  id: string
  zoneId: string
  placed: boolean
  absX: number
  absY: number
  absZ: number
  sizeW: number
  sizeH: number
  sizeD: number
  rotationZ: number
}

export class InstancedBuckets {
  private _meshes: Map<number, InstancedMesh> = new Map()
  private _zoneToMesh: Map<string, number> = new Map()
  private _instanceToLocation: Map<number, Map<number, string>> = new Map()
  private _locationToInstance: Map<string, { meshId: number; instanceId: number }> = new Map()

  build(locations: EnrichedLocation[]): void {
    const placed = locations.filter((l) => l.placed)

    const byZone = new Map<string, EnrichedLocation[]>()
    for (const loc of placed) {
      let arr = byZone.get(loc.zoneId)
      if (!arr) {
        arr = []
        byZone.set(loc.zoneId, arr)
      }
      arr.push(loc)
    }

    for (const [zoneId, locs] of byZone) {
      const mesh = new InstancedMesh(UNIT_BOX, locMaterial, locs.length)
      mesh.instanceColor = null

      const instMap = new Map<number, string>()

      for (let i = 0; i < locs.length; i++) {
        const loc = locs[i]!
        const matrix = makeInstanceMatrix(
          loc.absX,
          loc.absY,
          loc.absZ,
          loc.rotationZ,
          loc.sizeW,
          loc.sizeD,
          loc.sizeH,
        )
        mesh.setMatrixAt(i, matrix)
        instMap.set(i, loc.id)
        this._locationToInstance.set(loc.id, { meshId: mesh.id, instanceId: i })
      }

      mesh.instanceMatrix.needsUpdate = true
      mesh.computeBoundingBox()

      this._meshes.set(mesh.id, mesh)
      this._zoneToMesh.set(zoneId, mesh.id)
      this._instanceToLocation.set(mesh.id, instMap)
    }
  }

  get meshes(): IterableIterator<InstancedMesh> {
    return this._meshes.values()
  }

  bucketCount(): number {
    return this._meshes.size
  }

  meshIdForZone(zoneId: string): number | undefined {
    return this._zoneToMesh.get(zoneId)
  }

  instanceToLocation(meshId: number, instanceId: number): string | null {
    return this._instanceToLocation.get(meshId)?.get(instanceId) ?? null
  }

  locationToInstance(locationId: string): { meshId: number; instanceId: number } | null {
    return this._locationToInstance.get(locationId) ?? null
  }

  setColor(locationId: string, hex: number): void {
    const ref = this._locationToInstance.get(locationId)
    if (!ref) return
    const mesh = this._meshes.get(ref.meshId)
    if (!mesh) return
    if (!mesh.instanceColor) {
      mesh.instanceColor = null
    }
    mesh.setColorAt(ref.instanceId, new Color(hex))
    if (mesh.instanceColor) {
      mesh.instanceColor.needsUpdate = true
    }
  }

  dispose(): void {
    // UNIT_BOX is never disposed (shared global)
    for (const mesh of this._meshes.values()) {
      mesh.dispose()
    }
    this._meshes.clear()
    this._zoneToMesh.clear()
    this._instanceToLocation.clear()
    this._locationToInstance.clear()
  }
}
