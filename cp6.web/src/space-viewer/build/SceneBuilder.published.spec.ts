import { Matrix4, Quaternion, Vector3 } from 'three'
import { describe, expect, it } from 'vitest'
import { SceneBuilder } from './SceneBuilder'
import type { ISpaceDesignSceneDto } from '../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

function publishedScene(): ISpaceDesignSceneDto {
  return {
    schemaVersion: 1,
    authority: 'DesignRevision',
    runtimeOverlayIncluded: false,
    versionStatus: 'Published',
    racks: [{
      revision: {
        logicalId: '11111111-1111-1111-1111-111111111111',
        lifecycleState: 'Active',
      },
      floorLogicalId: '22222222-2222-2222-2222-222222222222',
      zoneLogicalId: '33333333-3333-3333-3333-333333333333',
      rackCode: 'R1',
      x: 1000,
      y: 2000,
      z: 100,
      rotationZ: 90,
      width: 2000,
      depth: 1000,
      height: 3000,
    }],
    rackLevels: [{
      revision: {
        logicalId: '44444444-4444-4444-4444-444444444444',
        lifecycleState: 'Active',
      },
      rackLogicalId: '11111111-1111-1111-1111-111111111111',
      levelNo: 1,
      bottomZ: 0,
      clearHeight: 1200,
      binCount: 2,
      depthCount: 1,
      cellWidth: 1000,
      cellDepth: 900,
      beamHeight: 100,
    }],
    locations: [{
      revision: {
        logicalId: '55555555-5555-5555-5555-555555555555',
        lifecycleState: 'Active',
      },
      floorLogicalId: '22222222-2222-2222-2222-222222222222',
      rackLogicalId: '11111111-1111-1111-1111-111111111111',
      locationCode: 'F1-R1-01-01',
      columnNo: 1,
      levelNo: 1,
      depthNo: 1,
      width: 1000,
      height: 1200,
      depth: 900,
    }],
    zones: [],
    aisles: [],
    elements: [],
  } as unknown as ISpaceDesignSceneDto
}

describe('SceneBuilder.buildPublished', () => {
  it('projects a Published location from rack-level authority exactly', () => {
    const result = new SceneBuilder().buildPublished(publishedScene())
    const mesh = [...result.buckets.meshes][0]!
    const matrix = new Matrix4()
    const position = new Vector3()
    const rotation = new Quaternion()
    const scale = new Vector3()
    mesh.getMatrixAt(0, matrix)
    matrix.decompose(position, rotation, scale)

    expect(result.buckets.instanceToLocation(mesh.id, 0)).toBe(
      '55555555-5555-5555-5555-555555555555',
    )
    expect(position.toArray()).toEqual([550, 2500, 800])
    expect(scale.toArray()).toEqual([1000, 900, 1200])
    expect(result.locationCodes.get(
      '55555555-5555-5555-5555-555555555555',
    )).toBe('F1-R1-01-01')
    expect(result.objects.some((object) => object.name === 'SpaceDesignParametric')).toBe(true)

    result.buckets.dispose()
    result.dispose()
  })

  it('fails closed for Draft or incomplete location geometry', () => {
    expect(() => new SceneBuilder().buildPublished({
      ...publishedScene(),
      versionStatus: 'Draft',
    })).toThrow('requires a Published scene')

    const scene = publishedScene()
    scene.racks = []
    expect(() => new SceneBuilder().buildPublished(scene)).toThrow()
  })
})
