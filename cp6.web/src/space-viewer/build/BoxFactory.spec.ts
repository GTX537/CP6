import { describe, expect, it } from 'vitest'
import { Matrix4, Quaternion, Vector3 } from 'three'
import type { EditorScene } from '@/types/space/scene'
import { makeInstanceMatrix } from './BoxFactory'
import { SceneBuilder } from './SceneBuilder'

describe('makeInstanceMatrix', () => {
  it('uses contract degrees and data X/Y/Z dimensions before SceneRoot conversion', () => {
    const matrix = makeInstanceMatrix(100, 200, 300, 90, 1000, 800, 2000)
    const position = new Vector3()
    const rotation = new Quaternion()
    const scale = new Vector3()

    matrix.decompose(position, rotation, scale)
    const localX = new Vector3(1, 0, 0).applyQuaternion(rotation)

    expect(position.toArray()).toEqual([100, 200, 300])
    expect(scale.toArray()).toEqual([1000, 800, 2000])
    expect(localX.x).toBeCloseTo(0)
    expect(localX.y).toBeCloseTo(1)
    expect(localX.z).toBeCloseTo(0)
  })

  it('centers instanced rack frames around the origin corner using contract degrees', () => {
    const scene: EditorScene = {
      source: {
        kind: 'Real',
        dataSourceId: 'design-scene',
        observedAtUtc: '2026-07-30T00:00:00Z',
        isSimulated: false,
        isAvailable: true,
      },
      floor: {
        id: 'floor',
        siteId: 'site',
        level: 1,
        floorCode: 'F1',
        floorName: 'Floor 1',
        height: 5000,
        underlayOffsetX: 0,
        underlayOffsetY: 0,
        originX: 0,
        originY: 0,
      },
      zones: [],
      aisles: [],
      racks: [
        {
          id: 'rack',
          zoneId: 'zone',
          floorId: 'floor',
          rackCode: 'R1',
          x: 100,
          y: 200,
          z: 50,
          rotationZ: 90,
          cols: 2,
          levels: 3,
          depthCount: 1,
          cellW: 1000,
          cellH: 1000,
          cellD: 600,
        },
      ],
      locations: [],
      markers: [],
    }

    const result = new SceneBuilder().build(scene)
    const frame = result.objects
      .flatMap((object) => object.children)
      .find((object) => object.name === 'space-instanced-racks')

    expect(frame).toBeDefined()
    const matrix = new Matrix4()
    ;(frame as { getMatrixAt: (index: number, target: Matrix4) => void }).getMatrixAt(0, matrix)
    const position = new Vector3()
    const rotation = new Quaternion()
    const scale = new Vector3()
    matrix.decompose(position, rotation, scale)
    const localX = new Vector3(1, 0, 0).applyQuaternion(rotation)

    expect(scale.toArray()).toEqual([2000, 600, 3000])
    expect(position.x).toBeCloseTo(-200)
    expect(position.y).toBeCloseTo(1200)
    expect(position.z).toBeCloseTo(1550)
    expect(localX.x).toBeCloseTo(0)
    expect(localX.y).toBeCloseTo(1)
    result.buckets.dispose()
  })
})
