import { describe, expect, it } from 'vitest'
import { Matrix4, Quaternion, Vector3 } from 'three'
import type { RackVO } from '@/types/space/scene'
import { buildInstancedRacks } from './InstancedRacks'

function rack(overrides: Partial<RackVO> = {}): RackVO {
  return {
    id: 'rack-1',
    zoneId: 'zone-1',
    floorId: 'floor-1',
    rackCode: 'R001',
    x: 1_000,
    y: 2_000,
    z: 0,
    rotationZ: 0,
    cols: 10,
    levels: 2,
    depthCount: 1,
    cellW: 1_000,
    cellH: 1_200,
    cellD: 1_100,
    ...overrides,
  }
}

describe('buildInstancedRacks', () => {
  it('packs every rack into one draw-call mesh', () => {
    const mesh = buildInstancedRacks([
      rack(),
      rack({ id: 'rack-2', rackCode: 'R002', x: 20_000 }),
    ])

    expect(mesh.count).toBe(2)
    expect(mesh.name).toBe('space-instanced-racks')
    expect(mesh.boundingBox).not.toBeNull()
  })

  it('preserves the rack envelope center, rotation and dimensions', () => {
    const mesh = buildInstancedRacks([rack({ rotationZ: 90 })])
    const matrix = new Matrix4()
    const position = new Vector3()
    const rotation = new Quaternion()
    const scale = new Vector3()

    mesh.getMatrixAt(0, matrix)
    matrix.decompose(position, rotation, scale)

    expect(position.x).toBeCloseTo(450)
    expect(position.y).toBeCloseTo(7_000)
    expect(position.z).toBeCloseTo(1_200)
    expect(scale.toArray()).toEqual([10_000, 1_100, 2_400])
  })
})
