import { InstancedMesh, MeshBasicMaterial } from 'three'
import type { RackVO } from '@/types/space/scene'
import { UNIT_BOX, makeInstanceMatrix } from './BoxFactory'

export const rackInstanceMaterial = new MeshBasicMaterial({
  color: 0x90a4ae,
  wireframe: true,
})

export function buildInstancedRacks(racks: readonly RackVO[]): InstancedMesh {
  const mesh = new InstancedMesh(UNIT_BOX, rackInstanceMaterial, racks.length)
  for (let index = 0; index < racks.length; index++) {
    const rack = racks[index]!
    const width = rack.cols * rack.cellW
    const height = rack.levels * rack.cellH
    const depth = rack.depthCount * rack.cellD
    const radians = (rack.rotationZ * Math.PI) / 180
    const centerX = width / 2
    const centerY = depth / 2
    mesh.setMatrixAt(index, makeInstanceMatrix(
      rack.x + centerX * Math.cos(radians) - centerY * Math.sin(radians),
      rack.y + centerX * Math.sin(radians) + centerY * Math.cos(radians),
      rack.z + height / 2,
      rack.rotationZ,
      width,
      depth,
      height,
    ))
  }
  mesh.instanceMatrix.needsUpdate = true
  mesh.computeBoundingBox()
  mesh.name = 'space-instanced-racks'
  return mesh
}
