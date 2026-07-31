import { describe, expect, it } from 'vitest'
import type { ISpaceDesignSceneDto } from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import { buildElementCanvasPlan } from './elementCanvasPlan'

describe('buildElementCanvasPlan', () => {
  it('projects active DesignRevision elements and excludes removed drafts', () => {
    const activeId = '11111111-1111-1111-1111-111111111111'
    const removedId = '22222222-2222-2222-2222-222222222222'
    const scene = {
      schemaVersion: 1,
      authority: 'DesignRevision',
      runtimeOverlayIncluded: false,
      racks: [],
      rackLevels: [],
      elements: [
        {
          revision: { logicalId: activeId, lifecycleState: 'Active' },
          elementType: 'Column',
          geometryJson:
            '{"schemaVersion":1,"kind":"box","width":400,"height":5000,"depth":400}',
          x: 1000,
          y: 2000,
          z: 0,
          rotationZ: 90,
          width: 400,
          height: 5000,
          depth: 400,
        },
        {
          revision: { logicalId: removedId, lifecycleState: 'RemoveRequested' },
          elementType: 'Door',
          geometryJson:
            '{"schemaVersion":1,"kind":"box","width":900,"height":2200,"depth":200}',
          x: 0,
          y: 0,
          z: 0,
          rotationZ: 0,
          width: 900,
          height: 2200,
          depth: 200,
        },
      ],
    } as unknown as ISpaceDesignSceneDto

    const plan = buildElementCanvasPlan(scene)

    expect(plan).toEqual([
      {
        kind: 'rect',
        logicalId: activeId,
        elementType: 'Column',
        centerX: 800,
        centerY: 2200,
        width: 400,
        depth: 400,
        rotationZ: 90,
      },
    ])
  })

  it('rotates polygon points into authoritative world coordinates', () => {
    const scene = {
      schemaVersion: 1,
      authority: 'DesignRevision',
      runtimeOverlayIncluded: false,
      racks: [],
      rackLevels: [],
      elements: [
        {
          revision: {
            logicalId: '33333333-3333-3333-3333-333333333333',
            lifecycleState: 'Active',
          },
          elementType: 'Dock',
          geometryJson:
            '{"schemaVersion":1,"kind":"polygon","outer":[{"x":0,"y":0},{"x":100,"y":0},{"x":100,"y":50}],"holes":[],"height":500}',
          x: 1000,
          y: 2000,
          z: 0,
          rotationZ: 90,
          width: 100,
          height: 500,
          depth: 50,
        },
      ],
    } as unknown as ISpaceDesignSceneDto

    const polygon = buildElementCanvasPlan(scene)[0]

    expect(polygon?.kind).toBe('polygon')
    if (polygon?.kind !== 'polygon') return
    expect(polygon.points[0]).toEqual({ x: 1000, y: 2000 })
    expect(polygon.points[1]?.x).toBeCloseTo(1000)
    expect(polygon.points[1]?.y).toBeCloseTo(2100)
  })
})
